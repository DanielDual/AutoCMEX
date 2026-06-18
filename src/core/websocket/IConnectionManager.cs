namespace AutoCMEX.Core.WebSocket;

using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading.Tasks;

/// <summary>
/// WebSocket 连接管理器接口
/// </summary>
public interface IConnectionManager
{
  /// <summary>注册新连接，返回 connectionId</summary>
  string RegisterConnection(System.Net.WebSockets.WebSocket webSocket, string remoteEndPoint);

  /// <summary>注销连接</summary>
  void UnregisterConnection(string connectionId);

  /// <summary>获取指定连接信息</summary>
  ConnectionInfo? GetConnection(string connectionId);

  /// <summary>获取所有连接信息</summary>
  IReadOnlyList<ConnectionInfo> GetAllConnections();

  /// <summary>向指定连接发送消息</summary>
  Task SendAsync(string connectionId, string message);

  /// <summary>向所有连接广播消息</summary>
  Task BroadcastAsync(string message);

  /// <summary>当前连接数</summary>
  int Count { get; }

  /// <summary>最大连接数</summary>
  int MaxConnections { get; }

  /// <summary>是否已达到最大连接数</summary>
  bool IsFull { get; }

  /// <summary>更新连接的最后活跃时间</summary>
  void UpdateLastActive(string connectionId);
}
