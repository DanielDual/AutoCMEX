namespace AutoCMEX;

using AutoCMEX.Core.WebSocket;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// WebSocket 状态面板的真机用例：加载真实 <c>WebSocketPanel.tscn</c> 并加入场景树，
/// 用真实 Label / ItemList 验证「显示的是实例本身」与连接列表同步。
/// </summary>
/// <remarks>
/// 场景契约（面板声明的 <c>[Node]</c> 是否都在 <c>.tscn</c> 里，含 <c>%ErrorLabel</c>）只有在真实场景上才验得到；
/// <see cref="TestWebSocketPanel"/> 用的是替身节点树，验不到这一层。
/// </remarks>
public class TestWebSocketPanelRuntime : TestClass
{
  private Node _host = default!;
  private MockWebSocketServer _server = default!;
  private WebSocketPanel _panel = default!;

  public TestWebSocketPanelRuntime(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _server = new MockWebSocketServer();

    _host = new Node();
    TestScene.AddChild(_host);

    _panel = GD.Load<PackedScene>("res://src/ui/websocket/WebSocketPanel.tscn")
      .Instantiate<WebSocketPanel>();
    (_panel as IAutoInit).IsTesting = true;
    _panel.FakeDependency<IWebSocketServer>(_server);
    _host.AddChild(_panel); // 进树即解析 [Node] 并触发 OnResolved
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
    _panel.StatusLabel.ShouldNotBeNull();
    _panel.ModeLabel.ShouldNotBeNull();
    _panel.PortLabel.ShouldNotBeNull();
    _panel.ConnectionCountLabel.ShouldNotBeNull();
    _panel.EventLabel.ShouldNotBeNull();
    _panel.ErrorLabel.ShouldNotBeNull();
    _panel.StartStopBtn.ShouldNotBeNull();
    _panel.ClientListLabel.ShouldNotBeNull();
    _panel.ClientList.ShouldNotBeNull();
    _panel.RefreshTimer.ShouldNotBeNull();
  }

  [Test]
  public void RealScene_ShowsActualServerInstance()
  {
    _server.Mode = "Server";
    _server.Port = 5200;
    _server.IsRunning = true;
    _server.ConnectionCount = 3;
    _panel.UpdateServer(_server);

    _panel.ModeLabel.Text.ShouldContain("Server");
    _panel.PortLabel.Text.ShouldContain("5200");
    _panel.ConnectionCountLabel.Text.ShouldContain("3");
    _panel.ClientListLabel.Text.ShouldBe("已连接客户端:");
    _panel.StartStopBtn.Text.ShouldBe("停止");
    _panel.ErrorLabel.Visible.ShouldBeFalse();
  }

  [Test]
  public void RealScene_ClientFailure_ShowsReasonOnErrorLabel()
  {
    _server.Mode = "Client";
    _server.Url = "ws://localhost:5140/?token=abc";
    _server.LastError = "未配置 Koishi 地址";
    _panel.UpdateServer(_server);

    _panel.ModeLabel.Text.ShouldContain("Client");
    _panel.PortLabel.Text.ShouldContain("ws://localhost:5140/?token=abc");
    _panel.ClientListLabel.Text.ShouldBe("连接的对端:");
    _panel.StartStopBtn.Text.ShouldBe("连接");
    _panel.ErrorLabel.Text.ShouldBe("未配置 Koishi 地址");
    _panel.ErrorLabel.Visible.ShouldBeTrue();

    // Client 只有一条链路，连接数与状态行重复，整行隐藏（而不是留一行没有前缀的裸文本）
    _panel.ConnectionCountLabel.Visible.ShouldBeFalse();
  }

  [Test]
  public void RealScene_ServerMode_ShowsConnectionCountRow()
  {
    _server.Mode = "Client";
    _panel.UpdateServer(_server);
    _panel.ConnectionCountLabel.Visible.ShouldBeFalse();

    _server.Mode = "Server";
    _server.IsRunning = true;
    _server.ConnectionCount = 2;
    _panel.UpdateServer(_server);

    _panel.ConnectionCountLabel.Visible.ShouldBeTrue();
    _panel.ConnectionCountLabel.Text.ShouldBe("连接数: 2");
  }

  [Test]
  public void RealScene_TracksClientList()
  {
    // 用面板自己的刷新定时器推进（即产品里每秒一次的那条路径）；
    // 不能拿 UpdateServer 当「立刻刷新」用——换实例时它会按设计丢弃在途事件与旧列表。
    _server.IsRunning = true;
    var refreshTimer = _panel.GetNode<Timer>("%RefreshTimer");

    refreshTimer.EmitSignal(Timer.SignalName.Timeout);
    _panel.ClientList.ItemCount.ShouldBe(0);

    _server.RaiseClientConnected("client-1");
    refreshTimer.EmitSignal(Timer.SignalName.Timeout);
    _panel.ClientList.ItemCount.ShouldBe(1);

    _server.RaiseClientConnected("client-2");
    refreshTimer.EmitSignal(Timer.SignalName.Timeout);
    _panel.ClientList.ItemCount.ShouldBe(2);

    _server.RaiseClientDisconnected("client-1");
    refreshTimer.EmitSignal(Timer.SignalName.Timeout);
    _panel.ClientList.ItemCount.ShouldBe(1);
  }
}
