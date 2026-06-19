namespace AutoCMEX.Core.WebSocket;

using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// WebSocket 消息处理器接口
/// </summary>
public interface IMessageHandler
{
  /// <summary>判断是否能处理指定类型的消息</summary>
  bool CanHandle(string messageType);

  /// <summary>处理消息，返回需要发送的响应消息列表</summary>
  Task<IReadOnlyList<WebSocketMessage>> HandleAsync(WebSocketMessage message, string connectionId);
}
