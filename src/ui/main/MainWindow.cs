namespace AutoCMEX.UI.Main;

using System.Collections.Generic;
using Godot;

/// <summary>
/// 主窗口脚本：左右两栏布局、板块切换
/// </summary>
public partial class MainWindow : Control
{
  [Export]
  public int LeftPanelWidth { get; set; } = 200;

  private VBoxContainer _leftPanel = default!;
  private Control _rightPanel = default!;
  private Control _currentPanel = default!;

  private Button _integrationBtn = default!;
  private Button _guessingBtn = default!;
  private Button _infoBtn = default!;
  private Button _settingsBtn = default!;
  private Button _helpBtn = default!;

  private readonly Dictionary<string, Control> _panels = new();
  private readonly Dictionary<string, Button> _navButtons = new();

  private const string DefaultPanel = "guessing";

  public override void _Ready()
  {
    // 获取节点引用
    _leftPanel = GetNode<VBoxContainer>("MainContainer/LeftPanel");
    _rightPanel = GetNode<Control>("MainContainer/RightPanel");

    _integrationBtn = GetNode<Button>("MainContainer/LeftPanel/IntegrationBtn");
    _guessingBtn = GetNode<Button>("MainContainer/LeftPanel/GuessingBtn");
    _infoBtn = GetNode<Button>("MainContainer/LeftPanel/InfoBtn");
    _settingsBtn = GetNode<Button>("MainContainer/LeftPanel/SettingsBtn");
    _helpBtn = GetNode<Button>("MainContainer/LeftPanel/HelpBtn");

    // 应用导出属性
    _leftPanel.CustomMinimumSize = new Vector2(LeftPanelWidth, 0);

    // 注册导航按钮
    _navButtons["integration"] = _integrationBtn;
    _navButtons["guessing"] = _guessingBtn;
    _navButtons["info"] = _infoBtn;
    _navButtons["settings"] = _settingsBtn;
    _navButtons["help"] = _helpBtn;

    // 连接信号
    _integrationBtn.Pressed += () => SwitchPanel("integration");
    _guessingBtn.Pressed += () => SwitchPanel("guessing");
    _infoBtn.Pressed += () => SwitchPanel("info");
    _settingsBtn.Pressed += () => SwitchPanel("settings");
    _helpBtn.Pressed += () => SwitchPanel("help");

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
    _rightPanel.AddChild(panel);
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
