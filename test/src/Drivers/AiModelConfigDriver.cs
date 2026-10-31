namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Godot;
using Moq;

public sealed class AiModelConfigDriver : IDisposable
{
  public AiModelConfigPanel Panel { get; }
  public Mock<IOptionButton> ActiveModelSelect { get; }
  public Mock<ISpinBox> TimeoutInput { get; }
  public Mock<IVBoxContainer> ModelList { get; }
  public Mock<IButton> AddModelBtn { get; }

  public AiModelConfigDriver(DataManager? dm = null)
  {
    Panel = new AiModelConfigPanel();
    (Panel as IAutoInit).IsTesting = true;

    ActiveModelSelect = new Mock<IOptionButton>();
    TimeoutInput = new Mock<ISpinBox>();
    TimeoutInput.SetupProperty(m => m.MinValue, 1);
    TimeoutInput.SetupProperty(m => m.MaxValue, 600);
    ModelList = new Mock<IVBoxContainer>();
    AddModelBtn = new Mock<IButton>();

    Panel.FakeNodeTree(
      new()
      {
        ["%ActiveModelSelect"] = ActiveModelSelect.Object,
        ["%TimeoutInput"] = TimeoutInput.Object,
        ["%ModelList"] = ModelList.Object,
        ["%AddModelBtn"] = AddModelBtn.Object,
      }
    );

    if (dm != null)
      Panel.FakeDependency<DataManager>(dm);

    Panel._Notification((int)Node.NotificationEnterTree);
    Panel._Notification((int)Node.NotificationReady);
  }

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
      Panel.QueueFree();
  }
}
