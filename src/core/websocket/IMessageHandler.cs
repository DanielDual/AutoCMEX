namespace AutoCMEX.Core.WebSocket;

using System.Threading.Tasks;

/// <summary>
/// WebSocket 消息处理器接口
/// </summary>
public interface IMessageHandler
{
  /// <summary>判断是否能处理指定类型的消息</summary>
  bool CanHandle(string messageType);

  /// <summary>处理消息，返回响应消息（可为 null 表示无需响应）</summary>
  Task<WebSocketMessage?> HandleAsync(WebSocketMessage message, string connectionId);
}
