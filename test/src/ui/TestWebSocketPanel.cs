namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class TestWebSocketPanel : TestClass
{
  private WebSocketPanel _panel = default!;
  private Mock<ILabel> _modeLabel = default!;
  private Mock<ILabel> _portLabel = default!;
  private Mock<ILabel> _connCountLabel = default!;
  private Mock<ILabel> _errorLabel = default!;
  private Mock<IButton> _startStopBtn = default!;
  private Mock<ILabel> _clientListLabel = default!;
  private Mock<IItemList> _clientList = default!;
  private Mock<ITimer> _refreshTimer = default!;
  private readonly List<Node> _toCleanup = new();

  public TestWebSocketPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new WebSocketPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    var statusLabel = new Mock<ILabel>();
    _modeLabel = new Mock<ILabel>();
    _portLabel = new Mock<ILabel>();
    _connCountLabel = new Mock<ILabel>();
    var eventLabel = new Mock<ILabel>();
    _errorLabel = new Mock<ILabel>();
    _startStopBtn = new Mock<IButton>();
    _clientListLabel = new Mock<ILabel>();
    _clientList = new Mock<IItemList>();
    _refreshTimer = new Mock<ITimer>();

    _panel.FakeNodeTree(
      new()
      {
        ["%StatusLabel"] = statusLabel.Object,
        ["%ModeLabel"] = _modeLabel.Object,
        ["%PortLabel"] = _portLabel.Object,
        ["%ConnectionCountLabel"] = _connCountLabel.Object,
        ["%EventLabel"] = eventLabel.Object,
        ["%ErrorLabel"] = _errorLabel.Object,
        ["%StartStopBtn"] = _startStopBtn.Object,
        ["%ClientListLabel"] = _clientListLabel.Object,
        ["%ClientList"] = _clientList.Object,
        ["%RefreshTimer"] = _refreshTimer.Object,
      }
    );

    _panel.FakeDependency<IWebSocketServer>(new MockWebSocketServer());
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

  /// <summary>触发面板的周期刷新（面板每秒同步一次）。</summary>
  private void Refresh() => _refreshTimer.Raise(t => t.Timeout += null);

  [Test]
  public void Panel_IsNotNull()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void StatusLabel_IsNotNull()
  {
    _panel.StatusLabel.ShouldNotBeNull();
  }

  [Test]
  public void StartStopBtn_IsNotNull()
  {
    _panel.StartStopBtn.ShouldNotBeNull();
  }

  [Test]
  public void ModeLabel_IsNotNull()
  {
    _panel.ModeLabel.ShouldNotBeNull();
  }

  [Test]
  public void UpdateServer_WithServer_SetsModeLabel()
  {
    var mockServer = new MockWebSocketServer { Mode = "Server" };
    _panel.UpdateServer(mockServer);
    _modeLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("Server")));
  }

  [Test]
  public void UpdateServer_ClientMode_SetsModeLabel()
  {
    var mockServer = new MockWebSocketServer { Mode = "Client" };
    _panel.UpdateServer(mockServer);
    _modeLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("Client")));
  }

  [Test]
  public void PortLabel_ServerMode_ShowsActualPort()
  {
    var mockServer = new MockWebSocketServer { Mode = "Server", Port = 5200 };
    _panel.UpdateServer(mockServer);
    _portLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("5200")));
  }

  [Test]
  public void PortLabel_ClientMode_ShowsActualUrl()
  {
    var mockServer = new MockWebSocketServer
    {
      Mode = "Client",
      Url = "ws://localhost:5140/?token=abc",
    };
    _panel.UpdateServer(mockServer);
    _portLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("ws://localhost:5140")));
  }

  [Test]
  public void ConnectionCountLabel_ServerMode_ShowsCount()
  {
    var mockServer = new MockWebSocketServer { Mode = "Server", IsRunning = true };
    _panel.UpdateServer(mockServer);
    _connCountLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("连接数")));
  }

  [Test]
  public void ErrorLabel_ShowsLastError()
  {
    var mockServer = new MockWebSocketServer { Mode = "Client", LastError = "未配置 Koishi 地址" };
    _panel.UpdateServer(mockServer);

    _errorLabel.VerifySet(m => m.Text = "未配置 Koishi 地址");
    _errorLabel.VerifySet(m => m.Visible = true);
  }

  [Test]
  public void ErrorLabel_HiddenWithoutError()
  {
    var mockServer = new MockWebSocketServer();
    _panel.UpdateServer(mockServer);

    _errorLabel.VerifySet(m => m.Visible = false);
  }

  [Test]
  public void ClientConnected_AddsToClientList()
  {
    var mockServer = new MockWebSocketServer();
    _panel.UpdateServer(mockServer);

    mockServer.RaiseClientConnected("client-1");
    Refresh();

    _clientList.Verify(
      m => m.AddItem("client-1", It.IsAny<Texture2D>(), It.IsAny<bool>()),
      Times.Once
    );
  }

  [Test]
  public void ClientDisconnected_RemovesFromClientList()
  {
    var mockServer = new MockWebSocketServer();
    _panel.UpdateServer(mockServer);

    mockServer.RaiseClientConnected("client-1");
    Refresh();
    mockServer.RaiseClientDisconnected("client-1");
    Refresh();

    _clientList.Verify(m => m.Clear(), Times.AtLeastOnce);
    _clientList.Verify(
      m => m.AddItem("client-1", It.IsAny<Texture2D>(), It.IsAny<bool>()),
      Times.Once
    );
  }

  [Test]
  public void ClientListLabel_FollowsMode()
  {
    var mockServer = new MockWebSocketServer { Mode = "Client" };
    _panel.UpdateServer(mockServer);

    _clientListLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("对端")));
  }

  [Test]
  public void StartStopBtn_ClientMode_NotConnected_SaysConnect()
  {
    var mockServer = new MockWebSocketServer { Mode = "Client", IsRunning = false };
    _panel.UpdateServer(mockServer);

    _startStopBtn.VerifySet(m => m.Text = "连接");
  }

  [Test]
  public void StartStopBtn_ServerMode_Running_SaysStop()
  {
    var mockServer = new MockWebSocketServer { Mode = "Server", IsRunning = true };
    _panel.UpdateServer(mockServer);

    _startStopBtn.VerifySet(m => m.Text = "停止");
  }
}

