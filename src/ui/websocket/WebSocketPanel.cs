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
  /// <remarks>
  /// 传入的就是当前实例时只刷新显示、不清空连接列表：重启流程可能把未重建的实例再交一次，
  /// 把它当「换实例」处理会丢掉仍在有效的连接记录。
  /// </remarks>
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

  /// <summary>
  /// 启停执行器：实例的启停一律经它下发（与配置变更触发的重启共用同一把锁）。
  /// 面板不直接调实例的启停——面板手上的实例可能是重启前的旧引用。
  /// </summary>
  [Dependency]
  public WebSocketLifecycle Lifecycle => this.DependOn<WebSocketLifecycle>();

  private IWebSocketServer? _server;

  /// <summary>待处理的连接变化（由 socket 线程投递，主线程在 <see cref="RefreshUI"/> 里消费）。</summary>
  private readonly ConcurrentQueue<(bool Connected, string Id)> _pendingClientEvents = new();

  /// <summary>已连接的一方 ID（服务端为各客户端，客户端为对端）。</summary>
  private readonly List<string> _clients = new();

  /// <summary>已渲染进 <see cref="ClientList"/> 的内容，用于避免每秒重建列表。</summary>
  private readonly List<string> _renderedClients = new();

  private string _lastEvent = "";

  /// <summary>「运行中 / 已连接」状态行的兜底配色（默认主题观感，与接入主题前一致）。</summary>
  private static readonly Color StatusOkFallback = new(0, 1, 0);

  /// <summary>「已停止 / 未连接」状态行的兜底配色（默认主题观感，与接入主题前一致）。</summary>
  private static readonly Color StatusErrorFallback = new(1, 0, 0);

  /// <summary>
  /// 本帧是否已排过刷新：连接/断开与状态变化常在同一事件链里连发，合并成每帧一次。
  /// </summary>
  /// <remarks>
  /// 事件可能来自 socket 线程，因此用 <c>volatile</c> 保证读写可见性；竞态最坏只是同帧多排一次刷新，
  /// 而 <see cref="RefreshUI"/> 本身是幂等、可重复执行的。
  /// </remarks>
  private volatile bool _refreshQueued;

  /// <summary>已执行的 <see cref="RefreshUI"/> 次数（测试用：断言同帧多事件只刷新一次）。</summary>
  internal int RefreshCountForTests { get; private set; }

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    RefreshTimer.Timeout += OnRefreshTimerTimeout;

    // 状态行的语义色是渲染时写进控件的，主题切换不会自己更新：订阅后合并到帧末重算一次
    ThemeChanged += OnThemeChanged;
  }

  /// <summary>主题变化：合并到帧末刷新一次状态行（<see cref="RefreshUI"/> 幂等）。</summary>
  private void OnThemeChanged() => QueueRefresh();

  public void OnResolved()
  {
    _server = Server;
    if (_server == null)
      return;

    _server.OnClientConnected += OnConnected;
    _server.OnClientDisconnected += OnDisconnected;
    _server.OnStateChanged += OnServerStateChanged;
    StartStopBtn.Pressed += OnStartStopPressed;

    RefreshUI();
  }

  /// <summary>
  /// 更新服务器引用（重启 WebSocket 时由 MainWindow 调用）
  /// </summary>
  /// <param name="server">新的服务器/客户端实例。</param>
  public void UpdateServer(IWebSocketServer server)
  {
    // 同一实例再交一次（例如重启流程发现无需重建）：连接记录与在途事件仍然有效，
    // 按「换实例」清空会让列表停在旧内容、后续连接也不再累积（潜伏缺陷），这里只刷新显示。
    if (ReferenceEquals(_server, server))
    {
      RefreshUI();
      return;
    }

    if (_server != null)
    {
      _server.OnClientConnected -= OnConnected;
      _server.OnClientDisconnected -= OnDisconnected;
      _server.OnStateChanged -= OnServerStateChanged;
    }

    _server = server;

    // 换了实例，旧实例的连接记录与在途事件都不再适用，全部丢弃后按新实例重建
    _pendingClientEvents.Clear();
    _clients.Clear();
    _renderedClients.Clear();

    // 列表控件也要显式清空：两个内部列表同时清空时 SyncClientList 会因「空 == 空」跳过重建，
    // 旧实例的连接会继续留在界面上
    ClientList.Clear();

    if (_server != null)
    {
      _server.OnClientConnected += OnConnected;
      _server.OnClientDisconnected += OnDisconnected;
      _server.OnStateChanged += OnServerStateChanged;
    }

    RefreshUI();
  }

  public override void _ExitTree()
  {
    ThemeChanged -= OnThemeChanged;

    if (RefreshTimer != null)
    {
      RefreshTimer.Timeout -= OnRefreshTimerTimeout;
    }

    if (_server != null)
    {
      _server.OnClientConnected -= OnConnected;
      _server.OnClientDisconnected -= OnDisconnected;
      _server.OnStateChanged -= OnServerStateChanged;
    }

    // 离场后不再需要排队标记：本帧已经不会有刷新要合并了
    _refreshQueued = false;
  }

  private void OnRefreshTimerTimeout()
  {
    RefreshUI();
  }

  /// <summary>
  /// 把本帧的刷新请求合并成一次：连接/断开/状态处理器都只调用它，不再各自排一次全量刷新。
  /// </summary>
  /// <remarks>
  /// 处理器可能运行在 socket 线程（规则 19），这里只置脏并排一次 deferred，真正刷新回主线程执行。
  /// 同一事件链里「连接 + 状态变化」或「断开 + 重连态」会连发多次通知，本方法保证只刷一次。
  /// </remarks>
  private void QueueRefresh()
  {
    if (_refreshQueued)
      return;

    _refreshQueued = true;
    CallDeferred(nameof(FlushRefresh));
  }

  /// <summary>执行本帧合并后的刷新；公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约。</summary>
  public void FlushRefresh()
  {
    _refreshQueued = false;
    RefreshUI();
  }

  /// <summary>
  /// 服务器状态变化：调度到主线程刷新显示（事件可能来自接受循环/连接循环线程）。
  /// </summary>
  /// <remarks>
  /// 这是显示更新的主驱动；1Hz 的 <see cref="RefreshTimer"/> 仍作为兜底，防止事件漏发
  /// 或状态由不经过事件的路径改变（例如旧实例在面板换引用后才变化）。
  /// </remarks>
  private void OnServerStateChanged()
  {
    QueueRefresh();
  }

  private void OnConnected(string id)
  {
    _lastEvent = $"已连接 {id} ({DateTime.Now:HH:mm:ss})";
    _pendingClientEvents.Enqueue((true, id));
    QueueRefresh();
  }

  private void OnDisconnected(string id)
  {
    _lastEvent = $"已断开 {id} ({DateTime.Now:HH:mm:ss})";
    _pendingClientEvents.Enqueue((false, id));
    QueueRefresh();
  }

  /// <summary>
  /// 启停按钮：交给 <see cref="WebSocketLifecycle"/> 执行——方向（在工作则停、否则启动）由它在锁内
  /// 按**当前实例**判定，不用面板的显示状态，也不用面板手上的实例引用。
  /// </summary>
  /// <remarks>
  /// 方向不能按 <see cref="IWebSocketServer.IsRunning"/> 判：Client 断线重连期间实例已在工作但未连接，
  /// 按「是否已连接」点下去只会再启动一次（幂等后成空操作），用户反而停不掉正在重连的客户端。
  /// 更不能作用在面板自己的实例引用上：配置变更重启期间该引用可能已是旧实例，点下去会把旧实例重新拉起。
  /// </remarks>
  private async void OnStartStopPressed()
  {
    try
    {
      await Lifecycle.ToggleAsync();
    }
    catch (Exception ex)
    {
      // 实例自身的日志已记录细节，这里只保证异常不会被信号回调吞掉
      GD.PrintErr($"WebSocketPanel: start/stop failed: {ex.GetType().Name}: {ex.Message}");
    }

    RefreshUI();
  }

  private void RefreshUI()
  {
    if (_server == null)
      return;

    RefreshCountForTests++;

    DrainClientEvents();

    var isRunning = _server.IsRunning;

    // 实例是否还在工作（Client 重连等待中为 true 而 IsRunning 为 false）
    var isActive = _server.IsActive;

    // 模式取自实例本身，不取自设置：设置改了但实例还没重建时，面板显示的必须是**正在跑的东西**
    var isClient = string.Equals(_server.Mode, "Client", StringComparison.OrdinalIgnoreCase);

    ModeLabel.Text = isClient ? "模式: Client（主动连接）" : "模式: Server（等待连接）";

    var statusText = isClient
      ? (isRunning ? "已连接" : (isActive ? "未连接（重连中）" : "未连接"))
      : (isRunning ? "运行中" : "已停止");
    StatusLabel.Text = statusText;

    // 用字色而不是 Modulate：Modulate 会连描边/背景一起染色，浅色主题下绿底红底更糊；
    // 语义色经 AppPalette 取，主题未提供时退回接入主题前的那组绿/红。
    StatusLabel.AddThemeColorOverride(
      "font_color",
      AppPalette.Get(
        (StatusLabel as INodeAdapter)?.TargetObj,
        isRunning ? ThemeCatalog.StatusOk : ThemeCatalog.StatusError,
        isRunning ? StatusOkFallback : StatusErrorFallback
      )
    );

    // 端口/地址按实际实例显示：Server 打实际监听端口，Client 打实际连接地址，
    // 不再沿用场景里硬编码的「端口: 5140」
    PortLabel.Text = isClient ? $"地址: {_server.Url}" : $"端口: {_server.Port}";

    // Client 只有一条链路，「连接数」与状态行重复；直接隐藏整行
    ConnectionCountLabel.Visible = !isClient;
    ConnectionCountLabel.Text = $"连接数: {_server.ConnectionCount}";

    SyncClientList(isClient);

    // 失败原因显式显示（未配置 Koishi 地址、端口被占用、连接失败等），无错误时整行隐藏
    var lastError = _server.LastError;
    ErrorLabel.Text = lastError;
    ErrorLabel.Visible = !string.IsNullOrEmpty(lastError);

    if (!string.IsNullOrEmpty(_lastEvent))
      EventLabel.Text = _lastEvent;

    StartStopBtn.Text = isClient ? (isActive ? "断开" : "连接") : (isRunning ? "停止" : "启动");
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
