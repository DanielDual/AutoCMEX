namespace AutoCMEX.Core.WebSocket;

using System;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// WebSocket 初始化器：封装 WebSocket 服务器/客户端的创建逻辑，
/// 消除 <see cref="AutoCMEX.UI.Main.MainWindow"/> 中 OnReady 与 RestartWebSocket 的重复代码。
/// </summary>
public class WebSocketInitializer
{
  private readonly ILog _log;
  private readonly IGuessProcessingService _guessProcessingService;

  /// <summary>
  /// 创建 WebSocket 初始化器。
  /// </summary>
  /// <param name="log">日志接口。</param>
  /// <param name="guessProcessingService">猜测处理服务。</param>
  public WebSocketInitializer(ILog log, IGuessProcessingService guessProcessingService)
  {
    _log = log;
    _guessProcessingService = guessProcessingService;
  }

  /// <summary>
  /// 根据设置创建 WebSocket 服务器或客户端实例。
  /// </summary>
  /// <param name="settings">应用设置。</param>
  /// <returns>配置好的 WebSocket 服务器实例。</returns>
  public IWebSocketServer CreateServer(AppSettings settings)
  {
    var protocolHandler = new ProtocolHandler();
    var messageRouter = new MessageRouter(_log);

    var commandHandler = new CommandHandler(_log, _guessProcessingService);
    var eventHandler = new EventHandler(_log);
    messageRouter.RegisterHandler(commandHandler);
    messageRouter.RegisterHandler(eventHandler);

    var isClientMode = string.Equals(
      settings.WebSocketMode,
      "Client",
      StringComparison.OrdinalIgnoreCase
    );

    if (isClientMode && !string.IsNullOrEmpty(settings.KoishiWebSocketUrl))
    {
      var clientUrl = WebSocketClient.BuildClientUrl(settings);
      return new WebSocketClient(
        clientUrl,
        protocolHandler,
        messageRouter,
        reconnectIntervalMs: 5000,
        heartbeatIntervalMs: settings.WebSocketHeartbeatIntervalMs,
        _log
      );
    }

    var connectionManager = new ConnectionManager(settings.WebSocketMaxConnections);
    var heartbeatService = new HeartbeatService(
      settings.WebSocketHeartbeatIntervalMs,
      settings.WebSocketHeartbeatTimeoutMs,
      _log
    );

    return new WebSocketServer(
      settings.WebSocketPort,
      connectionManager,
      protocolHandler,
      messageRouter,
      heartbeatService,
      settings.WebSocketEnableAuth,
      settings.WebSocketAuthToken,
      _log
    );
  }
}
