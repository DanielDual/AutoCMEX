namespace AutoCMEX.UI.Main;

using Godot;
using System.Collections.Generic;

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

    private readonly Dictionary<string, Control> _panels = new();
    private readonly Dictionary<string, Button> _navButtons = new();

    private const string DefaultPanel = "guessing";

    public override void _Ready()
    {
        SetupLayout();
        PreloadPanels();
        SwitchPanel(DefaultPanel);
    }

    /// <summary>
    /// 构建左右两栏布局
    /// </summary>
    private void SetupLayout()
    {
        // 主容器：水平布局
        var mainContainer = new HBoxContainer();
        mainContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(mainContainer);

        // 左栏：固定宽度
        _leftPanel = new VBoxContainer();
        _leftPanel.CustomMinimumSize = new Vector2(LeftPanelWidth, 0);
        mainContainer.AddChild(_leftPanel);

        // 分隔线
        var separator = new VSeparator();
        mainContainer.AddChild(separator);

        // 右栏：自适应
        _rightPanel = new Control();
        _rightPanel.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
        mainContainer.AddChild(_rightPanel);

        BuildLeftPanel();
    }

    /// <summary>
    /// 构建左栏导航
    /// </summary>
    private void BuildLeftPanel()
    {
        // Logo 占位
        var logoLabel = new Label();
        logoLabel.Text = "AutoCMEX";
        logoLabel.HorizontalAlignment = HorizontalAlignment.Center;
        logoLabel.AddThemeFontSizeOverride("font_size", 20);
        _leftPanel.AddChild(logoLabel);

        _leftPanel.AddChild(new HSeparator());

        // 功能板块按钮
        AddNavButton("integration", "整合");
        AddNavButton("guessing", "猜测");
        AddNavButton("info", "信息");

        // 弹性空白区域
        var spacer = new Control();
        spacer.SizeFlagsVertical = SizeFlags.Expand | SizeFlags.Fill;
        _leftPanel.AddChild(spacer);

        _leftPanel.AddChild(new HSeparator());

        AddNavButton("settings", "设置");
        AddNavButton("help", "帮助");

        _leftPanel.AddChild(new HSeparator());

        // 版本号
        var versionLabel = new Label();
        versionLabel.Text = "v0.0.1";
        versionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _leftPanel.AddChild(versionLabel);
    }

    /// <summary>
    /// 添加导航按钮
    /// </summary>
    private void AddNavButton(string panelKey, string text)
    {
        var button = new Button();
        button.Text = text;
        button.SizeFlagsHorizontal = SizeFlags.Fill;
        button.Pressed += () => SwitchPanel(panelKey);
        _leftPanel.AddChild(button);
        _navButtons[panelKey] = button;
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
