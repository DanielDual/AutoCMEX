namespace AutoCMEX.Core.WebSocket;

using System;
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
  private bool _disposed;

  /// <inheritdoc/>
  public bool IsRunning { get; private set; }

  /// <inheritdoc/>
  public int ConnectionCount => _connectionManager.Count;

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
  public Task StartAsync()
  {
    if (IsRunning)
      return Task.CompletedTask;

    try
    {
      _cts = new CancellationTokenSource();
      _listener = new HttpListener();
      _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
      _listener.Start();
      IsRunning = true;
      _log.Print($"WebSocketServer started on port {_port}.");

      _ = Task.Run(() => AcceptConnectionsLoop(_cts.Token));
    }
    catch (HttpListenerException ex)
    {
      _log.Err($"WebSocketServer failed to start on port {_port}: {ex.Message}");
      IsRunning = false;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public async Task StopAsync()
  {
    if (!IsRunning)
      return;

    IsRunning = false;
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

    _listener?.Stop();
    _listener?.Close();
    _log.Print($"WebSocketServer stopped on port {_port}.");
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
    _listener?.Close();
    GC.SuppressFinalize(this);
  }

  private async Task AcceptConnectionsLoop(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        var context = await _listener!.GetContextAsync();

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
      catch (Exception ex)
      {
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
          var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
          _connectionManager.UpdateLastActive(connectionId);

          await ProcessMessage(connectionId, text);
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
