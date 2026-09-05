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
  private Mock<IOptionButton> _bossSelect = default!;
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
    _bossSelect = new Mock<IOptionButton>();
    _bossSelect.SetupProperty(m => m.Selected, -1);
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
        ["%BossSelect"] = _bossSelect.Object,
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
  public void Refresh_UpdatesTree_WhenBossSelected()
  {
    _dm.Bosses.Add(new Boss { Name = "测试Boss" });
    _dm.Bosses[0].SpellCards.Add(new SpellCard { Name = new AutoValue<string>("符卡1") });
    _panel.SelectBoss(0);
    _panel.Refresh();
    var root = _spellCardTree.Object.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  // ==================== 回归测试：修复"导入对应表后 UI 空白" ====================

  [Test]
  public void Import_AutoSelectsFirstBoss_SoTreeIsNotBlank()
  {
    // 模拟导入：多个 Boss，但此前记录的下标已失效（越界）
    _dm.Settings.SelectedBossIndex.Value = 99;
    var bossA = new Boss { Name = "BossA" };
    bossA.SpellCards.Add(new SpellCard { Name = new AutoValue<string>("符卡A1") });
    _dm.Bosses.Add(bossA);
    var bossB = new Boss { Name = "BossB" };
    bossB.SpellCards.Add(new SpellCard { Name = new AutoValue<string>("符卡B1") });
    _dm.Bosses.Add(bossB);

    _panel.Refresh();

    // 越界下标被规范到首个 Boss，且树非空白
    _bossSelect.Verify(m => m.Select(It.IsAny<int>()), Times.AtLeastOnce());
    _dm.Settings.SelectedBossIndex.Value.ShouldBe(0);
    var root = _spellCardTree.Object.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void Import_EmptyTableResult_ClearsTree()
  {
    _dm.Settings.SelectedBossIndex.Value = 0;
    _panel.Refresh();

    _dm.Settings.SelectedBossIndex.Value.ShouldBe(-1);
    // 无 Boss 时树不创建任何节点（GetRoot 为 null，表示无内容展示）
    _spellCardTree.Object.GetRoot().ShouldBeNull();
  }

  [Test]
  public void SelectBoss_SwitchesCurrentBoss_AndRendersItsCards()
  {
    var bossA = new Boss { Name = "BossA" };
    bossA.SpellCards.Add(new SpellCard { Name = new AutoValue<string>("符卡A1") });
    _dm.Bosses.Add(bossA);
    var bossB = new Boss { Name = "BossB" };
    bossB.SpellCards.Add(new SpellCard { Name = new AutoValue<string>("符卡B1") });
    _dm.Bosses.Add(bossB);

    // 通过 Sync 模型切换选中
    _panel.SelectBoss(1);
    _panel.Refresh();

    _dm.Settings.SelectedBossIndex.Value.ShouldBe(1);
    _panel.GetCurrentBoss().ShouldNotBeNull();
    _panel.GetCurrentBoss()!.Name.ShouldBe("BossB");
  }

  [Test]
  public void GetDataManager_ReturnsInjectedInstance()
  {
    _panel.GetDataManager().ShouldBe(_dm);
  }
}
