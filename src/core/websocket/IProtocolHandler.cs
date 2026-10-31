namespace AutoCMEX.Core.WebSocket;

/// <summary>
/// WebSocket 协议处理器接口
/// </summary>
public interface IProtocolHandler
{
  /// <summary>解析原始消息为 WebSocketMessage</summary>
  WebSocketMessage ParseMessage(string rawMessage);

  /// <summary>序列化 WebSocketMessage 为 JSON 字符串</summary>
  string SerializeMessage(WebSocketMessage message);
}
