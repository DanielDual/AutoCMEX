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
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
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
    IProvide<IWebSocketServer>,
    IProvide<ILogService>
{
  [Export]
  public int LeftPanelWidth { get; set; } = 200;

  #region AutoConnect Nodes

  [Node("%LeftPanel")]
  public IVBoxContainer LeftPanel { get; set; } = default!;

  [Node("%RightPanel")]
  public IControl RightPanel { get; set; } = default!;

  [Node("%MergeBtn")]
  public IButton MergeBtn { get; set; } = default!;

  [Node("%GuessingBtn")]
  public IButton GuessingBtn { get; set; } = default!;

  [Node("%InfoBtn")]
  public IButton InfoBtn { get; set; } = default!;

  [Node("%SettingsBtn")]
  public IButton SettingsBtn { get; set; } = default!;

  [Node("%HelpBtn")]
  public IButton HelpBtn { get; set; } = default!;

  [Node("%LogBtn")]
  public IButton LogBtn { get; set; } = default!;

  [Node("%WebSocketBtn")]
  public IButton WebSocketBtn { get; set; } = default!;

  #endregion

  #region Panel Nodes (instanced in scene)

  [Node("%MergePanel")]
  public IControl MergePanelNode { get; set; } = default!;

  [Node("%GuessingPanel")]
  public IControl GuessingPanelNode { get; set; } = default!;

  [Node("%InfoPanel")]
  public IControl InfoPanelNode { get; set; } = default!;

  [Node("%SettingsPanel")]
  public IControl SettingsPanelNode { get; set; } = default!;

  [Node("%HelpPanel")]
  public IControl HelpPanelNode { get; set; } = default!;

  [Node("%LogPanel")]
  public IControl LogPanelNode { get; set; } = default!;

  [Node("%WebSocketPanel")]
  public IControl WebSocketPanelNode { get; set; } = default!;

  #endregion

  #region Provided Services

  private DataManager _dataManager = default!;
  private AiServiceFactory _aiServiceFactory = default!;
  private GuessPipeline _guessPipeline = default!;
  private GuessResponseHandler _guessResponseHandler = default!;
  private IGuessProcessingService _guessProcessingService = default!;
  private IWebSocketServer _webSocketServer = default!;
  private ILogService _logService = default!;

  DataManager IProvide<DataManager>.Value() => _dataManager;

  AiServiceFactory IProvide<AiServiceFactory>.Value() => _aiServiceFactory;

  GuessPipeline IProvide<GuessPipeline>.Value() => _guessPipeline;

  IGuessResponseHandler IProvide<IGuessResponseHandler>.Value() => _guessResponseHandler;

  IGuessProcessingService IProvide<IGuessProcessingService>.Value() => _guessProcessingService;

  IWebSocketServer IProvide<IWebSocketServer>.Value() => _webSocketServer;

  ILogService IProvide<ILogService>.Value() => _logService;

  #endregion

  private readonly Dictionary<string, INode> _panels = new();
  private readonly Dictionary<string, IButton> _navButtons = new();

  private AutoValue<string>.Binding? _webSocketModeBinding;
  private AutoValue<int>.Binding? _webSocketPortBinding;
  private AutoValue<string>.Binding? _koishiWebSocketUrlBinding;

  private IControl? _currentPanel;
  private const string DefaultPanel = "guessing";

  public override void _Notification(int what) => this.Notify(what);

  public override void _ExitTree()
  {
    _webSocketModeBinding?.Dispose();
    _webSocketPortBinding?.Dispose();
    _koishiWebSocketUrlBinding?.Dispose();
    _dataManager?.Dispose();
  }

  public void OnReady()
  {
    var isTesting = (this as IAutoInit).IsTesting;
    if (!isTesting)
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

      // 初始化日志服务
      _logService = AppLogs.GetOrCreate();

      // 订阅配置变更事件，自动重启 WebSocket（保存 Binding 引用以便释放）
      _webSocketModeBinding = _dataManager
        .Settings.WebSocketMode.Bind()
        .OnValue(_ => RestartWebSocket());
      _webSocketPortBinding = _dataManager
        .Settings.WebSocketPort.Bind()
        .OnValue(_ => RestartWebSocket());
      _koishiWebSocketUrlBinding = _dataManager
        .Settings.KoishiWebSocketUrl.Bind()
        .OnValue(_ => RestartWebSocket());
    }

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

    SwitchPanel(DefaultPanel);

    // 启动 WebSocket 服务器（测试模式下跳过，避免启动真实网络服务）
    if (!(this as IAutoInit).IsTesting)
      _ = _webSocketServer.StartAsync();
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

    // 更新面板绑定：通过接口解耦，避免具体类型检查
    if (WebSocketPanelNode is INodeAdapter adapter && adapter.TargetObj is IWebSocketPanel panel)
    {
      panel.UpdateServer(_webSocketServer, _dataManager.Settings.WebSocketMode.Value);
    }

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
    if (panel is IControl control)
    {
      control.Visible = true;
      _currentPanel = control;
    }

    // 更新按钮状态
    foreach (var (k, btn) in _navButtons)
    {
      btn.Disabled = k == key;
    }
  }
}
