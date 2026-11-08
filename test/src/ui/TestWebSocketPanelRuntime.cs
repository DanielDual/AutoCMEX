namespace AutoCMEX;

using System.Threading.Tasks;
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

  /// <summary>
  /// 状态事件驱动刷新：状态在「没有连接事件、也没人点按钮」的情况下变化时，
  /// 面板必须靠 <see cref="IWebSocketServer.OnStateChanged"/> 立刻更新，而不是等 1Hz 兜底轮询。
  /// </summary>
  [Test]
  public async Task RealScene_StateEventDrivesRefresh()
  {
    _server.Mode = "Server";
    _server.Port = 5200;
    _server.IsRunning = true;
    _panel.UpdateServer(_server);
    _panel.StatusLabel.Text.ShouldBe("运行中");

    // 监听失败：实例状态与错误原因都变了，但没有连接/断开事件可依赖（1Hz 定时器一帧内也不会到点）
    _server.IsRunning = false;
    _server.IsActive = false;
    _server.LastError = "端口 5200 监听失败：拒绝访问";
    _server.RaiseStateChanged();

    // 事件处理器只排队：等 deferred 刷新在主线程落地（两帧兜住 deferred 队列的刷出时机）
    await _panel.ToSignal(_panel.GetTree(), SceneTree.SignalName.ProcessFrame);
    await _panel.ToSignal(_panel.GetTree(), SceneTree.SignalName.ProcessFrame);

    _panel.StatusLabel.Text.ShouldBe("已停止");
    _panel.StartStopBtn.Text.ShouldBe("启动");
    _panel.ErrorLabel.Visible.ShouldBeTrue();
    _panel.ErrorLabel.Text.ShouldBe("端口 5200 监听失败：拒绝访问");
  }

  /// <summary>
  /// 同一实例重复下发不是「换实例」：连接列表与事件订阅都要保留。
  /// </summary>
  /// <remarks>
  /// 旧实现无条件清空内部连接列表：列表 UI 因「空 == 空」不重建而残留旧条目，
  /// 下一次连接又先清空再写入，看起来像是「同实例重启后只显示最后一条连接」。
  /// </remarks>
  [Test]
  public void RealScene_UpdateServer_SameInstanceKeepsConnectionsAndSingleSubscription()
  {
    var refreshTimer = _panel.GetNode<Timer>("%RefreshTimer");

    _server.IsRunning = true;
    _panel.UpdateServer(_server);
    _server.RaiseClientConnected("client-1");
    refreshTimer.EmitSignal(Timer.SignalName.Timeout);
    _panel.ClientList.ItemCount.ShouldBe(1);

    // 重启流程把未重建的同一实例再交一次
    _panel.UpdateServer(_server);

    _panel.ClientList.ItemCount.ShouldBe(1, "同实例不得清空已连接的客户端列表");
    _server.StateChangedSubscriberCount.ShouldBe(1, "同实例不得重复订阅状态事件");

    // 后续连接继续累加，而不是把旧连接挤掉
    _server.RaiseClientConnected("client-2");
    refreshTimer.EmitSignal(Timer.SignalName.Timeout);
    _panel.ClientList.ItemCount.ShouldBe(2);
  }

  /// <summary>换实例：旧实例的连接与订阅都要丢弃，新实例的状态事件要能驱动面板。</summary>
  [Test]
  public async Task RealScene_UpdateServer_DifferentInstanceRewiresStateEvents()
  {
    var refreshTimer = _panel.GetNode<Timer>("%RefreshTimer");

    _server.IsRunning = true;
    _panel.UpdateServer(_server);
    _server.RaiseClientConnected("client-1");
    refreshTimer.EmitSignal(Timer.SignalName.Timeout);
    _panel.ClientList.ItemCount.ShouldBe(1);

    var next = new MockWebSocketServer
    {
      Mode = "Server",
      IsRunning = false,
      LastError = "启动失败",
    };
    _panel.UpdateServer(next);

    _panel.ClientList.ItemCount.ShouldBe(0, "换实例后旧实例的连接不得留在列表里");
    _panel.ErrorLabel.Text.ShouldBe("启动失败");
    _server.StateChangedSubscriberCount.ShouldBe(0, "旧实例必须退订状态事件");
    next.StateChangedSubscriberCount.ShouldBe(1, "新实例必须订阅状态事件");

    // 新实例的状态事件照常驱动刷新
    next.IsRunning = true;
    next.LastError = string.Empty;
    next.RaiseStateChanged();

    await _panel.ToSignal(_panel.GetTree(), SceneTree.SignalName.ProcessFrame);
    await _panel.ToSignal(_panel.GetTree(), SceneTree.SignalName.ProcessFrame);

    _panel.StatusLabel.Text.ShouldBe("运行中");
    _panel.StartStopBtn.Text.ShouldBe("停止");
    _panel.ErrorLabel.Visible.ShouldBeFalse();
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
