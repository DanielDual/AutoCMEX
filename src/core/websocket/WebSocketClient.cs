namespace AutoCMEX.Core.WebSocket;

using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// WebSocket 客户端（ws-reserve 模式）：主动连接 Koishi WebSocket 服务
/// </summary>
public class WebSocketClient : IWebSocketServer, IDisposable
{
  private readonly string _url;
  private readonly ILog _log;
  private readonly IProtocolHandler _protocolHandler;
  private readonly MessageRouter _messageRouter;
  private readonly int _reconnectIntervalMs;
  private readonly int _heartbeatIntervalMs;
  private ClientWebSocket? _ws;
  private CancellationTokenSource? _cts;
  private bool _disposed;

  /// <inheritdoc/>
  public bool IsRunning { get; private set; }

  /// <inheritdoc/>
  public int ConnectionCount => IsRunning ? 1 : 0;

  /// <inheritdoc/>
  public event Action<string>? OnClientConnected;

  /// <inheritdoc/>
  public event Action<string>? OnClientDisconnected;

  /// <summary>
  /// 创建 WebSocket 客户端
  /// </summary>
  /// <param name="url">Koishi WebSocket 服务地址（如 ws://localhost:5140）</param>
  /// <param name="protocolHandler">协议处理器</param>
  /// <param name="messageRouter">消息路由器</param>
  /// <param name="reconnectIntervalMs">重连间隔（毫秒）</param>
  /// <param name="heartbeatIntervalMs">心跳间隔（毫秒）</param>
  public WebSocketClient(
    string url,
    IProtocolHandler protocolHandler,
    MessageRouter messageRouter,
    int reconnectIntervalMs = 5000,
    int heartbeatIntervalMs = 30000
  )
    : this(
      url,
      protocolHandler,
      messageRouter,
      reconnectIntervalMs,
      heartbeatIntervalMs,
      AppLogs.GetOrCreate().GetLogger(nameof(WebSocketClient))
    ) { }

  /// <summary>
  /// 创建 WebSocket 客户端（带日志注入）
  /// </summary>
  public WebSocketClient(
    string url,
    IProtocolHandler protocolHandler,
    MessageRouter messageRouter,
    int reconnectIntervalMs,
    int heartbeatIntervalMs,
    ILog log
  )
  {
    _url = url;
    _protocolHandler = protocolHandler;
    _messageRouter = messageRouter;
    _reconnectIntervalMs = reconnectIntervalMs;
    _heartbeatIntervalMs = heartbeatIntervalMs;
    _log = log;
  }

  /// <inheritdoc/>
  public Task StartAsync()
  {
    if (IsRunning)
      return Task.CompletedTask;

    _cts = new CancellationTokenSource();
    _ = Task.Run(() => ConnectLoop(_cts.Token));
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public async Task StopAsync()
  {
    if (!IsRunning)
      return;

    _cts?.Cancel();
    IsRunning = false;

    if (_ws?.State == WebSocketState.Open)
    {
      try
      {
        await _ws.CloseAsync(
          WebSocketCloseStatus.NormalClosure,
          "Client stopping.",
          CancellationToken.None
        );
      }
      catch (Exception ex)
      {
        _log.Warn($"WebSocketClient: error closing connection: {ex.Message}");
      }
    }

    _ws?.Dispose();
    _ws = null;
    _log.Print("WebSocketClient stopped.");
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _cts?.Cancel();
    _cts?.Dispose();
    _ws?.Dispose();
    GC.SuppressFinalize(this);
  }

  private async Task ConnectLoop(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _log.Print($"WebSocketClient: connecting to {_url}...");

        await _ws.ConnectAsync(new Uri(_url), token);
        IsRunning = true;
        var connectionId = "koishi-client";
        _log.Print($"WebSocketClient: connected to {_url}.");
        OnClientConnected?.Invoke(connectionId);

        // 启动心跳
        _ = Task.Run(() => HeartbeatLoop(connectionId, token), token);

        // 消息接收循环
        await ReceiveLoop(connectionId, token);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        _log.Warn($"WebSocketClient: connection failed: {ex.Message}");
      }

      IsRunning = false;
      OnClientDisconnected?.Invoke("koishi-client");

      if (!token.IsCancellationRequested)
      {
        _log.Print($"WebSocketClient: reconnecting in {_reconnectIntervalMs}ms...");
        try
        {
          await Task.Delay(_reconnectIntervalMs, token);
        }
        catch (OperationCanceledException)
        {
          break;
        }
      }
    }
  }

  private async Task ReceiveLoop(string connectionId, CancellationToken token)
  {
    var buffer = new byte[4096];

    while (_ws?.State == WebSocketState.Open && !token.IsCancellationRequested)
    {
      try
      {
        var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);

        if (result.MessageType == WebSocketMessageType.Close)
        {
          _log.Print(
            $"WebSocketClient: server closed connection. "
              + $"Status={result.CloseStatus}, Desc={result.CloseStatusDescription}"
          );
          break;
        }

        if (result.MessageType == WebSocketMessageType.Text)
        {
          var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
          await ProcessMessage(connectionId, text);
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (WebSocketException ex)
      {
        _log.Warn($"WebSocketClient: receive error: {ex.Message}");
        break;
      }
    }
  }

  private async Task ProcessMessage(string connectionId, string rawMessage)
  {
    try
    {
      var message = _protocolHandler.ParseMessage(rawMessage);
      _log.Print($"WebSocketClient received: type={message.Type}, id={message.Id}");

      var responses = await _messageRouter.RouteAsync(message, connectionId);

      foreach (var response in responses)
      {
        if (_ws?.State != WebSocketState.Open)
          break;

        var responseJson = _protocolHandler.SerializeMessage(response);
        _log.Print($"WebSocketClient sending: type={response.Type}, id={response.Id}");
        var bytes = Encoding.UTF8.GetBytes(responseJson);
        await _ws.SendAsync(
          new ArraySegment<byte>(bytes),
          WebSocketMessageType.Text,
          true,
          CancellationToken.None
        );
      }
    }
    catch (ProtocolException ex)
    {
      _log.Warn($"WebSocketClient: protocol error: [{ex.ErrorCode}] {ex.Message}");
    }
    catch (Exception ex)
    {
      _log.Err($"WebSocketClient: message processing error: {ex.Message}");
    }
  }

  private async Task HeartbeatLoop(string connectionId, CancellationToken token)
  {
    while (!token.IsCancellationRequested && _ws?.State == WebSocketState.Open)
    {
      try
      {
        await Task.Delay(_heartbeatIntervalMs, token);

        if (_ws?.State != WebSocketState.Open)
          break;

        var ping = WebSocketMessage.CreateAck("ping", "ping");
        var json = _protocolHandler.SerializeMessage(ping);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        _log.Warn($"WebSocketClient: heartbeat error: {ex.Message}");
        break;
      }
    }
  }
}
