namespace AutoCMEX.Core.WebSocket;

using System;
using System.Threading.Tasks;

/// <summary>
/// WebSocket 服务器接口
/// </summary>
public interface IWebSocketServer
{
  /// <summary>启动服务器</summary>
  Task StartAsync();

  /// <summary>停止服务器</summary>
  Task StopAsync();

  /// <summary>服务器是否正在运行</summary>
  bool IsRunning { get; }

  /// <summary>当前连接数</summary>
  int ConnectionCount { get; }

  /// <summary>客户端连接事件（参数：connectionId）</summary>
  event Action<string>? OnClientConnected;

  /// <summary>客户端断开事件（参数：connectionId）</summary>
  event Action<string>? OnClientDisconnected;

  /// <summary>
  /// 向已连接的对端主动推送消息（非应答）。
  /// </summary>
  /// <param name="message">出站消息。</param>
  /// <remarks>
  /// <para>
  /// 用于由本端发起的推送（信息板块的发布请求、群列表查询等），与"收到消息后回包"区分开。
  /// </para>
  /// <para>
  /// 无可用连接时直接返回，调用方应先用 <see cref="ConnectionCount"/> 预检，避免请求被静默丢弃。
  /// </para>
  /// </remarks>
  Task BroadcastAsync(WebSocketMessage message);
}
