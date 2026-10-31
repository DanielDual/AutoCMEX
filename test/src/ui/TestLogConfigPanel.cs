namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class TestLogConfigPanel : TestClass
{
  private LogConfigPanel _panel = default!;
  private Mock<IOptionButton> _minLevelOption = default!;
  private readonly List<Node> _toCleanup = new();

  public TestLogConfigPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new LogConfigPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    var maxFileCountInput = new Mock<ISpinBox>();
    maxFileCountInput.SetupProperty(m => m.MinValue);
    maxFileCountInput.SetupProperty(m => m.MaxValue);
    maxFileCountInput.SetupProperty(m => m.Value);

    _minLevelOption = new Mock<IOptionButton>();
    var applyConfigBtn = new Mock<IButton>();
    applyConfigBtn.SetupProperty(m => m.Text);
    var statusLabel = new Mock<IRichTextLabel>();

    _panel.FakeNodeTree(
      new()
      {
        ["%MaxFileCountInput"] = maxFileCountInput.Object,
        ["%MinLevelOption"] = _minLevelOption.Object,
        ["%ApplyConfigBtn"] = applyConfigBtn.Object,
        ["%StatusLabel"] = statusLabel.Object,
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
    _minLevelOption.Verify(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(3));
  }

  [Test]
  public void ApplyConfigBtn_HasCorrectText()
  {
    _panel.ApplyConfigBtn.Text.ShouldBe("Apply Config");
  }
}
