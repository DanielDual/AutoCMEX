namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Godot;

/// <summary>
/// Test driver for <see cref="ChatConfigPanel"/> — encapsulates setup with
/// fake node tree and fake dependency for unit testing.
/// </summary>
public sealed class ChatConfigDriver : IDisposable
{
  public ChatConfigPanel Panel { get; }
  public SpinBox PortInput { get; }
  public OptionButton ModeSelect { get; }
  public LineEdit KoishiUrlInput { get; }
  public HBoxContainer KoishiUrlRow { get; }
  public OptionButton FilterSelect { get; }
  public Button InstallBtn { get; }
  public FileDialog PluginFileDialog { get; }
  public AcceptDialog PluginOkDialog { get; }

  public ChatConfigDriver(DataManager? dm = null)
  {
    Panel = new ChatConfigPanel();

    PortInput = new SpinBox { Name = "PortInput", UniqueNameInOwner = true };
    Panel.AddChild(PortInput);
    Panel.PortInput = PortInput;

    ModeSelect = new OptionButton { Name = "ModeSelect", UniqueNameInOwner = true };
    Panel.AddChild(ModeSelect);
    Panel.ModeSelect = ModeSelect;

    KoishiUrlInput = new LineEdit { Name = "KoishiUrlInput", UniqueNameInOwner = true };
    Panel.AddChild(KoishiUrlInput);
    Panel.KoishiUrlInput = KoishiUrlInput;

    KoishiUrlRow = new HBoxContainer { Name = "KoishiUrlRow", UniqueNameInOwner = true };
    Panel.AddChild(KoishiUrlRow);
    Panel.KoishiUrlRow = KoishiUrlRow;

    FilterSelect = new OptionButton { Name = "FilterSelect", UniqueNameInOwner = true };
    Panel.AddChild(FilterSelect);
    Panel.FilterSelect = FilterSelect;

    InstallBtn = new Button { Name = "InstallBtn", UniqueNameInOwner = true };
    Panel.AddChild(InstallBtn);
    Panel.InstallBtn = InstallBtn;

    PluginFileDialog = new FileDialog { Name = "PluginFileDialog", UniqueNameInOwner = true };
    Panel.AddChild(PluginFileDialog);
    Panel.PluginFileDialog = PluginFileDialog;

    PluginOkDialog = new AcceptDialog { Name = "PluginOkDialog", UniqueNameInOwner = true };
    Panel.AddChild(PluginOkDialog);
    Panel.PluginOkDialog = PluginOkDialog;

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
