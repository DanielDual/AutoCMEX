namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.UI.Main;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// MainWindow 单元测试
/// </summary>
public class MainWindowTest : TestClass
{
  private MainWindow _mainWindow = default!;
  private readonly List<Node> _toCleanup = new();

  public MainWindowTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _mainWindow = new MainWindow();

    // 手动设置 [Node] 属性
    var leftPanel = new VBoxContainer();
    _mainWindow.AddChild(leftPanel);
    _mainWindow.LeftPanel = leftPanel;

    var rightPanel = new Control();
    _mainWindow.AddChild(rightPanel);
    _mainWindow.RightPanel = rightPanel;

    var mergeBtn = new Button();
    _mainWindow.AddChild(mergeBtn);
    _mainWindow.MergeBtn = mergeBtn;

    var guessingBtn = new Button();
    _mainWindow.AddChild(guessingBtn);
    _mainWindow.GuessingBtn = guessingBtn;

    var infoBtn = new Button();
    _mainWindow.AddChild(infoBtn);
    _mainWindow.InfoBtn = infoBtn;

    var settingsBtn = new Button();
    _mainWindow.AddChild(settingsBtn);
    _mainWindow.SettingsBtn = settingsBtn;

    var helpBtn = new Button();
    _mainWindow.AddChild(helpBtn);
    _mainWindow.HelpBtn = helpBtn;

    var logBtn = new Button();
    _mainWindow.AddChild(logBtn);
    _mainWindow.LogBtn = logBtn;

    var webSocketBtn = new Button();
    _mainWindow.AddChild(webSocketBtn);
    _mainWindow.WebSocketBtn = webSocketBtn;

    var mergePanel = new Control();
    _mainWindow.AddChild(mergePanel);
    _mainWindow.MergePanelNode = mergePanel;

    var guessingPanel = new Control();
    _mainWindow.AddChild(guessingPanel);
    _mainWindow.GuessingPanelNode = guessingPanel;

    var infoPanel = new Control();
    _mainWindow.AddChild(infoPanel);
    _mainWindow.InfoPanelNode = infoPanel;

    var settingsPanel = new Control();
    _mainWindow.AddChild(settingsPanel);
    _mainWindow.SettingsPanelNode = settingsPanel;

    var helpPanel = new Control();
    _mainWindow.AddChild(helpPanel);
    _mainWindow.HelpPanelNode = helpPanel;

    var logPanel = new AutoCMEX.UI.Logging.LogPanel();
    _mainWindow.AddChild(logPanel);
    _mainWindow.LogPanelNode = logPanel;

    var webSocketPanel = new AutoCMEX.UI.WebSocket.WebSocketPanel();
    _mainWindow.AddChild(webSocketPanel);
    _mainWindow.WebSocketPanelNode = webSocketPanel;

    // 触发 AutoInject 生命周期
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
}
