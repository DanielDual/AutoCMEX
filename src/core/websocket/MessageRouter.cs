namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chickensoft.Log;

/// <summary>
/// WebSocket 消息路由器：类型分发、处理器注册
/// </summary>
public class MessageRouter
{
  private readonly List<IMessageHandler> _handlers = new();
  private readonly ILog _log;

  /// <summary>
  /// 创建消息路由器
  /// </summary>
  public MessageRouter(ILog log)
  {
    _log = log;
  }

  /// <summary>
  /// 注册消息处理器
  /// </summary>
  public void RegisterHandler(IMessageHandler handler)
  {
    _handlers.Add(handler);
    _log.Print(
      $"MessageRouter: registered handler for type(s) handled by {handler.GetType().Name}."
    );
  }

  /// <summary>
  /// 路由消息到对应处理器
  /// </summary>
  public async Task<WebSocketMessage?> RouteAsync(WebSocketMessage message, string connectionId)
  {
    foreach (var handler in _handlers)
    {
      if (handler.CanHandle(message.Type))
      {
        _log.Print($"MessageRouter: routing type={message.Type} to {handler.GetType().Name}.");
        return await handler.HandleAsync(message, connectionId);
      }
    }

    _log.Warn($"MessageRouter: no handler for message type '{message.Type}'.");
    return WebSocketMessage.CreateError(
      message.Id,
      "UNKNOWN_TYPE",
      $"No handler registered for message type '{message.Type}'."
    );
  }
}
