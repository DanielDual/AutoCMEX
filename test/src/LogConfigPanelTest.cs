namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
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
    // Free all panels to prevent memory leaks
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

  private LogConfigPanel CreatePanel()
  {
    var panel = new LogConfigPanel();
    _panels.Add(panel);

    // Create child nodes programmatically
    var maxFileCountInput = new SpinBox
    {
      Name = "MaxFileCountInput",
      MinValue = 1,
      MaxValue = 1000,
      Value = 30,
    };
    panel.AddChild(maxFileCountInput);

    var minLevelOption = new OptionButton { Name = "MinLevelOption" };
    panel.AddChild(minLevelOption);

    var applyConfigBtn = new Button { Name = "ApplyConfigBtn" };
    panel.AddChild(applyConfigBtn);

    var statusLabel = new RichTextLabel { Name = "StatusLabel" };
    panel.AddChild(statusLabel);

    panel.OnReady();
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

    var panel = CreatePanel();
    panel.BindToService(svc);

    // After binding, the UI should reflect the service config
    // MaxFileCountInput.Value should be 10
    var input = panel.GetNode<SpinBox>("MaxFileCountInput");
    ((int)input.Value).ShouldBe(10);
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

    var panel = CreatePanel();
    panel.BindToService(svc);

    // Change values and apply
    var input = panel.GetNode<SpinBox>("MaxFileCountInput");
    input.Value = 20;

    var minLevelOption = panel.GetNode<OptionButton>("MinLevelOption");
    minLevelOption.Selected = 1; // Warn

    // Trigger apply
    var applyBtn = panel.GetNode<Button>("ApplyConfigBtn");
    applyBtn.Pressed += () => { };

    // Call OnApplyConfigPressed via reflection or directly
    // Since OnApplyConfigPressed is private, we test through the button press
    applyBtn.EmitSignal("pressed");

    // Verify config was updated
    svc.Config.MaxFileCount.ShouldBe(20);
  }

  [Test]
  public void ApplyConfig_NoService_DoesNotThrow()
  {
    var panel = CreatePanel();

    // Try to apply without binding to a service — should not throw
    var applyBtn = panel.GetNode<Button>("ApplyConfigBtn");
    applyBtn.EmitSignal("pressed");

    // Verify status label was updated (RichTextLabel uses bbcode, not Text property)
    var statusLabel = panel.GetNode<RichTextLabel>("StatusLabel");
    statusLabel.ShouldNotBeNull();
    statusLabel.GetParsedText().ShouldContain("not ready");
  }

  [Test]
  public void MinLevel_HasThreeOptions()
  {
    var panel = CreatePanel();
    var minLevelOption = panel.GetNode<OptionButton>("MinLevelOption");

    // Should have 3 items: Info, Warn, Error
    minLevelOption.ItemCount.ShouldBe(3);
  }

  [Test]
  public void MaxFileCount_HasValidRange()
  {
    var panel = CreatePanel();
    var input = panel.GetNode<SpinBox>("MaxFileCountInput");

    input.MinValue.ShouldBe(1);
    input.MaxValue.ShouldBe(1000);
  }
}
