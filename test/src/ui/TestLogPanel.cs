namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class TestLogPanel : TestClass
{
  private LogPanel _panel = default!;
  private Mock<IOptionButton> _levelFilter = default!;
  private readonly List<Node> _toCleanup = new();

  public TestLogPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new LogPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    var logView = new Mock<IRichTextLabel>();
    _levelFilter = new Mock<IOptionButton>();
    var moduleFilter = new Mock<IOptionButton>();
    var pauseBtn = new Mock<IButton>();
    pauseBtn.SetupProperty(m => m.ToggleMode);
    var clearBtn = new Mock<IButton>();
    var logDirLabel = new Mock<ILabel>();

    _panel.FakeNodeTree(
      new()
      {
        ["%LogView"] = logView.Object,
        ["%LevelFilter"] = _levelFilter.Object,
        ["%ModuleFilter"] = moduleFilter.Object,
        ["%PauseBtn"] = pauseBtn.Object,
        ["%ClearBtn"] = clearBtn.Object,
        ["%LogDirLabel"] = logDirLabel.Object,
      }
    );

    var logCfg = new LogConfig { LogDirectory = System.IO.Path.GetTempPath() };
    _panel.FakeDependency<ILogService>(new LogService(logCfg, includeGodotConsole: false));
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
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
  public void Panel_IsNotNull() => _panel.ShouldNotBeNull();

  [Test]
  public void LogView_IsNotNull() => _panel.LogView.ShouldNotBeNull();

  [Test]
  public void LevelFilter_HasFourOptions() =>
    _levelFilter.Verify(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(4));

  [Test]
  public void PauseBtn_IsToggleMode() => _panel.PauseBtn.ToggleMode.ShouldBeTrue();

  [Test]
  public void ClearBtn_IsNotNull() => _panel.ClearBtn.ShouldNotBeNull();
}
