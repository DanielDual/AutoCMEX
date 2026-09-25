namespace AutoCMEX;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 设置页「群聊」类别页的真机用例：加载真实 <c>ChatConfigPanel.tscn</c> 并加入场景树，
/// 用真实控件（SpinBox / LineEdit / Label）跑回写、回显与刷新护栏。
/// </summary>
/// <remarks>
/// 这三件事都依赖 Godot 控件真的会发信号（SpinBox 赋值即发 <c>value_changed</c>），Mock 替身发不出来，
/// 因此单靠 <see cref="TestChatConfigPanel"/> 的替身用例无法覆盖；同时本文件也顺带校验场景确实提供了
/// 面板声明的各个 <c>[Node]</c>（含 <c>%PluginPathLabel</c>）。
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
    _host.AddChild(_panel); // 进树即解析 [Node] 并触发 OnResolved → Refresh
  }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();
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
  public void RealScene_ShowsPluginStatus()
  {
    _panel.PluginPathLabel.Text.ShouldContain("未安装");

    const string dest = @"C:\koishi\plugins\auto-cmex";
    _dm.Settings.KoishiPluginPath.Value = dest;
    _panel.Refresh();

    _panel.PluginPathLabel.Text.ShouldContain(dest);
  }

  [Test]
  public void RealScene_ShowsUrlRowOnlyInClientMode()
  {
    _dm.Settings.WebSocketMode.Value = "Client";
    _panel.Refresh();
    _panel.KoishiUrlRow.Visible.ShouldBeTrue();

    _dm.Settings.WebSocketMode.Value = "Server";
    _panel.Refresh();
    _panel.KoishiUrlRow.Visible.ShouldBeFalse();
  }

  [Test]
  public void UserChangedPort_WritesBackToSettings()
  {
    _panel.PortInput.Value = 5200; // 真机 SpinBox 会就此发出 value_changed

    _dm.Settings.WebSocketPort.Value.ShouldBe(5200);
  }

  [Test]
  public void UserSubmittedKoishiUrl_WritesBackTrimmedValue()
  {
    _panel.KoishiUrlInput.Text = "  ws://localhost:5140  ";

    // 回车提交走真实信号：从场景里取真实 LineEdit 发 text_submitted
    var urlInput = _panel.GetNode<LineEdit>("%KoishiUrlInput");
    urlInput.EmitSignal(LineEdit.SignalName.TextSubmitted, "  ws://localhost:5140  ");

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
    _panel.KoishiUrlInput.Text.ShouldBe("ws://localhost:5140");
  }

  [Test]
  public void UserLeftKoishiUrlField_WritesBackTrimmedValue()
  {
    // 用户输完地址直接点别处而不回车，是同样常见的路径，走的是 focus_exited
    _panel.KoishiUrlInput.Text = "  ws://192.168.1.8:5140  ";

    var urlInput = _panel.GetNode<LineEdit>("%KoishiUrlInput");
    urlInput.EmitSignal(LineEdit.SignalName.FocusExited);

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://192.168.1.8:5140");
    _panel.KoishiUrlInput.Text.ShouldBe("ws://192.168.1.8:5140");
  }

  [Test]
  public void Refresh_DoesNotWriteBackClampedControlValue()
  {
    // 配置里的端口被手改成越界值：真机 SpinBox 赋值时会钳到 65535 并回发 value_changed。
    // 若没有「刷新期间不回写」的护栏，光是打开这一页就会把配置悄悄改成 65535，并连带触发一次自动保存。
    _dm.Settings.WebSocketPort.Value = 65536;

    _panel.Refresh();

    _dm.Settings.WebSocketPort.Value.ShouldBe(65536);
  }
}
