namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Moq;
using Shouldly;

public class TestSpellCardPanel : TestClass
{
  private SpellCardPanel _panel = default!;
  private DataManager _dm = default!;
  private Mock<ITree> _spellCardTree = default!;
  private readonly List<Node> _toCleanup = new();

  public TestSpellCardPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new SpellCardPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    var tree = new Tree();
    tree.Columns = 3;
    TestScene.AddChild(tree);
    _toCleanup.Add(tree);
    _spellCardTree = new Mock<ITree>();
    _spellCardTree
      .Setup(m => m.CreateItem(It.IsAny<TreeItem>(), It.IsAny<int>()))
      .Returns((TreeItem p, int i) => tree.CreateItem(p, i));
    _spellCardTree.Setup(m => m.GetRoot()).Returns(() => tree.GetRoot());
    var importCardBtn = new Mock<IButton>();
    var exportCardBtn = new Mock<IButton>();
    var addBossBtn = new Mock<IButton>();
    var addCardBtn = new Mock<IButton>();
    var deleteBtn = new Mock<IButton>();
    var importFileDialog = new Mock<IFileDialog>();
    var exportFileDialog = new Mock<IFileDialog>();
    var errorDialog = new Mock<IAcceptDialog>();

    _panel.FakeNodeTree(
      new()
      {
        ["%SpellCardTree"] = _spellCardTree.Object,
        ["%ImportCardBtn"] = importCardBtn.Object,
        ["%ExportCardBtn"] = exportCardBtn.Object,
        ["%AddBossBtn"] = addBossBtn.Object,
        ["%AddCardBtn"] = addCardBtn.Object,
        ["%DeleteBtn"] = deleteBtn.Object,
        ["%ImportFileDialog"] = importFileDialog.Object,
        ["%ExportFileDialog"] = exportFileDialog.Object,
        ["%ErrorDialog"] = errorDialog.Object,
      }
    );

    _panel.FakeDependency<DataManager>(_dm);
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }
    _toCleanup.Clear();
  }

  [Test]
  public void AddBoss_AddsToDataManager()
  {
    _dm.Bosses.Count.ShouldBe(0);
    _panel.GetOnAddBoss()();
    _dm.Bosses.Count.ShouldBe(1);
    _dm.Bosses[0].Name.ShouldBe("新 Boss");
  }

  [Test]
  public void Refresh_UpdatesTree()
  {
    _dm.Bosses.Add(new Boss { Name = "测试Boss" });
    _dm.Bosses[0].SpellCards.Add(new SpellCard { Name = new AutoValue<string>("符卡1") });
    _panel.GetSelectBoss()(_dm.Bosses[0]);
    _panel.Refresh();
    var root = _spellCardTree.Object.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void GetDataManager_ReturnsInjectedInstance()
  {
    _panel.GetDataManager().ShouldBe(_dm);
  }
}
