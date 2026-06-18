namespace AutoCMEX.Core.WebSocket;

using System;
using System.Net.WebSockets;

/// <summary>
/// WebSocket 连接信息
/// </summary>
public class ConnectionInfo
{
  /// <summary>连接唯一标识</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>WebSocket 实例</summary>
  public System.Net.WebSockets.WebSocket Socket { get; set; } = default!;

  /// <summary>连接建立时间</summary>
  public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

  /// <summary>最后活跃时间</summary>
  public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

  /// <summary>客户端 IP 地址</summary>
  public string RemoteEndPoint { get; set; } = string.Empty;

  /// <summary>是否已通过鉴权</summary>
  public bool IsAuthenticated { get; set; } = false;
}
