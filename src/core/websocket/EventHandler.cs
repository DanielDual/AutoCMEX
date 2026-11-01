namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using Chickensoft.Log;

/// <summary>
/// 事件消息处理器：处理 event 类型消息中的 status_query 与信息板块入站回执；连接/断开事件由 WebSocketServer 直接触发，不经此处
/// </summary>
public class EventHandler : IMessageHandler
{
  private readonly ILog _log;
  private readonly InfoEventBus? _infoEvents;

  /// <summary>
  /// 创建事件处理器
  /// </summary>
  public EventHandler(ILog log)
    : this(log, null) { }

  /// <summary>
  /// 创建事件处理器（带信息板块事件总线）
  /// </summary>
  /// <param name="log">日志接口。</param>
  /// <param name="infoEvents">信息板块入站事件总线；为 null 时信息板块事件被丢弃并记一条 warn（不回 error，也不当作未知事件）。</param>
  public EventHandler(ILog log, InfoEventBus? infoEvents)
  {
    _log = log;
    _infoEvents = infoEvents;
  }

  /// <inheritdoc/>
  public bool CanHandle(string messageType) => messageType == "event";

  /// <inheritdoc/>
  public Task<IReadOnlyList<WebSocketMessage>> HandleAsync(
    WebSocketMessage message,
    string connectionId
  )
  {
    var payload = message.Payload;

    if (!payload.TryGetProperty("event", out var eventEl))
    {
      _log.Warn($"EventHandler: missing 'event' field in event {message.Id}.");
      return Task.FromResult<IReadOnlyList<WebSocketMessage>>(
        new[]
        {
          WebSocketMessage.CreateError(
            message.Id,
            "INVALID_COMMAND",
            "Missing required field 'event'."
          ),
        }
      );
    }

    var eventName = eventEl.GetString() ?? string.Empty;

    _log.Print($"EventHandler: received event '{eventName}' from {connectionId}.");

    switch (eventName)
    {
      case "status_query":
        return HandleStatusQuery(message);

      default:
        // 信息板块的入站回执（群列表、发布结果）：先交给总线，再按“无同步应答”返回
        if (InfoEventBus.IsInfoEvent(eventName))
        {
          _infoEvents?.Publish(eventName, payload);

          if (_infoEvents is null)
            _log.Warn($"EventHandler: info event '{eventName}' dropped, bus is not wired.");

          return Task.FromResult<IReadOnlyList<WebSocketMessage>>(Array.Empty<WebSocketMessage>());
        }

        _log.Warn($"EventHandler: unknown event '{eventName}' from {connectionId}.");
        return Task.FromResult<IReadOnlyList<WebSocketMessage>>(
          new[]
          {
            WebSocketMessage.CreateError(
              message.Id,
              "INVALID_COMMAND",
              $"Unknown event '{eventName}'."
            ),
          }
        );
    }
  }

  private static Task<IReadOnlyList<WebSocketMessage>> HandleStatusQuery(WebSocketMessage message)
  {
    var response = WebSocketMessage.CreateEvent(
      "status_response",
      new { status = "running", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
    );

    return Task.FromResult<IReadOnlyList<WebSocketMessage>>(new[] { response });
  }
}
