namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 群聊配置面板的替身单测：端口提交式去抖、其余控件回写、
/// 设置值绑定驱动的回填、回填护栏与「切页不写回」纪律。
/// </summary>
public class TestChatConfigPanel : TestClass
{
  private ChatConfigPanel _panel = default!;
  private DataManager _dm = default!;
  private Mock<ISpinBox> _portInput = default!;
  private Mock<IOptionButton> _modeSelect = default!;
  private Mock<ILineEdit> _koishiUrlInput = default!;
  private Mock<IHBoxContainer> _koishiUrlRow = default!;
  private Mock<IOptionButton> _filterSelect = default!;
  private Mock<ILabel> _pluginPathLabel = default!;
  private readonly List<Node> _toCleanup = new();

  public TestChatConfigPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new ChatConfigPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    _portInput = new Mock<ISpinBox>();
    _portInput.SetupProperty(m => m.MinValue);
    _portInput.SetupProperty(m => m.MaxValue);
    _modeSelect = new Mock<IOptionButton>();
    _modeSelect.SetupProperty(m => m.ItemCount, 0);
    _modeSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback(() => _modeSelect.Object.ItemCount++);
    _koishiUrlInput = new Mock<ILineEdit>();
    _koishiUrlRow = new Mock<IHBoxContainer>();
    _filterSelect = new Mock<IOptionButton>();
    _filterSelect.SetupProperty(m => m.ItemCount, 0);
    _filterSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback(() => _filterSelect.Object.ItemCount++);
    var installBtn = new Mock<IButton>();
    _pluginPathLabel = new Mock<ILabel>();
    var pluginFileDialog = new Mock<IFileDialog>();
    var pluginOkDialog = new Mock<IAcceptDialog>();

    _panel.FakeNodeTree(
      new()
      {
        ["%PortInput"] = _portInput.Object,
        ["%ModeSelect"] = _modeSelect.Object,
        ["%KoishiUrlInput"] = _koishiUrlInput.Object,
        ["%KoishiUrlRow"] = _koishiUrlRow.Object,
        ["%FilterSelect"] = _filterSelect.Object,
        ["%InstallBtn"] = installBtn.Object,
        ["%PluginPathLabel"] = _pluginPathLabel.Object,
        ["%PluginFileDialog"] = pluginFileDialog.Object,
        ["%PluginOkDialog"] = pluginOkDialog.Object,
      }
    );

