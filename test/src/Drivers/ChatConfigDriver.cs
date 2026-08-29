namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Godot;
using Moq;

public sealed class ChatConfigDriver : IDisposable
{
  public ChatConfigPanel Panel { get; }
  public Mock<ISpinBox> PortInput { get; }
  public Mock<IOptionButton> ModeSelect { get; }
  public Mock<ILineEdit> KoishiUrlInput { get; }
  public Mock<IHBoxContainer> KoishiUrlRow { get; }
  public Mock<IOptionButton> FilterSelect { get; }
  public Mock<IButton> InstallBtn { get; }
  public Mock<IFileDialog> PluginFileDialog { get; }
  public Mock<IAcceptDialog> PluginOkDialog { get; }

  public ChatConfigDriver(DataManager? dm = null)
  {
    Panel = new ChatConfigPanel();
    (Panel as IAutoInit).IsTesting = true;

    PortInput = new Mock<ISpinBox>();
    PortInput.SetupProperty(m => m.MinValue, 1);
    PortInput.SetupProperty(m => m.MaxValue, 65535);
    ModeSelect = new Mock<IOptionButton>();
    KoishiUrlInput = new Mock<ILineEdit>();
    KoishiUrlRow = new Mock<IHBoxContainer>();
    FilterSelect = new Mock<IOptionButton>();
    InstallBtn = new Mock<IButton>();
    PluginFileDialog = new Mock<IFileDialog>();
    PluginOkDialog = new Mock<IAcceptDialog>();

    Panel.FakeNodeTree(
      new()
      {
        ["%PortInput"] = PortInput.Object,
        ["%ModeSelect"] = ModeSelect.Object,
        ["%KoishiUrlInput"] = KoishiUrlInput.Object,
        ["%KoishiUrlRow"] = KoishiUrlRow.Object,
        ["%FilterSelect"] = FilterSelect.Object,
        ["%InstallBtn"] = InstallBtn.Object,
        ["%PluginFileDialog"] = PluginFileDialog.Object,
        ["%PluginOkDialog"] = PluginOkDialog.Object,
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
