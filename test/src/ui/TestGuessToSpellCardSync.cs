namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
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

/// <summary>
/// 猜测板块内「处理」按钮与符卡表之间的联动回归测试。
/// </summary>
/// <remarks>
/// 用户复现：在猜测输入框里填入猜测文本、点「处理」之后，同一板块里的符卡表勾选框仍停在
/// 旧状态，要关掉重开才对得上。
/// 根因是符卡表只订阅了符卡列表的增删（<c>AutoList.OnModify</c>），而「处理」写入的是符卡
/// 自身的 <c>AutoValue</c>（<c>IsGuessedOut</c>）——元素内部属性变化不会触发集合级通知，
/// 于是没有任何刷新被排上。
/// 这里不复用面板的私有回调，而是从按钮出发走完整条链路（按钮 → 猜测服务 → 管道 → 数据层），
/// 断言符卡表当场同步，避免只测到订阅代码、漏掉真实调用路径。
/// </remarks>
public class TestGuessToSpellCardSync : TestClass
{
  private DataManager _dm = default!;
  private SpellCardPanel _spellPanel = default!;
  private GuessingPanel _guessPanel = default!;
  private Tree _tree = default!;
  private Mock<ITextEdit> _guessInput = default!;
  private Mock<IButton> _processBtn = default!;
  private readonly List<Node> _toCleanup = new();

