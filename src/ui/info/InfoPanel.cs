namespace AutoCMEX.UI.Info;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 信息板块主面板：顶部发布操作行 + 四栏（符卡 GIF 集 / 符卡猜测情况表 / 创作者剩余表 / 活动规则）。
/// </summary>
/// <remarks>
/// <para>
/// 四栏各有独立发布入口，均交由 <see cref="PublishService"/> 按固定顺序串行发送；顶部「一键转发」
/// 按 GIF 集 → 猜测表 → 创作者表 → 活动规则 依次发布。
/// </para>
/// <para>
/// 目标群不在信息页勾选：发布目标取设置板块「信息」类别页里「启用」的目标群。设置页是目标群的
/// 唯一维护入口（增删、改名、启停与从 Koishi 刷新），避免两处勾选态互相打架。
/// </para>
/// <para>
/// 发布结果统一走本面板的报告对话框：成功/失败逐项列出，存在失败项时可只重试失败项。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class InfoPanel
  : Control,
    IInfoPanel,
    IProvide<DataManager>,
    IProvide<InfoDataService>,
    IProvide<GifSetService>,
    IProvide<PublishService>,
    IProvide<BossDataWatcher>
{
  /// <summary>
  /// 猜测表自动推送的防抖窗口（秒）：一次猜测会连着触发多次数据变化，
  /// 等它们收敛成一张表再发，避免群里被中间态的表图刷屏。
  /// </summary>
  private const double AutoPublishDebounceSeconds = 1.5;

  #region AutoConnect Nodes

  [Node("%ForwardAllButton")]
  public IButton ForwardAllButton { get; set; } = default!;

  [Node("%AutoPublishGuessingTable")]
  public ICheckBox AutoPublishGuessingTable { get; set; } = default!;

  /// <summary>GIF 集栏（场景内实例，经 AutoInject 注入）。</summary>
  [Node("%GifSetPanel")]
  public IGifSetPanel GifSetPanelNode { get; set; } = default!;

  /// <summary>符卡猜测情况表栏（场景内实例，经 AutoInject 注入）。</summary>
  [Node("%GuessingTablePanel")]
  public IGuessingTablePanel GuessingTablePanelNode { get; set; } = default!;

  /// <summary>创作者剩余表栏（场景内实例，经 AutoInject 注入）。</summary>
  [Node("%CreatorRemainingPanel")]
  public ICreatorRemainingPanel CreatorRemainingPanelNode { get; set; } = default!;

  /// <summary>活动规则栏（场景内实例，经 AutoInject 注入）。</summary>
  [Node("%ActivityRulePanel")]
  public IActivityRulePanel ActivityRulePanelNode { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  [Dependency]
  public IWebSocketServer Server => this.DependOn<IWebSocketServer>();

  [Dependency]
  public InfoEventBus InfoEvents => this.DependOn<InfoEventBus>();

  #endregion

  #region Provided Services

  /// <summary>提供给子栏（[Dependency] 注入）的数据管理器（与祖先提供者是同一实例）。</summary>
  DataManager IProvide<DataManager>.Value() => _dm!;

  /// <summary>提供给子栏（[Dependency] 注入）的信息数据服务。</summary>
  InfoDataService IProvide<InfoDataService>.Value() => _dataService!;

  /// <summary>提供给子栏的 GIF 集服务（与发布编排共用同一实例）。</summary>
  GifSetService IProvide<GifSetService>.Value() => _gifSets!;

  /// <summary>提供给子栏的发布编排服务（各栏只发起发布请求，串行/失败即停/重试在本服务内）。</summary>
  PublishService IProvide<PublishService>.Value() => _publisher!;

  /// <summary>提供给子栏共享的数据观察者（两表栏共用一个实例）。</summary>
  BossDataWatcher IProvide<BossDataWatcher>.Value() => _watcher!;

  #endregion

  private DataManager? _dm;
  private InfoDataService? _dataService;
  private GifSetService? _gifSets;
  private PublishService? _publisher;

  /// <summary>
  /// 两张表栏共用的数据观察者：本面板创建并释放，两栏只订阅其
  /// <see cref="BossDataWatcher.Changed"/>。
  /// </summary>
  /// <remarks>
  /// 共享的意义：一次猜测 K 张符卡只由一个观察者发一轮通知，两栏各自按帧合并成一次重建；
  /// 各栏自建观察者会让通知数翻倍，而且结构变更重挂也会各做一遍。
  /// </remarks>
  private BossDataWatcher? _watcher;

  private AutoList<TargetGroup>.Binding? _targetGroupsBinding;
  private AutoValue<bool>.Binding? _autoPublishBinding;

  /// <summary>每个目标群元素级（启用 / 群 ID）的 AutoValue 绑定；列表增删时重挂。</summary>
  /// <remarks>
  /// 只绑列表不够：单个目标群的「启用」或「群 ID」被改时列表本身没有增删，<c>OnModify</c> 不触发，
  /// 「一键转发」可用态就会停在旧值。绑定回调只调度 <c>CallDeferred</c>，不直接动 UI（规则 19）。
  /// </remarks>
  private readonly List<IDisposable> _targetGroupFieldBindings = new();

  /// <summary>正在重挂目标群元素绑定：抑制订阅回放，避免回放回调自激。</summary>
  private bool _isRebindingGroupFields;

  private AcceptDialog? _reportDialog;
  private Button? _retryButton;
  private PublishReport? _lastReport;

  /// <summary>「一键转发」/ 自动推送的在飞标记（只用于禁用顶部按钮，单栏发布另见 <see cref="_publishingKinds"/>）。</summary>
  private bool _isPublishing;

  /// <summary>单栏发布的在飞栏位（仅主线程）：首次发布未结束时同一栏的重复触发直接丢弃。</summary>
  /// <remarks>
  /// 栏位的事件是同步派发的，栏位自己的忙碌态只覆盖派发段，覆盖不到真正的串行发送区间；
  /// 重入过滤放在这里，和「一键转发」的 <see cref="_isPublishing"/> 各管一条链，互不改语义。
  /// </remarks>
  private readonly HashSet<PublishItemKind> _publishingKinds = new();

  /// <summary>自动推送防抖计时器；每次表变化都重启它，因此只在最后一次变化后触发一次。</summary>
  private Timer? _autoPublishTimer;

  /// <summary>有「已变化但还没推出去」的表内容；发布期间攒下的变化靠它补发。</summary>
  private bool _autoPublishPending;

  /// <inheritdoc/>
  public override void _Notification(int what) => this.Notify(what);

  /// <inheritdoc/>
  public override void _ExitTree()
  {
    _targetGroupsBinding?.Dispose();
    _targetGroupsBinding = null;

    DisposeTargetGroupFieldBindings();

    _autoPublishBinding?.Dispose();
    _autoPublishBinding = null;

    _autoPublishTimer?.Stop();

    // 四栏都是场景内子节点：先退订它们的事件，避免本面板释放后仍有回调指过来
    UnsubscribeColumnEvents();

    // 两栏在 _ExitTree 里先退订（子节点先于父节点退出），这里才是共享观察者的唯一释放点
    _watcher?.Dispose();
    _watcher = null;

    InfoEvents.Received -= OnInfoEventReceived;
  }

  /// <summary>AutoInject 节点注入完成：只挂信号，服务待依赖解析后再创建。</summary>
  public void OnReady()
  {
    ForwardAllButton.Pressed += OnForwardAllPressed;
    AutoPublishGuessingTable.Toggled += OnAutoPublishToggled;
  }

  /// <summary>AutoInject 依赖解析完成：创建并提供子栏所需服务，再订阅四栏事件、绑数据与顶部操作行。</summary>
  /// <remarks>
  /// <see cref="IProvide{T}"/> 的服务必须先创建、再 <c>Provide()</c>：场景内实例的 GIF 集栏在
  /// 依赖解析时会等待本面板 provider 初始化，Provide 之后它才会执行自己的 <c>OnResolved</c>。
  /// </remarks>
  public void OnResolved()
  {
    _dm = DataManager;
    _dataService = new InfoDataService(_dm);
    _gifSets = new GifSetService(_dm, _dm.DataDir);

    // 传访问器而不是实例：WebSocket 重启会重建实例并释放旧实例，发布时必须取「当前」那一个
    _publisher = new PublishService(_dm, _dataService, _gifSets, () => Server);

    // 两表栏共用一个观察者；结构变更（重挂符卡属性绑定）经 dispatcher 回到主线程执行，
    // 因为写入可能来自 WebSocket 线程池（猜测模块在 Task.Run 里改数据）。
    _watcher = new BossDataWatcher(_dm, static action => Callable.From(action).CallDeferred());

    // 先订阅四栏事件再提供服务：Provide 会立刻驱动场景内实例的 OnResolved，
    // 那次首次重建若正好判出「表变了」会抛 TableChanged，晚订阅就漏掉了。
    SubscribeColumnEvents();

    // 再提供服务：四栏都是场景内实例，它们的依赖在等本面板的 provider 初始化
    this.Provide();

    _targetGroupsBinding = _dm.Settings.TargetGroups.Bind().OnModify(OnTargetGroupsChanged);
    RebindTargetGroupFieldBindings();
    UpdateForwardButtonState();

    // 先落初值再挂绑定：初值赋值不应被当成用户输入
    AutoPublishGuessingTable.ButtonPressed = _dm.InfoConfig.AutoPublishGuessingTable.Value;
    _autoPublishBinding = _dm
      .InfoConfig.AutoPublishGuessingTable.Bind()
      .OnValue(OnAutoPublishValueChanged);

    // 入站回执由 WebSocket 接收线程投递，订阅后统一切回主线程处理
    InfoEvents.Received += OnInfoEventReceived;
  }

  private void OnInfoEventReceived(string eventName, JsonElement payload) =>
    CallDeferred(nameof(DispatchInfoEvent), eventName, payload.GetRawText());

  /// <summary>
  /// 在主线程分发信息板块回执。
  /// </summary>
  /// <param name="eventName">事件名。</param>
  /// <param name="rawPayload">事件 payload 的原始 JSON（跨线程传字符串，避免传递非 Variant 类型）。</param>
  /// <remarks>公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约的一部分。</remarks>
  public void DispatchInfoEvent(string eventName, string rawPayload)
  {
    if (!IsInsideTree())
      return;

    JsonDocument document;
    try
    {
      document = JsonDocument.Parse(rawPayload);
    }
    catch (JsonException ex)
    {
      GD.PushWarning($"InfoPanel: 无法解析 {eventName} 回执：{ex.Message}");
      return;
    }

    using (document)
    {
      var payload = document.RootElement;

      switch (eventName)
      {
        // 群列表回执由设置板块「信息」类别页处理（刷新入口在那里），本面板收到时忽略
        case InfoProtocol.GroupListResultEvent:
          break;

        case InfoProtocol.PublishResultEvent:
          _publisher?.HandlePublishResult(payload);
          break;

        default:
          GD.PushWarning($"InfoPanel: 未处理的入站事件 {eventName}。");
          break;
      }
    }
  }

  /// <summary>
  /// 四栏引用，顺序为 GIF 集 → 猜测表 → 创作者表 → 活动规则。
  /// </summary>
  /// <remarks>
  /// 四栏都是 <c>InfoPanel.tscn</c> 的场景内实例（顺序由场景保证），依赖由本面板经 AutoInject 提供、
  /// 发布请求经各栏事件收口；供面板级测试断言装配结果。
  /// </remarks>
  public IReadOnlyList<Control?> ColumnPanels =>
    new Control?[]
    {
      GifSetPanelNode as Control,
      GuessingTablePanelNode as Control,
      CreatorRemainingPanelNode as Control,
      ActivityRulePanelNode as Control,
    };

  /// <summary>订阅四栏的事件（先退订再挂，依赖重复解析时不会重复挂接）。</summary>
  private void SubscribeColumnEvents()
  {
    UnsubscribeColumnEvents();

    GifSetPanelNode.PublishRequested += OnGifSetPublishRequested;

    GuessingTablePanelNode.PublishRequested += OnGuessingTablePublishRequested;
    GuessingTablePanelNode.TableChanged += OnGuessingTableChanged;

    CreatorRemainingPanelNode.PublishRequested += OnCreatorRemainingPublishRequested;

    ActivityRulePanelNode.PublishRequested += OnActivityRulePublishRequested;
  }

  /// <summary>退订四栏的事件：本面板退出前、子节点即将释放时调用。</summary>
  /// <remarks>场景内实例在 <c>OnReady</c> 之前未注入，故逐个判空。</remarks>
  private void UnsubscribeColumnEvents()
  {
    if (GifSetPanelNode is not null)
      GifSetPanelNode.PublishRequested -= OnGifSetPublishRequested;

    if (GuessingTablePanelNode is not null)
    {
      GuessingTablePanelNode.PublishRequested -= OnGuessingTablePublishRequested;
      GuessingTablePanelNode.TableChanged -= OnGuessingTableChanged;
    }

    if (CreatorRemainingPanelNode is not null)
      CreatorRemainingPanelNode.PublishRequested -= OnCreatorRemainingPublishRequested;

    if (ActivityRulePanelNode is not null)
      ActivityRulePanelNode.PublishRequested -= OnActivityRulePublishRequested;
  }

  /// <summary>GIF 集栏的发布请求。</summary>
  private async void OnGifSetPublishRequested() =>
    await PublishColumnSafelyAsync(PublishItemKind.GifSet);

  /// <summary>符卡猜测情况表栏的发布请求。</summary>
  private async void OnGuessingTablePublishRequested() =>
    await PublishColumnSafelyAsync(PublishItemKind.GuessingTable);

  /// <summary>创作者剩余表栏的发布请求。</summary>
  private async void OnCreatorRemainingPublishRequested() =>
    await PublishColumnSafelyAsync(PublishItemKind.CreatorRemainingTable);

  /// <summary>活动规则栏的发布请求。</summary>
  private async void OnActivityRulePublishRequested() =>
    await PublishColumnSafelyAsync(PublishItemKind.ActivityRule);

  /// <summary>
  /// 单栏发布请求的统一收口：复用本面板的发布编排（串行、失败即停、结果对话框）。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 栏位只抛事件、不持有发布回调；<c>async void</c> 是事件处理器的既定形态，
  /// 异常必须在此收口，否则会被 Godot 的同步上下文吞掉。
  /// </para>
  /// <para>
  /// 同一栏的重入护栏：事件是同步派发的，栏位自己的忙碌态只覆盖派发段，
  /// 「首次发布还没结束就再点一次」若不过滤会并发发出同一套内容（双击、连点）。
  /// 这里按 <see cref="PublishItemKind"/> 记在飞请求，重复触发直接丢弃，收尾时释放。
  /// </para>
  /// </remarks>
  /// <param name="kind">要发布的栏位。</param>
  private async Task PublishColumnSafelyAsync(PublishItemKind kind)
  {
    if (!_publishingKinds.Add(kind))
      return;

    try
    {
      await PublishColumnAsync(kind);
    }
    catch (Exception ex)
    {
      GD.PushWarning($"InfoPanel: {PublishOrder.GetTitle(kind)}发布失败：{ex.Message}");
    }
    finally
    {
      _publishingKinds.Remove(kind);
    }
  }

  private async Task PublishColumnAsync(PublishItemKind kind)
  {
    if (_publisher is null)
      return;

    var targets = CollectTargetGroupIds();
    if (!EnsureTargets(targets))
      return;

    // 本栏要发的就是这张表：撤销已排定的自动推送，避免同一张表发两遍
    if (kind == PublishItemKind.GuessingTable)
      CancelPendingAutoPublish();

    var report = await _publisher.PublishAsync(kind, targets, this);
    PresentReport(report);

    // 发布期间表又变了：补发一次（否则那一轮的最后一次变化会永远漏掉）
    if (kind == PublishItemKind.GuessingTable)
      FlushPendingAutoPublish();
  }

  private async void OnForwardAllPressed()
  {
    if (_publisher is null || _isPublishing)
      return;

    var targets = CollectTargetGroupIds();
    if (!EnsureTargets(targets))
      return;

    // 「一键转发」也会把猜测表发出去：同样撤销已排定的自动推送（发布期间的新变化仍会补发）
    CancelPendingAutoPublish();

    _isPublishing = true;
    ForwardAllButton.Disabled = true;
    try
    {
      var report = await _publisher.PublishAllAsync(targets, this);

      var attempted = report.Items.Select(item => item.Kind).ToHashSet();
      PresentReport(report, PublishOrder.Fixed.Where(kind => !attempted.Contains(kind)).ToArray());
    }
    finally
    {
      _isPublishing = false;
      if (IsInsideTree())
        UpdateForwardButtonState();

      FlushPendingAutoPublish();
    }
  }

  /// <summary>
  /// 收集发布目标：设置板块「信息」类别页里「启用」且群 ID 非空的目标群。
  /// </summary>
  /// <returns>目标群 channelId 列表；未启用任何目标群时为空。</returns>
  /// <remarks>信息页不再维护单独的勾选态，设置页的「启用」开关就是发布目标。</remarks>
  private IReadOnlyList<string> CollectTargetGroupIds()
  {
    if (_dm is null)
      return Array.Empty<string>();

    var ids = new List<string>();
    foreach (var group in _dm.Settings.TargetGroups)
    {
      if (!group.Enabled.Value)
        continue;

      var channelId = group.ChannelId.Value;
      if (!string.IsNullOrWhiteSpace(channelId))
        ids.Add(channelId);
    }

    return ids;
  }

  private bool EnsureTargets(IReadOnlyList<string> targets)
  {
    if (targets.Count > 0)
      return true;

    PresentMessage("未选择目标群", "请先在「设置 → 信息」里启用至少一个目标群，再执行发布。");
    return false;
  }

  /// <summary>目标群列表增删：重挂元素级绑定并刷新「一键转发」可用态（群列表本身在设置页展示）。</summary>
  /// <remarks>
  /// 绑定回调里只调度、不动 UI（规则 19）。列表增删会换掉元素对象，旧绑定必须释放、新群纳入，
  /// 之后才谈按钮态，因此两次 <c>CallDeferred</c> 顺序固定（先重挂、后刷新）。
  /// </remarks>
  private void OnTargetGroupsChanged()
  {
    CallDeferred(nameof(RebindTargetGroupFieldBindings));
    CallDeferred(nameof(UpdateForwardButtonState));
  }

  /// <summary>按当前目标群列表重挂「启用 / 群 ID」的元素级绑定。</summary>
  /// <remarks>
  /// 订阅 <see cref="AutoValue{T}"/> 会立刻回放一次当前值，重挂期间用
  /// <see cref="_isRebindingGroupFields"/> 抑制回放，避免每次重挂都排一次按钮刷新。
  /// 公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约的一部分。
  /// </remarks>
  public void RebindTargetGroupFieldBindings()
  {
    if (_dm is null)
      return;

    DisposeTargetGroupFieldBindings();

    _isRebindingGroupFields = true;
    try
    {
      foreach (var group in _dm.Settings.TargetGroups)
      {
        _targetGroupFieldBindings.Add(
          group.Enabled.Bind().OnValue(_ => OnTargetGroupFieldChanged())
        );
        _targetGroupFieldBindings.Add(
          group.ChannelId.Bind().OnValue(_ => OnTargetGroupFieldChanged())
        );
      }
    }
    finally
    {
      _isRebindingGroupFields = false;
    }
  }

  /// <summary>释放上一轮目标群元素级绑定。</summary>
  private void DisposeTargetGroupFieldBindings()
  {
    foreach (var binding in _targetGroupFieldBindings)
      binding.Dispose();

    _targetGroupFieldBindings.Clear();
  }

  /// <summary>目标群的启用/群 ID 变化会影响发布目标集合：调度一次按钮态刷新。</summary>
  private void OnTargetGroupFieldChanged()
  {
    if (_isRebindingGroupFields)
      return;

    CallDeferred(nameof(UpdateForwardButtonState));
  }

  private void OnAutoPublishToggled(bool pressed)
  {
    if (_dm is null || _dm.InfoConfig.AutoPublishGuessingTable.Value == pressed)
      return;

    _dm.InfoConfig.AutoPublishGuessingTable.Value = pressed;
    _dm.TriggerAutoSave();
  }

  private void OnAutoPublishValueChanged(bool value)
  {
    if (AutoPublishGuessingTable.ButtonPressed == value)
      return;

    AutoPublishGuessingTable.ButtonPressed = value;
  }

  /// <summary>
  /// 猜测表内容变化：勾选了自动推送就排一次（防抖后的）发布。
  /// </summary>
  /// <remarks>
  /// 这里不因「正在发布」而丢弃：变化先记在 <c>_autoPublishPending</c>，由发布收尾补发，
  /// 否则一轮猜测的最后一次变化恰好落在发布期间时会永远发不出去。
  /// </remarks>
  private void OnGuessingTableChanged()
  {
    if (_dm is null || !_dm.InfoConfig.AutoPublishGuessingTable.Value)
      return;

    _autoPublishPending = true;
    ScheduleAutoPublish();
  }

  /// <summary>
  /// 撤销已排定的自动推送：让给即将发出的同一张表（「一键转发」或本栏发布），避免重复发。
  /// </summary>
  private void CancelPendingAutoPublish()
  {
    _autoPublishPending = false;
    _autoPublishTimer?.Stop();
  }

  /// <summary>发布收尾：发布期间若攒下了表变化就补发一次。</summary>
  private void FlushPendingAutoPublish()
  {
    if (_autoPublishPending && IsInsideTree())
      ScheduleAutoPublish();
  }

  private void ScheduleAutoPublish()
  {
    if (!EnsureAutoPublishTimer())
      return;

    _autoPublishTimer!.Start();
  }

  private bool EnsureAutoPublishTimer()
  {
    if (_autoPublishTimer is not null && GodotObject.IsInstanceValid(_autoPublishTimer))
      return true;

    if (!IsInsideTree())
      return false;

    _autoPublishTimer = new Timer { OneShot = true, WaitTime = AutoPublishDebounceSeconds };
    _autoPublishTimer.Timeout += OnAutoPublishTimeout;
    AddChild(_autoPublishTimer);
    return true;
  }

  private async void OnAutoPublishTimeout()
  {
    try
    {
      await AutoPublishGuessingTableAsync();
    }
    catch (Exception ex)
    {
      GD.PushWarning($"InfoPanel: 自动推送猜测表失败：{ex.Message}");
    }
  }

  /// <summary>
  /// 静默把当前猜测表推给设置页启用的目标群。
  /// </summary>
  /// <remarks>
  /// 目标群复用设置页「启用」的目标群（与「一键转发」同一份）；自动推送不弹「未选择目标群」对话框——
  /// 猜测进行中反复弹窗会打断操作，只在输出窗口留一条日志。成功同样静默，
  /// 仅失败时复用发布结果对话框，因为失败基本意味着连接断了，必须让用户看见。
  /// 失败不重排：否则断连时会变成每 1.5 秒一次的重试风暴，等下一次表变化再试即可。
  /// </remarks>
  private async Task AutoPublishGuessingTableAsync()
  {
    if (_publisher is null || _dm is null || !_autoPublishPending)
      return;

    if (!_dm.InfoConfig.AutoPublishGuessingTable.Value)
    {
      _autoPublishPending = false;
      return;
    }

    // 一键转发进行中：这次变化先留着，等它结束再发（发出的是那之后收敛出来的表）
    if (_isPublishing)
    {
      ScheduleAutoPublish();
      return;
    }

    var targets = CollectTargetGroupIds();
    if (targets.Count == 0)
    {
      _autoPublishPending = false;
      GD.Print("InfoPanel: 已跳过猜测表自动推送：未启用任何目标群。");
      return;
    }

    // 先清标记再发送：发布期间新到的变化会重新置位，由收尾补发
    _autoPublishPending = false;
    _isPublishing = true;
    UpdateForwardButtonState();
    try
    {
      var report = await _publisher.PublishAsync(PublishItemKind.GuessingTable, targets, this);

      if (!report.IsSuccess)
      {
        PresentReport(report);
        return;
      }

      GD.Print($"InfoPanel: 已自动推送符卡猜测情况表到 {targets.Count} 个目标群。");
    }
    finally
    {
      _isPublishing = false;
      if (IsInsideTree())
        UpdateForwardButtonState();

      FlushPendingAutoPublish();
    }
  }

  /// <summary>按当前启用目标群与发布状态刷新「一键转发」可用态。</summary>
  /// <remarks>
  /// 列表绑定（<see cref="OnTargetGroupsChanged"/>）与元素绑定（启用 / 群 ID）都以
  /// <c>CallDeferred</c> 按名调用，故公开。
  /// </remarks>
  public void UpdateForwardButtonState()
  {
    if (ForwardAllButton is null)
      return;

    ForwardAllButton.Disabled = _isPublishing || CollectTargetGroupIds().Count == 0;
  }

  private void PresentReport(PublishReport report, IReadOnlyList<PublishItemKind>? notSent = null)
  {
    _lastReport = report;

    EnsureReportDialog();
    _reportDialog!.Title = "发布结果";

    var text = report.ToDisplayText();
    if (notSent is { Count: > 0 })
    {
      // 失败即停时报告里只有已尝试的项，必须显式写明哪些压根没发出去，否则像是漏发
      text +=
        $"\n未发送（前一项失败即停）：{string.Join("、", notSent.Select(PublishOrder.GetTitle))}";
    }

    _reportDialog.DialogText = text;

    if (_retryButton is not null)
      _retryButton.Visible = report.FailedKinds.Count > 0;

    _reportDialog.PopupCentered(new Vector2I(720, 460));
  }

  private void PresentMessage(string title, string text)
  {
    _lastReport = null;

    EnsureReportDialog();
    _reportDialog!.Title = title;
    _reportDialog.DialogText = text;

    if (_retryButton is not null)
      _retryButton.Visible = false;

    _reportDialog.PopupCentered(new Vector2I(720, 460));
  }

  private void EnsureReportDialog()
  {
    if (_reportDialog is not null && GodotObject.IsInstanceValid(_reportDialog))
      return;

    _reportDialog = new AcceptDialog { Title = "发布结果" };
    AddChild(_reportDialog);

    // 自定义按钮只在建对话框时添加一次：每次 Popup 都 AddButton 会把按钮越堆越多
    _retryButton = _reportDialog.AddButton("只重试失败项", true);
    _retryButton.Pressed += OnRetryFailedPressed;
  }

  private async void OnRetryFailedPressed()
  {
    var failedKinds = _lastReport?.FailedKinds;
    if (_publisher is null || failedKinds is null || failedKinds.Count == 0)
      return;

    var targets = CollectTargetGroupIds();
    if (!EnsureTargets(targets))
      return;

    _reportDialog?.Hide();

    var report = await _publisher.PublishAllAsync(targets, this, failedKinds);
    PresentReport(report);
  }
}
