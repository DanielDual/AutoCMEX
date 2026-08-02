namespace AutoCMEX.UI.WebSocket;

using System;
using AutoCMEX.Core.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// WebSocket 面板：显示服务器状态和已连接客户端列表
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class WebSocketPanel : Control
{
  [Node("%StatusLabel")]
  public Label StatusLabel { get; set; } = default!;

  [Node("%ModeLabel")]
  public Label ModeLabel { get; set; } = default!;

  [Node("%PortLabel")]
  public Label PortLabel { get; set; } = default!;

  [Node("%ConnectionCountLabel")]
  public Label ConnectionCountLabel { get; set; } = default!;

  [Node("%EventLabel")]
  public Label EventLabel { get; set; } = default!;

  [Node("%StartStopBtn")]
  public Button StartStopBtn { get; set; } = default!;

  [Node("%ClientList")]
  public ItemList ClientList { get; set; } = default!;

  [Node("%RefreshTimer")]
  public Timer RefreshTimer { get; set; } = default!;

  [Dependency]
  public IWebSocketServer Server => this.DependOn<IWebSocketServer>();

  private IWebSocketServer? _server;
  private string _mode = "Server";
  private string _lastEvent = "";

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    RefreshTimer.Timeout += OnRefreshTimerTimeout;
  }

  public void OnResolved()
  {
    _server = Server;
    if (_server == null)
      return;

    _server.OnClientConnected += OnConnected;
    _server.OnClientDisconnected += OnDisconnected;
    StartStopBtn.Pressed += OnStartStopPressed;

    RefreshUI();
  }

  /// <summary>
  /// 更新服务器引用（重启 WebSocket 时由 MainWindow 调用）
  /// </summary>
  public void UpdateServer(IWebSocketServer server, string mode)
  {
    if (_server != null)
    {
      _server.OnClientConnected -= OnConnected;
      _server.OnClientDisconnected -= OnDisconnected;
    }

    _server = server;
    _mode = mode;

    if (_server != null)
    {
      _server.OnClientConnected += OnConnected;
      _server.OnClientDisconnected += OnDisconnected;
    }

    RefreshUI();
  }

  public override void _ExitTree()
  {
    if (RefreshTimer != null)
    {
      RefreshTimer.Timeout -= OnRefreshTimerTimeout;
    }

    if (_server != null)
    {
      _server.OnClientConnected -= OnConnected;
      _server.OnClientDisconnected -= OnDisconnected;
    }
  }

  private void OnRefreshTimerTimeout()
  {
    RefreshUI();
  }

  private void OnConnected(string id)
  {
    _lastEvent = $"已连接 ({DateTime.Now:HH:mm:ss})";
    CallDeferred(nameof(RefreshUI));
  }

  private void OnDisconnected(string id)
  {
    _lastEvent = $"已断开 ({DateTime.Now:HH:mm:ss})";
    CallDeferred(nameof(RefreshUI));
  }

  private void OnStartStopPressed()
  {
    if (_server == null)
      return;

    if (_server.IsRunning)
      _ = _server.StopAsync();
    else
      _ = _server.StartAsync();

    RefreshUI();
  }

  private void RefreshUI()
  {
    if (_server == null)
      return;

    var isRunning = _server.IsRunning;
    var isClient = string.Equals(_mode, "Client", StringComparison.OrdinalIgnoreCase);

    ModeLabel.Text = isClient ? "模式: Client（主动连接）" : "模式: Server（等待连接）";

    var statusText = isClient
      ? (isRunning ? "已连接" : "未连接")
      : (isRunning ? "运行中" : "已停止");
    StatusLabel.Text = statusText;
    StatusLabel.Modulate = isRunning ? new Color(0, 1, 0) : new Color(1, 0, 0);

    ConnectionCountLabel.Text = isClient
      ? (isRunning ? "已连接" : "未连接")
      : $"连接数: {_server.ConnectionCount}";

    if (!string.IsNullOrEmpty(_lastEvent))
      EventLabel.Text = _lastEvent;

    StartStopBtn.Text = isRunning ? "断开" : "连接";
  }
}
