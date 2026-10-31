namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 设置面板「信息」类别页单测：目标群行的增删、群 ID 去重与空值保护。
/// </summary>
/// <remarks>
/// 节点用「真实节点 + 适配器」而不是 Mock：本面板的职责就是把数据渲染成行，只有真实节点才能验证
/// 行内容与信号连线；列表重建由 <c>AutoList</c> 绑定触发，测试中显式调用 <c>Refresh()</c> 模拟。
/// </remarks>
public class InfoConfigPanelTest : TestClass
{
  private string _dataDir = string.Empty;
  private DataManager _dataManager = default!;
  private InfoConfigPanel _panel = default!;
  private VBoxContainer _groupRows = default!;
  private Button _addButton = default!;
  private Label _statusLabel = default!;
  private Label _hintLabel = default!;

  public InfoConfigPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dataDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Test_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_dataDir);

    _dataManager = new DataManager(
      _dataDir,
      new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_dataDir)),
      new Mock<ILog>().Object
    );

    _groupRows = new VBoxContainer();
    _addButton = new Button();
    _statusLabel = new Label();
    _hintLabel = new Label();

    _panel = new InfoConfigPanel();
    (_panel as IAutoInit).IsTesting = true;

    _panel.FakeNodeTree(
      new()
      {
        ["%GroupRows"] = new VBoxContainerAdapter(_groupRows),
        ["%AddGroupButton"] = new ButtonAdapter(_addButton),
        ["%HintLabel"] = new LabelAdapter(_hintLabel),
        ["%StatusLabel"] = new LabelAdapter(_statusLabel),
      }
    );

    _panel.FakeDependency<DataManager>(_dataManager);
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    _panel?.QueueFree();
    _groupRows?.QueueFree();
    _addButton?.QueueFree();
    _statusLabel?.QueueFree();
    _hintLabel?.QueueFree();

    _dataManager?.Dispose();

    if (Directory.Exists(_dataDir))
      Directory.Delete(_dataDir, true);
  }

  [Test]
  public void HintLabel_ExplainsWhereGroupsAreUsed()
  {
    _hintLabel.Text.ShouldContain("目标群");
    _hintLabel.Text.ShouldContain("信息");
  }

  [Test]
  public void Refresh_WithoutGroups_ShowsEmptyStatus()
  {
    _panel.Refresh();

    _groupRows.GetChildCount().ShouldBe(0);
    _statusLabel.Text.ShouldBe("尚未配置目标群。");
  }

  [Test]
  public void Refresh_BuildsRowPerTargetGroupWithEditableFields()
  {
    _dataManager.Settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = "10001" },
        DisplayName = { Value = "示例群" },
        Enabled = { Value = true },
      }
    );

    _panel.Refresh();

    _groupRows.GetChildCount().ShouldBe(1);

    var row = _groupRows.GetChild(0);
    row.ShouldBeOfType<HBoxContainer>();
    row.GetChildCount().ShouldBe(4);

    var enabled = row.GetChild<CheckBox>(0);
    enabled.ButtonPressed.ShouldBeTrue();

    row.GetChild<LineEdit>(1).Text.ShouldBe("10001");
    row.GetChild<LineEdit>(2).Text.ShouldBe("示例群");
    row.GetChild<Button>(3).Text.ShouldBe("删除");

    _statusLabel.Text.ShouldBe("共 1 个目标群（启用 1 个）。");
  }

  [Test]
  public void AddGroupPressed_AppendsEmptyEnabledGroup()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);

    _dataManager.Settings.TargetGroups.Count.ShouldBe(1);
    var group = _dataManager.Settings.TargetGroups[0];
    group.ChannelId.Value.ShouldBe(string.Empty);
    group.DisplayName.Value.ShouldBe(string.Empty);
    // 新建即可用，避免「加一行但忘了勾选」导致发布时找不到目标
    group.Enabled.Value.ShouldBeTrue();
  }

  [Test]
  public void ToggleEnabled_WritesBackToModel()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    _panel.Refresh();

    _groupRows.GetChild(0).GetChild<CheckBox>(0).ButtonPressed = false;

    _dataManager.Settings.TargetGroups[0].Enabled.Value.ShouldBeFalse();
    _statusLabel.Text.ShouldBe("共 1 个目标群（启用 0 个）。");
  }

  [Test]
  public void RemoveButton_DropsGroupFromModel()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    _panel.Refresh();

    _groupRows.GetChild(0).GetChild<Button>(3).EmitSignal(BaseButton.SignalName.Pressed);

    _dataManager.Settings.TargetGroups.ShouldBeEmpty();
  }

  [Test]
  public void CommitChannelId_TrimsAndWritesModel()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    _panel.Refresh();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(1);
    input.Text = "  10001  ";
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "  10001  ");

    _dataManager.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10001");
  }

  [Test]
  public void CommitChannelId_EmptyKeepsPreviousValue()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    _dataManager.Settings.TargetGroups[0].ChannelId.Value = "10001";
    _panel.Refresh();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(1);
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "   ");

    _dataManager.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10001");
    _statusLabel.Text.ShouldContain("群 ID 不能为空");
  }

  [Test]
  public void CommitChannelId_DuplicateKeepsPreviousValue()
  {
    _dataManager.Settings.TargetGroups.Add(new TargetGroup { ChannelId = { Value = "10001" } });
    _dataManager.Settings.TargetGroups.Add(new TargetGroup { ChannelId = { Value = "10002" } });
    _panel.Refresh();

    var second = _groupRows.GetChild(1).GetChild<LineEdit>(1);
    second.EmitSignal(LineEdit.SignalName.TextSubmitted, "10001");

    _dataManager.Settings.TargetGroups[1].ChannelId.Value.ShouldBe("10002");
    _statusLabel.Text.ShouldContain("已存在");
  }

  [Test]
  public void CommitDisplayName_TrimsAndWritesModel()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    _panel.Refresh();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(2);
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "  示例群  ");

    _dataManager.Settings.TargetGroups[0].DisplayName.Value.ShouldBe("示例群");
  }
}
