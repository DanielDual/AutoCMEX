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
  private Mock<IAcceptDialog> _errorDialog = default!;
  private Tree _tree = default!;
  private readonly List<Node> _toCleanup = new();

  public TestAliasPanel(Node testScene)
    : base(testScene) { }

  /// <summary>取第 index 个创作者行（真树上的行，文本/选中状态都由测试直接改写）。</summary>
  private TreeItem CreatorRow(int index) => _tree.GetRoot().GetChild(index);

  /// <summary>取某个创作者下的第 index 条别名子行。</summary>
  private TreeItem AliasRow(int creatorIndex, int aliasIndex) =>
    CreatorRow(creatorIndex).GetChild(aliasIndex);

  /// <summary>模拟用户把某一行的单元格改成 text，随后面板会从该单元格读到这个值。</summary>
  private void EditItem(TreeItem item, string text)
  {
    item.SetText(0, text);
    _aliasTree.Setup(m => m.GetEdited()).Returns(item);
  }

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

    _tree = new Tree();
    _tree.Columns = 2;
    TestScene.AddChild(_tree);
    _toCleanup.Add(_tree);
    _aliasTree = new Mock<ITree>();
    _aliasTree
      .Setup(m => m.CreateItem(It.IsAny<TreeItem>(), It.IsAny<int>()))
      .Returns((TreeItem p, int i) => _tree.CreateItem(p, i));
    _aliasTree.Setup(m => m.GetRoot()).Returns(() => _tree.GetRoot());
    // 以下三项转发到真树：清空、隐藏根、选中项，让 Mock 与生产行为一致
    _aliasTree.Setup(m => m.Clear()).Callback(() => _tree.Clear());
    _aliasTree
      .SetupSet(m => m.HideRoot = It.IsAny<bool>())
      .Callback((bool value) => _tree.HideRoot = value);
    _aliasTree
      .Setup(m => m.GetNextSelected(It.IsAny<TreeItem>()))
      .Returns((TreeItem from) => _tree.GetNextSelected(from));
    var importAliasBtn = new Mock<IButton>();
    var exportAliasBtn = new Mock<IButton>();
    var addAliasBtn = new Mock<IButton>();
    var addAliasToCreatorBtn = new Mock<IButton>();
    var deleteAliasBtn = new Mock<IButton>();
    var importFileDialog = new Mock<IFileDialog>();
    var exportFileDialog = new Mock<IFileDialog>();
    _errorDialog = new Mock<IAcceptDialog>();

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
        ["%ErrorDialog"] = _errorDialog.Object,
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

  [Test]
  public void Refresh_BuildsCreatorRowsWithAliasChildren()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _dm.Aliases[0].Aliases.Add("a1");
    _dm.Aliases[0].Aliases.Add("a2");
    _dm.Aliases.Add(new CreatorAlias { MainName = "乙" });
    _panel.Refresh();

    _tree.GetRoot().GetChildCount().ShouldBe(2);
    CreatorRow(0).GetText(0).ShouldBe("甲");
    CreatorRow(0).GetChildCount().ShouldBe(2);
    AliasRow(0, 0).GetText(0).ShouldBe("a1");
    AliasRow(0, 1).GetText(0).ShouldBe("a2");
    CreatorRow(1).GetText(0).ShouldBe("乙");
    CreatorRow(1).GetChildCount().ShouldBe(0);
    CreatorRow(0).GetMetadata(0).AsInt32().ShouldBe(0);
    CreatorRow(1).GetMetadata(0).AsInt32().ShouldBe(1);
    AliasRow(0, 1).GetMetadata(0).AsInt32().ShouldBe(0);
  }

  [Test]
  public void OnAliasEdited_CreatorRowWithoutAliases_UpdatesMainName()
  {
    // 没有别名的创作者行同样是叶子，不能被当成别名行处理
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _dm.Aliases.Add(new CreatorAlias { MainName = "乙" });
    _panel.Refresh();

    EditItem(CreatorRow(1), "乙改名");
    _panel.GetOnAliasEdited()();

    _dm.Aliases[1].MainName.ShouldBe("乙改名");
    _dm.Aliases[0].MainName.ShouldBe("甲");
  }

  [Test]
  public void OnAliasEdited_AliasRow_UpdatesOnlyThatAlias()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _dm.Aliases[0].Aliases.Add("a1");
    _dm.Aliases[0].Aliases.Add("a2");
    _dm.Aliases.Add(new CreatorAlias { MainName = "乙" });
    _dm.Aliases[1].Aliases.Add("b1");
    _panel.Refresh();

    EditItem(AliasRow(0, 1), "a2改");
    _panel.GetOnAliasEdited()();

    _dm.Aliases[0].Aliases.Count.ShouldBe(2);
    _dm.Aliases[0].Aliases[0].ShouldBe("a1");
    _dm.Aliases[0].Aliases[1].ShouldBe("a2改");
    _dm.Aliases[0].MainName.ShouldBe("甲");
    _dm.Aliases[1].Aliases[0].ShouldBe("b1");
  }

  [Test]
  public void OnAddAliasToCreator_AddsToOwnerOfSelectedItem()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _dm.Aliases.Add(new CreatorAlias { MainName = "乙" });
    _dm.Aliases[1].Aliases.Add("b1");
    _panel.Refresh();
    AliasRow(1, 0).Select(0);

    _panel.GetOnAddAliasToCreator()();

    _dm.Aliases[1].Aliases.Count.ShouldBe(2);
    _dm.Aliases[1].Aliases[1].ShouldBe("新别名");
    _dm.Aliases[0].Aliases.Count.ShouldBe(0);
    CreatorRow(1).GetChildCount().ShouldBe(2);
  }

  [Test]
  public void OnAddAliasToCreator_NoSelection_ShowsError()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _panel.Refresh();

    _panel.GetOnAddAliasToCreator()();

    _dm.Aliases[0].Aliases.Count.ShouldBe(0);
    _errorDialog.VerifySet(m => m.DialogText = "请先在别名表中选择一个创作者", Times.Once);
  }

  [Test]
  public void OnDeleteSelected_CreatorRow_DeletesOnlyThatCreator()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "BAK" });
    _dm.Aliases.Add(new CreatorAlias { MainName = "BAK" });
    _dm.Aliases[1].Aliases.Add("标记");
    _panel.Refresh();
    CreatorRow(1).Select(0);

    _panel.GetOnDeleteSelected()();

    _dm.Aliases.Count.ShouldBe(1);
    _dm.Aliases[0].Aliases.Count.ShouldBe(0);
  }

  [Test]
  public void OnDeleteSelected_AliasRow_DeletesOnlyThatAlias()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _dm.Aliases[0].Aliases.Add("a1");
    _dm.Aliases[0].Aliases.Add("a2");
    _panel.Refresh();
    AliasRow(0, 0).Select(0);

    _panel.GetOnDeleteSelected()();

    _dm.Aliases.Count.ShouldBe(1);
    _dm.Aliases[0].Aliases.Count.ShouldBe(1);
    _dm.Aliases[0].Aliases[0].ShouldBe("a2");
    CreatorRow(0).GetChildCount().ShouldBe(1);
  }

  [Test]
  public void ExportAliasTable_WritesOneColumnPerAlias()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _dm.Aliases[0].Aliases.Add("a1");
    _dm.Aliases[0].Aliases.Add("a2");
    _dm.Aliases.Add(new CreatorAlias { MainName = "乙" });
    _dm.Aliases[1].Aliases.Add("b1");
    var path = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      $"AutoCMEX_Alias_{Guid.NewGuid():N}.csv"
    );

    try
    {
      _panel.GetOnAliasExportFileSelected()(path);

      var lines = System.IO.File.ReadAllLines(path);
      lines.Length.ShouldBe(3);
      lines[0].ShouldBe("主名,别名1,别名2");
      lines[1].ShouldBe("甲,a1,a2");
      lines[2].ShouldBe("乙,b1,");
    }
    finally
    {
      if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);
    }
  }

  [Test]
  public void ExportAliasTable_RoundTripsThroughImporter()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "甲" });
    _dm.Aliases[0].Aliases.Add("a1");
    _dm.Aliases[0].Aliases.Add("a2");
    _dm.Aliases.Add(new CreatorAlias { MainName = "乙" });
    _dm.Aliases[1].Aliases.Add("b1");
    var path = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      $"AutoCMEX_Alias_{Guid.NewGuid():N}.csv"
    );

    try
    {
      _panel.GetOnAliasExportFileSelected()(path);
      var result = ImporterFactory.Create(path).ImportAliasTable(path);

      result.IsSuccess.ShouldBeTrue();
      result.Data!.Count.ShouldBe(2);
      result.Data[0].MainName.ShouldBe("甲");
      result.Data[0].Aliases.Count.ShouldBe(2);
      result.Data[0].Aliases[0].ShouldBe("a1");
      result.Data[0].Aliases[1].ShouldBe("a2");
      result.Data[1].MainName.ShouldBe("乙");
      result.Data[1].Aliases.Count.ShouldBe(1);
      result.Data[1].Aliases[0].ShouldBe("b1");
    }
    finally
    {
      if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);
    }
  }
}
