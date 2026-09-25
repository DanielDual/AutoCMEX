namespace AutoCMEX.Core.WebSocket;

using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// WebSocket 服务器：HttpListener 多客户端、消息收发循环、统一异常捕获
/// </summary>
public class WebSocketServer : IWebSocketServer, IDisposable
{
  private readonly int _port;
  private readonly ILog _log;
  private readonly IConnectionManager _connectionManager;
  private readonly IProtocolHandler _protocolHandler;
  private readonly MessageRouter _messageRouter;
  private readonly HeartbeatService _heartbeatService;
  private readonly bool _enableAuth;
  private readonly string _authToken;
  private HttpListener? _listener;
  private CancellationTokenSource? _cts;
  private Task? _acceptLoopTask;
  private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
  private bool _disposed;
  private string _lastError = string.Empty;

  /// <inheritdoc/>
  public bool IsRunning { get; private set; }

  /// <inheritdoc/>
  public bool IsActive { get; private set; }

  /// <inheritdoc/>
  public int ConnectionCount => _connectionManager.Count;

  /// <inheritdoc/>
  public string Mode => "Server";

  /// <inheritdoc/>
  public int Port => _port;

  /// <inheritdoc/>
  public string Url => string.Empty;

  /// <inheritdoc/>
  public string LastError => _lastError;

  /// <inheritdoc/>
  public event Action<string>? OnClientConnected;

  /// <inheritdoc/>
  public event Action<string>? OnClientDisconnected;

  /// <summary>
  /// 创建 WebSocket 服务器
  /// </summary>
  public WebSocketServer(
    int port,
    IConnectionManager connectionManager,
    IProtocolHandler protocolHandler,
    MessageRouter messageRouter,
    HeartbeatService heartbeatService,
    bool enableAuth = false,
    string authToken = ""
  )
    : this(
      port,
      connectionManager,
      protocolHandler,
      messageRouter,
      heartbeatService,
      enableAuth,
      authToken,
      AppLogs.GetOrCreate().GetLogger(nameof(WebSocketServer))
    ) { }

  /// <summary>
  /// 创建 WebSocket 服务器（带日志注入）
  /// </summary>
  public WebSocketServer(
    int port,
    IConnectionManager connectionManager,
    IProtocolHandler protocolHandler,
    MessageRouter messageRouter,
    HeartbeatService heartbeatService,
    bool enableAuth,
    string authToken,
    ILog log
  )
  {
    _port = port;
    _connectionManager = connectionManager;
    _protocolHandler = protocolHandler;
    _messageRouter = messageRouter;
    _heartbeatService = heartbeatService;
    _enableAuth = enableAuth;
    _authToken = authToken;
    _log = log;

    _heartbeatService.OnHeartbeatTimeout += HandleHeartbeatTimeout;
  }

  /// <inheritdoc/>
  /// <remarks>
  /// 幂等判据是「接受循环是否还活着」，不是 <see cref="IsRunning"/>：
  /// 监听器启动失败或已被停掉时 IsRunning 为 false，但循环/监听器可能仍在，按它判定会重复建监听器。
  /// </remarks>
  public async Task StartAsync()
  {
    await _lifecycleGate.WaitAsync();
    try
    {
      if (_disposed || _acceptLoopTask is { IsCompleted: false })
        return;

      HttpListener? listener = null;
      try
      {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        listener.Start();
        _listener = listener;
        IsRunning = true;
        IsActive = true;
        _lastError = string.Empty;
        _log.Print($"WebSocketServer started on port {_port}.");

        // 循环只认这个 listener 实例：端口变更后重建时不会被字段换新影响
        _acceptLoopTask = Task.Run(() => AcceptConnectionsLoop(listener, token), token);
      }
      catch (HttpListenerException ex)
      {
        _lastError = $"端口 {_port} 监听失败：{ex.Message}";
        _log.Err($"WebSocketServer failed to start on port {_port}: {ex.Message}");
        CleanupFailedStart(listener);
      }
      catch (Exception ex)
      {
        // 非 HttpListenerException 的启动失败（前缀非法等）此前会抛出并被 `_ = StartAsync()` 丢掉，
        // 面板只剩「未运行」而没有任何原因；这里一律记下原因，避免同类静默。
        _lastError = $"端口 {_port} 监听失败：{ex.GetType().Name}: {ex.Message}";
        _log.Err(
          $"WebSocketServer failed to start on port {_port}: {ex.GetType().Name}: {ex.Message}"
        );
        CleanupFailedStart(listener);
      }
    }
    finally
    {
      _lifecycleGate.Release();
    }
  }

