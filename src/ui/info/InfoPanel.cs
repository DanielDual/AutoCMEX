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
/// 信息板块主面板：上栏四栏（符卡 GIF 集 / 符卡猜测情况表 / 创作者剩余表 / 活动规则），
/// 下栏为目标群多选与一键转发。
/// </summary>
/// <remarks>
/// <para>
/// 四栏各有独立发布入口，均交由 <see cref="PublishService"/> 按固定顺序串行发送；下栏「一键转发」
/// 按 GIF 集 → 猜测表 → 创作者表 → 活动规则 依次发布。
/// </para>
/// <para>
/// 发布结果统一走本面板的报告对话框：成功/失败逐项列出，存在失败项时可只重试失败项。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class InfoPanel : Control, IInfoPanel
{
  private const string GifSetPanelScenePath = "res://src/ui/info/GifSetPanel.tscn";
  private const string GuessingTablePanelScenePath = "res://src/ui/info/GuessingTablePanel.tscn";
  private const string CreatorRemainingPanelScenePath =
    "res://src/ui/info/CreatorRemainingPanel.tscn";
  private const string ActivityRulePanelScenePath = "res://src/ui/info/ActivityRulePanel.tscn";

  /// <summary>单栏最小宽度（像素）；窗口更窄时由上栏横向滚动承接，而不是把四栏挤变形。</summary>
  private const int ColumnMinWidth = 320;

  /// <summary>
  /// 猜测表自动推送的防抖窗口（秒）：一次猜测会连着触发多次数据变化，
  /// 等它们收敛成一张表再发，避免群里被中间态的表图刷屏。
  /// </summary>
  private const double AutoPublishDebounceSeconds = 1.5;

  #region AutoConnect Nodes

  [Node("%Columns")]
  public IHBoxContainer Columns { get; set; } = default!;

  [Node("%GroupList")]
  public IHBoxContainer GroupList { get; set; } = default!;

  [Node("%RefreshGroupsButton")]
  public IButton RefreshGroupsButton { get; set; } = default!;

  [Node("%ForwardAllButton")]
  public IButton ForwardAllButton { get; set; } = default!;

  [Node("%AutoPublishGuessingTable")]
  public ICheckBox AutoPublishGuessingTable { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  [Dependency]
  public IWebSocketServer Server => this.DependOn<IWebSocketServer>();

  [Dependency]
  public InfoEventBus InfoEvents => this.DependOn<InfoEventBus>();

  #endregion

  private DataManager? _dm;
  private InfoDataService? _dataService;
  private GifSetService? _gifSets;
  private PublishService? _publisher;
  private GroupDirectoryService? _groupDirectory;

  private GifSetPanel? _gifSetPanel;
  private GuessingTablePanel? _guessingTablePanel;
  private CreatorRemainingPanel? _creatorRemainingPanel;
  private ActivityRulePanel? _activityRulePanel;

  private readonly List<(TargetGroup Group, CheckBox Box)> _groupBoxes = new();
  private AutoList<TargetGroup>.Binding? _targetGroupsBinding;
  private AutoValue<bool>.Binding? _autoPublishBinding;

  private AcceptDialog? _reportDialog;
  private Button? _retryButton;
  private PublishReport? _lastReport;
  private bool _isPublishing;

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

    _autoPublishBinding?.Dispose();
    _autoPublishBinding = null;

    _autoPublishTimer?.Stop();

    InfoEvents.Received -= OnInfoEventReceived;
  }

  /// <summary>AutoInject 节点注入完成：只挂信号，服务待依赖解析后再创建。</summary>
  public void OnReady()
  {
    RefreshGroupsButton.Pressed += OnRefreshGroupsPressed;
    ForwardAllButton.Pressed += OnForwardAllPressed;
    AutoPublishGuessingTable.Toggled += OnAutoPublishToggled;
  }

  /// <summary>AutoInject 依赖解析完成：创建服务并装配四栏与下栏。</summary>
  public void OnResolved()
  {
    _dm = DataManager;
    _dataService = new InfoDataService(_dm);
    _gifSets = new GifSetService(_dm, _dm.DataDir);
    _publisher = new PublishService(_dm, _dataService, _gifSets, Server);
    _groupDirectory = new GroupDirectoryService(_dm, Server);

    BuildColumns();

    _targetGroupsBinding = _dm.Settings.TargetGroups.Bind().OnModify(OnTargetGroupsChanged);
    RebuildGroupList();

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
        case InfoProtocol.GroupListResultEvent:
          var added = _groupDirectory?.HandleGroupListResult(payload) ?? -1;
          if (added >= 0)
            PresentMessage("群列表已更新", $"新增 {added} 个目标群，请在下方勾选需要发布的群。");
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
  /// <remarks>供面板级测试断言装配结果；装载失败的栏位为 null 且已记录错误。</remarks>
  public IReadOnlyList<Control?> ColumnPanels =>
    new Control?[]
    {
      _gifSetPanel,
      _guessingTablePanel,
      _creatorRemainingPanel,
      _activityRulePanel,
    };

  private void BuildColumns()
  {
    _gifSetPanel = InstantiateColumn<GifSetPanel>(GifSetPanelScenePath);
    _gifSetPanel?.Setup(_dm!, _gifSets!, CreateColumnHandler(PublishItemKind.GifSet));

    _guessingTablePanel = InstantiateColumn<GuessingTablePanel>(GuessingTablePanelScenePath);
    _guessingTablePanel?.Setup(
      _dm!,
      _dataService!,
      CreateColumnHandler(PublishItemKind.GuessingTable),
      OnGuessingTableChanged
    );

    _creatorRemainingPanel = InstantiateColumn<CreatorRemainingPanel>(
      CreatorRemainingPanelScenePath
    );
    _creatorRemainingPanel?.Setup(
      _dm!,
      _dataService!,
      CreateColumnHandler(PublishItemKind.CreatorRemainingTable)
    );

    _activityRulePanel = InstantiateColumn<ActivityRulePanel>(ActivityRulePanelScenePath);
    _activityRulePanel?.Setup(_dm!, CreateColumnHandler(PublishItemKind.ActivityRule));
  }

  private T? InstantiateColumn<T>(string scenePath)
    where T : Control
  {
    var scene = GD.Load<PackedScene>(scenePath);
    if (scene is null)
    {
      GD.PushError($"InfoPanel: 栏位场景加载失败：{scenePath}");
      return null;
    }

    var column = scene.Instantiate<T>();
    column.CustomMinimumSize = new Vector2(ColumnMinWidth, 0);
    column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    column.SizeFlagsVertical = SizeFlags.ExpandFill;

    Columns.AddChild(column);

    // 注意：Setup 依赖 [Node] 注入结果，必须在 AddChild 之后调用
    return column;
  }

  private ColumnPublishHandler CreateColumnHandler(PublishItemKind kind) =>
    () => PublishColumnAsync(kind);

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

  private async void OnRefreshGroupsPressed()
  {
    if (_groupDirectory is null)
      return;

    RefreshGroupsButton.Disabled = true;
    try
    {
      var requested = await _groupDirectory.RequestAsync();
      if (requested)
      {
        PresentMessage("群列表刷新", "已向 Koishi 发起群列表查询，返回后会并入下方目标群列表。");
        return;
      }

      PresentMessage(
        "群列表刷新失败",
        "当前没有可用的 Koishi 连接，请先在「WebSocket」面板确认连接状态。"
      );
    }
    finally
    {
      if (IsInsideTree())
        RefreshGroupsButton.Disabled = false;
    }
  }

  private IReadOnlyList<string> CollectTargetGroupIds()
  {
    var ids = new List<string>();
    foreach (var (group, box) in _groupBoxes)
    {
      if (!box.ButtonPressed)
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

    PresentMessage("未选择目标群", "请先在下方勾选至少一个目标群，再执行发布。");
    return false;
  }

  private void RebuildGroupList()
  {
    if (_dm is null)
      return;

    foreach (var (_, box) in _groupBoxes)
    {
      GroupList.RemoveChild(box);
      box.QueueFree();
    }

    _groupBoxes.Clear();

    foreach (var group in _dm.Settings.TargetGroups)
    {
      var channelId = group.ChannelId.Value;
      var displayName = group.DisplayName.Value;

      var box = new CheckBox
      {
        Text = string.IsNullOrWhiteSpace(displayName) ? channelId : $"{displayName}（{channelId}）",
        ButtonPressed = group.Enabled.Value,
      };

      var captured = group;
      box.Toggled += pressed => OnGroupToggled(captured, pressed);

      GroupList.AddChild(box);
      _groupBoxes.Add((group, box));
    }

    UpdateForwardButtonState();
  }

  private void OnGroupToggled(TargetGroup group, bool pressed)
  {
    group.Enabled.Value = pressed;
    _dm?.TriggerAutoSave();
    UpdateForwardButtonState();
  }

  private void OnTargetGroupsChanged() => CallDeferred(nameof(RebuildGroupList));

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
  /// 静默把当前猜测表推给下栏勾选的目标群。
  /// </summary>
  /// <remarks>
  /// 目标群复用下栏勾选（与「一键转发」同一份）；自动推送不弹「未选择目标群」对话框——
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
      GD.Print("InfoPanel: 已跳过猜测表自动推送：未勾选任何目标群。");
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

  private void UpdateForwardButtonState()
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