    _panel.FakeDependency<DataManager>(_dm);
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }
    _toCleanup.Clear();
  }

  /// <summary>等引擎把本帧排队的 <c>CallDeferred</c> 回填执行完（规则 30：绑定回调后要等帧再断言）。</summary>
  private async Task WaitRefillAsync()
  {
    await _panel.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await _panel.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  /// <summary>为五个设置项各挂一个写入计数绑定（订阅即回放一次，调用方随后清零）。</summary>
  private List<IDisposable> CountSettingWrites(Action onWrite) =>
    new()
    {
      _dm.Settings.WebSocketPort.Bind().OnValue(_ => onWrite()),
      _dm.Settings.WebSocketMode.Bind().OnValue(_ => onWrite()),
      _dm.Settings.MessageFilterMode.Bind().OnValue(_ => onWrite()),
      _dm.Settings.KoishiWebSocketUrl.Bind().OnValue(_ => onWrite()),
      _dm.Settings.KoishiPluginPath.Bind().OnValue(_ => onWrite()),
    };

  /// <summary>释放计数绑定。</summary>
  private static void DisposeAll(List<IDisposable> bindings)
  {
    foreach (var binding in bindings)
      binding.Dispose();

    bindings.Clear();
  }

  [Test]
  public void Panel_IsNotNull()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void PortInput_HasCorrectRange()
  {
    _panel.PortInput.MinValue.ShouldBe(1);
    _panel.PortInput.MaxValue.ShouldBe(65535);
  }

  [Test]
  public void ModeSelect_HasTwoOptions()
  {
    _panel.ModeSelect.ItemCount.ShouldBe(2);
  }

  [Test]
  public void FilterSelect_HasThreeOptions()
  {
    _panel.FilterSelect.ItemCount.ShouldBe(3);
  }

  [Test]
  public void InstallBtn_IsNotNull()
  {
    _panel.InstallBtn.ShouldNotBeNull();
  }

  [Test]
  public async Task ExternalSettingsChange_RefillsEveryControlWithoutWritingBack()
  {
    var writes = 0;
    var bindings = CountSettingWrites(() => writes++);
    writes = 0;

    try
    {
      _dm.Settings.WebSocketPort.Value = 5200;
      _dm.Settings.WebSocketMode.Value = "Client";
      _dm.Settings.MessageFilterMode.Value = "ai";
      _dm.Settings.KoishiWebSocketUrl.Value = "ws://localhost:5140";
      _dm.Settings.KoishiPluginPath.Value = @"koishi\external\auto-cmex";

      await WaitRefillAsync();

      _portInput.VerifySet(m => m.Value = 5200);
      _modeSelect.Verify(m => m.Select(1), Times.AtLeastOnce);
      _filterSelect.Verify(m => m.Select(1), Times.AtLeastOnce);
      _koishiUrlRow.VerifySet(m => m.Visible = true);
      _koishiUrlInput.VerifySet(m => m.Text = "ws://localhost:5140");
      _pluginPathLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("auto-cmex")));

      // 回填是程序化写控件：不许反向写回设置（否则只是打开本页就触发一次自动保存）
      writes.ShouldBe(5);
    }
    finally
    {
      DisposeAll(bindings);
    }
  }

  [Test]
  public async Task ExternalModeChange_ShowsUrlRowOnlyInClientMode()
  {
    _dm.Settings.WebSocketMode.Value = "Server";
    await WaitRefillAsync();
    _koishiUrlRow.VerifySet(m => m.Visible = false);

    _dm.Settings.WebSocketMode.Value = "Client";
    await WaitRefillAsync();
    _koishiUrlRow.VerifySet(m => m.Visible = true);
  }

  /// <summary>
  /// 端口连改只写一次：真机 SpinBox 每次改值都同步发 <c>value_changed</c>，
  /// 逐次写模型会让 MainWindow 每 tick 重启一次 WebSocket 并重置保存防抖。
  /// </summary>
  [Test]
  public async Task PortChanges_AreDebouncedIntoOneWrite()
  {
    var writes = 0;
    var binding = _dm.Settings.WebSocketPort.Bind().OnValue(_ => writes++);
    writes = 0;

    try
    {
      _portInput.Raise(m => m.ValueChanged += null, 5200d);
      _portInput.Raise(m => m.ValueChanged += null, 5201d);
      _portInput.Raise(m => m.ValueChanged += null, 5202d);

      // 去抖窗口内不写模型 → 不触发 WebSocket 重启、不重置保存防抖
      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);
      writes.ShouldBe(0);

      // 替身面板不入树，引擎不会驱动 _Process，手工喂 delta（同 GifFramePlayerTest 的做法）
      _panel._Process(ChatConfigPanel.PortCommitDebounceSeconds / 2);
      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);
      writes.ShouldBe(0);

      _panel._Process(ChatConfigPanel.PortCommitDebounceSeconds);
      _dm.Settings.WebSocketPort.Value.ShouldBe(5202);
      writes.ShouldBe(1);

      // 写回后绑定排的回填不许再写第二笔
      await WaitRefillAsync();
      writes.ShouldBe(1);
    }
    finally
    {
      binding.Dispose();
    }
  }

  [Test]
  public void PortChange_EqualToCurrentValue_DoesNotWrite()
  {
    var writes = 0;
    var binding = _dm.Settings.WebSocketPort.Bind().OnValue(_ => writes++);
    writes = 0;

    try
    {
      _portInput.Raise(m => m.ValueChanged += null, 5140d);
      _panel._Process(ChatConfigPanel.PortCommitDebounceSeconds);

      writes.ShouldBe(0);
    }
    finally
    {
      binding.Dispose();
    }
  }

  [Test]
  public async Task PortChange_IsClampedBeforeWriteBack()
  {
    _portInput.Raise(m => m.ValueChanged += null, 70000d);
    _panel._Process(ChatConfigPanel.PortCommitDebounceSeconds);

    _dm.Settings.WebSocketPort.Value.ShouldBe(65535);

    await WaitRefillAsync();
  }

  [Test]
  public async Task ModeSelect_UserDriven_WritesBackAndShowsUrlRow()
  {
    _modeSelect.Raise(m => m.ItemSelected += null, 1L);

    _dm.Settings.WebSocketMode.Value.ShouldBe("Client");
    _koishiUrlRow.VerifySet(m => m.Visible = true);

    // 写模型后绑定会排一次回填：等它跑完，顺带确保回填不反向写回（否则会产生第二笔写）
    var writes = 0;
    var binding = _dm.Settings.WebSocketMode.Bind().OnValue(_ => writes++);
    writes = 0;
    try
    {
      await WaitRefillAsync();
      writes.ShouldBe(0);
    }
    finally
    {
      binding.Dispose();
    }
  }

  [Test]
  public async Task FilterSelect_UserDriven_WritesBack()
  {
    _filterSelect.Raise(m => m.ItemSelected += null, 2L);

    _dm.Settings.MessageFilterMode.Value.ShouldBe("strict_then_ai");

    await WaitRefillAsync();
    _dm.Settings.MessageFilterMode.Value.ShouldBe("strict_then_ai");
  }

  [Test]
  public async Task KoishiUrl_TextSubmitted_WritesBackTrimmedValue()
  {
    _koishiUrlInput.Raise(m => m.TextSubmitted += null, "  ws://localhost:5140  ");

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
    _koishiUrlInput.VerifySet(m => m.Text = "ws://localhost:5140");

    await WaitRefillAsync();
    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
  }

  [Test]
  public async Task KoishiUrl_TextSubmitted_KeepsUnchangedValue()
  {
    _dm.Settings.KoishiWebSocketUrl.Value = "ws://localhost:5140";

    _koishiUrlInput.Raise(m => m.TextSubmitted += null, "ws://localhost:5140");

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");

    await WaitRefillAsync();
    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
  }

  [Test]
  public async Task PluginPathLabel_ShowsNotInstalled_ByDefault()
  {
    await WaitRefillAsync();

    _pluginPathLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("未安装")));
  }

  [Test]
  public async Task PluginPathLabel_ShowsInstalledPath()
  {
    const string dest = @"koishi\plugins\auto-cmex";
    _dm.Settings.KoishiPluginPath.Value = dest;

    await WaitRefillAsync();

    _pluginPathLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains(dest)));
  }

  /// <summary>
  /// 切页（隐藏再显示）不该产生任何写回/保存：控件由绑定持续对齐，切页不再手动刷新，
  /// 回填期间的伪变更也被 <c>_applyingModel</c> 挡掉。
  /// </summary>
  [Test]
  public async Task PageSwitch_DoesNotWriteBackToSettings()
  {
    var writes = 0;
    var bindings = CountSettingWrites(() => writes++);
    writes = 0;

    try
    {
      _panel.Visible = false;
      _panel.Visible = true;

      await WaitRefillAsync();

      writes.ShouldBe(0);
      _dm.Settings.WebSocketMode.Value.ShouldBe("Server");
      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);
    }
    finally
    {
      DisposeAll(bindings);
    }
  }

  /// <summary>
  /// 去抖窗口内退出（关窗）：待提交的端口必须在退出前写回模型，交给退出落盘；
  /// 写回只发生一次，且退出路径不排自动保存（落盘统一由 DataManager.Dispose 负责）。
  /// </summary>
  [Test]
  public void ExitTree_CommitsPendingPortToModel()
  {
    var writes = 0;
    var binding = _dm.Settings.WebSocketPort.Bind().OnValue(_ => writes++);
    writes = 0;

    try
    {
      _portInput.Raise(m => m.ValueChanged += null, 5210d);

      // 去抖窗口还没到：此刻模型仍是旧端口，直接关窗会丢这一笔（本条用例要防的就是它）
      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);

      // 替身面板不入树，引擎不会替我们发退出通知，这里手工触发
      _panel._Notification((int)Node.NotificationExitTree);

      _dm.Settings.WebSocketPort.Value.ShouldBe(5210);
      writes.ShouldBe(1);
    }
    finally
    {
      binding.Dispose();
    }
  }

  [Test]
  public void ExitTree_IsIdempotent()
  {
    var writes = 0;
    var binding = _dm.Settings.WebSocketPort.Bind().OnValue(_ => writes++);
    writes = 0;

    try
    {
      _portInput.Raise(m => m.ValueChanged += null, 5220d);

      _panel._Notification((int)Node.NotificationExitTree);
      _panel._Notification((int)Node.NotificationExitTree); // 重复退出通知

      _dm.Settings.WebSocketPort.Value.ShouldBe(5220);
      writes.ShouldBe(1);
    }
    finally
    {
      binding.Dispose();
    }
  }

  /// <summary>没有待提交端口时退出：不能凭空写一笔配置。</summary>
  [Test]
  public void ExitTree_WithoutPendingPort_DoesNotWrite()
  {
    var writes = 0;
    var binding = _dm.Settings.WebSocketPort.Bind().OnValue(_ => writes++);
    writes = 0;

    try
    {
      _panel._Notification((int)Node.NotificationExitTree);

      writes.ShouldBe(0);
      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);
    }
    finally
    {
      binding.Dispose();
    }
  }

  /// <summary>
  /// 退出后绑定已释放、排队中的回填不再执行：外部再改配置既不该回填控件，也不该把状态写回去。
  /// </summary>
  [Test]
  public async Task ExitTree_StopsRefillingControls()
  {
    // 退出前先排一次回填（模型变更 → CallDeferred），它会在退出之后才执行
    _dm.Settings.WebSocketMode.Value = "Client";

    _panel._Notification((int)Node.NotificationExitTree);

    await WaitRefillAsync();

    // 排队中的回填被退出护栏挡掉：控件保持退出前的对齐结果
    _modeSelect.Verify(m => m.Select(1), Times.Never);
    _koishiUrlRow.VerifySet(m => m.Visible = true, Times.Never);

    // 退出后外部再改配置：绑定已释放，不排回填、不写回
    _dm.Settings.WebSocketPort.Value = 5300;
    _dm.Settings.WebSocketMode.Value = "Server";

    await WaitRefillAsync();

    _portInput.VerifySet(m => m.Value = 5300, Times.Never);
    _dm.Settings.WebSocketPort.Value.ShouldBe(5300);
  }
}
