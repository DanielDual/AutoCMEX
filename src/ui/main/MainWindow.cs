namespace AutoCMEX.UI.Main;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using AutoCMEX.UI.Info;
using AutoCMEX.UI.Logging;
using AutoCMEX.UI.Merge;
using AutoCMEX.UI.Settings;
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
    IProvide<WebSocketLifecycle>,
    IProvide<IDroppedGuessRetryService>,
    IProvide<InfoEventBus>,
    IProvide<ILogService>
{
  [Export]
  public int LeftPanelWidth { get; set; } = 200;

  #region AutoConnect Nodes

  [Node("%LeftPanel")]
  public IVBoxContainer LeftPanel { get; set; } = default!;

  /// <summary>左栏顶部的品牌 Logo（浅色主题换成浅色版贴图）。</summary>
  [Node("%Logo")]
  public ITextureRect Logo { get; set; } = default!;

  [Node("%RightPanel")]
  public IControl RightPanel { get; set; } = default!;

  /// <summary>主分割条（左导航 / 右内容）；位置由 <c>ui_layout.json</c> 的 <c>main.nav</c> 记录还原。</summary>
  [Node("%MainContainer")]
  public ISplitContainer MainContainer { get; set; } = default!;

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
  public IMergePanel MergePanelNode { get; set; } = default!;

  [Node("%GuessingPanel")]
  public IGuessingPanel GuessingPanelNode { get; set; } = default!;

  [Node("%InfoPanel")]
  public IInfoPanel InfoPanelNode { get; set; } = default!;

  [Node("%SettingsPanel")]
  public ISettingsPanel SettingsPanelNode { get; set; } = default!;

  [Node("%HelpPanel")]
  public Control HelpPanelNode { get; set; } = default!;

  [Node("%LogPanel")]
  public ILogPanel LogPanelNode { get; set; } = default!;

  [Node("%WebSocketPanel")]
  public IWebSocketPanel WebSocketPanelNode { get; set; } = default!;

  #endregion

  #region Provided Services

  private DataManager _dataManager = default!;
  private AiServiceFactory _aiServiceFactory = default!;
  private GuessPipeline _guessPipeline = default!;
  private GuessResponseHandler _guessResponseHandler = default!;
  private IGuessProcessingService _guessProcessingService = default!;
  private IWebSocketServer _webSocketServer = default!;
  private WebSocketLifecycle _webSocketLifecycle = default!;
  private IDroppedGuessRetryService _droppedGuessRetryService = default!;
  private ILogService _logService = default!;

  /// <summary>
  /// 信息板块入站事件总线：始终可用（测试模式也要提供），供信息面板订阅 Koishi 回执。
  /// </summary>
  private readonly InfoEventBus _infoEvents = new();

  DataManager IProvide<DataManager>.Value() => _dataManager;

  AiServiceFactory IProvide<AiServiceFactory>.Value() => _aiServiceFactory;

  GuessPipeline IProvide<GuessPipeline>.Value() => _guessPipeline;

  IGuessResponseHandler IProvide<IGuessResponseHandler>.Value() => _guessResponseHandler;

  IGuessProcessingService IProvide<IGuessProcessingService>.Value() => _guessProcessingService;

  IWebSocketServer IProvide<IWebSocketServer>.Value() => _webSocketServer;

  WebSocketLifecycle IProvide<WebSocketLifecycle>.Value() => _webSocketLifecycle;

  IDroppedGuessRetryService IProvide<IDroppedGuessRetryService>.Value() =>
    _droppedGuessRetryService;

  InfoEventBus IProvide<InfoEventBus>.Value() => _infoEvents;

  ILogService IProvide<ILogService>.Value() => _logService;

  #endregion

  private readonly Dictionary<string, Control> _panels = new();
  private readonly Dictionary<string, IButton> _navButtons = new();

  private AutoValue<string>.Binding? _webSocketModeBinding;
  private AutoValue<int>.Binding? _webSocketPortBinding;
  private AutoValue<string>.Binding? _koishiWebSocketUrlBinding;

  /// <summary>主题应用器；测试模式不构造（<c>null</c>），避免测试期改全局主题与清屏色。</summary>
  private ThemeApplier? _themeApplier;

  /// <summary>界面主题配置的绑定：值变化时排队应用主题，退出时释放。</summary>
  private AutoValue<string>.Binding? _themeIdBinding;

  /// <summary>主题绑定已就绪：订阅会立刻回放一次当前值，置位前先抑制这次伪变更。</summary>
  private bool _themeBindingReady;

  /// <summary>已排队一次主题应用（同一帧内多次改值只应用一次）。</summary>
  private bool _themeApplyQueued;

  /// <summary>深色版 Logo（默认与其它非浅色主题使用）；资源缺失时为 <c>null</c>。</summary>
  private Texture2D? _logoDark;

  /// <summary>浅色版 Logo（浅色主题使用）；资源缺失时为 <c>null</c>。</summary>
  private Texture2D? _logoLight;

  private Control? _currentPanel;
  private const string DefaultPanel = "guessing";

  /// <summary>主窗口几何接线路；测试模式不构造（<c>null</c>）。</summary>
  private WindowLayoutState? _windowLayout;

  /// <summary>主分割条的布局接线路；测试模式不构造（<c>null</c>）。</summary>
  private SplitLayoutLink? _mainNavSplitLink;

  /// <summary>
  /// 当前运行时主窗口实例。Main 的退出路径不持有 DataManager，
  /// 经 <see cref="FlushPendingSave"/> 这个静态入口让主窗口先落盘（测试模式不注册）。
  /// </summary>
  private static MainWindow? _activeInstance;

  /// <summary>
  /// 退出前的末次保存是否已执行。用于幂等：重复关窗/退出通知只写一次盘。
  /// </summary>
  private bool _finalSaveFlushed;

  /// <summary>
  /// 已进入退出流程（关窗通知或 <c>_ExitTree</c>）：此期间的配置写入只该落盘，不得再触发 WebSocket 重启。
  /// </summary>
  private bool _exiting;

  /// <summary>
  /// Godot 通知入口：关窗通知（<see cref="NotificationWMCloseRequest"/>）先立起退出护栏，再交给
  /// AutoInject/AutoOn 的既有通知分发。
  /// </summary>
  /// <remarks>
  /// Godot 拆解节点树时子节点先退出，设置面板会在自己的退出回调里把去抖窗口内尚未提交的
  /// 端口编辑写回模型；若不提前置位，这笔写入会经配置绑定触发一次 WebSocket 重启，
  /// 在拆解期再启停/重建实例是不允许的（会留下多余实例并碰正在释放的节点）。
  /// 这里必须显式置位：不能只依赖 <c>_ExitTree</c> 的时序（后者晚于子面板退出）。
  /// </remarks>
  public override void _Notification(int what)
  {
    if (what == (int)NotificationWMCloseRequest)
    {
      _exiting = true;
      // 关窗时先把窗口几何写进模型，再同步落盘；_ExitTree 仍会兜底一次（幂等）
      _windowLayout?.Capture();
      SavePendingChanges();
    }
    else if (what == (int)NotificationWMSizeChanged || what == (int)NotificationWMPositionChanged)
    {
      // 窗口尺寸 / 位置变化时刷新「还原矩形」：最大化状态下写回的是这里记下的普通态几何
      _windowLayout?.Track();
    }

    this.Notify(what);
  }

  /// <summary>是否已立起退出护栏（测试用：验证关窗通知在树拆解前就生效）。</summary>
  internal bool IsExitingForTests => _exiting;

  /// <summary>是否已建立主分割条接线路（测试用：验证测试模式跳过接线）。</summary>
  internal bool HasMainNavSplitLinkForTests => _mainNavSplitLink is not null;

  public override void _ExitTree()
  {
    // 必须最先置位：拆解期间子面板的退出回调仍会写配置（恢复去抖中的端口编辑），
    // 那些写入不许再触发 WebSocket 重启
    _exiting = true;

    // 先摘掉退出入口：主窗口正在离开节点树，Main 的退出通知不应再走末次保存
    if (ReferenceEquals(_activeInstance, this))
      _activeInstance = null;

    // 先摘掉配置绑定，保证退出过程中不会再有新的重启请求进来。
    // 主题绑定同时摘下：已排队的 deferred 应用还有 ApplyThemeFromSettings 里的 _exiting 守卫兜底。
    _themeIdBinding?.Dispose();
    _themeIdBinding = null;
    _themeApplyQueued = false;
    _webSocketModeBinding?.Dispose();
    _webSocketPortBinding?.Dispose();
    _koishiWebSocketUrlBinding?.Dispose();
    _webSocketLifecycle?.Dispose();
    // 退出前把窗口几何与导航分割条写回模型：随后 Dispose 的末次落盘把它们写进 ui_layout.json
    _windowLayout?.Capture();
    _mainNavSplitLink?.Capture();
    // DataManager.Dispose 内部会再同步落盘一次，覆盖「关窗通知没走到」的退出路径
    _dataManager?.Dispose();
    _finalSaveFlushed = true;
  }

  /// <summary>
  /// 请求当前主窗口把尚未落盘的改动立即写入磁盘（应用退出路径专用）。
  /// </summary>
  /// <remarks>
  /// 数据改动经 <see cref="DataManager.TriggerAutoSave"/> 走 1500ms 防抖，关窗/退出时
  /// 最后一笔改动可能还在防抖窗口里；<see cref="AutoCMEX.Main"/> 没有 DataManager 引用，
  /// 因此经此静态入口让主窗口先落盘，再由 Main 关闭日志服务（落盘过程要写日志）。
  /// 幂等：同一实例只写一次；无实例（测试模式或服务未初始化）时为空操作。
  /// </remarks>
  public static void FlushPendingSave()
  {
    var window = _activeInstance;
    if (window == null || !GodotObject.IsInstanceValid(window))
      return;

    window.SavePendingChanges();
  }

  /// <summary>
  /// 执行退出前的末次保存；写盘失败只记错误日志，不打断退出流程。
  /// </summary>
  private void SavePendingChanges()
  {
    if (_finalSaveFlushed)
      return;

    _finalSaveFlushed = true;

    // 测试模式不构造 DataManager，这里保持与旧退出行为一致（什么都不做）
    if (_dataManager == null)
      return;

    try
    {
      _dataManager.SaveAll();
    }
    catch (Exception ex)
    {
      _logService
        .GetLogger(nameof(MainWindow))
        .Err($"MainWindow: flush pending save failed: {ex.GetType().Name}: {ex.Message}");
    }
  }

  public void OnReady()
  {
    var isTesting = (this as IAutoInit).IsTesting;
    if (!isTesting)
    {
      // 唯一 bootstrap：生产装配需要一个「取得 ILogService」的起点（Main 启动时已初始化，这里只是取用）。
      // 此后本窗口内所有手动构造的服务都从这里显式取模块 logger，UI 层不再静态定位日志；
      // AppLogs 只保留 Main.cs 启动与这一处 bootstrap。
      _logService = AppLogs.GetOrCreate();

      // 初始化核心服务
      var dataDir = ProjectSettings.GlobalizePath("user://data/");
      var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(dataDir));
      // 模块日志取用器：核心服务经 DataManager.GetLogger 取到带模块名的日志，核心层因此不需要
      // 静态日志定位；测试用 4 参构造（工厂默认退回注入的 log），既有用例无需改动。
      // 自动保存的「捕获 + 序列化」必须回主线程执行：防抖到期在池线程，只把原子写盘留在池线程，
      // 避免池线程枚举活的 AutoList（含 Boss.SpellCards 等嵌套集合）。
      _dataManager = new DataManager(
        dataDir,
        encryptor,
        _logService.GetLogger(nameof(DataManager)),
        action => Callable.From(action).CallDeferred(),
        module => _logService.GetLogger(module)
      );
      _dataManager.LoadAll();

      // 主题接线：先记下引擎清屏色作为「默认主题」的兜底底色（应用主题后它会被改写，故必须先用先取），
      // 再同步应用一次存档主题。顺序必须在窗口几何还原之前：主题只改配色不改尺寸，
      // 先定外观再摆几何，避免启动时先按旧外观露一帧再变脸。
      _logoDark = GD.Load<Texture2D>("res://src/assets/brand/logo-lockup.svg");
      _logoLight = GD.Load<Texture2D>("res://src/assets/brand/logo-lockup-light.svg");
      var defaultBackground = RenderingServer.GetDefaultClearColor();
      _themeApplier = new ThemeApplier(
        defaultBackground,
        // null 主题表示「恢复引擎默认外观」，因此这里必须允许把 null 写进 Theme 属性
        theme => GetWindow().Theme = theme!,
        color => RenderingServer.SetDefaultClearColor(color)
      );
      ApplyThemeFromSettings();

      // 主窗口几何还原：数据加载完成后立即应用（越早越不闪），测试模式不进入本分支
      _windowLayout = new WindowLayoutState(
        AdaptMainWindow(),
        _dataManager,
        _logService.GetLogger(nameof(WindowLayoutState))
      );
      _windowLayout.Apply();

      var aiLog = _logService.GetLogger(nameof(AiServiceFactory));
      _aiServiceFactory = new AiServiceFactory(_dataManager, aiLog);

      _guessResponseHandler = new GuessResponseHandler();
      _guessPipeline = new GuessPipeline(
        _guessResponseHandler,
        _dataManager.Aliases,
        _logService.GetLogger(nameof(GuessPipeline))
      );
      var droppedGuessRepository = new DroppedGuessRepository();
      _guessProcessingService = new GuessProcessingService(
        _dataManager,
        _aiServiceFactory,
        _guessResponseHandler,
        droppedGuessRepository,
        _logService.GetLogger(nameof(GuessProcessingService))
      );

      // 初始化 WebSocket（Server 或 Client 模式）。实例的创建/启停/重启统一走 WebSocketLifecycle：
      // 配置绑定的 OnValue 是「订阅即回调」，启动时三个绑定会连打三次重启，
      // 必须由它串行化并跳过「配置其实没变」的重复重启，否则会留下继续连接却无人引用的孤儿实例。
      var wsLog = _logService.GetLogger("WebSocket");
      var wsInitializer = new WebSocketInitializer(wsLog, _guessProcessingService, _infoEvents);
      _webSocketLifecycle = new WebSocketLifecycle(wsInitializer.CreateServer, wsLog);
      _webSocketServer = _webSocketLifecycle.Create(_dataManager.Settings);

      // 丢包重试协调器：在 lifecycle 之后构造，重试成功时经它向活跃端点推送回复
      _droppedGuessRetryService = new DroppedGuessRetryService(
        _guessProcessingService,
        _webSocketLifecycle,
        _logService.GetLogger(nameof(DroppedGuessRetryService))
      );

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

      // 主题绑定：订阅即用当前值回放一次，而启动时刚同步应用过，故先抑制这次回放
      //（不抑制会在启动帧多排一次应用，要等一帧才轮到它，还白跑一遍加载）。
      _themeBindingReady = false;
      _themeIdBinding = _dataManager.Settings.ThemeId.Bind().OnValue(_ => QueueThemeApply());
      _themeBindingReady = true;

      // 注册退出入口：Main 关窗/退出时经静态方法触发末次保存。
      // 测试模式不注册，避免静态实例跨用例残留。
      _activeInstance = this;
    }

    // 通知 AutoInject 依赖已就绪
    this.Provide();
  }

  public void OnProvided()
  {
    // 所有依赖已提供，初始化 UI
    LeftPanel.CustomMinimumSize = new Vector2(LeftPanelWidth, 0);

    // 导航分割条布局还原（规则 6：依赖解析完成后接线；测试模式没有 DataManager，跳过）。
    // 取到局部变量再判空：直接对字段判空会让后续对 _dataManager 的读取被流分析视为可能为空
    var dataManager = _dataManager;
    if (dataManager is not null)
    {
      _mainNavSplitLink = new SplitLayoutLink(MainContainer, UiLayoutKeys.MainNav, dataManager);
      _mainNavSplitLink.Apply();
    }

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

    // 注册场景中的面板（统一转成 Godot Control，供板块切换时控制 Visible）
    _panels["merge"] = ToControl(MergePanelNode);
    _panels["guessing"] = ToControl(GuessingPanelNode);
    _panels["info"] = ToControl(InfoPanelNode);
    _panels["settings"] = ToControl(SettingsPanelNode);
    _panels["help"] = ToControl(HelpPanelNode);
    _panels["logging"] = ToControl(LogPanelNode);
    _panels["websocket"] = ToControl(WebSocketPanelNode);

    SwitchPanel(DefaultPanel);

    // 启动 WebSocket 服务器（测试模式下跳过，避免启动真实网络服务）
    if (!(this as IAutoInit).IsTesting)
      _ = _webSocketLifecycle.StartAsync(_dataManager.Settings);
  }

  /// <summary>
  /// 把设置里的界面主题应用到当前窗口，并换上与主题明暗匹配的 Logo。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 公开仅为让 <see cref="CallDeferred(string)"/> 能按名调用：外部（设置页、启动流程）一律只写
  /// <c>Settings.ThemeId</c>，由绑定驱动本方法，<b>本方法不属于对外契约</b>。
  /// </para>
  /// <para>
  /// <c>_exiting</c> / 未入树 / 应用器未构造（测试模式）三种时机一律早退：此时改全局外观没有意义，
  /// 还可能与正在拆解的节点或测试用例抢状态。应用失败时应用器已回退默认主题，这里只补一条 WARN，
  /// 面向用户的提示由设置页负责。
  /// </para>
  /// </remarks>
  public void ApplyThemeFromSettings()
  {
    _themeApplyQueued = false;

    if (_exiting || !IsInsideTree() || _themeApplier is null)
      return;

    var result = _themeApplier.Apply(_dataManager.Settings.ThemeId.Value);

    if (!result.Ok)
      _logService?.GetLogger("Theme").Warn($"MainWindow: apply theme failed: {result.Error}");

    // 浅色主题下深色 Logo 几乎不可见，故按「实际生效」的主题换贴图：
    // 未知 ID / 加载失败时应用器已回退默认主题，这里自然也落回深色版。
    // 贴图加载失败（null）时保持场景里配的原值，不把 Logo 清空。
    var resolved = ThemeCatalog.TryResolve(result.AppliedId, out var option, out _);
    var texture = resolved && option.IsLight ? _logoLight : _logoDark;
    if (texture is not null)
      Logo.Texture = texture;
  }

  /// <summary>
  /// 排队一次主题应用：绑定回调只置脏并转 <c>CallDeferred</c>，不在回调栈里改界面（规则 19）。
  /// </summary>
  /// <remarks>
  /// 三重守卫：绑定尚未就绪（订阅回放）、本帧已排队（同一帧多次改值合并）、已在退出流程。
  /// </remarks>
  private void QueueThemeApply()
  {
    if (!_themeBindingReady || _themeApplyQueued || _exiting)
      return;

    _themeApplyQueued = true;
    CallDeferred(nameof(ApplyThemeFromSettings));
  }

  /// <summary>
  /// 重启 WebSocket 服务（切换模式或配置变更时调用）
  /// </summary>
  /// <remarks>
  /// 改为 <c>async Task</c> 以便护栏用例可 await；信号/绑定回调用弃元丢弃返回的 Task，
  /// 异常仍在本方法的 catch 内收口。重启本身由 <see cref="WebSocketLifecycle"/> 串行化，
  /// 并在「配置其实没变」时原样返回现有实例，不重建不启停。
  /// </remarks>
  public async Task RestartWebSocket()
  {
    // 退出/已离树：此时的配置写入（典型：设置面板退出时恢复未提交的端口编辑）只该落盘，
    // 不得再重建 WebSocket——否则关窗路径上会与已关闭的日志服务抢生命周期
    if (_exiting || !IsInsideTree())
      return;

    try
    {
      _webSocketServer = await _webSocketLifecycle.RestartAsync(_dataManager.Settings);

      // 更新面板绑定：通过接口解耦，避免具体类型检查。
      // 不再把「设置里的模式」传进去——面板一律按实例自身的实际模式/端口/地址显示。
      if (WebSocketPanelNode is IWebSocketPanel panel)
      {
        panel.UpdateServer(_webSocketServer);
      }
    }
    catch (Exception ex)
    {
      // 测试模式不初始化 _logService：这里必须空安全，否则护栏用例会在 catch 里 NRE
      _logService
        ?.GetLogger("WebSocket")
        .Err($"MainWindow: restart WebSocket failed: {ex.GetType().Name}: {ex.Message}");
    }
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

  /// <summary>
  /// 取主窗口的节点接口适配器；拿不到（未入树 / 适配失败）时返回 <c>null</c>，让几何接线路整体退化。
  /// </summary>
  /// <returns>主窗口的 <see cref="IWindow"/> 适配器，或 <c>null</c>。</returns>
  private IWindow? AdaptMainWindow()
  {
    var window = GetWindow();
    if (window is null)
      return null;

    return GodotInterfaces.AdaptNode(window) as IWindow;
  }

  /// <summary>
  /// 将 <c>[Node]</c> 属性解析出的面板对象统一转为 Godot <see cref="Control"/>，
  /// 以便通过 <see cref="Control.Visible"/> 切换板块显示。
  /// 脚本面板实例本身是 <c>Control</c> 子类；原生面板经适配器包装，取其 <see cref="INodeAdapter.TargetObj"/>。
  /// </summary>
  private static Control? ToControl(object panel) =>
    panel switch
    {
      Control c => c,
      INodeAdapter a => a.TargetObj as Control,
      _ => null,
    };
}
