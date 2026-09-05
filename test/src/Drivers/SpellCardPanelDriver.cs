namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Godot;
using Moq;

public sealed class SpellCardPanelDriver : IDisposable
{
  public SpellCardPanel Panel { get; }
  public Mock<ITree> SpellCardTree { get; }
  public Mock<IOptionButton> BossSelect { get; }
  public Mock<IButton> ImportCardBtn { get; }
  public Mock<IButton> ExportCardBtn { get; }
  public Mock<IButton> AddBossBtn { get; }
  public Mock<IButton> AddCardBtn { get; }
  public Mock<IButton> DeleteBtn { get; }
  public Mock<IFileDialog> ImportFileDialog { get; }
  public Mock<IFileDialog> ExportFileDialog { get; }
  public Mock<IAcceptDialog> ErrorDialog { get; }

  public SpellCardPanelDriver(DataManager? dm = null)
  {
    Panel = new SpellCardPanel();
    (Panel as IAutoInit).IsTesting = true;

    SpellCardTree = new Mock<ITree>();
    BossSelect = new Mock<IOptionButton>();
    ImportCardBtn = new Mock<IButton>();
    ExportCardBtn = new Mock<IButton>();
    AddBossBtn = new Mock<IButton>();
    AddCardBtn = new Mock<IButton>();
    DeleteBtn = new Mock<IButton>();
    ImportFileDialog = new Mock<IFileDialog>();
    ExportFileDialog = new Mock<IFileDialog>();
    ErrorDialog = new Mock<IAcceptDialog>();

    Panel.FakeNodeTree(
      new()
      {
        ["%SpellCardTree"] = SpellCardTree.Object,
        ["%BossSelect"] = BossSelect.Object,
        ["%ImportCardBtn"] = ImportCardBtn.Object,
        ["%ExportCardBtn"] = ExportCardBtn.Object,
        ["%AddBossBtn"] = AddBossBtn.Object,
        ["%AddCardBtn"] = AddCardBtn.Object,
        ["%DeleteBtn"] = DeleteBtn.Object,
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
