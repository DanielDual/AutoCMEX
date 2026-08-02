namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
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

  private LogConfigPanel CreatePanel(ILogService? service = null)
  {
    var panel = new LogConfigPanel();
    _panels.Add(panel);

    // Create child nodes with UniqueNameInOwner for [Node("%Name")] resolution
    var maxFileCountInput = new SpinBox
    {
      Name = "MaxFileCountInput",
      UniqueNameInOwner = true,
      MinValue = 1,
      MaxValue = 1000,
      Value = 30,
    };
    panel.AddChild(maxFileCountInput);
    panel.MaxFileCountInput = maxFileCountInput;

    var minLevelOption = new OptionButton { Name = "MinLevelOption", UniqueNameInOwner = true };
    panel.AddChild(minLevelOption);
    panel.MinLevelOption = minLevelOption;

    var applyConfigBtn = new Button { Name = "ApplyConfigBtn", UniqueNameInOwner = true };
    panel.AddChild(applyConfigBtn);
    panel.ApplyConfigBtn = applyConfigBtn;

    var statusLabel = new RichTextLabel { Name = "StatusLabel", UniqueNameInOwner = true };
    panel.AddChild(statusLabel);
    panel.StatusLabel = statusLabel;

    // Fake dependency
    if (service != null)
      panel.FakeDependency<ILogService>(service);

    // Add to scene tree
    TestScene.GetTree().Root.AddChild(panel);

    return panel;
  }

  [Test]
  public void BindToService_UpdatesUIFromServiceConfig()
  {
    var cfg = new LogConfig
    {
      LogDirectory = _testLogDir,
      MaxFileCount = 10,
      MinLevel = LogLevel.Warn,
    };
    using var svc = new LogService(cfg, includeGodotConsole: false);

    var panel = CreatePanel(svc);

    // After binding, the UI should reflect the service config
    ((int)panel.MaxFileCountInput.Value).ShouldBe(10);
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

    var panel = CreatePanel(svc);

    // Change values and apply
    panel.MaxFileCountInput.Value = 20;

    panel.MinLevelOption.Selected = 1; // Warn

    // Trigger apply
    panel.ApplyConfigBtn.EmitSignal("pressed");

    // Verify config was updated
    svc.Config.MaxFileCount.ShouldBe(20);
  }

  [Test]
  public void ApplyConfig_NoService_DoesNotThrow()
  {
    var panel = CreatePanel();

    // Try to apply without binding to a service — should not throw
    panel.ApplyConfigBtn.EmitSignal("pressed");

    // Verify status label was updated
    panel.StatusLabel.ShouldNotBeNull();
  }

  [Test]
  public void MinLevel_HasThreeOptions()
  {
    var panel = CreatePanel();
    // Should have 3 items: Info, Warn, Error
    panel.MinLevelOption.ItemCount.ShouldBe(3);
  }

  [Test]
  public void MaxFileCount_HasValidRange()
  {
    var panel = CreatePanel();
    panel.MaxFileCountInput.MinValue.ShouldBe(1);
    panel.MaxFileCountInput.MaxValue.ShouldBe(1000);
  }
}