public class MockWebSocketServer : IWebSocketServer
{
  public bool IsRunning { get; set; }

  /// <summary>当前连接数；用例可设为非零以验证面板的连接数显示。</summary>
  public int ConnectionCount { get; set; }

  /// <summary>实际运行模式；可被用例改成 "Client" 以验证面板不再跟随设置里的模式。</summary>
  public string Mode { get; set; } = "Server";

  /// <summary>Server 模式实际监听端口。</summary>
  public int Port { get; set; } = 5140;

  /// <summary>Client 模式实际连接的对端地址。</summary>
  public string Url { get; set; } = string.Empty;

  /// <summary>最近一次启动/运行失败的原因。</summary>
  public string LastError { get; set; } = string.Empty;

  public event Action<string>? OnClientConnected;
  public event Action<string>? OnClientDisconnected;

  public Task StartAsync()
  {
    IsRunning = true;
    return Task.CompletedTask;
  }

  public Task StopAsync()
  {
    IsRunning = false;
    return Task.CompletedTask;
  }

  public Task BroadcastAsync(WebSocketMessage message)
  {
    BroadcastedMessages.Add(message);
    return Task.CompletedTask;
  }

  /// <summary>已推送的出站消息（供断言用）。</summary>
  public List<WebSocketMessage> BroadcastedMessages { get; } = new();

  /// <summary>触发「客户端已连接」（面板的连接列表据此同步）。</summary>
  /// <param name="id">连接 ID。</param>
  public void RaiseClientConnected(string id) => OnClientConnected?.Invoke(id);

  /// <summary>触发「客户端已断开」。</summary>
  /// <param name="id">连接 ID。</param>
  public void RaiseClientDisconnected(string id) => OnClientDisconnected?.Invoke(id);
}
