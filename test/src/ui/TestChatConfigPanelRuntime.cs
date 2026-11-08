namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 设置页「群聊」类别页的真机用例：加载真实 <c>ChatConfigPanel.tscn</c> 并加入场景树，
/// 用真实控件（SpinBox / LineEdit / Label）跑回写、回填与回填护栏。
/// </summary>
/// <remarks>
/// 这些行为依赖 Godot 控件真的会发信号（SpinBox 赋值即发 <c>value_changed</c> 且会钳位、
/// LineEdit 发 <c>text_submitted</c>/<c>focus_exited</c>），Mock 替身发不出来，
/// 因此单靠 <see cref="TestChatConfigPanel"/> 的替身用例无法覆盖；同时本文件也顺带校验
/// 场景确实提供了面板声明的各个 <c>[Node]</c>（含 <c>%PluginPathLabel</c>）。
/// </remarks>
public class TestChatConfigPanelRuntime : TestClass
{
  private Node _host = default!;
  private DataManager _dm = default!;
  private ChatConfigPanel _panel = default!;

  public TestChatConfigPanelRuntime(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _host = new Node();
    TestScene.AddChild(_host);

    _panel = GD.Load<PackedScene>("res://src/ui/settings/ChatConfigPanel.tscn")
      .Instantiate<ChatConfigPanel>();
    (_panel as IAutoInit).IsTesting = true;
    _panel.FakeDependency<DataManager>(_dm);
    _host.AddChild(_panel); // 进树即解析 [Node] 并触发 OnResolved → 绑定回填
  }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();
  }

  /// <summary>等两帧，让绑定排队的回填（<c>CallDeferred</c>）执行完（规则 30）。</summary>
  private static async Task WaitRefillAsync(Node node)
  {
    await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  /// <summary>等真实时间流逝（端口去抖按帧 delta 累计，用真实等待而不是固定帧数）。</summary>
  private static async Task WaitSecondsAsync(Node node, double seconds) =>
    await node.ToSignal(node.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

  /// <summary>为三个会触发 WebSocket 重启的设置项各挂一个写入计数绑定（订阅即回放一次，调用方随后清零）。</summary>
  private List<IDisposable> CountSettingWrites(Action onWrite) =>
    new()
    {
      _dm.Settings.WebSocketPort.Bind().OnValue(_ => onWrite()),
      _dm.Settings.WebSocketMode.Bind().OnValue(_ => onWrite()),
      _dm.Settings.KoishiWebSocketUrl.Bind().OnValue(_ => onWrite()),
    };

  /// <summary>释放计数绑定。</summary>
  private static void DisposeAll(List<IDisposable> bindings)
  {
    foreach (var binding in bindings)
      binding.Dispose();

    bindings.Clear();
  }

  [Test]
  public void RealScene_ResolvesEveryNode()
  {
    _panel.PortInput.ShouldNotBeNull();
    _panel.PortInput.MinValue.ShouldBe(1);
    _panel.PortInput.MaxValue.ShouldBe(65535);
    _panel.ModeSelect.ShouldNotBeNull();
    _panel.KoishiUrlInput.ShouldNotBeNull();
    _panel.KoishiUrlRow.ShouldNotBeNull();
    _panel.FilterSelect.ShouldNotBeNull();
    _panel.PluginPathLabel.ShouldNotBeNull();
  }

  [Test]
  public async Task RealScene_ShowsPluginStatus()
  {
    _panel.PluginPathLabel.Text.ShouldContain("未安装");

    const string dest = @"koishi\plugins\auto-cmex";
    _dm.Settings.KoishiPluginPath.Value = dest;

    await WaitRefillAsync(_panel);

    _panel.PluginPathLabel.Text.ShouldContain(dest);
  }

  [Test]
  public async Task RealScene_ShowsUrlRowOnlyInClientMode()
  {
    _dm.Settings.WebSocketMode.Value = "Client";
    await WaitRefillAsync(_panel);
    _panel.KoishiUrlRow.Visible.ShouldBeTrue();

    _dm.Settings.WebSocketMode.Value = "Server";
    await WaitRefillAsync(_panel);
    _panel.KoishiUrlRow.Visible.ShouldBeFalse();
  }

  /// <summary>
  /// 端口连改只写一次（= 只重置一次保存防抖、只让 MainWindow 重启一次 WebSocket）。
  /// </summary>
  [Test]
  public async Task UserChangedPort_WritesBackOnceAfterDebounce()
  {
    await WaitRefillAsync(_panel); // 先让首次回填把手动改的值对齐到控件

    var writes = 0;
    var binding = _dm.Settings.WebSocketPort.Bind().OnValue(_ => writes++);
    writes = 0;

    try
    {
      // 真机 SpinBox 每次改值都同步发 value_changed：连改三次只应产生一次写回
      _panel.PortInput.Value = 5200;
      _panel.PortInput.Value = 5201;
      _panel.PortInput.Value = 5202;

      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);
      writes.ShouldBe(0);

      await WaitSecondsAsync(_panel, ChatConfigPanel.PortCommitDebounceSeconds + 0.2);

      _dm.Settings.WebSocketPort.Value.ShouldBe(5202);
      writes.ShouldBe(1);

      // 写回后绑定排的回填要跑完（也避免带着未执行的 deferred 回调进 Cleanup），且不许再写第二笔
      await WaitRefillAsync(_panel);
      writes.ShouldBe(1);
    }
    finally
    {
      binding.Dispose();
    }
  }

  /// <summary>
  /// 外部改配置只回填控件、不回写设置：真机 SpinBox 被赋越界值时会钳位并同步回发
  /// <c>value_changed</c>，若没有 <c>_applyingModel</c> 护栏，打开一次本页就会把配置悄悄改掉。
  /// </summary>
  [Test]
  public async Task ModelRefill_DoesNotWriteBack()
  {
    await WaitRefillAsync(_panel);

    var writes = 0;
    var bindings = CountSettingWrites(() => writes++);
    writes = 0;

    try
    {
      // 用户手打字但还没提交（没回车/没失焦）——模拟真机上正在编辑的地址框
      _panel.KoishiUrlInput.Text = "  ws://typed-but-not-submitted  ";

      _dm.Settings.WebSocketPort.Value = 65536; // 越界：真机 SpinBox 回填时钳到 65535 并回发
      _dm.Settings.WebSocketMode.Value = "Client";
      _dm.Settings.KoishiWebSocketUrl.Value = "ws://localhost:9999";

      await WaitRefillAsync(_panel);
      // 钳位值若被写回，去抖窗口（300ms）结束后模型会变成 65535，多等一会儿把它暴露出来
      await WaitSecondsAsync(_panel, ChatConfigPanel.PortCommitDebounceSeconds + 0.2);

      _dm.Settings.WebSocketPort.Value.ShouldBe(65536);
      _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:9999");
      writes.ShouldBe(3); // 只有外部那三笔；回填本身一笔都不写
    }
    finally
    {
      DisposeAll(bindings);
    }
  }

  [Test]
  public async Task UserChangedMode_ShowsUrlRow()
  {
    var modeSelect = _panel.GetNode<OptionButton>("%ModeSelect");
    modeSelect.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);

    _dm.Settings.WebSocketMode.Value.ShouldBe("Client");
    _panel.KoishiUrlRow.Visible.ShouldBeTrue();

    // 写模型后绑定排的回填要跑完，且不许把状态写回去
    await WaitRefillAsync(_panel);
    _dm.Settings.WebSocketMode.Value.ShouldBe("Client");
  }

  [Test]
  public async Task UserSubmittedKoishiUrl_WritesBackTrimmedValue()
  {
    _panel.KoishiUrlInput.Text = "  ws://localhost:5140  ";

    // 回车提交走真实信号：从场景里取真实 LineEdit 发 text_submitted
    var urlInput = _panel.GetNode<LineEdit>("%KoishiUrlInput");
    urlInput.EmitSignal(LineEdit.SignalName.TextSubmitted, "  ws://localhost:5140  ");

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
    _panel.KoishiUrlInput.Text.ShouldBe("ws://localhost:5140");

    await WaitRefillAsync(_panel);
    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
  }

  [Test]
  public async Task UserLeftKoishiUrlField_WritesBackTrimmedValue()
  {
    // 用户输完地址直接点别处而不回车，是同样常见的路径，走的是 focus_exited
    _panel.KoishiUrlInput.Text = "  ws://192.0.2.10:5140  ";

    var urlInput = _panel.GetNode<LineEdit>("%KoishiUrlInput");
    urlInput.EmitSignal(LineEdit.SignalName.FocusExited);

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://192.0.2.10:5140");
    _panel.KoishiUrlInput.Text.ShouldBe("ws://192.0.2.10:5140");

    await WaitRefillAsync(_panel);
    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://192.0.2.10:5140");
  }

  /// <summary>切页（隐藏再显示）不产生写回/保存：控件由绑定持续对齐，切页不再手动刷新。</summary>
  [Test]
  public async Task PageSwitch_DoesNotWriteBack()
  {
    await WaitRefillAsync(_panel);

    var writes = 0;
    var bindings = CountSettingWrites(() => writes++);
    writes = 0;

    try
    {
      _panel.Visible = false;
      _panel.Visible = true;

      await WaitRefillAsync(_panel);

      writes.ShouldBe(0);
      _dm.Settings.WebSocketMode.Value.ShouldBe("Server");
    }
    finally
    {
      DisposeAll(bindings);
    }
  }

  /// <summary>
  /// 改端口后立即退出（不等 300ms 去抖）：面板离开场景树时必须把这笔编辑写回模型，
  /// 交给退出路径的落盘（DataManager.Dispose）；写回只发生一次，也不排自动保存。
  /// </summary>
  [Test]
  public async Task PanelLeavingTree_CommitsPendingPort()
  {
    await WaitRefillAsync(_panel); // 先让首次回填把手动改的值对齐到控件

    var writes = 0;
    var binding = _dm.Settings.WebSocketPort.Bind().OnValue(_ => writes++);
    writes = 0;

    try
    {
      _panel.PortInput.Value = 5230; // 真机 SpinBox 同步发 value_changed，进入去抖窗口

      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);

      // 不等去抖：立刻把面板移出场景树（关窗时也是子节点先离树，顺序与此一致）
      _host.RemoveChild(_panel);
      _panel.QueueFree();

      await WaitRefillAsync(_host);

      _dm.Settings.WebSocketPort.Value.ShouldBe(5230);
      writes.ShouldBe(1);
    }
    finally
    {
      binding.Dispose();
    }
  }

  /// <summary>没有待提交端口时退出：不能因为退出凭空写一笔配置。</summary>
  [Test]
  public async Task PanelLeavingTree_WithoutPendingPort_DoesNotWrite()
  {
    await WaitRefillAsync(_panel);

    var writes = 0;
    var bindings = CountSettingWrites(() => writes++);
    writes = 0;

    try
    {
      _host.RemoveChild(_panel);
      _panel.QueueFree();

      await WaitRefillAsync(_host);

      writes.ShouldBe(0);
      _dm.Settings.WebSocketPort.Value.ShouldBe(5140);
    }
    finally
    {
      DisposeAll(bindings);
    }
  }
}
