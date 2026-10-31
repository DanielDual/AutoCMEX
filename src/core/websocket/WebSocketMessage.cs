namespace AutoCMEX.Core.WebSocket;

using System;
using System.Text.Json;

/// <summary>
/// WebSocket 消息基类
/// </summary>
public class WebSocketMessage
{
  /// <summary>消息唯一标识（UUID）</summary>
  public string Id { get; set; } = Guid.NewGuid().ToString();

  /// <summary>消息类型：command / event / error / ack</summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>发送时间戳（Unix 毫秒）</summary>
  public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

  /// <summary>消息体</summary>
  public JsonElement Payload { get; set; }

  /// <summary>
  /// 创建错误响应消息
  /// </summary>
  public static WebSocketMessage CreateError(string originalId, string code, string message)
  {
    var payload = JsonSerializer.SerializeToElement(
      new
      {
        code,
        message,
        details = new { },
      }
    );

    return new WebSocketMessage
    {
      Id = Guid.NewGuid().ToString(),
      Type = "error",
      Payload = payload,
    };
  }

  /// <summary>
  /// 创建 ACK 确认消息
  /// </summary>
  public static WebSocketMessage CreateAck(string originalId, string status)
  {
    var payload = JsonSerializer.SerializeToElement(new { originalId, status });

    return new WebSocketMessage
    {
      Id = Guid.NewGuid().ToString(),
      Type = "ack",
      Payload = payload,
    };
  }

  /// <summary>
  /// 创建事件消息
  /// </summary>
  public static WebSocketMessage CreateEvent(string eventName, object data)
  {
    var payload = JsonSerializer.SerializeToElement(new { @event = eventName, data });

    return new WebSocketMessage
    {
      Id = Guid.NewGuid().ToString(),
      Type = "event",
      Payload = payload,
    };
  }
}
