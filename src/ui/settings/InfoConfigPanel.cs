namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using System.Text.Json;
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
/// 设置面板「信息」类别页：维护发布的目标群列表（群 ID、展示名、是否启用），并承载
/// 「从 Koishi 刷新群列表」与符卡 GIF 录制设置分组。
/// </summary>
/// <remarks>
/// <para>
/// 目标群的唯一维护入口：启用开关就是信息板块的发布目标，信息页不再单独勾选。
/// </para>
/// <para>
/// 沿用 <c>AiModelConfigPanel</c> 的范式：事件处理器只写数据模型，列表 UI 由
/// <c>AutoList</c> 绑定驱动的重建负责，不手工推 UI。
/// </para>
/// <para>
/// 行内字段（群 ID / 展示名 / 启用）另挂元素级 <c>AutoValue</c> 绑定：Koishi 回执改的是元素字段，
/// 列表级 <c>OnModify</c> 看不到，只靠它会让行停在旧值；行自己提交的写回不再触发重建，
/// 重建请求统一经 <c>CallDeferred</c> 落到主线程。
/// </para>
/// <para>
/// 与 Koishi 的交互：Koishi 回传的群列表经 <see cref="InfoEventBus"/> 投递到本页
/// （<see cref="InfoProtocol.GroupListResultEvent"/>），由 <c>GroupDirectoryService</c> 并入同一份
/// 目标群列表；这里负责手工增删与命名纠正，两处显示同一份数据。
/// </para>
/// <para>
/// 文本输入只在提交/失焦时写回模型：若每次按键都写回，绑定触发的重建会打断正在进行的输入。
/// </para>
/// <para>
/// 本页是有意保留的双滚动结构：目标群列表固定高度、自己滚（行数随活动规模浮动，不能让它把页面撑长），
/// 整页再套一层纵向滚动，往下翻即可看全常显的录制设置分组。滚轮到列表边界后由引擎默认冒泡给页面，
/// 本页不额外接管。
/// </para>
/// <para>
/// 录制设置分组（<see cref="RecordingConfigPanel"/>）挂在目标群列表下方（页面内容末尾）：本页没有独立场景
/// （节点直接写在 <c>SettingsPanel.tscn</c> 里），而分组自带二十余个控件，写进共享场景既挤又难维护，
/// 故按本页既有的「C# 构建行」范式在依赖解析后按需创建。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class InfoConfigPanel : VBoxContainer, IInfoConfigPanel, IProvide<DataManager>
{
  #region AutoConnect Nodes

  [Node("%GroupRows")]
  public IVBoxContainer GroupRows { get; set; } = default!;

  [Node("%AddGroupButton")]
  public IButton AddGroupButton { get; set; } = default!;

  [Node("%RefreshGroupsButton")]
  public IButton RefreshGroupsButton { get; set; } = default!;

  [Node("%HintLabel")]
  public ILabel HintLabel { get; set; } = default!;

  [Node("%StatusLabel")]
  public ILabel StatusLabel { get; set; } = default!;

  /// <summary>页面内容容器；录制设置分组挂在它的末尾（目标群列表与「添加目标群」行之后）。</summary>
  [Node("%PageBody")]
  public IVBoxContainer PageBody { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  [Dependency]
  public IWebSocketServer Server => this.DependOn<IWebSocketServer>();

  [Dependency]
  public InfoEventBus InfoEvents => this.DependOn<InfoEventBus>();

  /// <summary>提供给运行时创建的录制设置分组的 DataManager（就近解析，与页面同一实例）。</summary>
  /// <remarks>
  /// 录制分组是 <see cref="EnsureRecordingSection"/> 在依赖解析后用 <c>new</c> 建出来的子节点，
  /// 它的 <c>[Dependency] DataManager</c> 需要最近的祖先 provider；本页是它的直接宿主，
  /// 故把同一个实例往下提供，不另造服务（与 <c>AiModelConfigPanel</c> 提供给模型行的做法一致）。
  /// </remarks>
  DataManager IProvide<DataManager>.Value() => _dm!;

  #endregion

  private DataManager? _dm;
  private AppSettings _settings = new();
  private AutoList<TargetGroup>.Binding? _binding;
  private RecordingConfigPanel? _recordingSection;
  private GroupDirectoryService? _groupDirectory;

  /// <summary>刷新群列表的结果对话框（懒创建，复用同一个实例）。</summary>
  private AcceptDialog? _dialog;

  /// <summary>目标群列表下方挂载的录制设置分组（依赖解析后创建，未解析时为 null）。</summary>
  public RecordingConfigPanel? RecordingSection => _recordingSection;

  private readonly List<Control> _rows = new();

  /// <summary>行内元素（展示名 / 群 ID / 启用）的 AutoValue 绑定；重建行前统一释放。</summary>
  /// <remarks>
  /// 列表级 <c>OnModify</c> 只在增删时触发；Koishi 回执改的是元素字段，不挂元素级绑定就一直停在旧行上。
  /// </remarks>
  private readonly List<IDisposable> _rowBindings = new();

  /// <summary>正在重建行：元素绑定的「订阅即回调」不得再请求一次重建，否则会自激成每帧重建。</summary>
  private bool _isRebuildingRows;

  /// <summary>正在把本行用户输入写回模型：此时元素绑定回调不请求重建（本行已经是最新值）。</summary>
  private bool _isWritingFromRow;

  /// <summary>已排队一次列表重建：同一帧内多处元素变化合并为一次。</summary>
  private volatile bool _refreshQueued;

  /// <inheritdoc/>
  public override void _Notification(int what)
  {
    this.Notify(what);
    if (what == (int)NotificationExitTree)
    {
      _binding?.Dispose();
      _binding = null;

      DisposeRowBindings();
      _refreshQueued = false;

      InfoEvents.Received -= OnInfoEventReceived;
    }
  }

  /// <summary>AutoInject 节点注入完成：只挂信号。</summary>
  public void OnReady()
  {
    AddGroupButton.Pressed += OnAddGroupPressed;
    RefreshGroupsButton.Pressed += OnRefreshGroupsPressed;
    HintLabel.Text =
      "目标群是发布内容的接收方；「启用」的目标群就是信息板块的发布目标。「刷新群列表」从 Koishi 拉取群列表并并入此处，新群默认不启用。";

    // 类别页是切显隐而不是重建：每次被切到本页时刷新一次录制分组的插件/磁盘状态
    VisibilityChanged += OnVisibilityChanged;
  }

  /// <summary>AutoInject 依赖解析完成：绑定目标群列表、首次刷新，并挂上录制设置分组。</summary>
  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm is null)
      return;

    _settings = _dm.Settings;

    // 录制设置分组是运行时创建的子节点，其 [Dependency] DataManager 从本页就近解析；
    // 必须先 Provide 再挂子节点，否则子节点的依赖解析会一直等 provider 初始化。
    this.Provide();

    // 传访问器而不是实例：WebSocket 重启会重建实例并释放旧实例，查群列表时必须取「当前」那一个；
    // 日志按本服务模块名取自 DataManager 的工厂，不再走 core 的静态日志定位兜底。
    _groupDirectory = new GroupDirectoryService(
      _dm,
      () => Server,
      _dm.GetLogger(nameof(GroupDirectoryService))
    );
    _binding = _settings.TargetGroups.Bind().OnModify(OnTargetGroupsChanged);
    Refresh();
    EnsureRecordingSection();

    // 入站回执由 WebSocket 接收线程投递，订阅后统一切回主线程处理
    InfoEvents.Received += OnInfoEventReceived;
  }

  /// <summary>创建并挂载录制设置分组（幂等；依赖未解析时不创建）。</summary>
  /// <returns>已挂载的分组；依赖未解析时为 null。</returns>
  /// <remarks>
  /// 不再手工传 DataManager：分组自己用 AutoInject 从本页（<see cref="IProvide{T}"/>）就近解析，
  /// 与页内其它动态节点同一范式。
  /// </remarks>
  public RecordingConfigPanel? EnsureRecordingSection()
  {
    if (_recordingSection is not null || _dm is null)
      return _recordingSection;

    _recordingSection = new RecordingConfigPanel { Name = "RecordingSection" };
    PageBody.AddChild(_recordingSection);
    return _recordingSection;
  }

  /// <summary>按当前数据重建目标群行（供绑定回调与测试调用）。</summary>
  /// <remarks>
  /// 重建前释放旧的元素级绑定：旧行马上会被释放，绑定继续回调只会操作已释放节点。
  /// 重建期间抑制元素绑定的「订阅即回调」，否则每挂一条绑定就再排一次重建，变成每帧重建。
  /// </remarks>
  public void Refresh()
  {
    _refreshQueued = false;
    DisposeRowBindings();

    _isRebuildingRows = true;
    try
    {
      foreach (var row in _rows)
      {
        GroupRows.RemoveChild(row);
        row.QueueFree();
      }

      _rows.Clear();

      foreach (var group in _settings.TargetGroups)
        _rows.Add(BuildRow(group));
    }
    finally
    {
      _isRebuildingRows = false;
    }

    StatusLabel.Text =
      _settings.TargetGroups.Count == 0
        ? "尚未配置目标群。"
        : $"共 {_settings.TargetGroups.Count} 个目标群（启用 {CountEnabled()} 个）。";
  }

  /// <summary>释放上一轮行内元素绑定。</summary>
  private void DisposeRowBindings()
  {
    foreach (var binding in _rowBindings)
      binding.Dispose();

    _rowBindings.Clear();
  }

  private Control BuildRow(TargetGroup group)
  {
    var row = new HBoxContainer();

    var enabled = new CheckBox
    {
      ButtonPressed = group.Enabled.Value,
      TooltipText = "启用即发布目标；发布内容会发送到所有已启用的群",
    };
    enabled.Toggled += pressed =>
    {
      // 勾选只更新状态、不重建整表：写模型时抑制元素绑定，避免同一次点击触发全表重建
      WriteFromRow(() => group.Enabled.Value = pressed);
      _dm?.TriggerAutoSave();
      UpdateStatus();
    };
    row.AddChild(enabled);

    var baseline = new RowBaseline
    {
      ChannelId = group.ChannelId.Value,
      DisplayName = group.DisplayName.Value,
    };

    var channelId = new LineEdit
    {
      Text = baseline.ChannelId,
      PlaceholderText = "群 ID（channelId）",
      CustomMinimumSize = new Vector2(180, 0),
      SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    // 只在提交/失焦时写回：逐键写回会被绑定重建打断输入
    channelId.TextSubmitted += text => CommitChannelId(group, text, baseline);
    channelId.FocusExited += () => CommitChannelId(group, channelId.Text, baseline);
    row.AddChild(channelId);

    var displayName = new LineEdit
    {
      Text = baseline.DisplayName,
      PlaceholderText = "展示名（可留空）",
      CustomMinimumSize = new Vector2(180, 0),
      SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    displayName.TextSubmitted += text => CommitDisplayName(group, text, baseline);
    displayName.FocusExited += () => CommitDisplayName(group, displayName.Text, baseline);
    row.AddChild(displayName);

    // 元素级绑定：Koishi 回执之类的「别处改动」也要让行跟上（列表级 OnModify 不覆盖字段变化）
    _rowBindings.Add(group.Enabled.Bind().OnValue(_ => OnRowElementChanged()));
    _rowBindings.Add(group.ChannelId.Bind().OnValue(_ => OnRowElementChanged()));
    _rowBindings.Add(group.DisplayName.Bind().OnValue(_ => OnRowElementChanged()));

    var remove = new Button { Text = "删除" };
    remove.Pressed += () =>
    {
      // 只改数据模型；列表 UI 由 AutoList 绑定自动重建
      _settings.TargetGroups.Remove(group);
      _dm?.TriggerAutoSave();
    };
    row.AddChild(remove);

    GroupRows.AddChild(row);
    return row;
  }

  /// <summary>元素字段被行外改动时排一次重建；本行自己的写回与建行回放不再触发。</summary>
  private void OnRowElementChanged()
  {
    if (_isRebuildingRows || _isWritingFromRow)
      return;

    QueueRefresh();
  }

  /// <summary>把同帧内多次重建请求合并成一次（绑定回调里只调度，规则 19）。</summary>
  private void QueueRefresh()
  {
    if (_refreshQueued)
      return;

    _refreshQueued = true;
    CallDeferred(nameof(FlushRefresh));
  }

  /// <summary>执行排队中的重建（公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约）。</summary>
  public void FlushRefresh()
  {
    _refreshQueued = false;
    Refresh();
  }

  /// <summary>重建列表后显示一条提示（公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约）。</summary>
  /// <param name="notice">提示文本。</param>
  /// <remarks>
  /// 失败提示必须等重建之后再写：<see cref="Refresh"/> 会重置 <see cref="StatusLabel"/>，
  /// 且不能在输入框正在发信号的调用栈里同步重建它所在的行（会销毁发信号者）。
  /// </remarks>
  public void RefreshWithNotice(string notice)
  {
    Refresh();
    StatusLabel.Text = notice;
  }

  /// <summary>把本行用户输入写回模型；期间元素绑定视为「已与模型一致」，不排重建。</summary>
  /// <param name="write">写回动作。</param>
  private void WriteFromRow(Action write)
  {
    _isWritingFromRow = true;
    try
    {
      write();
    }
    finally
    {
      _isWritingFromRow = false;
    }
  }

  private void CommitChannelId(TargetGroup group, string text, RowBaseline baseline)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(group.ChannelId.Value, value, StringComparison.Ordinal))
    {
      // 文本已与模型一致：基线跟上当前值，后续「改回该值」才不会被误判成 stale
      baseline.ChannelId = value;
      return;
    }

    // 文本仍等于本行基线：说明是别处（Koishi 回执）改了模型，不是用户输入，以模型为准
    if (string.Equals(value, baseline.ChannelId, StringComparison.Ordinal))
    {
      QueueRefresh();
      return;
    }

    // C6：不在这里同步 Refresh()——它会销毁正在发信号的输入框所在行
    if (value.Length == 0)
    {
      CallDeferred(nameof(RefreshWithNotice), "群 ID 不能为空，已保留原值。");
      return;
    }

    if (IsDuplicated(group, value))
    {
      CallDeferred(nameof(RefreshWithNotice), $"群 ID {value} 已存在，已保留原值。");
      return;
    }

    WriteFromRow(() => group.ChannelId.Value = value);
    baseline.ChannelId = value;
    _dm?.TriggerAutoSave();
    UpdateStatus();
  }

  private void CommitDisplayName(TargetGroup group, string text, RowBaseline baseline)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(group.DisplayName.Value, value, StringComparison.Ordinal))
    {
      // 文本已与模型一致：基线跟上当前值，后续「改回该值」才不会被误判成 stale
      baseline.DisplayName = value;
      return;
    }

    // 写回前以模型当前值为准：文本仍等于本行基线（说明别处已改名）就按模型重建，不用旧文本覆盖。
    // 文本与基线不同（含用户显式清空成空串）一律视为用户意图，正常写回。
    if (string.Equals(value, baseline.DisplayName, StringComparison.Ordinal))
    {
      QueueRefresh();
      return;
    }

    WriteFromRow(() => group.DisplayName.Value = value);
    baseline.DisplayName = value;
    _dm?.TriggerAutoSave();
    UpdateStatus();
  }

  private bool IsDuplicated(TargetGroup self, string channelId)
  {
    foreach (var group in _settings.TargetGroups)
    {
      if (
        !ReferenceEquals(group, self)
        && string.Equals(group.ChannelId.Value, channelId, StringComparison.Ordinal)
      )
        return true;
    }

    return false;
  }

  private int CountEnabled()
  {
    var count = 0;
    foreach (var group in _settings.TargetGroups)
    {
      if (group.Enabled.Value)
        count++;
    }

    return count;
  }

  private void UpdateStatus() =>
    StatusLabel.Text = $"共 {_settings.TargetGroups.Count} 个目标群（启用 {CountEnabled()} 个）。";

  /// <summary>目标群列表增删：排一次重建（与元素级变化合并成一次）。</summary>
  private void OnTargetGroupsChanged() => QueueRefresh();

  /// <summary>本页被切显示时刷新录制分组状态（「整合」板块的 Editor Sharp 目录可能刚改过，影响「从 Sharp 目录推断」是否可用）。</summary>
  private void OnVisibilityChanged()
  {
    if (Visible)
      _recordingSection?.Refresh();
  }

  /// <summary>
  /// 点「刷新群列表」：向 Koishi 发起群列表查询，回执到达后由 <see cref="DispatchInfoEvent"/> 并入。
  /// </summary>
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

  private void OnInfoEventReceived(string eventName, JsonElement payload) =>
    CallDeferred(nameof(DispatchInfoEvent), eventName, payload.GetRawText());

  /// <summary>
  /// 在主线程分发信息板块回执；本页只关心群列表结果。
  /// </summary>
  /// <param name="eventName">事件名。</param>
  /// <param name="rawPayload">事件 payload 的原始 JSON（跨线程传字符串，避免传递非 Variant 类型）。</param>
  /// <remarks>公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约的一部分。</remarks>
  public void DispatchInfoEvent(string eventName, string rawPayload)
  {
    if (eventName != InfoProtocol.GroupListResultEvent || _groupDirectory is null)
      return;

    JsonDocument document;
    try
    {
      document = JsonDocument.Parse(rawPayload);
    }
    catch (JsonException ex)
    {
      GD.PushWarning($"InfoConfigPanel: 无法解析 {eventName} 回执：{ex.Message}");
      return;
    }

    using (document)
    {
      var added = _groupDirectory.HandleGroupListResult(document.RootElement, out var renamed);
      if (added < 0)
        return;

      // 回执可能只改元素字段（群名）：列表级 OnModify 不触发，这里显式排一次重建；
      // 与元素绑定排的重建合并为同一帧一次
      QueueRefresh();
      PresentMessage("群列表已更新", BuildGroupListMessage(added, renamed));
    }
  }

  /// <summary>拼群列表回执的结果提示（新增与改名分开说，避免「刷新了但界面没变」的误解）。</summary>
  /// <param name="added">新增的目标群数量。</param>
  /// <param name="renamed">被更名的已有目标群数量。</param>
  /// <returns>面向用户的提示文本。</returns>
  private static string BuildGroupListMessage(int added, int renamed)
  {
    if (added > 0 && renamed > 0)
      return $"新增 {added} 个目标群，更新 {renamed} 个群名，请在下方启用需要发布的群。";

    if (added > 0)
      return $"新增 {added} 个目标群，请在下方启用需要发布的群。";

    if (renamed > 0)
      return $"更新 {renamed} 个群名。";

    return "群列表没有变化。";
  }

  /// <summary>显示一条结果消息（对话框懒创建；未入树时不弹窗，仅保留文本供测试断言）。</summary>
  private void PresentMessage(string title, string text)
  {
    EnsureDialog();
    _dialog!.Title = title;
    _dialog.DialogText = text;

    if (IsInsideTree())
      _dialog.PopupCentered(new Vector2I(720, 460));
  }

  private void EnsureDialog()
  {
    if (_dialog is not null && GodotObject.IsInstanceValid(_dialog))
      return;

    _dialog = new AcceptDialog { Title = "群列表刷新" };
    AddChild(_dialog);
  }

  private void OnAddGroupPressed()
  {
    // 只改数据模型；列表 UI 由 AutoList 绑定自动重建
    _settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = string.Empty },
        DisplayName = { Value = string.Empty },
        Enabled = { Value = true },
      }
    );
    _dm?.TriggerAutoSave();
  }

  /// <summary>
  /// 一行内文本字段的「建行基线」：建行时取模型值，成功写回后随模型更新。
  /// </summary>
  /// <remarks>
  /// 只用于区分「用户没改过这一格、是别处改了模型」与「用户确实改成了这个值」。
  /// 若基线停留在建行时的值，用户把值改回建行原值（如群 ID 10001 → 10002 → 10001）
  /// 会被误判成 stale 而静默丢弃。
  /// </remarks>
  private sealed class RowBaseline
  {
    /// <summary>群 ID 基线。</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>展示名基线。</summary>
    public string DisplayName { get; set; } = string.Empty;
  }
}
