namespace AutoCMEX.Core.WebSocket;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// WebSocket 消息分发器
/// </summary>
public class MessageHandler
{
  private readonly WebSocketServer _server;
  private readonly ILog _log;

  public event Action<string, string, string>? OnGuessMessage;

  public MessageHandler(WebSocketServer server)
    : this(server, AppLogs.GetOrCreate().GetLogger(nameof(MessageHandler))) { }

  public MessageHandler(WebSocketServer server, ILog log)
  {
    _server = server;
    _log = log;
    _server.OnMessageReceived += HandleMessage;
  }

  /// <summary>
  /// 发送回应消息
  /// </summary>
  public async Task SendResponseAsync(string text, string originalMessageId)
  {
    var message = JsonSerializer.Serialize(
      new { type = "response", payload = new { text, original_message_id = originalMessageId } }
    );
    _log.Print(
      $"MessageHandler.SendResponseAsync: id={originalMessageId}, text_len={text?.Length ?? 0}"
    );
    await _server.SendAsync(message);
  }

  private void HandleMessage(string rawMessage)
  {
    _log.Print($"MessageHandler received message: len={rawMessage?.Length ?? 0}");
    try
    {
      using var doc = JsonDocument.Parse(rawMessage ?? "{}");
      var root = doc.RootElement;

      var type = root.GetProperty("type").GetString();

      switch (type)
      {
        case "guess_message":
          var payload = root.GetProperty("payload");
          var text = payload.GetProperty("text").GetString() ?? string.Empty;
          var sender = payload.TryGetProperty("sender", out var s) ? s.GetString() ?? "" : "";
          var timestamp = payload.TryGetProperty("timestamp", out var t) ? t.GetString() ?? "" : "";
          _log.Print($"Dispatching guess_message: sender={sender}, text_len={text.Length}");
          OnGuessMessage?.Invoke(text, sender, timestamp);
          break;

        case "heartbeat":
          _log.Print("Received heartbeat, replying.");
          // 心跳消息，回复心跳
          _ = _server.SendAsync(
            JsonSerializer.Serialize(
              new
              {
                type = "heartbeat",
                payload = new { timestamp = DateTime.UtcNow.ToString("o") },
              }
            )
          );
          break;

        default:
          _log.Warn($"MessageHandler: unknown message type '{type}'.");
          break;
      }
    }
    catch (Exception ex)
    {
      _log.Err($"MessageHandler: failed to parse message: {ex.GetType().Name}: {ex.Message}");
    }
  }
}
