namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// LogConfigPanel unit tests.
/// </summary>
public class LogConfigPanelTest : TestClass
{
  private string _testLogDir = "";
  private readonly System.Collections.Generic.List<LogConfigPanel> _panels = new();

  public LogConfigPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _testLogDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}");
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var panel in _panels)
    {
      if (panel != null && !panel.IsQueuedForDeletion())
      {
        panel.QueueFree();
      }
    }
    _panels.Clear();

    if (Directory.Exists(_testLogDir))
    {
      try
      {
        Directory.Delete(_testLogDir, true);
      }
      catch { }
    }
  }

  private LogConfigPanel CreatePanel(
    ILogService? service = null,
    Mock<ISpinBox>? maxFileCountInput = null,
    Mock<IOptionButton>? minLevelOption = null,
    Mock<IButton>? applyConfigBtn = null,
    Mock<IRichTextLabel>? statusLabel = null
  )
  {
    var panel = new LogConfigPanel();
    (panel as IAutoInit).IsTesting = true;
    _panels.Add(panel);

    // Use Moq mocks with GodotNodeInterfaces types for FakeNodeTree
    maxFileCountInput ??= new Mock<ISpinBox>();
    maxFileCountInput.SetupProperty(m => m.MinValue, 1);
    maxFileCountInput.SetupProperty(m => m.MaxValue, 1000);
    maxFileCountInput.SetupProperty(m => m.Value, 30);

    minLevelOption ??= new Mock<IOptionButton>();

    applyConfigBtn ??= new Mock<IButton>();

    statusLabel ??= new Mock<IRichTextLabel>();

    panel.FakeNodeTree(
      new()
      {
        ["%MaxFileCountInput"] = maxFileCountInput.Object,
        ["%MinLevelOption"] = minLevelOption.Object,
        ["%ApplyConfigBtn"] = applyConfigBtn.Object,
        ["%StatusLabel"] = statusLabel.Object,
      }
    );

    // Always provide a service for AutoInject resolution; use a default if none given
    var resolvedService =
      service
      ?? new LogService(new LogConfig { LogDirectory = _testLogDir }, includeGodotConsole: false);
    panel.FakeDependency<ILogService>(resolvedService);

    // Trigger AutoInject resolution: ConnectNodes + OnReady() + OnResolved()
    panel._Notification((int)Node.NotificationEnterTree);
    panel._Notification((int)Node.NotificationReady);

    return panel;
  }

  [Test]
  public void FakeDependency_UpdatesUIFromServiceConfig()
  {
    var cfg = new LogConfig
    {
      LogDirectory = _testLogDir,
      MaxFileCount = 10,
      MinLevel = LogLevel.Warn,
    };
    using var svc = new LogService(cfg, includeGodotConsole: false);

    var maxFileCountInput = new Mock<ISpinBox>();
    maxFileCountInput.SetupProperty(m => m.MinValue, 1);
    maxFileCountInput.SetupProperty(m => m.MaxValue, 1000);
    maxFileCountInput.SetupProperty(m => m.Value, 30);

    var panel = CreatePanel(svc, maxFileCountInput: maxFileCountInput);

    // After binding, OnResolved() should set Value = 10 from service config
    maxFileCountInput.VerifySet(m => m.Value = 10);
  }

  [Test]
  public void ApplyConfig_UpdatesServiceConfig()
  {
    var cfg = new LogConfig
    {
      LogDirectory = _testLogDir,
      MaxFileCount = 10,
      MinLevel = LogLevel.Info,
    };
    using var svc = new LogService(cfg, includeGodotConsole: false);

    var maxFileCountInput = new Mock<ISpinBox>();
    maxFileCountInput.SetupProperty(m => m.MinValue, 1);
    maxFileCountInput.SetupProperty(m => m.MaxValue, 1000);
    maxFileCountInput.SetupProperty(m => m.Value, 20);

    var minLevelOption = new Mock<IOptionButton>();
    minLevelOption.SetupProperty(m => m.Selected, 1);

    var applyConfigBtn = new Mock<IButton>();

    var panel = CreatePanel(
      svc,
      maxFileCountInput: maxFileCountInput,
      minLevelOption: minLevelOption,
      applyConfigBtn: applyConfigBtn
    );

    // Simulate user input (OnResolved overwrites Value from service config)
    maxFileCountInput.Object.Value = 20;

    // Trigger apply via the Pressed event
    applyConfigBtn.Raise(b => b.Pressed += null);

    // Verify config was updated
    svc.Config.MaxFileCount.ShouldBe(20);
  }

  [Test]
  public void ApplyConfig_WithDefaultService_DoesNotThrow()
  {
    var applyConfigBtn = new Mock<IButton>();
    var statusLabel = new Mock<IRichTextLabel>();

    var panel = CreatePanel(applyConfigBtn: applyConfigBtn, statusLabel: statusLabel);

    // Apply with the default service — should not throw
    applyConfigBtn.Raise(b => b.Pressed += null);

    // Verify status label was updated
    statusLabel.Verify(m => m.Clear());
    statusLabel.Verify(m => m.AppendText(It.IsAny<string>()));
  }

  [Test]
  public void MinLevel_HasThreeOptions()
  {
    var minLevelOption = new Mock<IOptionButton>();

    var panel = CreatePanel(minLevelOption: minLevelOption);

    // Should have 3 items: Info, Warn, Error
    minLevelOption.Verify(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(3));
  }

  [Test]
  public void MaxFileCount_HasValidRange()
  {
    var maxFileCountInput = new Mock<ISpinBox>();
    maxFileCountInput.SetupProperty(m => m.MinValue, 1);
    maxFileCountInput.SetupProperty(m => m.MaxValue, 1000);

    var panel = CreatePanel(maxFileCountInput: maxFileCountInput);

    maxFileCountInput.VerifySet(m => m.MinValue = 1);
    maxFileCountInput.VerifySet(m => m.MaxValue = 1000);
  }
}
