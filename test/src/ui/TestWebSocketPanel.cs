namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

public class TestWebSocketPanel : TestClass
{
  private WebSocketPanel _panel = default!;

  /// <summary>面板手上（用于显示）的实例。</summary>
  private MockWebSocketServer _server = default!;

  /// <summary>生命周期工厂下次返回的实例；用例可换掉它来模拟「配置变更重启后换了实例」。</summary>
  private MockWebSocketServer _nextInstance = default!;
  private WebSocketLifecycle _lifecycle = default!;
  private Mock<ILabel> _statusLabel = default!;
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

    _statusLabel = new Mock<ILabel>();
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
        ["%StatusLabel"] = _statusLabel.Object,
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

    _server = new MockWebSocketServer();
    _nextInstance = _server;
    _lifecycle = new WebSocketLifecycle(_ => _nextInstance, new Mock<ILog>().Object);
    _lifecycle.Create(new AppSettings());

    _panel.FakeDependency<IWebSocketServer>(_server);
    _panel.FakeDependency<WebSocketLifecycle>(_lifecycle);
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

  /// <summary>
  /// 状态事件驱动刷新：只改实例状态并触发 <see cref="IWebSocketServer.OnStateChanged"/>，
  /// 不依赖 1Hz 的 <see cref="WebSocketPanel.RefreshTimer"/> 兜底，标签也要更新。
  /// </summary>
  [Test]
  public async Task StateEvent_DrivesRefreshUI()
  {
    _server.Mode = "Server";
    _server.IsRunning = false;
    _server.LastError = "端口 5140 监听失败：拒绝访问";

    _server.RaiseStateChanged();

    // 事件处理器只排队，等 deferred 刷新在主线程落地
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    _statusLabel.VerifySet(m => m.Text = "已停止");
    _startStopBtn.VerifySet(m => m.Text = "启动");
    _errorLabel.VerifySet(m => m.Text = "端口 5140 监听失败：拒绝访问");
    _errorLabel.VerifySet(m => m.Visible = true);
  }

  /// <summary>
  /// 同帧多事件合并：连接、断开与状态变化常在同一条事件链里连发，
  /// 面板必须只做一次全量刷新（而不是每个事件各排一次 deferred 刷新）。
  /// </summary>
  [Test]
  public async Task SameFrameEventChain_RefreshesOnce()
  {
    _server.Mode = "Server";
    _server.IsRunning = true;
    _panel.UpdateServer(_server);

    var before = _panel.RefreshCountForTests;

    // 同一帧：连接 → 状态变化 → 断开 → 状态变化（真实实例在连接/断开时都会各抛两种事件）
    _server.RaiseClientConnected("client-1");
    _server.RaiseStateChanged();
    _server.RaiseClientDisconnected("client-1");
    _server.RaiseStateChanged();

    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    (_panel.RefreshCountForTests - before).ShouldBe(1, "同帧多次事件只应触发一次全量刷新");

    // 合并不是丢事件：下一帧的事件照常再刷一次
    _server.RaiseStateChanged();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    (_panel.RefreshCountForTests - before).ShouldBe(2, "合并不丢事件，下一帧的变化应再刷一次");
  }

