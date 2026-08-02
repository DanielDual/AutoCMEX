namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Godot;

/// <summary>
/// Test driver for <see cref="AiModelConfigPanel"/> — encapsulates setup with
/// fake node tree and fake dependency for unit testing.
/// </summary>
public sealed class AiModelConfigDriver : IDisposable
{
  public AiModelConfigPanel Panel { get; }
  public OptionButton ActiveModelSelect { get; }
  public SpinBox TimeoutInput { get; }
  public VBoxContainer ModelList { get; }
  public Button AddModelBtn { get; }

  public AiModelConfigDriver(DataManager? dm = null)
  {
    Panel = new AiModelConfigPanel();

    ActiveModelSelect = new OptionButton { Name = "ActiveModelSelect", UniqueNameInOwner = true };
    Panel.AddChild(ActiveModelSelect);
    Panel.ActiveModelSelect = ActiveModelSelect;

    TimeoutInput = new SpinBox { Name = "TimeoutInput", UniqueNameInOwner = true };
    Panel.AddChild(TimeoutInput);
    Panel.TimeoutInput = TimeoutInput;

    ModelList = new VBoxContainer { Name = "ModelList", UniqueNameInOwner = true };
    Panel.AddChild(ModelList);
    Panel.ModelList = ModelList;

    AddModelBtn = new Button { Name = "AddModelBtn", UniqueNameInOwner = true };
    Panel.AddChild(AddModelBtn);
    Panel.AddModelBtn = AddModelBtn;

    if (dm != null)
      Panel.FakeDependency<DataManager>(dm);

    Panel._Notification((int)Node.NotificationReady);
  }

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
      Panel.QueueFree();
  }
}
