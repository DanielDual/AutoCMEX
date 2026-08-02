namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Godot;

/// <summary>
/// Test driver for <see cref="LogPanel"/> — encapsulates setup with
/// fake node tree and fake dependency for unit testing.
/// </summary>
public sealed class LogPanelDriver : IDisposable
{
  public LogPanel Panel { get; }
  public RichTextLabel LogView { get; }
  public OptionButton LevelFilter { get; }
  public OptionButton ModuleFilter { get; }
  public Button PauseBtn { get; }
  public Button ClearBtn { get; }
  public Label LogDirLabel { get; }

  public LogPanelDriver(ILogService? service = null)
  {
    Panel = new LogPanel();

    LogView = new RichTextLabel { Name = "LogView", UniqueNameInOwner = true };
    Panel.AddChild(LogView);
    Panel.LogView = LogView;

    LevelFilter = new OptionButton { Name = "LevelFilter", UniqueNameInOwner = true };
    Panel.AddChild(LevelFilter);
    Panel.LevelFilter = LevelFilter;

    ModuleFilter = new OptionButton { Name = "ModuleFilter", UniqueNameInOwner = true };
    Panel.AddChild(ModuleFilter);
    Panel.ModuleFilter = ModuleFilter;

    PauseBtn = new Button { Name = "PauseBtn", UniqueNameInOwner = true };
    Panel.AddChild(PauseBtn);
    Panel.PauseBtn = PauseBtn;

    ClearBtn = new Button { Name = "ClearBtn", UniqueNameInOwner = true };
    Panel.AddChild(ClearBtn);
    Panel.ClearBtn = ClearBtn;

    LogDirLabel = new Label { Name = "LogDirLabel", UniqueNameInOwner = true };
    Panel.AddChild(LogDirLabel);
    Panel.LogDirLabel = LogDirLabel;

    if (service != null)
      Panel.FakeDependency<ILogService>(service);

    Panel._Notification((int)Node.NotificationReady);
  }

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
      Panel.QueueFree();
  }
}
