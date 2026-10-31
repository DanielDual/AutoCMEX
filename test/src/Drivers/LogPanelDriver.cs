namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Godot;
using Moq;

public sealed class LogPanelDriver : IDisposable
{
  public LogPanel Panel { get; }
  public Mock<IRichTextLabel> LogView { get; }
  public Mock<IOptionButton> LevelFilter { get; }
  public Mock<IOptionButton> ModuleFilter { get; }
  public Mock<IButton> PauseBtn { get; }
  public Mock<IButton> ClearBtn { get; }
  public Mock<ILabel> LogDirLabel { get; }

  public LogPanelDriver(ILogService? service = null)
  {
    Panel = new LogPanel();
    (Panel as IAutoInit).IsTesting = true;

    LogView = new Mock<IRichTextLabel>();
    LevelFilter = new Mock<IOptionButton>();
    ModuleFilter = new Mock<IOptionButton>();
    PauseBtn = new Mock<IButton>();
    ClearBtn = new Mock<IButton>();
    LogDirLabel = new Mock<ILabel>();

    Panel.FakeNodeTree(
      new()
      {
        ["%LogView"] = LogView.Object,
        ["%LevelFilter"] = LevelFilter.Object,
        ["%ModuleFilter"] = ModuleFilter.Object,
        ["%PauseBtn"] = PauseBtn.Object,
        ["%ClearBtn"] = ClearBtn.Object,
        ["%LogDirLabel"] = LogDirLabel.Object,
      }
    );

    if (service != null)
      Panel.FakeDependency<ILogService>(service);

    Panel._Notification((int)Node.NotificationEnterTree);
    Panel._Notification((int)Node.NotificationReady);
  }

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
      Panel.QueueFree();
  }
}
