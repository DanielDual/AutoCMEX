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

  /// <summary>
  /// 本实例**实际**运行模式："Server"（监听端口）或 "Client"（主动连接对端）。
  /// </summary>
  /// <remarks>
  /// 面板与日志一律以此为准，不要用设置里的模式：设置改动后实例可能尚未重建，两者会短暂不一致；
  /// 且 Client 模式在未配置地址时不会回退成 Server，只会在 <see cref="LastError"/> 里说明原因。
  /// </remarks>
  string Mode { get; }

  /// <summary>Server 模式实际监听的端口；Client 模式为 0（不监听端口）。</summary>
  int Port { get; }

  /// <summary>Client 模式实际连接的对端地址（已含 Token）；Server 模式为空串。</summary>
  string Url { get; }

  /// <summary>
  /// 最近一次启动/运行失败的原因（面向用户的中文短语，如「未配置 Koishi 地址」）；
  /// 正常启动后清空，无错误时为空串。用于状态面板显式显示，不代替日志。
  /// </summary>
  string LastError { get; }

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
