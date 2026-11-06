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
public partial class InfoConfigPanel : VBoxContainer, IInfoConfigPanel
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

  /// <inheritdoc/>
  public override void _Notification(int what)
  {
    this.Notify(what);
    if (what == (int)NotificationExitTree)
    {
      _binding?.Dispose();
      _binding = null;

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
    _groupDirectory = new GroupDirectoryService(_dm, Server);
    _binding = _settings.TargetGroups.Bind().OnModify(OnTargetGroupsChanged);
    Refresh();
    EnsureRecordingSection();

    // 入站回执由 WebSocket 接收线程投递，订阅后统一切回主线程处理
    InfoEvents.Received += OnInfoEventReceived;
  }

  /// <summary>创建并挂载录制设置分组（幂等；依赖未解析时不创建）。</summary>
  /// <returns>已挂载的分组；依赖未解析时为 null。</returns>
  public RecordingConfigPanel? EnsureRecordingSection()
  {
    if (_recordingSection is not null || _dm is null)
      return _recordingSection;

    _recordingSection = new RecordingConfigPanel(_dm) { Name = "RecordingSection" };
    PageBody.AddChild(_recordingSection);
    return _recordingSection;
  }

  /// <summary>按当前数据重建目标群行（供绑定回调与测试调用）。</summary>
  public void Refresh()
  {
    foreach (var row in _rows)
    {
      GroupRows.RemoveChild(row);
      row.QueueFree();
    }

    _rows.Clear();

    foreach (var group in _settings.TargetGroups)
      _rows.Add(BuildRow(group));

    StatusLabel.Text =
      _settings.TargetGroups.Count == 0
        ? "尚未配置目标群。"
        : $"共 {_settings.TargetGroups.Count} 个目标群（启用 {CountEnabled()} 个）。";
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
      group.Enabled.Value = pressed;
      _dm?.TriggerAutoSave();
      UpdateStatus();
    };
    row.AddChild(enabled);

    var channelId = new LineEdit
    {
      Text = group.ChannelId.Value,
      PlaceholderText = "群 ID（channelId）",
      CustomMinimumSize = new Vector2(180, 0),
      SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    // 只在提交/失焦时写回：逐键写回会被绑定重建打断输入
    channelId.TextSubmitted += text => CommitChannelId(group, text);
    channelId.FocusExited += () => CommitChannelId(group, channelId.Text);
    row.AddChild(channelId);

    var displayName = new LineEdit
    {
      Text = group.DisplayName.Value,
      PlaceholderText = "展示名（可留空）",
      CustomMinimumSize = new Vector2(180, 0),
      SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    displayName.TextSubmitted += text => CommitDisplayName(group, text);
    displayName.FocusExited += () => CommitDisplayName(group, displayName.Text);
    row.AddChild(displayName);

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

  private void CommitChannelId(TargetGroup group, string text)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(group.ChannelId.Value, value, StringComparison.Ordinal))
      return;

    // 先重建列表再写提示：Refresh() 会重置 StatusLabel，顺序反了提示看不见
    if (value.Length == 0)
    {
      Refresh();
      StatusLabel.Text = "群 ID 不能为空，已保留原值。";
      return;
    }

    if (IsDuplicated(group, value))
    {
      Refresh();
      StatusLabel.Text = $"群 ID {value} 已存在，已保留原值。";
      return;
    }

    group.ChannelId.Value = value;
    _dm?.TriggerAutoSave();
    UpdateStatus();
  }

  private void CommitDisplayName(TargetGroup group, string text)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(group.DisplayName.Value, value, StringComparison.Ordinal))
      return;

    group.DisplayName.Value = value;
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

  private void OnTargetGroupsChanged() => CallDeferred(nameof(Refresh));

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
      var added = _groupDirectory.HandleGroupListResult(document.RootElement);
      if (added < 0)
        return;

      PresentMessage("群列表已更新", $"新增 {added} 个目标群，请在下方启用需要发布的群。");
    }
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
}
