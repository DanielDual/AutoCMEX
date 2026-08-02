namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class TestWebSocketPanel : TestClass
{
  private WebSocketPanel _panel = default!;
  private readonly List<Node> _toCleanup = new();

  public TestWebSocketPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new WebSocketPanel();

    var statusLabel = new Label();
    _panel.AddChild(statusLabel);
    _panel.StatusLabel = statusLabel;

    var modeLabel = new Label();
    _panel.AddChild(modeLabel);
    _panel.ModeLabel = modeLabel;

    var portLabel = new Label();
    _panel.AddChild(portLabel);
    _panel.PortLabel = portLabel;

    var connCountLabel = new Label();
    _panel.AddChild(connCountLabel);
    _panel.ConnectionCountLabel = connCountLabel;

    var eventLabel = new Label();
    _panel.AddChild(eventLabel);
    _panel.EventLabel = eventLabel;

    var startStopBtn = new Button();
    _panel.AddChild(startStopBtn);
    _panel.StartStopBtn = startStopBtn;

    var clientList = new ItemList();
    _panel.AddChild(clientList);
    _panel.ClientList = clientList;

    var refreshTimer = new Timer();
    _panel.AddChild(refreshTimer);
    _panel.RefreshTimer = refreshTimer;

    _panel.FakeDependency<IWebSocketServer>(new MockWebSocketServer());
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
    _panel.ModeLabel.Text.ShouldContain("Server");
  }

  [Test]
  public void UpdateServer_ClientMode_SetsModeLabel()
  {
    var mockServer = new MockWebSocketServer();
    _panel.UpdateServer(mockServer, "Client");
    _panel.ModeLabel.Text.ShouldContain("Client");
  }
}

/// <summary>
/// 用于测试的模拟 WebSocket 服务器
/// </summary>
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
