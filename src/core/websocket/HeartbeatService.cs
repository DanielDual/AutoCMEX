namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chickensoft.Log;

/// <summary>
/// WebSocket 心跳服务：独立管理每连接的 Ping/Pong，超时自动断开
/// </summary>
public class HeartbeatService : IDisposable
{
  private readonly int _intervalMs;
  private readonly int _timeoutMs;
  private readonly ILog _log;
  private readonly ConcurrentDictionary<string, HeartbeatState> _states = new();
  private bool _disposed;

  /// <summary>心跳超时事件（参数：connectionId）</summary>
  public event Action<string>? OnHeartbeatTimeout;

  /// <summary>
  /// 创建心跳服务
  /// </summary>
  /// <param name="intervalMs">心跳间隔（毫秒）</param>
  /// <param name="timeoutMs">心跳超时（毫秒）</param>
  /// <param name="log">日志实例</param>
  public HeartbeatService(int intervalMs, int timeoutMs, ILog log)
  {
    _intervalMs = intervalMs;
    _timeoutMs = timeoutMs;
    _log = log;
  }

  /// <summary>
  /// 为指定连接启动心跳
  /// </summary>
  public void StartHeartbeat(string connectionId, System.Net.WebSockets.WebSocket socket)
  {
    var state = new HeartbeatState
    {
      ConnectionId = connectionId,
      Socket = socket,
      Cts = new CancellationTokenSource(),
    };

    if (_states.TryAdd(connectionId, state))
    {
      _ = Task.Run(() => HeartbeatLoop(state));
      _log.Print($"HeartbeatService: started for {connectionId}.");
    }
  }

  /// <summary>
  /// 停止指定连接的心跳
  /// </summary>
  public void StopHeartbeat(string connectionId)
  {
    if (_states.TryRemove(connectionId, out var state))
    {
      state.Cts.Cancel();
      state.Cts.Dispose();
      _log.Print($"HeartbeatService: stopped for {connectionId}.");
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;

    foreach (var state in _states.Values)
    {
      state.Cts.Cancel();
      state.Cts.Dispose();
    }

    _states.Clear();
    GC.SuppressFinalize(this);
  }

  private async Task HeartbeatLoop(HeartbeatState state)
  {
    var token = state.Cts.Token;

    while (!token.IsCancellationRequested)
    {
      try
      {
        await Task.Delay(_intervalMs, token);

        if (state.Socket.State != WebSocketState.Open)
          break;

        // 发送 Ping
        var pingCts = new CancellationTokenSource(_timeoutMs);
        try
        {
          await state.Socket.SendAsync(
            new ArraySegment<byte>(Array.Empty<byte>()),
            WebSocketMessageType.Text,
            true,
            pingCts.Token
          );
        }
        catch (OperationCanceledException)
        {
          _log.Warn($"HeartbeatService: timeout for {state.ConnectionId}.");
          OnHeartbeatTimeout?.Invoke(state.ConnectionId);
          break;
        }
        catch (WebSocketException)
        {
          _log.Warn($"HeartbeatService: send failed for {state.ConnectionId}.");
          OnHeartbeatTimeout?.Invoke(state.ConnectionId);
          break;
        }
        finally
        {
          pingCts.Dispose();
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }
  }

  private sealed class HeartbeatState
  {
    public string ConnectionId { get; set; } = string.Empty;
    public System.Net.WebSockets.WebSocket Socket { get; set; } = default!;
    public CancellationTokenSource Cts { get; set; } = default!;
  }
}
