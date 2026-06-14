namespace AutoCMEX.UI.Main;

using System.Collections.Generic;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
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
    IProvide<GuessPipeline>,
    IProvide<IGuessResponseHandler>
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

  #endregion

  #region Provided Services

  private DataManager _dataManager = default!;
  private GuessPipeline _guessPipeline = default!;
  private GuessResponseHandler _guessResponseHandler = default!;

  DataManager IProvide<DataManager>.Value() => _dataManager;

  GuessPipeline IProvide<GuessPipeline>.Value() => _guessPipeline;

  IGuessResponseHandler IProvide<IGuessResponseHandler>.Value() => _guessResponseHandler;

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

    _guessResponseHandler = new GuessResponseHandler();
    _guessPipeline = new GuessPipeline(_guessResponseHandler, _dataManager.Aliases);

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

    // 连接信号
    IntegrationBtn.Pressed += () => SwitchPanel("integration");
    GuessingBtn.Pressed += () => SwitchPanel("guessing");
    InfoBtn.Pressed += () => SwitchPanel("info");
    SettingsBtn.Pressed += () => SwitchPanel("settings");
    HelpBtn.Pressed += () => SwitchPanel("help");

    PreloadPanels();
    SwitchPanel(DefaultPanel);
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