  /// <summary>
  /// 主题变化合并到帧末：状态行的语义色是渲染时写进控件的，主题切换要重算，但不得在信号回调里同步重画。
  /// </summary>
  /// <remarks>
  /// 这里不手工顶替帧末调用，而是等真实的 <c>ProcessFrame</c>：若去掉合并标记
  /// （<see cref="WebSocketPanel"/> 的 <c>_refreshQueued</c>），两次通知会各排一次 deferred，
  /// 刷两次，本用例随之变红。
  /// </remarks>
  [Test]
  public async Task ThemeChanged_DefersRefreshAndMergesToOnePass()
  {
    _panel.UpdateServer(_server);
    var before = _panel.RefreshCountForTests;

    _panel.EmitSignal(Control.SignalName.ThemeChanged);
    _panel.EmitSignal(Control.SignalName.ThemeChanged);

    _panel.RefreshCountForTests.ShouldBe(before, "主题变化不得同步刷新状态行");

    // 规则 30：CallDeferred 之后必须等帧再断言，不手工调 FlushRefresh
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    (_panel.RefreshCountForTests - before).ShouldBe(1, "同帧两次主题通知只应合并成一次刷新");
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

  [Test]
  public void StartStopBtn_ClientMode_Reconnecting_SaysDisconnect()
  {
    // 客户端断线重连中：未连接（IsRunning=false）但实例仍在工作，
    // 按钮必须仍能给用户提供「断开」，否则正在重连的客户端停不掉
    var mockServer = new MockWebSocketServer
    {
      Mode = "Client",
      IsRunning = false,
      IsActive = true,
    };
    _panel.UpdateServer(mockServer);

    _startStopBtn.VerifySet(m => m.Text = "断开");
    _statusLabel.VerifySet(m => m.Text = "未连接（重连中）");
  }

  [Test]
  public void StartStopPressed_WhileReconnecting_StopsInsteadOfStartingAgain()
  {
    // Arrange：重连等待中的客户端（控制器当前实例）
    _server.Mode = "Client";
    _server.IsRunning = false;
    _server.IsActive = true;
    _panel.UpdateServer(_server);

    // Act：点一次启停按钮
    _startStopBtn.Raise(button => button.Pressed += null);

    // Assert：走的是停止分支（旧实现按 IsRunning 判定，会再启动一次）
    _server.StopCount.ShouldBe(1);
    _server.StartCount.ShouldBe(0);
  }

  [Test]
  public void StartStopPressed_WhenStopped_StartsTheInstance()
  {
    // Arrange：已创建但未启动的实例
    _server.Mode = "Client";
    _panel.UpdateServer(_server);

    // Act：点一次启停按钮
    _startStopBtn.Raise(button => button.Pressed += null);

    // Assert
    _server.StartCount.ShouldBe(1);
    _server.StopCount.ShouldBe(0);
  }

  [Test]
  public void StartStopPressed_ActsOnLifecycleCurrentInstance_NotOnStalePanelReference()
  {
    // Arrange：配置变更重启的窗口内，面板手上的引用已是被替换掉的旧实例
    var stale = new MockWebSocketServer
    {
      Mode = "Client",
      IsRunning = true,
      IsActive = true,
    };
    _panel.UpdateServer(stale);

    var current = new MockWebSocketServer { Mode = "Client" };
    _nextInstance = current;
    _lifecycle.Create(new AppSettings());

    // Act：此时点按钮
    _startStopBtn.Raise(button => button.Pressed += null);

    // Assert：作用在控制器的当前实例上——面板若自行判方向并对旧引用下手，
    // 旧实例会被重新拉起（与新实例同时在跑，正是连接震颤的成因）
    current.StartCount.ShouldBe(1);
    stale.StartCount.ShouldBe(0);
    stale.StopCount.ShouldBe(0);
  }
}

public class MockWebSocketServer : IWebSocketServer
{
  public bool IsRunning { get; set; }

  /// <summary>实例是否持有活动链路；用例可单独设置以模拟「未连接但仍在重连」的 Client。</summary>
  public bool IsActive { get; set; }

  /// <summary>启动/停止被调用的次数（供启停方向断言用）。</summary>
  public int StartCount { get; private set; }

  /// <summary>停止被调用的次数（供启停方向断言用）。</summary>
  public int StopCount { get; private set; }

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

  private Action? _onStateChanged;

  /// <summary>状态变化事件；订阅/退订计数用于断言「同实例不重复挂、换实例重挂」。</summary>
  public event Action? OnStateChanged
  {
    add
    {
      _onStateChanged += value;
      StateChangedSubscriberCount++;
    }
    remove
    {
      _onStateChanged -= value;
      StateChangedSubscriberCount--;
    }
  }

  /// <summary>当前订阅状态变化事件的回调数（供断言用）。</summary>
  public int StateChangedSubscriberCount { get; private set; }

  public Task StartAsync()
  {
    StartCount++;
    IsRunning = true;
    IsActive = true;
    RaiseStateChanged();
    return Task.CompletedTask;
  }

  public Task StopAsync()
  {
    StopCount++;
    IsRunning = false;
    IsActive = false;
    RaiseStateChanged();
    return Task.CompletedTask;
  }

  public Task<int> BroadcastAsync(WebSocketMessage message)
  {
    BroadcastedMessages.Add(message);
    // 这个替身代表一条在跑的单链路：送出去就是送到了一个对端
    return Task.FromResult(1);
  }

  /// <summary>已推送的出站消息（供断言用）。</summary>
  public List<WebSocketMessage> BroadcastedMessages { get; } = new();

  /// <summary>触发「客户端已连接」（面板的连接列表据此同步）。</summary>
  /// <param name="id">连接 ID。</param>
  public void RaiseClientConnected(string id) => OnClientConnected?.Invoke(id);

  /// <summary>触发「客户端已断开」。</summary>
  /// <param name="id">连接 ID。</param>
  public void RaiseClientDisconnected(string id) => OnClientDisconnected?.Invoke(id);

  /// <summary>
  /// 触发「服务器状态变化」（生产里由实例在启动/停止/连接/断开/失败时自己触发）。
  /// </summary>
  public void RaiseStateChanged() => _onStateChanged?.Invoke();
}
