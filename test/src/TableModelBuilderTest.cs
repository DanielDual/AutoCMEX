namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Info;
using AutoCMEX.Test.Drivers;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 两表渲染模型单测：列结构、单元格映射、染色透传与空数据不发布。
/// </summary>
public class TableModelBuilderTest : TestClass
{
  private string _tempDir = string.Empty;
  private TableModelBuilderDriver _driver = default!;

  public TableModelBuilderTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Test_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_tempDir);

    _driver = new TableModelBuilderDriver(_tempDir);
    _driver.AddFixtureBoss();
  }

  [Cleanup]
  public void Cleanup()
  {
    _driver?.Dispose();

    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  [Test]
  public void GuessingTable_HasThreeColumnsInRequirementOrder()
  {
    var table = _driver.BuildGuessingTable();

    table.Title.ShouldBe(TableModelBuilder.GuessingTableTitle);
    table.Columns.Count.ShouldBe(3);
    table
      .Columns.Select(column => column.Header)
      .ShouldBe(new[] { "创作者", "符卡编号", "符卡名" });
    table.Columns[1].Align.ShouldBe(TableColumnAlign.Center);
    table.Rows.Count.ShouldBe(TableModelBuilderDriver.FixtureCardCount);
    table.HasContent.ShouldBeTrue();
  }

  [Test]
  public void GuessingTable_MasksUnrevealedCreatorAndHighlightsGuessedRow()
  {
    var rows = _driver.BuildGuessingTable().Rows;

    // 夹具约定：每个创作者名下「前若干张」为已猜出（详见 TableModelBuilderDriver）
    // 第 1 张（Alpha，已猜出）：露出创作者并整行染绿
    rows[0].Cells.ShouldBe(new[] { "Alpha", "1", "符卡 1" });
    rows[0].IsHighlighted.ShouldBeTrue();

    // 第 2 张（Alpha，未猜出）：第一栏留空（防剧透遮罩）且不染色
    rows[1].Cells.ShouldBe(new[] { string.Empty, "2", "符卡 2" });
    rows[1].IsHighlighted.ShouldBeFalse();

    // 第 4 张（Beta 首张，已猜出）
    rows[3].Cells.ShouldBe(new[] { "Beta", "4", "符卡 4" });
    rows[3].IsHighlighted.ShouldBeTrue();
  }

  [Test]
  public void GuessingTable_NeverLeaksCreatorOfUnrevealedCard()
  {
    var rows = _driver.BuildGuessingTable().Rows;

    // 遮罩是硬约束：未猜出行不允许出现任何创作者名（含 Beta/Gamma）
    foreach (var row in rows)
    {
      if (row.IsHighlighted)
        row.Cells[0].ShouldNotBeNullOrWhiteSpace();
      else
        row.Cells[0].ShouldBe(string.Empty);
    }
  }

  [Test]
  public void GuessingTable_RowOrderFollowsCardOrderAndIndexIsOneBased()
  {
    var rows = _driver.BuildGuessingTable().Rows;

    rows.Select(row => row.Cells[1]).ShouldBe(new[] { "1", "2", "3", "4", "5", "6" });
    rows.Select(row => row.Cells[2])
      .ShouldBe(new[] { "符卡 1", "符卡 2", "符卡 3", "符卡 4", "符卡 5", "符卡 6" });
  }

  [Test]
  public void CreatorTable_HasTwoColumnsAndCountsRemaining()
  {
    var table = _driver.BuildCreatorRemainingTable();

    table.Title.ShouldBe(TableModelBuilder.CreatorRemainingTableTitle);
    table
      .Columns.Select(column => column.Header)
      .ShouldBe(new[] { "创作者", "剩余未被猜测符卡数" });

    // 按创作者首次出现顺序，剩余 = 总数 − 已猜出数
    table.Rows.Select(row => row.Cells[0]).ShouldBe(new[] { "Alpha", "Beta", "Gamma" });
    table.Rows.Select(row => row.Cells[1]).ShouldBe(new[] { "2", "0", "1" });
  }

  [Test]
  public void CreatorTable_HighlightsOnlyFullyGuessedCreator()
  {
    var rows = _driver.BuildCreatorRemainingTable().Rows;

    rows.Select(row => row.IsHighlighted).ShouldBe(new[] { false, true, false });
  }

  [Test]
  public void EmptySnapshot_YieldsEmptyTablesWithoutContent()
  {
    using var emptyDriver = new TableModelBuilderDriver(_tempDir);

    var guessing = emptyDriver.BuildGuessingTable();
    var creator = emptyDriver.BuildCreatorRemainingTable();

    // 无 Boss 时列结构仍在（展示层可直接渲染空表），但不允许发布
    guessing.Columns.Count.ShouldBe(3);
    creator.Columns.Count.ShouldBe(2);
    guessing.HasContent.ShouldBeFalse();
    creator.HasContent.ShouldBeFalse();
  }

  [Test]
  public void BuildGuessingTable_NullSnapshot_Throws()
  {
    Should.Throw<ArgumentNullException>(() => TableModelBuilder.BuildGuessingTable(null!));
    Should.Throw<ArgumentNullException>(() => TableModelBuilder.BuildCreatorRemainingTable(null!));
  }
}
