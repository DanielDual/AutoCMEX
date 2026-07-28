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

  [Node("MainContainer/LeftPanel/MergeBtn")]
  public Button MergeBtn { get; set; } = default!;

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

  #region Panel Nodes (instanced in scene)

  [Node("MainContainer/RightPanel/MergePanel")]
  public Control MergePanelNode { get; set; } = default!;

  [Node("MainContainer/RightPanel/GuessingPanel")]
  public Control GuessingPanelNode { get; set; } = default!;

  [Node("MainContainer/RightPanel/InfoPanel")]
  public Control InfoPanelNode { get; set; } = default!;

  [Node("MainContainer/RightPanel/SettingsPanel")]
  public Control SettingsPanelNode { get; set; } = default!;

  [Node("MainContainer/RightPanel/HelpPanel")]
  public Control HelpPanelNode { get; set; } = default!;

  [Node("MainContainer/RightPanel/LogPanel")]
  public LogPanel LogPanelNode { get; set; } = default!;

  [Node("MainContainer/RightPanel/WebSocketPanel")]
  public WebSocketPanel WebSocketPanelNode { get; set; } = default!;

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
    var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(dataDir));
    _dataManager = new DataManager(dataDir, encryptor);
    _dataManager.LoadAll();

    _aiServiceFactory = new AiServiceFactory(_dataManager);

    _guessResponseHandler = new GuessResponseHandler();
    _guessPipeline = new GuessPipeline(_guessResponseHandler, _dataManager.Aliases);
    var droppedGuessRepository = new DroppedGuessRepository();
    _guessProcessingService = new GuessProcessingService(
      _dataManager,
      _aiServiceFactory,
      _guessResponseHandler,
      droppedGuessRepository
    );

    // 初始化 WebSocket（Server 或 Client 模式）
    var wsLog = AppLogs.GetOrCreate().GetLogger("WebSocket");
    var wsInitializer = new WebSocketInitializer(wsLog, _guessProcessingService);
    _webSocketServer = wsInitializer.CreateServer(_dataManager.Settings);

    // 订阅配置变更事件，自动重启 WebSocket
    _dataManager.Settings.PropertyChanged += OnSettingsPropertyChanged;

    // 通知 AutoInject 依赖已就绪
    this.Provide();
  }

  public void OnProvided()
  {
    // 所有依赖已提供，初始化 UI
    LeftPanel.CustomMinimumSize = new Vector2(LeftPanelWidth, 0);

    // 注册导航按钮
    _navButtons["merge"] = MergeBtn;
    _navButtons["guessing"] = GuessingBtn;
    _navButtons["info"] = InfoBtn;
    _navButtons["settings"] = SettingsBtn;
    _navButtons["help"] = HelpBtn;
    _navButtons["logging"] = LogBtn;
    _navButtons["websocket"] = WebSocketBtn;

    // 连接信号
    MergeBtn.Pressed += () => SwitchPanel("merge");
    GuessingBtn.Pressed += () => SwitchPanel("guessing");
    InfoBtn.Pressed += () => SwitchPanel("info");
    SettingsBtn.Pressed += () => SwitchPanel("settings");
    HelpBtn.Pressed += () => SwitchPanel("help");
    LogBtn.Pressed += () => SwitchPanel("logging");
    WebSocketBtn.Pressed += () => SwitchPanel("websocket");

    // 注册场景中的面板
    _panels["merge"] = MergePanelNode;
    _panels["guessing"] = GuessingPanelNode;
    _panels["info"] = InfoPanelNode;
    _panels["settings"] = SettingsPanelNode;
    _panels["help"] = HelpPanelNode;
    _panels["logging"] = LogPanelNode;
    _panels["websocket"] = WebSocketPanelNode;

    // 绑定日志面板
    var logService = AppLogs.GetOrCreate();
    LogPanelNode.BindToService(logService);

    // 绑定 WebSocket 面板
    WebSocketPanelNode.SetServer(_webSocketServer, _dataManager.Settings.WebSocketMode);

    SwitchPanel(DefaultPanel);

    // 启动 WebSocket 服务器
    _ = _webSocketServer.StartAsync();
  }

  /// <summary>
  /// 配置变更回调：当 WebSocket 相关配置变化时自动重启
  /// </summary>
  private void OnSettingsPropertyChanged(string propertyName)
  {
    if (
      propertyName == nameof(AppSettings.WebSocketMode)
      || propertyName == nameof(AppSettings.WebSocketPort)
      || propertyName == nameof(AppSettings.KoishiWebSocketUrl)
    )
    {
      RestartWebSocket();
    }
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

    // 使用初始化器创建新实例
    var wsInitializer = new WebSocketInitializer(wsLog, _guessProcessingService);
    _webSocketServer = wsInitializer.CreateServer(_dataManager.Settings);

    // 更新面板绑定
    WebSocketPanelNode.SetServer(_webSocketServer, _dataManager.Settings.WebSocketMode);

    await _webSocketServer.StartAsync();
    wsLog.Print("MainWindow: WebSocket restarted.");
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
