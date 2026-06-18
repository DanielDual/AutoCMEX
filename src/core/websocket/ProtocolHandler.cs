namespace AutoCMEX.Core.WebSocket;

using System;
using System.Text.Json;

/// <summary>
/// WebSocket 协议处理器：JSON 解析/序列化与消息验证
/// </summary>
public class ProtocolHandler : IProtocolHandler
{
  private const int MaxMessageSizeBytes = 65536; // 64KB

  private static readonly string[] ValidTypes = { "command", "event", "ack" };

  /// <inheritdoc/>
  public WebSocketMessage ParseMessage(string rawMessage)
  {
    if (string.IsNullOrEmpty(rawMessage))
      throw new ProtocolException("INVALID_FORMAT", "Message is empty.");

    if (rawMessage.Length > MaxMessageSizeBytes)
      throw new ProtocolException(
        "INVALID_FORMAT",
        $"Message exceeds max size of {MaxMessageSizeBytes} bytes."
      );

    JsonDocument doc;
    try
    {
      doc = JsonDocument.Parse(rawMessage);
    }
    catch (JsonException ex)
    {
      throw new ProtocolException("INVALID_FORMAT", $"Invalid JSON: {ex.Message}");
    }

    var root = doc.RootElement;

    if (root.ValueKind != JsonValueKind.Object)
      throw new ProtocolException("INVALID_FORMAT", "Message must be a JSON object.");

    var id = root.TryGetProperty("id", out var idEl)
      ? idEl.GetString() ?? Guid.NewGuid().ToString()
      : Guid.NewGuid().ToString();

    if (!root.TryGetProperty("type", out var typeEl))
      throw new ProtocolException("INVALID_FORMAT", "Missing required field 'type'.");

    var type = typeEl.GetString() ?? string.Empty;

    if (Array.IndexOf(ValidTypes, type) < 0)
      throw new ProtocolException(
        "UNKNOWN_TYPE",
        $"Unknown message type '{type}'. Valid types: command, event, ack."
      );

    var timestamp =
      root.TryGetProperty("timestamp", out var tsEl) && tsEl.TryGetInt64(out var ts)
        ? ts
        : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    var payload = root.TryGetProperty("payload", out var pEl)
      ? pEl.Clone()
      : JsonSerializer.SerializeToElement(new { });

    return new WebSocketMessage
    {
      Id = id,
      Type = type,
      Timestamp = timestamp,
      Payload = payload,
    };
  }

  /// <inheritdoc/>
  public string SerializeMessage(WebSocketMessage message)
  {
    var obj = new
    {
      id = message.Id,
      type = message.Type,
      timestamp = message.Timestamp,
      payload = message.Payload,
    };

    return JsonSerializer.Serialize(obj);
  }
}

/// <summary>
/// 协议异常，包含错误码
/// </summary>
public class ProtocolException : Exception
{
  public string ErrorCode { get; }

  public ProtocolException(string errorCode, string message)
    : base(message)
  {
    ErrorCode = errorCode;
  }
}
