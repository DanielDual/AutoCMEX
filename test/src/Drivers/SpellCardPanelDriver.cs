namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Godot;

/// <summary>
/// Test driver for <see cref="SpellCardPanel"/> — encapsulates setup with
/// fake node tree and fake dependency for unit testing.
/// </summary>
public sealed class SpellCardPanelDriver : IDisposable
{
  public SpellCardPanel Panel { get; }
  public Tree SpellCardTree { get; }
  public Button ImportCardBtn { get; }
  public Button ExportCardBtn { get; }
  public Button AddBossBtn { get; }
  public Button AddCardBtn { get; }
  public Button DeleteBtn { get; }
  public FileDialog ImportFileDialog { get; }
  public FileDialog ExportFileDialog { get; }
  public AcceptDialog ErrorDialog { get; }

  public SpellCardPanelDriver(DataManager? dm = null)
  {
    Panel = new SpellCardPanel();

    SpellCardTree = new Tree { Name = "SpellCardTree", UniqueNameInOwner = true };
    Panel.AddChild(SpellCardTree);
    Panel.SpellCardTree = SpellCardTree;

    ImportCardBtn = new Button { Name = "ImportCardBtn", UniqueNameInOwner = true };
    Panel.AddChild(ImportCardBtn);
    Panel.ImportCardBtn = ImportCardBtn;

    ExportCardBtn = new Button { Name = "ExportCardBtn", UniqueNameInOwner = true };
    Panel.AddChild(ExportCardBtn);
    Panel.ExportCardBtn = ExportCardBtn;

    AddBossBtn = new Button { Name = "AddBossBtn", UniqueNameInOwner = true };
    Panel.AddChild(AddBossBtn);
    Panel.AddBossBtn = AddBossBtn;

    AddCardBtn = new Button { Name = "AddCardBtn", UniqueNameInOwner = true };
    Panel.AddChild(AddCardBtn);
    Panel.AddCardBtn = AddCardBtn;

    DeleteBtn = new Button { Name = "DeleteBtn", UniqueNameInOwner = true };
    Panel.AddChild(DeleteBtn);
    Panel.DeleteBtn = DeleteBtn;

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
