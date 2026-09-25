namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
  private Tree _tree = default!;
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

    // 转发 lambda 必须捕获本次用例的局部变量：上一个用例遗留的延迟刷新若也写当前字段，
    // 就会把它的行建到本次用例的树里（测试实例在整类用例间是同一个）
    var tree = new Tree();
    tree.Columns = 3;
    TestScene.AddChild(tree);
    _toCleanup.Add(tree);
    _tree = tree;
    _spellCardTree = new Mock<ITree>();
    _spellCardTree
      .Setup(m => m.CreateItem(It.IsAny<TreeItem>(), It.IsAny<int>()))
      .Returns((TreeItem p, int i) => tree.CreateItem(p, i));
    _spellCardTree.Setup(m => m.GetRoot()).Returns(() => tree.GetRoot());
    // 真实 Tree.Clear() 会释放所有行；测试里若放过它，重建后的行会和旧行混在一起
    _spellCardTree.Setup(m => m.Clear()).Callback(() => tree.Clear());
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

  // ==================== 回归测试：符卡属性变化要立刻同步到符卡表 ====================

  /// <summary>猜测处理（<c>GuessPipeline</c>）把符卡标记成「已猜出」后，符卡表的勾选框要立刻同步。</summary>
  /// <remarks>
  /// 此前只订阅了符卡列表的增删，符卡自身的 <c>AutoValue</c> 变化不触发任何刷新，勾选框一直停在旧状态，
  /// 要等下次重启读盘才对得上。
  /// </remarks>
  [Test]
  public async Task GuessedOutWrite_SyncsCheckboxWithoutRebuildingTree()
  {
    var boss = AddBoss(("符卡1", "作者A"), ("符卡2", "作者B"));
    _panel.SelectBoss(0);
    _panel.Refresh();
    await SettleAsync();

    var rowBefore = CardRow(0);
    rowBefore.IsChecked(2).ShouldBeFalse();
    CardRow(1).IsChecked(2).ShouldBeFalse();

    boss.SpellCards[0].IsGuessedOut.Value = true;
    await SettleAsync();

    CardRow(0).IsChecked(2).ShouldBeTrue("符卡被标记「已猜出」后，符卡表的勾选框应立即同步");
    CardRow(1).IsChecked(2).ShouldBeFalse("未变化的行不应被带动");
    CardRow(0).ShouldBeSameAs(rowBefore, "属性变化应就地改写该行，整树重建会丢掉滚动位置与选中项");
  }

  /// <summary>符卡名与创作者两列同样要跟着符卡属性走。</summary>
  [Test]
  public async Task NameAndCreatorWrites_SyncCellText()
  {
    var boss = AddBoss(("符卡1", ""));
    _panel.SelectBoss(0);
    _panel.Refresh();
    await SettleAsync();

    CardRow(0).GetText(1).ShouldBe("(未揭晓)");

    boss.SpellCards[0].Name.Value = "改名后的符卡";
    boss.SpellCards[0].Creator.Value = "作者A";
    await SettleAsync();

    CardRow(0).GetText(0).ShouldBe("改名后的符卡");
    CardRow(0).GetText(1).ShouldBe("作者A");

    boss.SpellCards[0].Creator.Value = string.Empty;
    await SettleAsync();

    CardRow(0).GetText(1).ShouldBe("(未揭晓)");
  }

  /// <summary>一次猜测可能标掉多张符卡：同一帧内的多次变化不许漏刷。</summary>
  [Test]
  public async Task BatchWrites_AllSyncWithinOneFrame()
  {
    var boss = AddBoss(("符卡1", "作者A"), ("符卡2", "作者B"), ("符卡3", "作者C"));
    _panel.SelectBoss(0);
    _panel.Refresh();
    await SettleAsync();

    foreach (var card in boss.SpellCards)
      card.IsGuessedOut.Value = true;

    await SettleAsync();

    for (var i = 0; i < boss.SpellCards.Count; i++)
      CardRow(i).IsChecked(2).ShouldBeTrue($"第 {i + 1} 张符卡的勾选框应同步");
  }

  /// <summary>渲染之后新加进列表的符卡也要纳入订阅（列表就地修改，Boss 实例没变）。</summary>
  [Test]
  public async Task CardAddedAfterRender_IsWatchedToo()
  {
    var boss = AddBoss(("符卡1", "作者A"));
    _panel.SelectBoss(0);
    _panel.Refresh();
    await SettleAsync();

    var added = new SpellCard
    {
      Name = new AutoValue<string>("符卡2"),
      Creator = new AutoValue<string>("作者B"),
    };
    boss.SpellCards.Add(added);
    await SettleAsync();

    CardRow(1).GetText(0).ShouldBe("符卡2");

    added.IsGuessedOut.Value = true;
    await SettleAsync();

    CardRow(1).IsChecked(2).ShouldBeTrue("新增的符卡也应跟着属性变化同步");
  }

  /// <summary>切换 Boss 后旧 Boss 的符卡不应再影响当前树（订阅随切换释放并重挂）。</summary>
  [Test]
  public async Task SwitchingBoss_StopsWatchingPreviousBossCards()
  {
    var bossA = AddBoss(("符卡A1", "作者A"));
    AddBoss(("符卡B1", "作者B"));
    _panel.SelectBoss(0);
    _panel.Refresh();
    _panel.SelectBoss(1);
    _panel.Refresh();
    await SettleAsync();

    CardRow(0).GetText(0).ShouldBe("符卡B1");

    bossA.SpellCards[0].IsGuessedOut.Value = true;
    await SettleAsync();

    CardRow(0).GetText(0).ShouldBe("符卡B1");
    CardRow(0).IsChecked(2).ShouldBeFalse("切走 Boss 后不应再被旧 Boss 的符卡变化带动");
  }

  // ==================== 测试辅助 ====================

  /// <summary>建一个含指定符卡（符卡名 / 创作者）的 Boss 并加进数据层，返回该 Boss。</summary>
  private Boss AddBoss(params (string Name, string Creator)[] cards)
  {
    var boss = new Boss { Name = $"Boss{_dm.Bosses.Count + 1}" };
    foreach (var (name, creator) in cards)
    {
      boss.SpellCards.Add(
        new SpellCard
        {
          Name = new AutoValue<string>(name),
          Creator = new AutoValue<string>(creator),
        }
      );
    }

    _dm.Bosses.Add(boss);
    return boss;
  }

  /// <summary>取真实树里最后一次渲染出的第 index 张符卡行。</summary>
  private TreeItem CardRow(int index)
  {
    var root = _tree.GetRoot();
    root.ShouldNotBeNull("真实 Tree 应已渲染出根节点");
    var bossItem = root.GetChild(0);
    bossItem.ShouldNotBeNull("真实 Tree 应有 Boss 行");
    var row = bossItem.GetChild(index);
    row.ShouldNotBeNull($"真实 Tree 应有第 {index} 行符卡");
    return row;
  }

  /// <summary>等两帧：足够让靠 CallDeferred 排队的刷新跑到。</summary>
  private async Task SettleAsync()
  {
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
  }
}
