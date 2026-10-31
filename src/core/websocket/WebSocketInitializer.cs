namespace AutoCMEX.Core.WebSocket;

using System;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Info;
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
  private readonly InfoEventBus? _infoEvents;

  /// <summary>
  /// 创建 WebSocket 初始化器。
  /// </summary>
  /// <param name="log">日志接口。</param>
  /// <param name="guessProcessingService">猜测处理服务。</param>
  public WebSocketInitializer(ILog log, IGuessProcessingService guessProcessingService)
    : this(log, guessProcessingService, null) { }

  /// <summary>
  /// 创建 WebSocket 初始化器（带信息板块事件总线）。
  /// </summary>
  /// <param name="log">日志接口。</param>
  /// <param name="guessProcessingService">猜测处理服务。</param>
  /// <param name="infoEvents">
  /// 信息板块入站事件总线；透传给事件处理器，使 WebSocket 重建后回执仍能到达信息面板。
  /// </param>
  public WebSocketInitializer(
    ILog log,
    IGuessProcessingService guessProcessingService,
    InfoEventBus? infoEvents
  )
  {
    _log = log;
    _guessProcessingService = guessProcessingService;
    _infoEvents = infoEvents;
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
    var eventHandler = new EventHandler(_log, _infoEvents);
    messageRouter.RegisterHandler(commandHandler);
    messageRouter.RegisterHandler(eventHandler);

    var isClientMode = string.Equals(
      settings.WebSocketMode.Value,
      "Client",
      StringComparison.OrdinalIgnoreCase
    );

    if (isClientMode)
    {
      // 地址为空时**不再静默回退成 Server**：仍建 Client 实例，由它的 StartAsync 明确失败并把
      // 「未配置 Koishi 地址」记进 LastError——否则设置写着 Client、实际却跑着 Server，面板与
      // 设置页都显示 Client，用户看到的模式与真实行为不符。
      var clientUrl = WebSocketClient.BuildClientUrl(settings);
      return new WebSocketClient(
        clientUrl,
        protocolHandler,
        messageRouter,
        reconnectIntervalMs: 5000,
        heartbeatIntervalMs: settings.WebSocketHeartbeatIntervalMs.Value,
        _log
      );
    }

    var connectionManager = new ConnectionManager(settings.WebSocketMaxConnections.Value);
    var heartbeatService = new HeartbeatService(
      settings.WebSocketHeartbeatIntervalMs.Value,
      settings.WebSocketHeartbeatTimeoutMs.Value,
      _log
    );

    return new WebSocketServer(
      settings.WebSocketPort.Value,
      connectionManager,
      protocolHandler,
      messageRouter,
      heartbeatService,
      settings.WebSocketEnableAuth.Value,
      settings.WebSocketAuthToken.Value,
      _log
    );
  }
}
