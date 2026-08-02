namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class TestLogPanel : TestClass
{
  private LogPanel _panel = default!;
  private readonly List<Node> _toCleanup = new();

  public TestLogPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new LogPanel();

    var logView = new RichTextLabel();
    _panel.AddChild(logView);
    _panel.LogView = logView;

    var levelFilter = new OptionButton();
    _panel.AddChild(levelFilter);
    _panel.LevelFilter = levelFilter;

    var moduleFilter = new OptionButton();
    _panel.AddChild(moduleFilter);
    _panel.ModuleFilter = moduleFilter;

    var pauseBtn = new Button();
    _panel.AddChild(pauseBtn);
    _panel.PauseBtn = pauseBtn;

    var clearBtn = new Button();
    _panel.AddChild(clearBtn);
    _panel.ClearBtn = clearBtn;

    var logDirLabel = new Label();
    _panel.AddChild(logDirLabel);
    _panel.LogDirLabel = logDirLabel;

    var logCfg = new LogConfig { LogDirectory = System.IO.Path.GetTempPath() };
    _panel.FakeDependency<ILogService>(new LogService(logCfg, includeGodotConsole: false));
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
  public void Panel_IsNotNull()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void LogView_IsNotNull()
  {
    _panel.LogView.ShouldNotBeNull();
  }

  [Test]
  public void LevelFilter_HasFourOptions()
  {
    _panel.LevelFilter.ItemCount.ShouldBe(4);
  }

  [Test]
  public void PauseBtn_IsToggleMode()
  {
    _panel.PauseBtn.ToggleMode.ShouldBeTrue();
  }

  [Test]
  public void ClearBtn_IsNotNull()
  {
    _panel.ClearBtn.ShouldNotBeNull();
  }
}
