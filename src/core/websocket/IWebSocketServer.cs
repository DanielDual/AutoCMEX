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
}
