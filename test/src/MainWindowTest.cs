namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.UI.Guessing;
using AutoCMEX.UI.Logging;
using AutoCMEX.UI.Main;
using AutoCMEX.UI.Merge;
using AutoCMEX.UI.Settings;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// MainWindow 单元测试
/// </summary>
public class MainWindowTest : TestClass
{
  private MainWindow _mainWindow = default!;
  private readonly List<Node> _toCleanup = new();

  // 脚本面板：使用真实面板实例（实现各自接口）。无脚本面板：使用原生 Control。
  private MergePanel _mergePanel = default!;
  private GuessingPanel _guessingPanel = default!;
  private Control _infoPanel = default!;
  private SettingsPanel _settingsPanel = default!;
  private Control _helpPanel = default!;
  private LogPanel _logPanel = default!;
  private WebSocketPanel _webSocketPanel = default!;

  private Mock<IButton> _mergeBtn = default!;
  private Mock<IButton> _guessingBtn = default!;
  private Mock<IButton> _infoBtn = default!;
  private Mock<IButton> _settingsBtn = default!;
  private Mock<IButton> _helpBtn = default!;
  private Mock<IButton> _logBtn = default!;
  private Mock<IButton> _webSocketBtn = default!;

  public MainWindowTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _mainWindow = new MainWindow();
    (_mainWindow as IAutoInit).IsTesting = true;
    _toCleanup.Add(_mainWindow);

    var leftPanel = new Mock<IVBoxContainer>();
    var rightPanel = new Mock<IControl>();

    _mergeBtn = new Mock<IButton>();
    _guessingBtn = new Mock<IButton>();
    _infoBtn = new Mock<IButton>();
    _settingsBtn = new Mock<IButton>();
    _helpBtn = new Mock<IButton>();
    _logBtn = new Mock<IButton>();
    _webSocketBtn = new Mock<IButton>();

    // 脚本面板：真实实例直接赋给 [Node] 属性（已赋值属性会被 AutoConnect 跳过）。
    // 原生（无脚本）面板：属性类型为 IControl，经 Adapt 得到 IControl 适配器，TargetObj 指向真实 Control。
    _mergePanel = new MergePanel();
    _guessingPanel = new GuessingPanel();
    _infoPanel = new Control();
    _settingsPanel = new SettingsPanel();
    _helpPanel = new Control();
    _logPanel = new LogPanel();
    _webSocketPanel = new WebSocketPanel();

    _mainWindow.MergePanelNode = _mergePanel;
    _mainWindow.GuessingPanelNode = _guessingPanel;
    _mainWindow.InfoPanelNode = _infoPanel;
    _mainWindow.SettingsPanelNode = _settingsPanel;
    _mainWindow.HelpPanelNode = _helpPanel;
    _mainWindow.LogPanelNode = _logPanel;
    _mainWindow.WebSocketPanelNode = _webSocketPanel;

    // 模拟 .tscn 初始状态：默认面板 guessing 可见，其余面板隐藏
    _mergePanel.Visible = false;
    _infoPanel.Visible = false;
    _settingsPanel.Visible = false;
    _helpPanel.Visible = false;
    _logPanel.Visible = false;
    _webSocketPanel.Visible = false;

    _mainWindow.FakeNodeTree(
      new()
      {
        ["%LeftPanel"] = leftPanel.Object,
        ["%RightPanel"] = rightPanel.Object,
        ["%MergeBtn"] = _mergeBtn.Object,
        ["%GuessingBtn"] = _guessingBtn.Object,
        ["%InfoBtn"] = _infoBtn.Object,
        ["%SettingsBtn"] = _settingsBtn.Object,
        ["%HelpBtn"] = _helpBtn.Object,
        ["%LogBtn"] = _logBtn.Object,
        ["%WebSocketBtn"] = _webSocketBtn.Object,
      }
    );

    // 触发 AutoInject 生命周期
    _mainWindow._Notification((int)Node.NotificationEnterTree);
    _mainWindow._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }
    _toCleanup.Clear();
  }

  [Test]
  public void MainWindow_LoadsSuccessfully()
  {
    _mainWindow.ShouldNotBeNull();
  }

  [Test]
  public void MainWindow_HasLeftPanelWidth()
  {
    _mainWindow.LeftPanelWidth.ShouldBe(200);
  }

  [Test]
  public void MainWindow_IsControlType()
  {
    _mainWindow.ShouldBeAssignableTo<Control>();
  }

  [Test]
  public void MainWindow_LeftPanelWidth_IsPositive()
  {
    _mainWindow.LeftPanelWidth.ShouldBeGreaterThan(0);
  }

  [Test]
  public void SwitchPanel_ShowsTargetPanel()
  {
    // 初始状态：默认面板 guessing 可见，其余隐藏
    _guessingPanel.Visible.ShouldBeTrue();
    _mergePanel.Visible.ShouldBeFalse();
    _logPanel.Visible.ShouldBeFalse();
    _webSocketPanel.Visible.ShouldBeFalse();

    // 切换到 merge 面板
    _mergeBtn.Raise(b => b.Pressed += null);

    // 目标面板可见，原面板隐藏
    _mergePanel.Visible.ShouldBeTrue();
    _guessingPanel.Visible.ShouldBeFalse();

    // 切换到 logging 面板
    _logBtn.Raise(b => b.Pressed += null);

    _logPanel.Visible.ShouldBeTrue();
    _mergePanel.Visible.ShouldBeFalse();

    // 切换到 websocket 面板
    _webSocketBtn.Raise(b => b.Pressed += null);

    _webSocketPanel.Visible.ShouldBeTrue();
    _logPanel.Visible.ShouldBeFalse();
  }

  [Test]
  public void MainWindow_PanelNodes_ExposeInterfaces()
  {
    // 脚本面板以对应接口类型暴露；无脚本面板以 Control 类型暴露
    _mainWindow.GuessingPanelNode.ShouldBeAssignableTo<IGuessingPanel>();
    _mainWindow.SettingsPanelNode.ShouldBeAssignableTo<ISettingsPanel>();
    _mainWindow.LogPanelNode.ShouldBeAssignableTo<ILogPanel>();
    _mainWindow.WebSocketPanelNode.ShouldBeAssignableTo<IWebSocketPanel>();
    _mainWindow.MergePanelNode.ShouldBeAssignableTo<Control>();
    _mainWindow.InfoPanelNode.ShouldBeAssignableTo<Control>();
    _mainWindow.HelpPanelNode.ShouldBeAssignableTo<Control>();
  }
}
