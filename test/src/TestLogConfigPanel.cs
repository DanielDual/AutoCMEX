namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class TestLogConfigPanel : TestClass
{
  private LogConfigPanel _panel = default!;
  private readonly List<Node> _toCleanup = new();

  public TestLogConfigPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new LogConfigPanel();

    var maxFileCountInput = new SpinBox();
    _panel.AddChild(maxFileCountInput);
    _panel.MaxFileCountInput = maxFileCountInput;

    var minLevelOption = new OptionButton();
    _panel.AddChild(minLevelOption);
    _panel.MinLevelOption = minLevelOption;

    var applyConfigBtn = new Button();
    _panel.AddChild(applyConfigBtn);
    _panel.ApplyConfigBtn = applyConfigBtn;

    var statusLabel = new RichTextLabel();
    _panel.AddChild(statusLabel);
    _panel.StatusLabel = statusLabel;

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
  public void MaxFileCountInput_IsNotNull()
  {
    _panel.MaxFileCountInput.ShouldNotBeNull();
  }

  [Test]
  public void MinLevelOption_IsNotNull()
  {
    _panel.MinLevelOption.ShouldNotBeNull();
  }

  [Test]
  public void ApplyConfigBtn_IsNotNull()
  {
    _panel.ApplyConfigBtn.ShouldNotBeNull();
  }

  [Test]
  public void StatusLabel_IsNotNull()
  {
    _panel.StatusLabel.ShouldNotBeNull();
  }

  [Test]
  public void MaxFileCountInput_HasCorrectRange()
  {
    _panel.MaxFileCountInput.MinValue.ShouldBe(1);
    _panel.MaxFileCountInput.MaxValue.ShouldBe(1000);
  }

  [Test]
  public void MinLevelOption_HasThreeOptions()
  {
    _panel.MinLevelOption.ItemCount.ShouldBe(3);
  }

  [Test]
  public void ApplyConfigBtn_HasCorrectText()
  {
    _panel.ApplyConfigBtn.Text.ShouldBe("Apply Config");
  }
}
