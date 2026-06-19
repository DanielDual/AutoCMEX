namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Guessing;
using Chickensoft.Log;

/// <summary>
/// 命令消息处理器：处理 command 类型消息，替代旧 MessageHandler
/// </summary>
public class CommandHandler : IMessageHandler
{
  private readonly ILog _log;
  private readonly IGuessProcessingService _guessProcessingService;

  /// <summary>
  /// 创建命令处理器
  /// </summary>
  public CommandHandler(ILog log, IGuessProcessingService guessProcessingService)
  {
    _log = log;
    _guessProcessingService = guessProcessingService;
  }

  /// <inheritdoc/>
  public bool CanHandle(string messageType) => messageType == "command";

  /// <inheritdoc/>
  public Task<IReadOnlyList<WebSocketMessage>> HandleAsync(
    WebSocketMessage message,
    string connectionId
  )
  {
    var payload = message.Payload;

    if (!payload.TryGetProperty("action", out var actionEl))
    {
      _log.Warn($"CommandHandler: missing 'action' field in command {message.Id}.");
      return Task.FromResult<IReadOnlyList<WebSocketMessage>>(
        new[]
        {
          WebSocketMessage.CreateError(
            message.Id,
            "INVALID_COMMAND",
            "Missing required field 'action'."
          ),
        }
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
        return Task.FromResult<IReadOnlyList<WebSocketMessage>>(
          new[]
          {
            WebSocketMessage.CreateError(
              message.Id,
              "INVALID_COMMAND",
              $"Unknown action '{action}'."
            ),
          }
        );
    }
  }

  private async Task<IReadOnlyList<WebSocketMessage>> HandleGuess(
    WebSocketMessage message,
    string connectionId
  )
  {
    var payload = message.Payload;

    if (!payload.TryGetProperty("params", out var paramsEl))
    {
      _log.Warn($"CommandHandler: missing 'params' in guess command {message.Id}.");
      return new[]
      {
        WebSocketMessage.CreateError(
          message.Id,
          "INVALID_COMMAND",
          "Missing required field 'params'."
        ),
      };
    }

    if (!paramsEl.TryGetProperty("message", out var msgEl))
    {
      _log.Warn($"CommandHandler: missing 'message' in guess command {message.Id}.");
      return new[]
      {
        WebSocketMessage.CreateError(
          message.Id,
          "INVALID_COMMAND",
          "Missing required field 'params.message'."
        ),
      };
    }

    var text = msgEl.GetString() ?? string.Empty;
    var sender = paramsEl.TryGetProperty("sender", out var sEl) ? sEl.GetString() ?? "" : "";

    _log.Print($"CommandHandler: dispatching guess from {sender} (conn={connectionId}).");

    var result = await _guessProcessingService.ProcessManagedAsync(text);
    var responses = new List<WebSocketMessage>
    {
      WebSocketMessage.CreateAck(message.Id, "success"),
    };

    switch (result.Status)
    {
      case GuessProcessingStatus.Success:
        if (result.ShouldReply)
        {
          responses.Add(
            WebSocketMessage.CreateEvent(
              "guess_result",
              new
              {
                requestId = message.Id,
                replyText = result.ReplyText,
                normalizedGuess = result.NormalizedGuess,
              }
            )
          );
        }
        else
        {
          _log.Print(
            $"CommandHandler: guess {message.Id} processed successfully but produced no reply."
          );
        }

        break;

      case GuessProcessingStatus.NotGuess:
        _log.Print(
          $"CommandHandler: guess {message.Id} treated as non-guess: {result.FailureReason}"
        );
        break;

      case GuessProcessingStatus.Error:
        _log.Err($"CommandHandler: guess {message.Id} failed: {result.FailureReason}");
        break;
    }

    return responses;
  }

  private static Task<IReadOnlyList<WebSocketMessage>> HandlePing(WebSocketMessage message)
  {
    var pong = WebSocketMessage.CreateAck(message.Id, "success");
    return Task.FromResult<IReadOnlyList<WebSocketMessage>>(new[] { pong });
  }
}
