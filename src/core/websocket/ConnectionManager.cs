namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// WebSocket 连接管理器：注册/注销/查询、每连接发送队列、广播、并发安全
/// </summary>
public class ConnectionManager : IConnectionManager, IDisposable
{
  private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
  private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _sendQueues = new();
  private readonly ConcurrentDictionary<string, SemaphoreSlim> _sendLocks = new();
  private bool _disposed;

  /// <inheritdoc/>
  public int Count => _connections.Count;

  /// <inheritdoc/>
  public int MaxConnections { get; }

  /// <inheritdoc/>
  public bool IsFull => _connections.Count >= MaxConnections;

  /// <summary>
  /// 创建连接管理器
  /// </summary>
  /// <param name="maxConnections">最大并发连接数</param>
  public ConnectionManager(int maxConnections = 100)
  {
    MaxConnections = maxConnections;
  }

  /// <inheritdoc/>
  public string RegisterConnection(System.Net.WebSockets.WebSocket webSocket, string remoteEndPoint)
  {
    if (IsFull)
      throw new InvalidOperationException($"Connection limit reached ({MaxConnections}).");

    var connectionId = Guid.NewGuid().ToString();
    var info = new ConnectionInfo
    {
      Id = connectionId,
      Socket = webSocket,
      ConnectedAt = DateTime.UtcNow,
      LastActiveAt = DateTime.UtcNow,
      RemoteEndPoint = remoteEndPoint,
    };

    if (!_connections.TryAdd(connectionId, info))
      throw new InvalidOperationException($"Failed to register connection {connectionId}.");

    _sendQueues[connectionId] = new ConcurrentQueue<string>();
    _sendLocks[connectionId] = new SemaphoreSlim(1, 1);

    return connectionId;
  }

  /// <inheritdoc/>
  public void UnregisterConnection(string connectionId)
  {
    _connections.TryRemove(connectionId, out _);
    _sendQueues.TryRemove(connectionId, out _);

    if (_sendLocks.TryRemove(connectionId, out var sem))
      sem.Dispose();
  }

  /// <inheritdoc/>
  public ConnectionInfo? GetConnection(string connectionId)
  {
    _connections.TryGetValue(connectionId, out var info);
    return info;
  }

  /// <inheritdoc/>
  public IReadOnlyList<ConnectionInfo> GetAllConnections()
  {
    return _connections.Values.ToList().AsReadOnly();
  }

  /// <inheritdoc/>
  public async Task SendAsync(string connectionId, string message)
  {
    if (!_sendQueues.TryGetValue(connectionId, out var queue))
      return;

    queue.Enqueue(message);

    // 触发发送循环
    if (_sendLocks.TryGetValue(connectionId, out var sem))
    {
      await sem.WaitAsync();
      try
      {
        await FlushQueue(connectionId);
      }
      finally
      {
        sem.Release();
      }
    }
  }

  /// <inheritdoc/>
  public async Task BroadcastAsync(string message)
  {
    var tasks = _connections.Keys.Select(id => SendAsync(id, message));
    await Task.WhenAll(tasks);
  }

  /// <summary>
  /// 更新连接的最后活跃时间
  /// </summary>
  public void UpdateLastActive(string connectionId)
  {
    if (_connections.TryGetValue(connectionId, out var info))
      info.LastActiveAt = DateTime.UtcNow;
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;

    foreach (var sem in _sendLocks.Values)
      sem.Dispose();

    _sendLocks.Clear();
    _sendQueues.Clear();
    _connections.Clear();

    GC.SuppressFinalize(this);
  }

  private async Task FlushQueue(string connectionId)
  {
    if (!_connections.TryGetValue(connectionId, out var info))
      return;

    if (info.Socket.State != WebSocketState.Open)
      return;

    if (!_sendQueues.TryGetValue(connectionId, out var queue))
      return;

    while (queue.TryDequeue(out var message))
    {
      try
      {
        var bytes = Encoding.UTF8.GetBytes(message);
        await info.Socket.SendAsync(
          new ArraySegment<byte>(bytes),
          WebSocketMessageType.Text,
          true,
          CancellationToken.None
        );
      }
      catch (WebSocketException)
      {
        // 发送失败，消息丢失（连接可能已断开）
        break;
      }
    }
  }
}
