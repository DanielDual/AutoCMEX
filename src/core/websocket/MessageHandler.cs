namespace AutoCMEX.Core.WebSocket;

using System;
using System.Text.Json;

/// <summary>
/// WebSocket 消息分发器
/// </summary>
public class MessageHandler
{
    private readonly WebSocketServer _server;

    public event Action<string, string, string>? OnGuessMessage;

    public MessageHandler(WebSocketServer server)
    {
        _server = server;
        _server.OnMessageReceived += HandleMessage;
    }

    /// <summary>
    /// 发送回应消息
    /// </summary>
    public async System.Threading.Tasks.Task SendResponseAsync(string text, string originalMessageId)
    {
        var message = JsonSerializer.Serialize(new
        {
            type = "response",
            payload = new
            {
                text,
                original_message_id = originalMessageId
            }
        });

        await _server.SendAsync(message);
    }

    private void HandleMessage(string rawMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "guess_message":
                    var payload = root.GetProperty("payload");
                    var text = payload.GetProperty("text").GetString() ?? string.Empty;
                    var sender = payload.TryGetProperty("sender", out var s) ? s.GetString() ?? "" : "";
                    var timestamp = payload.TryGetProperty("timestamp", out var t) ? t.GetString() ?? "" : "";
                    OnGuessMessage?.Invoke(text, sender, timestamp);
                    break;

                case "heartbeat":
                    // 心跳消息，回复心跳
                    _ = _server.SendAsync(JsonSerializer.Serialize(new
                    {
                        type = "heartbeat",
                        payload = new { timestamp = DateTime.UtcNow.ToString("o") }
                    }));
                    break;
            }
        }
        catch (Exception)
        {
            // 忽略无法解析的消息
        }
    }
}
