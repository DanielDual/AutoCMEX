namespace AutoCMEX.Core.WebSocket;

using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Models;
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
  private Task? _loopTask;
  private Task? _heartbeatTask;
  private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
  private bool _disposed;
  private string _lastError = string.Empty;

  /// <inheritdoc/>
  public bool IsRunning { get; private set; }

  /// <inheritdoc/>
  public bool IsActive { get; private set; }

  /// <inheritdoc/>
  public int ConnectionCount => IsRunning ? 1 : 0;

  /// <inheritdoc/>
  public string Mode => "Client";

  /// <inheritdoc/>
  public int Port => 0;

  /// <inheritdoc/>
  public string Url => _url;

  /// <inheritdoc/>
  public string LastError => _lastError;

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
  /// <remarks>
  /// 幂等判据是「连接循环是否还活着」，不是 <see cref="IsRunning"/>：断线重连期间
  /// <see cref="IsRunning"/> 为 false 而循环仍在跑，按它判定会再起一条循环，
  /// 同一实例产生两个并发客户端（对端「新连接踢旧连接」→ 互踢震荡）。
  /// </remarks>
  public async Task StartAsync()
  {
    await _lifecycleGate.WaitAsync();
    try
    {
      if (_disposed || _loopTask is { IsCompleted: false })
        return;

      // 未配置对端地址：既不连接、也不回退成 Server，直接失败并把原因留给面板/日志显示
      if (string.IsNullOrWhiteSpace(_url))
      {
        _lastError = "未配置 Koishi 地址";
        _log.Err(
          "WebSocketClient: 未配置 Koishi 地址，Client 模式未启动"
            + "（请在 设置 → 群聊 填写 Koishi 地址）。"
        );
        return;
      }

      _lastError = string.Empty;
      _cts = new CancellationTokenSource();
      var token = _cts.Token;
      IsActive = true;
      _loopTask = Task.Run(() => ConnectLoop(token), token);
    }
    finally
    {
      _lifecycleGate.Release();
    }
  }

  /// <inheritdoc/>
  /// <remarks>
  /// 必须无条件取消并等待连接循环结束，不能用 <see cref="IsRunning"/> 提前返回：
  /// 重连等待中它同样是 false，按它返回会留下仍在跑的循环，5 秒后自己连回来（「停不掉的客户端」）。
  /// 连接循环在每条连接结束时都会取消并等待心跳循环退出，因此等到循环返回即代表本实例上再无活动任务
  /// （不会再有心跳往已释放的 socket 上发 ping）。
  /// </remarks>
  public async Task StopAsync()
  {
    await _lifecycleGate.WaitAsync();
    try
    {
      if (_disposed)
        return;

      _cts?.Cancel();

      var loop = _loopTask;
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
          _log.Warn($"WebSocketClient: connect loop ended with error: {ex.Message}");
        }
      }

      IsRunning = false;
      IsActive = false;
      _loopTask = null;
      _cts?.Dispose();
      _cts = null;

      // 循环已结束、socket 未被中止，这里补一次优雅关闭，让对端记「正常关闭」而不是 1006
      if (_ws?.State == WebSocketState.Open)
      {
        try
        {
          using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
          await _ws.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Client stopping.",
            closeTimeout.Token
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
    finally
    {
      _lifecycleGate.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<int> BroadcastAsync(WebSocketMessage message)
  {
    var ws = _ws;
    if (ws is null || ws.State != WebSocketState.Open)
    {
      _log.Warn($"WebSocketClient: outbound {message.Type} skipped, connection is not open.");
      return 0;
    }

    var json = _protocolHandler.SerializeMessage(message);
    _log.Print($"WebSocketClient sending: type={message.Type}, id={message.Id}");
    var bytes = Encoding.UTF8.GetBytes(json);
    await ws.SendAsync(
      new ArraySegment<byte>(bytes),
      WebSocketMessageType.Text,
      true,
      CancellationToken.None
    );

    // 客户端只有一个对端：送出去就是 1，送不出去（未连接或上面抛异常）就是 0
    return 1;
  }

  /// <summary>
  /// 根据设置构建 Client 模式的 WebSocket URL（自动补全 ws:// 前缀和 Token）。
  /// </summary>
  /// <param name="settings">应用设置。</param>
  /// <returns>
  /// 完整的 WebSocket URL；**未配置地址时返回空串**（不补出 <c>"ws://"</c> 这种半成品），
  /// 由 <see cref="StartAsync"/> 拒绝启动并记录「未配置 Koishi 地址」。
  /// </returns>
  public static string BuildClientUrl(AppSettings settings)
  {
    var url = settings.KoishiWebSocketUrl.Value.Trim();

    if (url.Length == 0)
    {
      return string.Empty;
    }

    // 自动补全 ws:// 前缀
    if (
      !url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
      && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
    )
    {
      url = "ws://" + url;
    }

    // 自动附加 Token
    if (
      settings.WebSocketEnableAuth.Value && !string.IsNullOrEmpty(settings.WebSocketAuthToken.Value)
    )
    {
      var separator = url.Contains('?') ? "&" : "?";
      url = $"{url}{separator}token={Uri.EscapeDataString(settings.WebSocketAuthToken.Value)}";
    }

    return url;
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _cts?.Cancel();
    _cts?.Dispose();
    _loopTask = null;
    _heartbeatTask = null;
    _ws?.Dispose();
    IsActive = false;
    GC.SuppressFinalize(this);
  }

  private async Task ConnectLoop(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      // 每条连接一个独立的心跳作用域：连接结束即取消它。旧写法的心跳是 fire-and-forget 且读共享的 _ws 字段，
      // 重连后旧心跳会跟着新 socket 继续发 ping（每重连一次多一条心跳），也没有任何句柄可以等它退出。
      using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
      var connectionToken = connectionCts.Token;

      try
      {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _log.Print($"WebSocketClient: connecting to {_url}...");

        await _ws.ConnectAsync(new Uri(_url), connectionToken);
        IsRunning = true;
        _lastError = string.Empty;
        var connectionId = "koishi-client";
        _log.Print($"WebSocketClient: connected to {_url}.");
        OnClientConnected?.Invoke(connectionId);

        // 心跳绑定本次连接的 socket 与令牌：socket 一失效它自己就退出，不读共享字段
        var ws = _ws;
        _heartbeatTask = Task.Run(
          () => HeartbeatLoop(connectionId, ws, connectionToken),
          connectionToken
        );

        // 消息接收循环
        await ReceiveLoop(connectionId, connectionToken);
      }
      catch (OperationCanceledException)
      {
        // 停止客户端或取消本次连接：都不是错误，继续与否交给下面的 while 条件
      }
      catch (Exception ex)
      {
        // 记下失败原因：未连接时面板能直接显示「为什么没连上」，而不是只有一个「未连接」
        _lastError = $"连接 {_url} 失败：{ex.Message}";
        _log.Warn($"WebSocketClient: connection failed: {ex.Message}");
      }

      IsRunning = false;
      OnClientDisconnected?.Invoke("koishi-client");

      // 心跳随本次连接一起收尾：先取消连接作用域（不影响整个客户端），再等它真正退出。
      // 连接循环返回前不留后台任务，StopAsync 等待循环结束即代表实例上再无活动任务。
      connectionCts.Cancel();
      await AwaitHeartbeatAsync();

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
    var messageStream = new MemoryStream();

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
          messageStream.Write(buffer, 0, result.Count);
          if (result.EndOfMessage)
          {
            var text = Encoding.UTF8.GetString(
              messageStream.GetBuffer(),
              0,
              (int)messageStream.Length
            );
            messageStream.SetLength(0);
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

  /// <summary>
  /// 心跳循环：按间隔向本次连接的 socket 发 ping，直到连接失效或被取消。
  /// </summary>
  /// <param name="connectionId">连接标识（仅用于日志）。</param>
  /// <param name="ws">本次连接的 socket（不读共享字段，避免重连后旧心跳往新 socket 上发 ping）。</param>
  /// <param name="token">本次连接的取消令牌。</param>
  private async Task HeartbeatLoop(string connectionId, ClientWebSocket ws, CancellationToken token)
  {
    while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
    {
      try
      {
        await Task.Delay(_heartbeatIntervalMs, token);

        if (ws.State != WebSocketState.Open)
          break;

        var ping = WebSocketMessage.CreateAck("ping", "ping");
        var json = _protocolHandler.SerializeMessage(ping);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        _log.Warn($"WebSocketClient: heartbeat error ({connectionId}): {ex.Message}");
        break;
      }
    }
  }

  /// <summary>
  /// 等待心跳循环退出（调用前须已取消它的令牌），保证连接循环返回时没有残留任务。
  /// </summary>
  private async Task AwaitHeartbeatAsync()
  {
    var heartbeat = _heartbeatTask;
    _heartbeatTask = null;

    if (heartbeat is null)
      return;

    try
    {
      await heartbeat;
    }
    catch (OperationCanceledException)
    {
      // 取消是心跳退出的正常路径
    }
    catch (Exception ex)
    {
      _log.Warn($"WebSocketClient: heartbeat loop ended with error: {ex.Message}");
    }
  }
}
