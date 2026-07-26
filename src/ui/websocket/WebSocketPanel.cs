namespace AutoCMEX.UI.WebSocket;

using System;
using AutoCMEX.Core.WebSocket;
using Godot;

/// <summary>
/// WebSocket 面板：显示服务器状态和已连接客户端列表
/// </summary>
public partial class WebSocketPanel : Control
{
  private IWebSocketServer? _server;
  private string _mode = "Server";
  private string _lastEvent = "";
  private Timer? _refreshTimer;

  private Label? _statusLabel;
  private Label? _modeLabel;
  private Label? _portLabel;
  private Label? _connectionCountLabel;
  private Label? _eventLabel;
  private Button? _startStopBtn;
  private ItemList? _clientList;

  /// <summary>
  /// 设置 WebSocket 服务器引用（在 AddChild 之前调用）
  /// </summary>
  public void SetServer(IWebSocketServer server, string mode)
  {
    _server = server;
    _mode = mode;
  }

  public override void _Ready()
  {
    _statusLabel = GetNode<Label>("MainContainer/StatusContainer/StatusLabel");
    _modeLabel = GetNode<Label>("MainContainer/ModeLabel");
    _portLabel = GetNode<Label>("MainContainer/PortLabel");
    _connectionCountLabel = GetNode<Label>("MainContainer/ConnectionCountLabel");
    _eventLabel = GetNode<Label>("MainContainer/EventLabel");
    _startStopBtn = GetNode<Button>("MainContainer/StartStopBtn");
    _clientList = GetNode<ItemList>("MainContainer/ClientList");

    if (_server == null)
      return;

    _server.OnClientConnected += OnConnected;
    _server.OnClientDisconnected += OnDisconnected;
    _startStopBtn.Pressed += OnStartStopPressed;

    // 使用 Godot 原生 Timer 节点替代 System.Timers.Timer，与主循环天然同步
    _refreshTimer = new Timer();
    _refreshTimer.WaitTime = 1.0;
    _refreshTimer.Autostart = true;
    _refreshTimer.Timeout += OnRefreshTimerTimeout;
    AddChild(_refreshTimer);

    RefreshUI();
  }

  public override void _ExitTree()
  {
    if (_refreshTimer != null)
    {
      _refreshTimer.Timeout -= OnRefreshTimerTimeout;
      _refreshTimer.Stop();
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
    if (_server == null || _startStopBtn == null)
      return;

    if (_server.IsRunning)
      _ = _server.StopAsync();
    else
      _ = _server.StartAsync();

    RefreshUI();
  }

  private void RefreshUI()
  {
    if (_server == null || _statusLabel == null)
      return;

    var isRunning = _server.IsRunning;
    var isClient = string.Equals(_mode, "Client", StringComparison.OrdinalIgnoreCase);

    // 模式
    if (_modeLabel != null)
      _modeLabel.Text = isClient ? "模式: Client（主动连接）" : "模式: Server（等待连接）";

    // 状态
    var statusText = isClient
      ? (isRunning ? "已连接" : "未连接")
      : (isRunning ? "运行中" : "已停止");
    _statusLabel.Text = statusText;
    _statusLabel.Modulate = isRunning ? new Color(0, 1, 0) : new Color(1, 0, 0);

    // 连接数
    if (_connectionCountLabel != null)
      _connectionCountLabel.Text = isClient
        ? (isRunning ? "已连接" : "未连接")
        : $"连接数: {_server.ConnectionCount}";

    // 最近事件
    if (_eventLabel != null && !string.IsNullOrEmpty(_lastEvent))
      _eventLabel.Text = _lastEvent;

    // 按钮
    if (_startStopBtn != null)
      _startStopBtn.Text = isRunning ? "断开" : "连接";
  }
}