  /// <inheritdoc/>
  /// <remarks>
  /// 不能按 <see cref="IsRunning"/> 提前返回：它只表示「监听器在跑」，而停止的真正工作是
  /// 关掉监听器并等待接受循环退出；提前返回会留下仍占着端口的旧监听器（端口变更时表现为新端口启动失败）。
  /// </remarks>
  public async Task StopAsync()
  {
    await _lifecycleGate.WaitAsync();
    try
    {
      if (_disposed)
        return;

      _cts?.Cancel();

      // 关闭所有连接
      foreach (var conn in _connectionManager.GetAllConnections())
      {
        try
        {
          if (conn.Socket.State == WebSocketState.Open)
          {
            await conn.Socket.CloseAsync(
              WebSocketCloseStatus.NormalClosure,
              "Server shutting down.",
              CancellationToken.None
            );
          }
        }
        catch (Exception ex)
        {
          _log.Warn($"Error closing connection {conn.Id}: {ex.Message}");
        }
      }

      // 先停监听器让 GetContextAsync 立刻失败，再等接受循环真正退出
      var listener = _listener;
      _listener = null;
      try
      {
        listener?.Stop();
        listener?.Close();
      }
      catch (Exception ex)
      {
        _log.Warn($"WebSocketServer: error closing listener on port {_port}: {ex.Message}");
      }

      var loop = _acceptLoopTask;
      if (loop is not null)
      {
        try
        {
          await loop;
        }
        catch (OperationCanceledException)
        {
          // 取消是停止的正常路径
        }
        catch (Exception ex)
        {
          _log.Warn($"WebSocketServer: accept loop ended with error: {ex.Message}");
        }
      }

      IsRunning = false;
      IsActive = false;
      _acceptLoopTask = null;
      _cts?.Dispose();
      _cts = null;
      _log.Print($"WebSocketServer stopped on port {_port}.");
    }
    finally
    {
      _lifecycleGate.Release();
    }
  }

  /// <summary>
  /// 启动失败后的收尾：丢掉半成品监听器与令牌源，保证下一次启动能干净重试。
  /// </summary>
  /// <param name="listener">本次尝试创建的监听器（可能为 null 或已 Start 失败）。</param>
  private void CleanupFailedStart(HttpListener? listener)
  {
    IsRunning = false;
    IsActive = false;
    _acceptLoopTask = null;
    _listener = null;

    try
    {
      listener?.Stop();
      listener?.Close();
    }
    catch (Exception ex)
    {
      _log.Warn($"WebSocketServer: error closing listener after failed start: {ex.Message}");
    }

    _cts?.Dispose();
    _cts = null;
  }

