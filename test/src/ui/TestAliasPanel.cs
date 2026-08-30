namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class TestAliasPanel : TestClass
{
  private AliasPanel _panel = default!;
  private DataManager _dm = default!;
  private Mock<ITree> _aliasTree = default!;
  private readonly List<Node> _toCleanup = new();

  public TestAliasPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new AliasPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    var tree = new Tree();
    tree.Columns = 2;
    TestScene.AddChild(tree);
    _toCleanup.Add(tree);
    _aliasTree = new Mock<ITree>();
    _aliasTree
      .Setup(m => m.CreateItem(It.IsAny<TreeItem>(), It.IsAny<int>()))
      .Returns((TreeItem p, int i) => tree.CreateItem(p, i));
    _aliasTree.Setup(m => m.GetRoot()).Returns(() => tree.GetRoot());
    var importAliasBtn = new Mock<IButton>();
    var exportAliasBtn = new Mock<IButton>();
    var addAliasBtn = new Mock<IButton>();
    var addAliasToCreatorBtn = new Mock<IButton>();
    var deleteAliasBtn = new Mock<IButton>();
    var importFileDialog = new Mock<IFileDialog>();
    var exportFileDialog = new Mock<IFileDialog>();
    var errorDialog = new Mock<IAcceptDialog>();

    _panel.FakeNodeTree(
      new()
      {
        ["%AliasTree"] = _aliasTree.Object,
        ["%ImportAliasBtn"] = importAliasBtn.Object,
        ["%ExportAliasBtn"] = exportAliasBtn.Object,
        ["%AddAliasBtn"] = addAliasBtn.Object,
        ["%AddAliasToCreatorBtn"] = addAliasToCreatorBtn.Object,
        ["%DeleteAliasBtn"] = deleteAliasBtn.Object,
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
  public void AddAlias_AddsToDataManager()
  {
    _dm.Aliases.Count.ShouldBe(0);
    _panel.GetOnAlias()();
    _dm.Aliases.Count.ShouldBe(1);
    _dm.Aliases[0].MainName.ShouldBe("新创作者");
  }

  [Test]
  public void Refresh_UpdatesTree()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "测试创作者" });
    _panel.Refresh();
    var root = _aliasTree.Object.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(1);
  }

  [Test]
  public void GetDataManager_ReturnsInjectedInstance()
  {
    _panel.GetDataManager().ShouldBe(_dm);
  }
}
