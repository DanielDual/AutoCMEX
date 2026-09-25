namespace AutoCMEX.UI.WebSocket;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AutoCMEX.Core.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// WebSocket 面板接口：解耦 MainWindow 对具体面板类型的依赖。
/// </summary>
/// <remarks>
/// 注意：不继承 <see cref="IControl"/>，因为 Godot 4.7.1 的 <see cref="Control"/>
/// 无法直接实现 <see cref="IControl"/>（AccessibilityLive 返回类型不匹配）。
/// </remarks>
public interface IWebSocketPanel
{
  /// <summary>更新服务器引用（重启 WebSocket 时由 MainWindow 调用）。</summary>
  /// <param name="server">
  /// 新的服务器/客户端实例。模式、端口、地址、错误一律从它自身读取，不再由调用方按设置传入，
  /// 否则设置改动后实例尚未重建时面板会显示与真实行为不符的模式。
  /// </param>
  void UpdateServer(IWebSocketServer server);
}

/// <summary>
/// WebSocket 面板：显示服务器状态和已连接客户端列表
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class WebSocketPanel : Control, IWebSocketPanel
{
  [Node("%StatusLabel")]
  public ILabel StatusLabel { get; set; } = default!;

  [Node("%ModeLabel")]
  public ILabel ModeLabel { get; set; } = default!;

  [Node("%PortLabel")]
  public ILabel PortLabel { get; set; } = default!;

  [Node("%ConnectionCountLabel")]
  public ILabel ConnectionCountLabel { get; set; } = default!;

  [Node("%EventLabel")]
  public ILabel EventLabel { get; set; } = default!;

  [Node("%ErrorLabel")]
  public ILabel ErrorLabel { get; set; } = default!;

  [Node("%StartStopBtn")]
  public IButton StartStopBtn { get; set; } = default!;

  [Node("%ClientListLabel")]
  public ILabel ClientListLabel { get; set; } = default!;

  [Node("%ClientList")]
  public IItemList ClientList { get; set; } = default!;

  [Node("%RefreshTimer")]
  public ITimer RefreshTimer { get; set; } = default!;

  [Dependency]
  public IWebSocketServer Server => this.DependOn<IWebSocketServer>();

  private IWebSocketServer? _server;

  /// <summary>待处理的连接变化（由 socket 线程投递，主线程在 <see cref="RefreshUI"/> 里消费）。</summary>
  private readonly ConcurrentQueue<(bool Connected, string Id)> _pendingClientEvents = new();

  /// <summary>已连接的一方 ID（服务端为各客户端，客户端为对端）。</summary>
  private readonly List<string> _clients = new();

  /// <summary>已渲染进 <see cref="ClientList"/> 的内容，用于避免每秒重建列表。</summary>
  private readonly List<string> _renderedClients = new();

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
  /// <param name="server">新的服务器/客户端实例。</param>
  public void UpdateServer(IWebSocketServer server)
  {
    if (_server != null)
    {
      _server.OnClientConnected -= OnConnected;
      _server.OnClientDisconnected -= OnDisconnected;
    }

    _server = server;

    // 换了实例，旧实例的连接记录与在途事件都不再适用，全部丢弃后按新实例重建
    _pendingClientEvents.Clear();
    _clients.Clear();
    _renderedClients.Clear();

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
    _lastEvent = $"已连接 {id} ({DateTime.Now:HH:mm:ss})";
    _pendingClientEvents.Enqueue((true, id));
    Callable.From(RefreshUI).CallDeferred();
  }

  private void OnDisconnected(string id)
  {
    _lastEvent = $"已断开 {id} ({DateTime.Now:HH:mm:ss})";
    _pendingClientEvents.Enqueue((false, id));
    Callable.From(RefreshUI).CallDeferred();
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

    DrainClientEvents();

    var isRunning = _server.IsRunning;

    // 模式取自实例本身，不取自设置：设置改了但实例还没重建时，面板显示的必须是**正在跑的东西**
    var isClient = string.Equals(_server.Mode, "Client", StringComparison.OrdinalIgnoreCase);

    ModeLabel.Text = isClient ? "模式: Client（主动连接）" : "模式: Server（等待连接）";

    var statusText = isClient
      ? (isRunning ? "已连接" : "未连接")
      : (isRunning ? "运行中" : "已停止");
    StatusLabel.Text = statusText;
    StatusLabel.Modulate = isRunning ? new Color(0, 1, 0) : new Color(1, 0, 0);

    // 端口/地址按实际实例显示：Server 打实际监听端口，Client 打实际连接地址，
    // 不再沿用场景里硬编码的「端口: 5140」
    PortLabel.Text = isClient ? $"地址: {_server.Url}" : $"端口: {_server.Port}";

    // Client 只有一条链路，「连接数」与状态行重复，且会变成一行没有前缀的裸文本；直接隐藏整行
    ConnectionCountLabel.Visible = !isClient;
    ConnectionCountLabel.Text = $"连接数: {_server.ConnectionCount}";

    SyncClientList(isClient);

    // 失败原因显式显示（未配置 Koishi 地址、端口被占用、连接失败等），无错误时整行隐藏
    var lastError = _server.LastError;
    ErrorLabel.Text = lastError;
    ErrorLabel.Visible = !string.IsNullOrEmpty(lastError);

    if (!string.IsNullOrEmpty(_lastEvent))
      EventLabel.Text = _lastEvent;

    StartStopBtn.Text = isClient ? (isRunning ? "断开" : "连接") : (isRunning ? "停止" : "启动");
  }

  /// <summary>把 socket 线程投递的连接变化并入本地列表（只在主线程调用）。</summary>
  private void DrainClientEvents()
  {
    while (_pendingClientEvents.TryDequeue(out var change))
    {
      if (change.Connected)
      {
        if (!_clients.Contains(change.Id))
        {
          _clients.Add(change.Id);
        }
      }
      else
      {
        _clients.Remove(change.Id);
      }
    }
  }

  /// <summary>把已连接列表同步到 <see cref="ClientList"/>，并按模式修正列表标题。</summary>
  /// <param name="isClient">当前是否为 Client 模式。</param>
  private void SyncClientList(bool isClient)
  {
    if (!SequenceEqual(_renderedClients, _clients))
    {
      _renderedClients.Clear();
      _renderedClients.AddRange(_clients);

      ClientList.Clear();
      foreach (var id in _renderedClients)
      {
        ClientList.AddItem(id);
      }
    }

    ClientListLabel.Text = isClient ? "连接的对端:" : "已连接客户端:";
  }

  private static bool SequenceEqual(List<string> left, List<string> right)
  {
    if (left.Count != right.Count)
    {
      return false;
    }

    for (var i = 0; i < left.Count; i++)
    {
      if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
      {
        return false;
      }
    }

    return true;
  }
}
