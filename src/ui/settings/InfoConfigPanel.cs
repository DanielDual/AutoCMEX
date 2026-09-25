namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 设置面板「信息」类别页：维护发布的目标群列表（群 ID、展示名、是否启用）。
/// </summary>
/// <remarks>
/// <para>
/// 沿用 <c>AiModelConfigPanel</c> 的范式：事件处理器只写数据模型，列表 UI 由
/// <c>AutoList</c> 绑定驱动的重建负责，不手工推 UI。
/// </para>
/// <para>
/// 与信息板块的分工：Koishi 拉取的群会并入同一份目标群列表（见 <c>GroupDirectoryService</c>），
/// 这里负责手工增删与命名纠正，两处显示同一份数据。
/// </para>
/// <para>
/// 文本输入只在提交/失焦时写回模型：若每次按键都写回，绑定触发的重建会打断正在进行的输入。
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

  [Node("%HintLabel")]
  public ILabel HintLabel { get; set; } = default!;

  [Node("%StatusLabel")]
  public ILabel StatusLabel { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  #endregion

  private DataManager? _dm;
  private AppSettings _settings = new();
  private AutoList<TargetGroup>.Binding? _binding;

  private readonly List<Control> _rows = new();

  /// <inheritdoc/>
  public override void _Notification(int what)
  {
    this.Notify(what);
    if (what == (int)NotificationExitTree)
    {
      _binding?.Dispose();
      _binding = null;
    }
  }

  /// <summary>AutoInject 节点注入完成：只挂信号。</summary>
  public void OnReady()
  {
    AddGroupButton.Pressed += OnAddGroupPressed;
    HintLabel.Text =
      "目标群是发布内容的接收方；勾选状态在「信息」板块下栏调整。「信息」板块的「刷新群列表」可从 Koishi 拉取并并入此处。";
  }

  /// <summary>AutoInject 依赖解析完成：绑定目标群列表并首次刷新。</summary>
  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm is null)
      return;

    _settings = _dm.Settings;
    _binding = _settings.TargetGroups.Bind().OnModify(OnTargetGroupsChanged);
    Refresh();
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
      TooltipText = "启用后可在「信息」板块下栏勾选发布",
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
