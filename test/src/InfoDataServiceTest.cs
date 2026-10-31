namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 信息数据服务（两张表推导）单测：遮罩、染色、序号与分组计数。
/// </summary>
public class InfoDataServiceTest : TestClass
{
  private string _dataDir = string.Empty;
  private DataManager _dataManager = default!;

  public InfoDataServiceTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dataDir = Path.Combine(Path.GetTempPath(), "autocmex_tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_dataDir);
    _dataManager = new DataManager(
      _dataDir,
      new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_dataDir))
    );
  }

  [Cleanup]
  public void Cleanup()
  {
    _dataManager?.Dispose();
    if (Directory.Exists(_dataDir))
      Directory.Delete(_dataDir, recursive: true);
  }

  [Test]
  public void BuildSnapshotWithoutBossReturnsEmpty()
  {
    var snapshot = new InfoDataService(_dataManager).BuildSnapshot();

    snapshot.HasBoss.ShouldBeFalse();
    snapshot.TotalCards.ShouldBe(0);
    snapshot.GuessingRows.ShouldBeEmpty();
    snapshot.CreatorRows.ShouldBeEmpty();
  }

  [Test]
  public void GuessingRowsMaskCreatorUntilGuessedOut()
  {
    AddBoss(
      "SampleBoss",
      SpellCard("非符", "SampleCreator"),
      SpellCard("符卡 A", "SampleCreator", guessedOut: true)
    );

    var snapshot = new InfoDataService(_dataManager).BuildSnapshot();

    snapshot.HasBoss.ShouldBeTrue();
    snapshot.GuessingRows.Count.ShouldBe(2);

    // 未猜出：第一栏必须为空（防剧透），且不染色
    snapshot.GuessingRows[0].CreatorDisplay.ShouldBe(string.Empty);
    snapshot.GuessingRows[0].IsGuessedOut.ShouldBeFalse();

    // 已猜出：露出创作者并染绿
    snapshot.GuessingRows[1].CreatorDisplay.ShouldBe("SampleCreator");
    snapshot.GuessingRows[1].IsGuessedOut.ShouldBeTrue();
  }

  [Test]
  public void GuessingRowIndexEqualsPositionPlusOne()
  {
    AddBoss("SampleBoss", SpellCard("A", "C1"), SpellCard("B", "C1"), SpellCard("C", "C2"));

    var rows = new InfoDataService(_dataManager).BuildSnapshot().GuessingRows;

    rows.Select(row => row.Index).ShouldBe(new[] { 1, 2, 3 });
    rows.Select(row => row.SpellCardName).ShouldBe(new[] { "A", "B", "C" });
  }

  [Test]
  public void CreatorRowsCountGuessedOutWithinGroup()
  {
    AddBoss(
      "SampleBoss",
      SpellCard("A", "Alpha", guessedOut: true),
      SpellCard("B", "Alpha"),
      SpellCard("C", "Alpha"),
      SpellCard("D", "Beta", guessedOut: true),
      SpellCard("E", "Beta", guessedOut: true)
    );

    var rows = new InfoDataService(_dataManager).BuildSnapshot().CreatorRows;

    rows.Count.ShouldBe(2);

    rows[0].Creator.ShouldBe("Alpha");
    rows[0].Total.ShouldBe(3);
    rows[0].GuessedOutCount.ShouldBe(1);
    rows[0].Remaining.ShouldBe(2);
    rows[0].IsFullyGuessedOut.ShouldBeFalse();

    // 全部猜出 → 剩余 0 → 该行染绿
    rows[1].Creator.ShouldBe("Beta");
    rows[1].Total.ShouldBe(2);
    rows[1].GuessedOutCount.ShouldBe(2);
    rows[1].Remaining.ShouldBe(0);
    rows[1].IsFullyGuessedOut.ShouldBeTrue();
  }

  [Test]
  public void CreatorRowsSkipCardsWithoutCreator()
  {
    AddBoss(
      "SampleBoss",
      SpellCard("A", "Alpha"),
      SpellCard("B", string.Empty),
      SpellCard("C", "   ")
    );

    var snapshot = new InfoDataService(_dataManager).BuildSnapshot();

    // 猜测表三栏保留全部符卡（信息完整），创作者表不把「无创作者」凑成一行
    snapshot.GuessingRows.Count.ShouldBe(3);
    snapshot.CreatorRows.Count.ShouldBe(1);
    snapshot.CreatorRows[0].Creator.ShouldBe("Alpha");
  }

  [Test]
  public void OutOfRangeSelectedBossIndexFallsBackToFirstBoss()
  {
    AddBoss("First", SpellCard("A", "Alpha"));
    AddBoss("Second", SpellCard("B", "Beta"));
    _dataManager.Settings.SelectedBossIndex.Value = 99;

    var service = new InfoDataService(_dataManager);
    var boss = service.ResolveCurrentBoss();

    boss!.Name.ShouldBe("First");
    // 越界下标要写回设置，避免每次推导都重新回退
    _dataManager.Settings.SelectedBossIndex.Value.ShouldBe(0);
    service.BuildSnapshot().BossName.ShouldBe("First");
  }

  private void AddBoss(string name, params SpellCard[] cards)
  {
    var boss = new Boss { Name = name };
    foreach (var card in cards)
      boss.SpellCards.Add(card);

    _dataManager.Bosses.Add(boss);
  }

  private static SpellCard SpellCard(string name, string creator, bool guessedOut = false) =>
    new()
    {
      Name = { Value = name },
      Creator = { Value = creator },
      IsGuessedOut = { Value = guessedOut },
    };
}
