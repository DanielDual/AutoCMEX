namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
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
/// 行内容与信号连线。行内元素由 <c>AutoValue</c> 绑定驱动重建，因此验证绑定链路的用例必须
/// 「写模型 → 等帧（<c>CallDeferred</c> 落主线程）→ 断言」，不能显式调 <c>Refresh()</c> 冒充绑定。
/// </remarks>
public class InfoConfigPanelTest : TestClass
{
  private string _dataDir = string.Empty;
  private DataManager _dataManager = default!;
  private InfoConfigPanel _panel = default!;
  private VBoxContainer _groupRows = default!;
  private Button _addButton = default!;
  private Button _refreshButton = default!;
  private Label _statusLabel = default!;
  private Label _hintLabel = default!;
  private Mock<IWebSocketServer> _server = default!;
  private InfoEventBus _bus = default!;
  private VBoxContainer _pageBody = default!;

  /// <summary>本页发出的 WebSocket 消息（按顺序），用于断言群列表查询。</summary>
  private readonly System.Collections.Generic.List<WebSocketMessage> _sent = new();

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

    _sent.Clear();
    _groupRows = new VBoxContainer();
    _addButton = new Button();
    _refreshButton = new Button();
    _statusLabel = new Label();
    _hintLabel = new Label();
    _pageBody = new VBoxContainer();

    _server = new Mock<IWebSocketServer>();
    _server.SetupGet(s => s.ConnectionCount).Returns(1);
    _server
      .Setup(s => s.BroadcastAsync(It.IsAny<WebSocketMessage>()))
      .Callback<WebSocketMessage>(_sent.Add)
      .ReturnsAsync(1);
    _bus = new InfoEventBus();

    _panel = new InfoConfigPanel();
    (_panel as IAutoInit).IsTesting = true;

    _panel.FakeNodeTree(
      new()
      {
        ["%GroupRows"] = new VBoxContainerAdapter(_groupRows),
        ["%PageBody"] = new VBoxContainerAdapter(_pageBody),
        ["%AddGroupButton"] = new ButtonAdapter(_addButton),
        ["%RefreshGroupsButton"] = new ButtonAdapter(_refreshButton),
        ["%HintLabel"] = new LabelAdapter(_hintLabel),
        ["%StatusLabel"] = new LabelAdapter(_statusLabel),
      }
    );

    _panel.FakeDependency<DataManager>(_dataManager);
    _panel.FakeDependency<IWebSocketServer>(_server.Object);
    _panel.FakeDependency<InfoEventBus>(_bus);
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    _panel?.QueueFree();
    _groupRows?.QueueFree();
    _addButton?.QueueFree();
    _refreshButton?.QueueFree();
    _statusLabel?.QueueFree();
    _hintLabel?.QueueFree();
    _pageBody?.QueueFree();

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

  /// <summary>无目标群时的初始渲染（OnResolved/绑定链路）显示空状态，不手动 Refresh。</summary>
  [Test]
  public async Task NoTargetGroups_ShowsEmptyStatus()
  {
    await SettleAsync();

    _groupRows.GetChildCount().ShouldBe(0);
    _statusLabel.Text.ShouldBe("尚未配置目标群。");
  }

