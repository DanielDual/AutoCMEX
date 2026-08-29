namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.UI.Main;
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

  private Mock<IControl> _mergePanel = default!;
  private Mock<IControl> _guessingPanel = default!;
  private Mock<IControl> _infoPanel = default!;
  private Mock<IControl> _settingsPanel = default!;
  private Mock<IControl> _helpPanel = default!;
  private Mock<IControl> _logPanel = default!;
  private Mock<IControl> _webSocketPanel = default!;

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

    _mergePanel = new Mock<IControl>();
    _guessingPanel = new Mock<IControl>();
    _infoPanel = new Mock<IControl>();
    _settingsPanel = new Mock<IControl>();
    _helpPanel = new Mock<IControl>();
    _logPanel = new Mock<IControl>();
    _webSocketPanel = new Mock<IControl>();

    // 面板 Visible 属性需要 SetupProperty 才能验证切换行为
    foreach (
      var panel in new[]
      {
        _mergePanel,
        _guessingPanel,
        _infoPanel,
        _settingsPanel,
        _helpPanel,
        _logPanel,
        _webSocketPanel,
      }
    )
    {
      panel.SetupProperty(m => m.Visible, false);
    }

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
        ["%MergePanel"] = _mergePanel.Object,
        ["%GuessingPanel"] = _guessingPanel.Object,
        ["%InfoPanel"] = _infoPanel.Object,
        ["%SettingsPanel"] = _settingsPanel.Object,
        ["%HelpPanel"] = _helpPanel.Object,
        ["%LogPanel"] = _logPanel.Object,
        ["%WebSocketPanel"] = _webSocketPanel.Object,
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
    _guessingPanel.Object.Visible.ShouldBeTrue();
    _mergePanel.Object.Visible.ShouldBeFalse();
    _logPanel.Object.Visible.ShouldBeFalse();
    _webSocketPanel.Object.Visible.ShouldBeFalse();

    // 切换到 merge 面板
    _mergeBtn.Raise(b => b.Pressed += null);

    // 目标面板可见，原面板隐藏
    _mergePanel.Object.Visible.ShouldBeTrue();
    _guessingPanel.Object.Visible.ShouldBeFalse();

    // 切换到 logging 面板（INode 适配器在运行时实现 IControl）
    _logBtn.Raise(b => b.Pressed += null);

    _logPanel.Object.Visible.ShouldBeTrue();
    _mergePanel.Object.Visible.ShouldBeFalse();

    // 切换到 websocket 面板
    _webSocketBtn.Raise(b => b.Pressed += null);

    _webSocketPanel.Object.Visible.ShouldBeTrue();
    _logPanel.Object.Visible.ShouldBeFalse();
  }
}
