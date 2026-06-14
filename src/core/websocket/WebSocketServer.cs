namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// WebSocket 服务端
/// </summary>
public class WebSocketServer : IDisposable
{
  private readonly int _port;
  private HttpListener? _listener;
  private CancellationTokenSource? _cts;
  private readonly ConcurrentQueue<string> _messageQueue = new();
  private System.Net.WebSockets.WebSocket? _connectedSocket;
  private readonly object _socketLock = new();
  private bool _disposed;

  public event Action<string>? OnMessageReceived;
  public bool IsRunning { get; private set; }

  public WebSocketServer(int port)
  {
    _port = port;
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

    _ = Task.Run(() => ListenLoop(_cts.Token));
  }

  /// <summary>
  /// 停止 WebSocket 服务
  /// </summary>
  public void Stop()
  {
    IsRunning = false;
    _cts?.Cancel();

    lock (_socketLock)
    {
      _connectedSocket?.Dispose();
      _connectedSocket = null;
    }

    _listener?.Stop();
    _listener?.Close();
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
      _messageQueue.Enqueue(message);
      return;
    }

    var bytes = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(
      new ArraySegment<byte>(bytes),
      WebSocketMessageType.Text,
      true,
      CancellationToken.None
    );
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

          // 发送缓存消息
          while (_messageQueue.TryDequeue(out var msg))
          {
            var bytes = Encoding.UTF8.GetBytes(msg);
            await ws.SendAsync(
              new ArraySegment<byte>(bytes),
              WebSocketMessageType.Text,
              true,
              token
            );
          }

          await ReceiveLoop(ws, token);
        }
        else
        {
          context.Response.StatusCode = 400;
          context.Response.Close();
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception)
      {
        await Task.Delay(1000, token);
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
          await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
          break;
        }

        if (result.MessageType == WebSocketMessageType.Text)
        {
          var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
          OnMessageReceived?.Invoke(text);
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (WebSocketException)
      {
        break;
      }
    }

    lock (_socketLock)
    {
      if (_connectedSocket == ws)
        _connectedSocket = null;
    }
  }
}