  /// <summary>写模型新增目标群后由 Sync 绑定驱动重建行，不再手动 Refresh 模拟绑定。</summary>
  [Test]
  public async Task TargetGroupAdd_BuildsRowWithEditableFields()
  {
    _dataManager.Settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = "10001" },
        DisplayName = { Value = "示例群" },
        Enabled = { Value = true },
      }
    );

    await SettleAsync();

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
  public async Task ToggleEnabled_WritesBackToModelWithoutRebuildingRows()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    await SettleAsync();

    var row = _groupRows.GetChild(0);
    row.GetChild<CheckBox>(0).ButtonPressed = false;
    await SettleAsync();

    _dataManager.Settings.TargetGroups[0].Enabled.Value.ShouldBeFalse();
    _statusLabel.Text.ShouldBe("共 1 个目标群（启用 0 个）。");
    // 勾选只更新状态、不重建整表：行实例保持不变即没有走进重建分支
    _groupRows.GetChild(0).ShouldBeSameAs(row);
  }

  [Test]
  public async Task RemoveButton_DropsGroupFromModel()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    // 绑定路径：列表变化驱动建行（只为拿到删除按钮，不模拟绑定）
    await SettleAsync();

    _groupRows.GetChild(0).GetChild<Button>(3).EmitSignal(BaseButton.SignalName.Pressed);

    _dataManager.Settings.TargetGroups.ShouldBeEmpty();
  }

  [Test]
  public async Task CommitChannelId_TrimsAndWritesModel()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    // 绑定路径：列表变化驱动建行（后续输入是模拟用户操作）
    await SettleAsync();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(1);
    input.Text = "  10001  ";
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "  10001  ");

    _dataManager.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10001");
  }

  [Test]
  public async Task CommitChannelId_EmptyKeepsPreviousValue()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    _dataManager.Settings.TargetGroups[0].ChannelId.Value = "10001";
    // 绑定路径：列表变化驱动建行（行内展示模型当前的 10001）
    await SettleAsync();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(1);
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "   ");
    await SettleAsync();

    _dataManager.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10001");
    // 重建与提示都在下一帧执行：不能在信号回调里同步销毁正在发信号的输入框所在行（C6）
    _statusLabel.Text.ShouldContain("群 ID 不能为空");
    _groupRows.GetChild(0).GetChild<LineEdit>(1).Text.ShouldBe("10001");
  }

  [Test]
  public async Task CommitChannelId_DuplicateKeepsPreviousValue()
  {
    _dataManager.Settings.TargetGroups.Add(new TargetGroup { ChannelId = { Value = "10001" } });
    _dataManager.Settings.TargetGroups.Add(new TargetGroup { ChannelId = { Value = "10002" } });
    // 绑定路径：列表变化驱动建行
    await SettleAsync();

    var second = _groupRows.GetChild(1).GetChild<LineEdit>(1);
    second.EmitSignal(LineEdit.SignalName.TextSubmitted, "10001");
    await SettleAsync();

    _dataManager.Settings.TargetGroups[1].ChannelId.Value.ShouldBe("10002");
    _statusLabel.Text.ShouldContain("已存在");
    _groupRows.GetChild(1).GetChild<LineEdit>(1).Text.ShouldBe("10002");
  }

  [Test]
  public async Task CommitChannelId_RevertToOriginalAfterWrite_WritesModel()
  {
    _dataManager.Settings.TargetGroups.Add(new TargetGroup { ChannelId = { Value = "10001" } });
    // 绑定路径：列表变化驱动建行（后续两次提交是模拟用户操作）
    await SettleAsync();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(1);

    // 先正常改到 10002：写回成功（基线应随之更新）
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "10002");
    _dataManager.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10002");

    // 再改回建行原值 10001：必须照常写回，不能被当成「模型已被别处改过」的 stale 丢弃
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "10001");
    _dataManager.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10001");
  }

  [Test]
  public async Task CommitDisplayName_RevertToOriginalAfterWrite_WritesModel()
  {
    _dataManager.Settings.TargetGroups.Add(
      new TargetGroup { ChannelId = { Value = "10001" }, DisplayName = { Value = "旧群名" } }
    );
    // 绑定路径：列表变化驱动建行（后续两次提交是模拟用户操作）
    await SettleAsync();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(2);

    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "新群名");
    _dataManager.Settings.TargetGroups[0].DisplayName.Value.ShouldBe("新群名");

    // 改回建行原值同样要写回（基线已在上一笔写回时更新）
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "旧群名");
    _dataManager.Settings.TargetGroups[0].DisplayName.Value.ShouldBe("旧群名");
  }

  [Test]
  public async Task CommitDisplayName_TrimsAndWritesModel()
  {
    _addButton.EmitSignal(BaseButton.SignalName.Pressed);
    // 绑定路径：列表变化驱动建行
    await SettleAsync();

    var input = _groupRows.GetChild(0).GetChild<LineEdit>(2);
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "  示例群  ");

    _dataManager.Settings.TargetGroups[0].DisplayName.Value.ShouldBe("示例群");
  }

  [Test]
  public async Task GroupListResultEvent_RenameUpdatesModelAndRowText()
  {
    _dataManager.Settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = "10001" },
        DisplayName = { Value = "旧群名" },
        Enabled = { Value = true },
      }
    );
    // 绑定路径：列表变化驱动建行（回执处理走真实调度，不手动模拟）
    await SettleAsync();

    _panel.DispatchInfoEvent(
      InfoProtocol.GroupListResultEvent,
      JsonSerializer.Serialize(
        new
        {
          data = new[]
          {
            new
            {
              channelId = "10001",
              guildId = "9001",
              name = "新群名",
            },
          },
        }
      )
    );
    await SettleAsync();

    var group = _dataManager.Settings.TargetGroups[0];
    group.DisplayName.Value.ShouldBe("新群名");
    // 行内展示名由元素级绑定驱动重建：不能停在旧群名上
    _groupRows.GetChildCount().ShouldBe(1);
    _groupRows.GetChild(0).GetChild<LineEdit>(2).Text.ShouldBe("新群名");
    // 群列表刷新不得改动用户勾选
    group.Enabled.Value.ShouldBeTrue();
    FindDialog(_panel).DialogText.ShouldContain("更新 1 个群名");
  }

  [Test]
  public async Task CommitDisplayName_StaleTextKeepsModelButExplicitClearWrites()
  {
    _dataManager.Settings.TargetGroups.Add(
      new TargetGroup { ChannelId = { Value = "10001" }, DisplayName = { Value = "旧群名" } }
    );
    // 绑定路径：列表变化驱动建行（之后的外部模型改动仍按 CallDeferred 延迟重建）
    await SettleAsync();

    // Koishi 回执先改了模型，但行重建是 CallDeferred 的：此刻行内文本仍是旧群名
    _dataManager.Settings.TargetGroups[0].DisplayName.Value = "新群名";
    var input = _groupRows.GetChild(0).GetChild<LineEdit>(2);

    // 文本仍等于建行基线 → stale，不能把回执刚更新成的新群名覆盖回旧名
    input.EmitSignal(LineEdit.SignalName.TextSubmitted, "旧群名");
    _dataManager.Settings.TargetGroups[0].DisplayName.Value.ShouldBe("新群名");

    await SettleAsync();
    _groupRows.GetChild(0).GetChild<LineEdit>(2).Text.ShouldBe("新群名");

    // 文本与基线不同（用户显式清空）→ 视为用户意图，正常写回空
    var cleared = _groupRows.GetChild(0).GetChild<LineEdit>(2);
    cleared.Text = string.Empty;
    cleared.EmitSignal(LineEdit.SignalName.TextSubmitted, string.Empty);

    _dataManager.Settings.TargetGroups[0].DisplayName.Value.ShouldBe(string.Empty);
    await SettleAsync();
    _groupRows.GetChild(0).GetChild<LineEdit>(2).Text.ShouldBe(string.Empty);
  }

  [Test]
  public void OnResolved_MountsRecordingSectionAtEndOfPageBody()
  {
    _panel.RecordingSection.ShouldNotBeNull();
    _pageBody.GetChildCount().ShouldBeGreaterThan(0);
    _pageBody.GetChild(_pageBody.GetChildCount() - 1).ShouldBe(_panel.RecordingSection);
  }

  [Test]
  public void EnsureRecordingSection_IsIdempotent()
  {
    var section = _panel.RecordingSection.ShouldNotBeNull();
    var count = _pageBody.GetChildCount();

    _panel.EnsureRecordingSection().ShouldBeSameAs(section);

    _pageBody.GetChildCount().ShouldBe(count);
  }

  [Test]
  public void RefreshGroups_SendsQueryToKoishi()
  {
    _refreshButton.EmitSignal(BaseButton.SignalName.Pressed);

    _sent.Count.ShouldBe(1);
    _sent[0].Payload.GetProperty("event").GetString().ShouldBe(InfoProtocol.QueryGroupListEvent);
    FindDialog(_panel).DialogText.ShouldContain("已向 Koishi 发起群列表查询");
  }

  [Test]
  public void RefreshGroups_WithoutConnection_ShowsFailureDialog()
  {
    _server.SetupGet(s => s.ConnectionCount).Returns(0);

    _refreshButton.EmitSignal(BaseButton.SignalName.Pressed);

    _sent.ShouldBeEmpty("无连接时不应发出查询");
    FindDialog(_panel).Title.ShouldBe("群列表刷新失败");
  }

  [Test]
  public void GroupListResultEvent_MergesGroupsAndShowsDialog()
  {
    _panel.DispatchInfoEvent(
      InfoProtocol.GroupListResultEvent,
      JsonSerializer.Serialize(
        new
        {
          data = new[]
          {
            new
            {
              channelId = "10001",
              guildId = "9001",
              name = "示例群",
            },
            new
            {
              channelId = "10002",
              guildId = "9001",
              name = "另一群",
            },
          },
        }
      )
    );

    _dataManager.Settings.TargetGroups.Count.ShouldBe(2);
    _dataManager.Settings.TargetGroups[0].DisplayName.Value.ShouldBe("示例群");
    _dataManager.Settings.TargetGroups[0].Enabled.Value.ShouldBeFalse("新并入的群默认不启用");
    FindDialog(_panel).Title.ShouldBe("群列表已更新");
    FindDialog(_panel).DialogText.ShouldContain("新增 2");
  }

  [Test]
  public void DispatchInfoEvent_IgnoresOtherEventsAndMalformedPayload()
  {
    _panel.DispatchInfoEvent(InfoProtocol.PublishResultEvent, "{}");
    _panel.DispatchInfoEvent(InfoProtocol.GroupListResultEvent, "{ not json");

    _dataManager.Settings.TargetGroups.ShouldBeEmpty();
  }

  /// <summary>等两帧让 <c>CallDeferred</c> 排定的重建真正执行（面板不在场景树里，借测试场景的树等帧）。</summary>
  private async Task SettleAsync()
  {
    await _panel.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await _panel.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  /// <summary>取本页懒创建的结果对话框。</summary>
  private static AcceptDialog FindDialog(Node panel)
  {
    var dialog = panel.GetChildren().OfType<AcceptDialog>().LastOrDefault();
    dialog.ShouldNotBeNull("本页应弹出结果对话框");
    return dialog!;
  }
}
