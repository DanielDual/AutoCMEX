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
  private readonly List<Node> _toCleanup = new();

  public TestWebSocketPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new WebSocketPanel();
    (_panel as IAutoInit).IsTesting = true;

    var statusLabel = new Mock<ILabel>();
    _modeLabel = new Mock<ILabel>();
    var portLabel = new Mock<ILabel>();
    var connCountLabel = new Mock<ILabel>();
    var eventLabel = new Mock<ILabel>();
    var startStopBtn = new Mock<IButton>();
    var clientList = new Mock<IItemList>();
    var refreshTimer = new Mock<ITimer>();

    _panel.FakeNodeTree(
      new()
      {
        ["%StatusLabel"] = statusLabel.Object,
        ["%ModeLabel"] = _modeLabel.Object,
        ["%PortLabel"] = portLabel.Object,
        ["%ConnectionCountLabel"] = connCountLabel.Object,
        ["%EventLabel"] = eventLabel.Object,
        ["%StartStopBtn"] = startStopBtn.Object,
        ["%ClientList"] = clientList.Object,
        ["%RefreshTimer"] = refreshTimer.Object,
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
    var mockServer = new MockWebSocketServer();
    _panel.UpdateServer(mockServer, "Server");
    _modeLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("Server")));
  }

  [Test]
  public void UpdateServer_ClientMode_SetsModeLabel()
  {
    var mockServer = new MockWebSocketServer();
    _panel.UpdateServer(mockServer, "Client");
    _modeLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("Client")));
  }
}

public class MockWebSocketServer : IWebSocketServer
{
  public bool IsRunning { get; set; }
  public int ConnectionCount => 0;
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
}