  /// <inheritdoc/>
  public async Task BroadcastAsync(WebSocketMessage message)
  {
    var json = _protocolHandler.SerializeMessage(message);
    _log.Print($"WebSocketServer broadcasting: type={message.Type}, id={message.Id}");

    var sentCount = 0;
    foreach (var conn in _connectionManager.GetAllConnections())
    {
      try
      {
        await _connectionManager.SendAsync(conn.Id, json);
        sentCount++;
      }
      catch (Exception ex)
      {
        // 单个连接失败不影响其它目标：记录后继续，失败明细由调用方按目标群汇总
        _log.Warn($"WebSocketServer broadcast failed for {conn.Id}: {ex.Message}");
      }
    }

    _log.Print($"WebSocketServer broadcast delivered to {sentCount} connection(s).");
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _heartbeatService.OnHeartbeatTimeout -= HandleHeartbeatTimeout;
    _cts?.Cancel();
    _cts?.Dispose();
    _acceptLoopTask = null;
    _listener?.Close();
    IsActive = false;
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// 接受连接循环：只服务传入的 <paramref name="listener"/> 实例，直到被取消或监听器被关闭。
  /// </summary>
  /// <param name="listener">本次启动创建的监听器（不读字段，避免重启换实例时操作错对象）。</param>
  /// <param name="token">取消令牌。</param>
  private async Task AcceptConnectionsLoop(HttpListener listener, CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        var context = await listener.GetContextAsync();

        if (context.Request.IsWebSocketRequest)
        {
          _ = Task.Run(() => HandleConnection(context, token), token);
        }
        else
        {
          _log.Warn("WebSocketServer: rejected non-WS request.");
          context.Response.StatusCode = 400;
          context.Response.Close();
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (HttpListenerException)
      {
        break;
      }
      catch (ObjectDisposedException)
      {
        break;
      }
      catch (InvalidOperationException)
      {
        // 监听器已停止后 GetContextAsync 可能抛此异常，属停止的正常路径
        break;
      }
      catch (Exception ex)
      {
        if (token.IsCancellationRequested)
        {
          break;
        }

        _log.Err($"WebSocketServer.AcceptConnectionsLoop error: {ex.GetType().Name}: {ex.Message}");
        try
        {
          await Task.Delay(1000, token);
        }
        catch (OperationCanceledException)
        {
          break;
        }
      }
    }
  }

  private async Task HandleConnection(HttpListenerContext context, CancellationToken token)
  {
    var remoteEndPoint = context.Request.RemoteEndPoint?.ToString() ?? "unknown";

    // Token 鉴权
    if (_enableAuth)
    {
      var tokenFromQuery = context.Request.QueryString["token"];
      var tokenFromHeader = context.Request.Headers["Authorization"];

      var providedToken =
        tokenFromQuery
        ?? (
          tokenFromHeader?.StartsWith("Bearer ", StringComparison.Ordinal) == true
            ? tokenFromHeader["Bearer ".Length..]
            : null
        );

      if (string.IsNullOrEmpty(providedToken) || providedToken != _authToken)
      {
        _log.Warn($"WebSocketServer: auth failed for {remoteEndPoint}.");
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
      }
    }

    // 检查连接数限制
    if (_connectionManager.IsFull)
    {
      _log.Warn(
        $"WebSocketServer: connection limit reached ({_connectionManager.MaxConnections})."
      );
      context.Response.StatusCode = 503;
      context.Response.Close();
      return;
    }

    try
    {
      var wsContext = await context.AcceptWebSocketAsync(null);
      var ws = wsContext.WebSocket;

      var connectionId = _connectionManager.RegisterConnection(ws, remoteEndPoint);
      _heartbeatService.StartHeartbeat(connectionId, ws);

      _log.Print($"WebSocket client connected: {connectionId} from {remoteEndPoint}.");
      OnClientConnected?.Invoke(connectionId);

      await ReceiveLoop(connectionId, ws, token);
    }
    catch (Exception ex)
    {
      _log.Err($"WebSocketServer.HandleConnection error: {ex.GetType().Name}: {ex.Message}");
    }
  }

  private async Task ReceiveLoop(
    string connectionId,
    System.Net.WebSockets.WebSocket ws,
    CancellationToken token
  )
  {
    var buffer = new byte[4096];
    var messageStream = new MemoryStream();

    while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
    {
      try
      {
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);

        if (result.MessageType == WebSocketMessageType.Close)
        {
          _log.Print($"WebSocket client {connectionId} closed the connection.");
          await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
          break;
        }

        if (result.MessageType == WebSocketMessageType.Text)
        {
          messageStream.Write(buffer, 0, result.Count);
          if (result.EndOfMessage)
          {
            var text = Encoding.UTF8.GetString(
              messageStream.GetBuffer(),
              0,
              (int)messageStream.Length
            );
            messageStream.SetLength(0);
            _connectionManager.UpdateLastActive(connectionId);
            _ = Task.Run(() => ProcessMessage(connectionId, text));
          }
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (WebSocketException ex)
      {
        _log.Warn($"WebSocket receive error for {connectionId}: {ex.GetType().Name}: {ex.Message}");
        break;
      }
    }

    // 清理连接
    _heartbeatService.StopHeartbeat(connectionId);
    _connectionManager.UnregisterConnection(connectionId);
    _log.Print($"WebSocket client disconnected: {connectionId}.");
    OnClientDisconnected?.Invoke(connectionId);
  }

  private async Task ProcessMessage(string connectionId, string rawMessage)
  {
    try
    {
      var message = _protocolHandler.ParseMessage(rawMessage);
      _log.Print($"WebSocket received from {connectionId}: type={message.Type}, id={message.Id}");

      var responses = await _messageRouter.RouteAsync(message, connectionId);

      foreach (var response in responses)
      {
        var responseJson = _protocolHandler.SerializeMessage(response);
        _log.Print($"WebSocket sending to {connectionId}: type={response.Type}, id={response.Id}");
        await _connectionManager.SendAsync(connectionId, responseJson);
      }
    }
    catch (ProtocolException ex)
    {
      _log.Warn($"Protocol error from {connectionId}: [{ex.ErrorCode}] {ex.Message}");
      var errorMsg = WebSocketMessage.CreateError("unknown", ex.ErrorCode, ex.Message);
      var errorJson = _protocolHandler.SerializeMessage(errorMsg);
      await _connectionManager.SendAsync(connectionId, errorJson);
    }
    catch (Exception ex)
    {
      _log.Err($"Message processing error for {connectionId}: {ex.GetType().Name}: {ex.Message}");
      var errorMsg = WebSocketMessage.CreateError("unknown", "INTERNAL_ERROR", ex.Message);
      var errorJson = _protocolHandler.SerializeMessage(errorMsg);
      await _connectionManager.SendAsync(connectionId, errorJson);
    }
  }

  private void HandleHeartbeatTimeout(string connectionId)
  {
    _log.Warn($"Heartbeat timeout for {connectionId}, disconnecting.");
    _heartbeatService.StopHeartbeat(connectionId);
    _connectionManager.UnregisterConnection(connectionId);
    OnClientDisconnected?.Invoke(connectionId);
  }
}
