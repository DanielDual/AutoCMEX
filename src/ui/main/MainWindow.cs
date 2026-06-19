namespace AutoCMEX.UI.Main;

using System;
using System.Collections.Generic;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using AutoCMEX.UI.Logging;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 主窗口脚本：左右两栏布局、板块切换，同时作为 DI 容器提供核心服务
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class MainWindow
  : Control,
    IProvide<DataManager>,
    IProvide<AiServiceFactory>,
    IProvide<GuessPipeline>,
    IProvide<IGuessResponseHandler>,
    IProvide<IGuessProcessingService>,
    IProvide<IWebSocketServer>
{
  [Export]
  public int LeftPanelWidth { get; set; } = 200;

  #region AutoConnect Nodes

  [Node("MainContainer/LeftPanel")]
  public VBoxContainer LeftPanel { get; set; } = default!;

  [Node("MainContainer/RightPanel")]
  public Control RightPanel { get; set; } = default!;

  [Node("MainContainer/LeftPanel/IntegrationBtn")]
  public Button IntegrationBtn { get; set; } = default!;

  [Node("MainContainer/LeftPanel/GuessingBtn")]
  public Button GuessingBtn { get; set; } = default!;

  [Node("MainContainer/LeftPanel/InfoBtn")]
  public Button InfoBtn { get; set; } = default!;

  [Node("MainContainer/LeftPanel/SettingsBtn")]
  public Button SettingsBtn { get; set; } = default!;

  [Node("MainContainer/LeftPanel/HelpBtn")]
  public Button HelpBtn { get; set; } = default!;

  [Node("MainContainer/LeftPanel/LogBtn")]
  public Button LogBtn { get; set; } = default!;

  [Node("MainContainer/LeftPanel/WebSocketBtn")]
  public Button WebSocketBtn { get; set; } = default!;

  #endregion

  #region Provided Services

  private DataManager _dataManager = default!;
  private AiServiceFactory _aiServiceFactory = default!;
  private GuessPipeline _guessPipeline = default!;
  private GuessResponseHandler _guessResponseHandler = default!;
  private IGuessProcessingService _guessProcessingService = default!;
  private IWebSocketServer _webSocketServer = default!;

  DataManager IProvide<DataManager>.Value() => _dataManager;

  AiServiceFactory IProvide<AiServiceFactory>.Value() => _aiServiceFactory;

  GuessPipeline IProvide<GuessPipeline>.Value() => _guessPipeline;

  IGuessResponseHandler IProvide<IGuessResponseHandler>.Value() => _guessResponseHandler;

  IGuessProcessingService IProvide<IGuessProcessingService>.Value() => _guessProcessingService;

  IWebSocketServer IProvide<IWebSocketServer>.Value() => _webSocketServer;

  #endregion

  private readonly Dictionary<string, Control> _panels = new();
  private readonly Dictionary<string, Button> _navButtons = new();

  private Control? _currentPanel;
  private const string DefaultPanel = "guessing";

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    // 初始化核心服务
    var dataDir = ProjectSettings.GlobalizePath("user://data/");
    var keyPath = ProjectSettings.GlobalizePath("user://data/key.bin");
    var encryptor = new AesEncryptor(keyPath);
    _dataManager = new DataManager(dataDir, encryptor);
    _dataManager.LoadAll();

    _aiServiceFactory = new AiServiceFactory(_dataManager);

    _guessResponseHandler = new GuessResponseHandler();
    _guessPipeline = new GuessPipeline(_guessResponseHandler, _dataManager.Aliases);
    _guessProcessingService = new GuessProcessingService(
      _dataManager,
      _aiServiceFactory,
      _guessResponseHandler
    );

    // 初始化 WebSocket（Server 或 Client 模式）
    var settings = _dataManager.Settings;
    var wsLog = AppLogs.GetOrCreate().GetLogger("WebSocket");
    var protocolHandler = new ProtocolHandler();
    var messageRouter = new MessageRouter(wsLog);

    var commandHandler = new CommandHandler(wsLog, _guessProcessingService);
    var eventHandler = new AutoCMEX.Core.WebSocket.EventHandler(wsLog);
    messageRouter.RegisterHandler(commandHandler);
    messageRouter.RegisterHandler(eventHandler);

    var isClientMode = string.Equals(
      settings.WebSocketMode,
      "Client",
      StringComparison.OrdinalIgnoreCase
    );

    if (isClientMode && !string.IsNullOrEmpty(settings.KoishiWebSocketUrl))
    {
      var clientUrl = BuildClientUrl(settings);
      _webSocketServer = new WebSocketClient(
        clientUrl,
        protocolHandler,
        messageRouter,
        reconnectIntervalMs: 5000,
        heartbeatIntervalMs: settings.WebSocketHeartbeatIntervalMs,
        wsLog
      );
    }
    else
    {
      var connectionManager = new ConnectionManager(settings.WebSocketMaxConnections);
      var heartbeatService = new HeartbeatService(
        settings.WebSocketHeartbeatIntervalMs,
        settings.WebSocketHeartbeatTimeoutMs,
        wsLog
      );

      _webSocketServer = new WebSocketServer(
        settings.WebSocketPort,
        connectionManager,
        protocolHandler,
        messageRouter,
        heartbeatService,
        settings.WebSocketEnableAuth,
        settings.WebSocketAuthToken,
        wsLog
      );
    }

    // 通知 AutoInject 依赖已就绪
    this.Provide();
  }

  public void OnProvided()
  {
    // 所有依赖已提供，初始化 UI
    LeftPanel.CustomMinimumSize = new Vector2(LeftPanelWidth, 0);

    // 注册导航按钮
    _navButtons["integration"] = IntegrationBtn;
    _navButtons["guessing"] = GuessingBtn;
    _navButtons["info"] = InfoBtn;
    _navButtons["settings"] = SettingsBtn;
    _navButtons["help"] = HelpBtn;
    _navButtons["logging"] = LogBtn;
    _navButtons["websocket"] = WebSocketBtn;

    // 连接信号
    IntegrationBtn.Pressed += () => SwitchPanel("integration");
    GuessingBtn.Pressed += () => SwitchPanel("guessing");
    InfoBtn.Pressed += () => SwitchPanel("info");
    SettingsBtn.Pressed += () => SwitchPanel("settings");
    HelpBtn.Pressed += () => SwitchPanel("help");
    LogBtn.Pressed += () => SwitchPanel("logging");
    WebSocketBtn.Pressed += () => SwitchPanel("websocket");

    PreloadPanels();
    SetupLogPanel();
    SetupWebSocketPanel();
    SwitchPanel(DefaultPanel);

    // 启动 WebSocket 服务器
    _ = _webSocketServer.StartAsync();
  }

  /// <summary>
  /// 预加载所有板块场景
  /// </summary>
  private void PreloadPanels()
  {
    LoadPanel("integration", "res://src/ui/integration/IntegrationPanel.tscn");
    LoadPanel("guessing", "res://src/ui/guessing/GuessingPanel.tscn");
    LoadPanel("info", "res://src/ui/info/InfoPanel.tscn");
    LoadPanel("settings", "res://src/ui/settings/SettingsPanel.tscn");
    LoadPanel("help", "res://src/ui/help/HelpPanel.tscn");
  }

  /// <summary>
  /// 实例化并绑定日志面板。
  /// </summary>
  private void SetupLogPanel()
  {
    var path = "res://src/ui/logging/LogPanel.tscn";
    if (!ResourceLoader.Exists(path))
      return;
    var scene = ResourceLoader.Load<PackedScene>(path);
    var panel = scene.Instantiate<LogPanel>();
    panel.Visible = false;
    panel.SetAnchorsPreset(LayoutPreset.FullRect);
    RightPanel.AddChild(panel);
    _panels["logging"] = panel;
    var logService = AppLogs.GetOrCreate();
    panel.BindToService(logService);
  }

  /// <summary>
  /// 实例化并绑定 WebSocket 面板。
  /// </summary>
  private void SetupWebSocketPanel()
  {
    var path = "res://src/ui/websocket/WebSocketPanel.tscn";
    if (!ResourceLoader.Exists(path))
      return;
    var scene = ResourceLoader.Load<PackedScene>(path);
    var panel = scene.Instantiate<WebSocketPanel>();
    panel.Visible = false;
    panel.SetAnchorsPreset(LayoutPreset.FullRect);
    panel.SetServer(_webSocketServer, _dataManager.Settings.WebSocketMode);
    RightPanel.AddChild(panel);
    _panels["websocket"] = panel;
  }

  /// <summary>
  /// 重启 WebSocket 服务（切换模式或配置变更时调用）
  /// </summary>
  public async void RestartWebSocket()
  {
    var wsLog = AppLogs.GetOrCreate().GetLogger("WebSocket");
    wsLog.Print("MainWindow: restarting WebSocket...");

    // 停止旧实例
    await _webSocketServer.StopAsync();

    var settings = _dataManager.Settings;
    var protocolHandler = new ProtocolHandler();
    var messageRouter = new MessageRouter(wsLog);

    var commandHandler = new CommandHandler(wsLog, _guessProcessingService);
    var eventHandler = new AutoCMEX.Core.WebSocket.EventHandler(wsLog);
    messageRouter.RegisterHandler(commandHandler);
    messageRouter.RegisterHandler(eventHandler);

    var isClientMode = string.Equals(
      settings.WebSocketMode,
      "Client",
      StringComparison.OrdinalIgnoreCase
    );

    if (isClientMode && !string.IsNullOrEmpty(settings.KoishiWebSocketUrl))
    {
      var clientUrl = BuildClientUrl(settings);
      _webSocketServer = new WebSocketClient(
        clientUrl,
        protocolHandler,
        messageRouter,
        reconnectIntervalMs: 5000,
        heartbeatIntervalMs: settings.WebSocketHeartbeatIntervalMs,
        wsLog
      );
    }
    else
    {
      var connectionManager = new ConnectionManager(settings.WebSocketMaxConnections);
      var heartbeatService = new HeartbeatService(
        settings.WebSocketHeartbeatIntervalMs,
        settings.WebSocketHeartbeatTimeoutMs,
        wsLog
      );

      _webSocketServer = new WebSocketServer(
        settings.WebSocketPort,
        connectionManager,
        protocolHandler,
        messageRouter,
        heartbeatService,
        settings.WebSocketEnableAuth,
        settings.WebSocketAuthToken,
        wsLog
      );
    }

    // 更新面板绑定
    if (_panels.TryGetValue("websocket", out var panel) && panel is WebSocketPanel wsPanel)
    {
      wsPanel.SetServer(_webSocketServer, settings.WebSocketMode);
    }

    await _webSocketServer.StartAsync();
    wsLog.Print("MainWindow: WebSocket restarted.");
  }

  /// <summary>
  /// 构建 Client 模式的 WebSocket URL（自动补全 ws:// 前缀和 Token）
  /// </summary>
  private static string BuildClientUrl(AppSettings settings)
  {
    var url = settings.KoishiWebSocketUrl.Trim();

    // 自动补全 ws:// 前缀
    if (
      !url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
      && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
    )
    {
      url = "ws://" + url;
    }

    // 自动附加 Token
    if (settings.WebSocketEnableAuth && !string.IsNullOrEmpty(settings.WebSocketAuthToken))
    {
      var separator = url.Contains('?') ? "&" : "?";
      url = $"{url}{separator}token={Uri.EscapeDataString(settings.WebSocketAuthToken)}";
    }

    return url;
  }

  /// <summary>
  /// 加载单个板块场景
  /// </summary>
  private void LoadPanel(string key, string path)
  {
    if (!ResourceLoader.Exists(path))
      return;

    var scene = ResourceLoader.Load<PackedScene>(path);
    var panel = scene.Instantiate<Control>();
    panel.Visible = false;
    panel.SetAnchorsPreset(LayoutPreset.FullRect);
    RightPanel.AddChild(panel);
    _panels[key] = panel;
  }

  /// <summary>
  /// 切换板块
  /// </summary>
  private void SwitchPanel(string key)
  {
    if (!_panels.TryGetValue(key, out var panel))
      return;

    // 隐藏当前面板
    if (_currentPanel != null)
      _currentPanel.Visible = false;

    // 显示目标面板
    panel.Visible = true;
    _currentPanel = panel;

    // 更新按钮状态
    foreach (var (k, btn) in _navButtons)
    {
      btn.Disabled = k == key;
    }
  }
}
