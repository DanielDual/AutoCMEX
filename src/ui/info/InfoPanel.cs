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

  #region AutoConnect Nodes

  [Node("%Columns")]
  public IHBoxContainer Columns { get; set; } = default!;

  [Node("%GroupList")]
  public IHBoxContainer GroupList { get; set; } = default!;

  [Node("%RefreshGroupsButton")]
  public IButton RefreshGroupsButton { get; set; } = default!;

  [Node("%ForwardAllButton")]
  public IButton ForwardAllButton { get; set; } = default!;

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

  private AcceptDialog? _reportDialog;
  private Button? _retryButton;
  private PublishReport? _lastReport;
  private bool _isPublishing;

  /// <inheritdoc/>
  public override void _Notification(int what) => this.Notify(what);

  /// <inheritdoc/>
  public override void _ExitTree()
  {
    _targetGroupsBinding?.Dispose();
    _targetGroupsBinding = null;

    InfoEvents.Received -= OnInfoEventReceived;
  }

  /// <summary>AutoInject 节点注入完成：只挂信号，服务待依赖解析后再创建。</summary>
  public void OnReady()
  {
    RefreshGroupsButton.Pressed += OnRefreshGroupsPressed;
    ForwardAllButton.Pressed += OnForwardAllPressed;
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
      CreateColumnHandler(PublishItemKind.GuessingTable)
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

    var report = await _publisher.PublishAsync(kind, targets, this);
    PresentReport(report);
  }

  private async void OnForwardAllPressed()
  {
    if (_publisher is null || _isPublishing)
      return;

    var targets = CollectTargetGroupIds();
    if (!EnsureTargets(targets))
      return;

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