  public TestGuessToSpellCardSync(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();
    // 走严格模式，保证「处理」不落到 AI 兜底分支上
    _dm.Settings.MessageFilterMode.Value = "strict";

    SetupSpellCardPanel();
    SetupGuessingPanel();
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

  /// <summary>点「处理」后两张符卡被标成已猜出，符卡表要在同一帧内同步，不该等到重启。</summary>
  [Test]
  public async Task ProcessButton_MarksGuessedOut_AndSpellCardTableSyncs()
  {
    AddBoss(("符卡1", "作者A"), ("符卡2", "作者B"), ("符卡3", "作者C"));
    _spellPanel.SelectBoss(0);
    _spellPanel.Refresh();
    await SettleAsync();

    CardRow(0).IsChecked(2).ShouldBeFalse();
    CardRow(1).IsChecked(2).ShouldBeFalse();
    CardRow(2).IsChecked(2).ShouldBeFalse();

    // 与用户操作一致：输入框填猜测文本 → 点「处理」
    _guessInput.Object.Text = "1作者A 2作者B";
    _processBtn.Raise(b => b.Pressed += null);
    await SettleAsync();

    CardRow(0).IsChecked(2).ShouldBeTrue("点「处理」后符卡表应立刻勾上「已猜出」");
    CardRow(1).IsChecked(2).ShouldBeTrue("点「处理」后符卡表应立刻勾上「已猜出」");
    CardRow(2).IsChecked(2).ShouldBeFalse("本次没猜到的符卡不应被连带标成已猜出");
  }

  /// <summary>只猜一张不构成有效猜测（判定规则要求 ≥2 张全对），此时不该有任何标记。</summary>
  [Test]
  public async Task ProcessButton_SingleWrongGuess_LeavesTableUntouched()
  {
    AddBoss(("符卡1", "作者A"), ("符卡2", "作者B"));
    _spellPanel.SelectBoss(0);
    _spellPanel.Refresh();
    await SettleAsync();

    _guessInput.Object.Text = "1作者B 2作者B";
    _processBtn.Raise(b => b.Pressed += null);
    await SettleAsync();

    CardRow(0).IsChecked(2).ShouldBeFalse("猜错不应标记已猜出");
    CardRow(1).IsChecked(2).ShouldBeFalse("猜错不应标记已猜出");
  }

  // ==================== 装配 ====================

  /// <summary>装配符卡表：真实 <see cref="Tree"/>，保证勾选框状态可断言。</summary>
  /// <remarks>
  /// 转发 lambda 必须捕获本次用例的局部变量——测试实例在整类用例间是同一个，
  /// 上一个用例遗留的延迟刷新若写当前字段，会把它的行建到本次用例的树里。
  /// </remarks>
  private void SetupSpellCardPanel()
  {
    var tree = new Tree { Columns = 3 };
    TestScene.AddChild(tree);
    _toCleanup.Add(tree);
    _tree = tree;

    var spellTree = new Mock<ITree>();
    spellTree
      .Setup(m => m.CreateItem(It.IsAny<TreeItem>(), It.IsAny<int>()))
      .Returns((TreeItem p, int i) => tree.CreateItem(p, i));
    spellTree.Setup(m => m.GetRoot()).Returns(() => tree.GetRoot());
    // 真实 Tree.Clear() 会释放所有行；测试里若放过它，重建后的行会和旧行混在一起
    spellTree.Setup(m => m.Clear()).Callback(() => tree.Clear());

    var bossSelect = new Mock<IOptionButton>();
    bossSelect.SetupProperty(m => m.Selected, -1);

    _spellPanel = new SpellCardPanel();
    (_spellPanel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_spellPanel);
    _spellPanel.FakeNodeTree(
      new()
      {
        ["%SpellCardTree"] = spellTree.Object,
        ["%BossSelect"] = bossSelect.Object,
        ["%ImportCardBtn"] = new Mock<IButton>().Object,
        ["%ExportCardBtn"] = new Mock<IButton>().Object,
        ["%AddBossBtn"] = new Mock<IButton>().Object,
        ["%AddCardBtn"] = new Mock<IButton>().Object,
        ["%DeleteBtn"] = new Mock<IButton>().Object,
        ["%ImportFileDialog"] = new Mock<IFileDialog>().Object,
        ["%ExportFileDialog"] = new Mock<IFileDialog>().Object,
        ["%ErrorDialog"] = new Mock<IAcceptDialog>().Object,
      }
    );
    _spellPanel.FakeDependency<DataManager>(_dm);
    _spellPanel._Notification((int)Node.NotificationEnterTree);
    _spellPanel._Notification((int)Node.NotificationReady);
  }

  /// <summary>装配猜测面板：真实 <see cref="GuessProcessingService"/>，与符卡表共用同一个数据层。</summary>
  private void SetupGuessingPanel()
  {
    _guessInput = new Mock<ITextEdit>();
    _guessInput.SetupProperty(m => m.Text, string.Empty);
    _processBtn = new Mock<IButton>();
    var fuzzifyBtn = new Mock<IButton>();
    fuzzifyBtn.SetupProperty(m => m.Disabled);
    var responseDisplay = new Mock<IRichTextLabel>();
    responseDisplay.SetupProperty(m => m.Text, string.Empty);
    var droppedList = new Mock<IItemList>();
    var retryDroppedBtn = new Mock<IButton>();
    retryDroppedBtn.SetupProperty(m => m.Disabled);
    var clearDroppedBtn = new Mock<IButton>();
    clearDroppedBtn.SetupProperty(m => m.Disabled);

    _guessPanel = new GuessingPanel();
    (_guessPanel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_guessPanel);
    _guessPanel.FakeNodeTree(
      new()
      {
        ["%GuessInput"] = _guessInput.Object,
        ["%FuzzifyBtn"] = fuzzifyBtn.Object,
        ["%ProcessBtn"] = _processBtn.Object,
        ["%ResponseDisplay"] = responseDisplay.Object,
        ["%DroppedList"] = droppedList.Object,
        ["%RetryDroppedBtn"] = retryDroppedBtn.Object,
        ["%ClearDroppedBtn"] = clearDroppedBtn.Object,
      }
    );
    _guessPanel.FakeDependency<DataManager>(_dm);
    _guessPanel.FakeDependency<AiServiceFactory>(new AiServiceFactory(_dm));
    _guessPanel.FakeDependency<IGuessProcessingService>(
      new GuessProcessingService(
        _dm,
        new AiServiceFactory(_dm),
        new GuessResponseHandler(),
        new DroppedGuessRepository()
      )
    );
    _guessPanel._Notification((int)Node.NotificationEnterTree);
    _guessPanel._Notification((int)Node.NotificationReady);
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
