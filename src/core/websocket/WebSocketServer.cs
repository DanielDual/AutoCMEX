namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// WebSocket 服务端
/// </summary>
public class WebSocketServer : IDisposable
{
  private readonly int _port;
  private readonly ILog _log;
  private HttpListener? _listener;
  private CancellationTokenSource? _cts;
  private readonly ConcurrentQueue<string> _messageQueue = new();
  private System.Net.WebSockets.WebSocket? _connectedSocket;
  private readonly object _socketLock = new();
  private bool _disposed;

  public event Action<string>? OnMessageReceived;
  public bool IsRunning { get; private set; }

  public WebSocketServer(int port)
    : this(port, AppLogs.GetOrCreate().GetLogger(nameof(WebSocketServer))) { }

  public WebSocketServer(int port, ILog log)
  {
    _port = port;
    _log = log;
  }

  /// <summary>
  /// 启动 WebSocket 服务
  /// </summary>
  public void Start()
  {
    if (IsRunning)
      return;

    _cts = new CancellationTokenSource();
    _listener = new HttpListener();
    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
    _listener.Start();
    IsRunning = true;
    _log.Print($"WebSocketServer started on port {_port}.");

    _ = Task.Run(() => ListenLoop(_cts.Token));
  }

  /// <summary>
  /// 停止 WebSocket 服务
  /// </summary>
  public void Stop()
  {
    if (!IsRunning)
      return;
    IsRunning = false;
    _cts?.Cancel();

    lock (_socketLock)
    {
      _connectedSocket?.Dispose();
      _connectedSocket = null;
    }

    _listener?.Stop();
    _listener?.Close();
    _log.Print($"WebSocketServer stopped on port {_port}.");
  }

  /// <summary>
  /// 发送消息到客户端
  /// </summary>
  public async Task SendAsync(string message)
  {
    System.Net.WebSockets.WebSocket? socket;
    lock (_socketLock)
    {
      socket = _connectedSocket;
    }

    if (socket == null || socket.State != WebSocketState.Open)
    {
      _log.Warn(
        $"WebSocketServer.SendAsync: no open client, queueing message (len={message?.Length ?? 0})."
      );
      _messageQueue.Enqueue(message);
      return;
    }

    try
    {
      var bytes = Encoding.UTF8.GetBytes(message);
      await socket.SendAsync(
        new ArraySegment<byte>(bytes),
        WebSocketMessageType.Text,
        true,
        CancellationToken.None
      );
      _log.Print($"WebSocketServer.SendAsync: sent {bytes.Length} bytes.");
    }
    catch (Exception ex)
    {
      _log.Err($"WebSocketServer.SendAsync failed: {ex.GetType().Name}: {ex.Message}");
      throw;
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    Stop();
    _cts?.Dispose();
    _listener?.Close();
    GC.SuppressFinalize(this);
  }

  private async Task ListenLoop(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        var context = await _listener!.GetContextAsync();

        if (context.Request.IsWebSocketRequest)
        {
          var wsContext = await context.AcceptWebSocketAsync(null);
          var ws = wsContext.WebSocket;

          lock (_socketLock)
          {
            _connectedSocket?.Dispose();
            _connectedSocket = ws;
          }
          _log.Print("WebSocket client connected.");

          // 发送缓存消息
          int flushed = 0;
          while (_messageQueue.TryDequeue(out var msg))
          {
            var bytes = Encoding.UTF8.GetBytes(msg);
            await ws.SendAsync(
              new ArraySegment<byte>(bytes),
              WebSocketMessageType.Text,
              true,
              token
            );
            flushed++;
          }
          if (flushed > 0)
            _log.Print($"Flushed {flushed} queued messages to client.");

          await ReceiveLoop(ws, token);
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
      catch (Exception ex)
      {
        _log.Err($"WebSocketServer.ListenLoop error: {ex.GetType().Name}: {ex.Message}");
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

  private async Task ReceiveLoop(System.Net.WebSockets.WebSocket ws, CancellationToken token)
  {
    var buffer = new byte[4096];

    while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
    {
      try
      {
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);

        if (result.MessageType == WebSocketMessageType.Close)
        {
          _log.Print("WebSocket client closed the connection.");
          await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
          break;
        }

        if (result.MessageType == WebSocketMessageType.Text)
        {
          var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
          _log.Print($"WebSocket received {text.Length} chars.");
          OnMessageReceived?.Invoke(text);
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (WebSocketException ex)
      {
        _log.Warn($"WebSocket receive error: {ex.GetType().Name}: {ex.Message}");
        break;
      }
    }

    lock (_socketLock)
    {
      if (_connectedSocket == ws)
        _connectedSocket = null;
    }
    _log.Print("WebSocket client disconnected.");
  }
}
