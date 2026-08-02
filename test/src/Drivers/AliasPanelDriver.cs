namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Godot;

/// <summary>
/// Test driver for <see cref="AliasPanel"/> — encapsulates setup with
/// fake node tree and fake dependency for unit testing.
/// </summary>
public sealed class AliasPanelDriver : IDisposable
{
  public AliasPanel Panel { get; }
  public Tree AliasTree { get; }
  public Button ImportAliasBtn { get; }
  public Button ExportAliasBtn { get; }
  public Button AddAliasBtn { get; }
  public Button AddAliasToCreatorBtn { get; }
  public Button DeleteAliasBtn { get; }
  public FileDialog ImportFileDialog { get; }
  public FileDialog ExportFileDialog { get; }
  public AcceptDialog ErrorDialog { get; }

  public AliasPanelDriver(DataManager? dm = null)
  {
    Panel = new AliasPanel();

    AliasTree = new Tree { Name = "AliasTree", UniqueNameInOwner = true };
    Panel.AddChild(AliasTree);
    Panel.AliasTree = AliasTree;

    ImportAliasBtn = new Button { Name = "ImportAliasBtn", UniqueNameInOwner = true };
    Panel.AddChild(ImportAliasBtn);
    Panel.ImportAliasBtn = ImportAliasBtn;

    ExportAliasBtn = new Button { Name = "ExportAliasBtn", UniqueNameInOwner = true };
    Panel.AddChild(ExportAliasBtn);
    Panel.ExportAliasBtn = ExportAliasBtn;

    AddAliasBtn = new Button { Name = "AddAliasBtn", UniqueNameInOwner = true };
    Panel.AddChild(AddAliasBtn);
    Panel.AddAliasBtn = AddAliasBtn;

    AddAliasToCreatorBtn = new Button { Name = "AddAliasToCreatorBtn", UniqueNameInOwner = true };
    Panel.AddChild(AddAliasToCreatorBtn);
    Panel.AddAliasToCreatorBtn = AddAliasToCreatorBtn;

    DeleteAliasBtn = new Button { Name = "DeleteAliasBtn", UniqueNameInOwner = true };
    Panel.AddChild(DeleteAliasBtn);
    Panel.DeleteAliasBtn = DeleteAliasBtn;

    ImportFileDialog = new FileDialog { Name = "ImportFileDialog", UniqueNameInOwner = true };
    Panel.AddChild(ImportFileDialog);
    Panel.ImportFileDialog = ImportFileDialog;

    ExportFileDialog = new FileDialog { Name = "ExportFileDialog", UniqueNameInOwner = true };
    Panel.AddChild(ExportFileDialog);
    Panel.ExportFileDialog = ExportFileDialog;

    ErrorDialog = new AcceptDialog { Name = "ErrorDialog", UniqueNameInOwner = true };
    Panel.AddChild(ErrorDialog);
    Panel.ErrorDialog = ErrorDialog;

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
