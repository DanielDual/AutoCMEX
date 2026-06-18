namespace AutoCMEX.Core.WebSocket;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Chickensoft.Log;

/// <summary>
/// 命令消息处理器：处理 command 类型消息，替代旧 MessageHandler
/// </summary>
public class CommandHandler : IMessageHandler
{
  private readonly ILog _log;

  /// <summary>猜测消息事件（text, sender, timestamp, connectionId）</summary>
  public event Action<string, string, string, string>? OnGuessMessage;

  /// <summary>
  /// 创建命令处理器
  /// </summary>
  public CommandHandler(ILog log)
  {
    _log = log;
  }

  /// <inheritdoc/>
  public bool CanHandle(string messageType) => messageType == "command";

  /// <inheritdoc/>
  public Task<WebSocketMessage?> HandleAsync(WebSocketMessage message, string connectionId)
  {
    var payload = message.Payload;

    if (!payload.TryGetProperty("action", out var actionEl))
    {
      _log.Warn($"CommandHandler: missing 'action' field in command {message.Id}.");
      return Task.FromResult<WebSocketMessage?>(
        WebSocketMessage.CreateError(
          message.Id,
          "INVALID_COMMAND",
          "Missing required field 'action'."
        )
      );
    }

    var action = actionEl.GetString() ?? string.Empty;

    switch (action)
    {
      case "guess":
        return HandleGuess(message, connectionId);

      case "ping":
        return HandlePing(message);

      default:
        _log.Warn($"CommandHandler: unknown action '{action}' in command {message.Id}.");
        return Task.FromResult<WebSocketMessage?>(
          WebSocketMessage.CreateError(message.Id, "INVALID_COMMAND", $"Unknown action '{action}'.")
        );
    }
  }

  private Task<WebSocketMessage?> HandleGuess(WebSocketMessage message, string connectionId)
  {
    var payload = message.Payload;

    if (!payload.TryGetProperty("params", out var paramsEl))
    {
      _log.Warn($"CommandHandler: missing 'params' in guess command {message.Id}.");
      return Task.FromResult<WebSocketMessage?>(
        WebSocketMessage.CreateError(
          message.Id,
          "INVALID_COMMAND",
          "Missing required field 'params'."
        )
      );
    }

    var text = paramsEl.TryGetProperty("message", out var msgEl)
      ? msgEl.GetString() ?? string.Empty
      : string.Empty;
    var sender = paramsEl.TryGetProperty("sender", out var sEl) ? sEl.GetString() ?? "" : "";
    var timestamp = paramsEl.TryGetProperty("timestamp", out var tEl) ? tEl.GetString() ?? "" : "";

    _log.Print($"CommandHandler: dispatching guess from {sender} (conn={connectionId}).");
    OnGuessMessage?.Invoke(text, sender, timestamp, connectionId);

    // 返回 ACK 确认收到
    var ack = WebSocketMessage.CreateAck(message.Id, "success");
    return Task.FromResult<WebSocketMessage?>(ack);
  }

  private static Task<WebSocketMessage?> HandlePing(WebSocketMessage message)
  {
    var pong = WebSocketMessage.CreateAck(message.Id, "success");
    return Task.FromResult<WebSocketMessage?>(pong);
  }
}
