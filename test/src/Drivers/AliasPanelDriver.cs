namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Godot;
using Moq;

public sealed class AliasPanelDriver : IDisposable
{
  public AliasPanel Panel { get; }
  public Mock<ITree> AliasTree { get; }
  public Mock<IButton> ImportAliasBtn { get; }
  public Mock<IButton> ExportAliasBtn { get; }
  public Mock<IButton> AddAliasBtn { get; }
  public Mock<IButton> AddAliasToCreatorBtn { get; }
  public Mock<IButton> DeleteAliasBtn { get; }
  public Mock<IFileDialog> ImportFileDialog { get; }
  public Mock<IFileDialog> ExportFileDialog { get; }
  public Mock<IAcceptDialog> ErrorDialog { get; }

  public AliasPanelDriver(DataManager? dm = null)
  {
    Panel = new AliasPanel();
    (Panel as IAutoInit).IsTesting = true;

    AliasTree = new Mock<ITree>();
    ImportAliasBtn = new Mock<IButton>();
    ExportAliasBtn = new Mock<IButton>();
    AddAliasBtn = new Mock<IButton>();
    AddAliasToCreatorBtn = new Mock<IButton>();
    DeleteAliasBtn = new Mock<IButton>();
    ImportFileDialog = new Mock<IFileDialog>();
    ExportFileDialog = new Mock<IFileDialog>();
    ErrorDialog = new Mock<IAcceptDialog>();

    Panel.FakeNodeTree(
      new()
      {
        ["%AliasTree"] = AliasTree.Object,
        ["%ImportAliasBtn"] = ImportAliasBtn.Object,
        ["%ExportAliasBtn"] = ExportAliasBtn.Object,
        ["%AddAliasBtn"] = AddAliasBtn.Object,
        ["%AddAliasToCreatorBtn"] = AddAliasToCreatorBtn.Object,
        ["%DeleteAliasBtn"] = DeleteAliasBtn.Object,
        ["%ImportFileDialog"] = ImportFileDialog.Object,
        ["%ExportFileDialog"] = ExportFileDialog.Object,
        ["%ErrorDialog"] = ErrorDialog.Object,
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
