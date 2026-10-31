namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Storage;
using Chickensoft.GoDotTest;
using ClosedXML.Excel;
using Godot;
using Shouldly;

/// <summary>
/// ExcelImporter unit tests.
/// </summary>
public class ExcelImporterTest : TestClass
{
  private string _tempDir = string.Empty;

  public ExcelImporterTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Test_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_tempDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  // ==================== ImportSpellCardTable Tests ====================

  [Test]
  public void ImportSpellCardTable_ValidWorkbook_ReturnsBosses()
  {
    var path = Path.Combine(_tempDir, "spellcard.xlsx");
    CreateSpellCardWorkbook(
      path,
      new[]
      {
        new[] { "Boss1", "Card1", "Alice" },
        new[] { "Boss1", "Card2", "Bob" },
        new[] { "Boss2", "Card3", "Charlie" },
      }
    );

    var result = new ExcelImporter().ImportSpellCardTable(path);

    result.IsSuccess.ShouldBeTrue();
    result.Data!.Count.ShouldBe(2);
    result.Data[0].Name.ShouldBe("Boss1");
    result.Data[0].SpellCards.Count.ShouldBe(2);
    result.Data[0].SpellCards[0].Name.Value.ShouldBe("Card1");
    result.Data[0].SpellCards[0].Creator.Value.ShouldBe("Alice");
    result.Data[1].Name.ShouldBe("Boss2");
    result.Data[1].SpellCards.Count.ShouldBe(1);
  }

  [Test]
  public void ImportSpellCardTable_EmptyFile_ReturnsError()
  {
    var path = Path.Combine(_tempDir, "empty.xlsx");
    // Create a workbook with no data
    using (var wb = new XLWorkbook())
    {
      wb.Worksheets.Add("Sheet1");
      wb.SaveAs(path);
    }

    var result = new ExcelImporter().ImportSpellCardTable(path);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("空");
  }

  [Test]
  public void ImportSpellCardTable_OnlyHeader_ReturnsError()
  {
    var path = Path.Combine(_tempDir, "header_only.xlsx");
    using (var wb = new XLWorkbook())
    {
      var ws = wb.Worksheets.Add("Sheet1");
      ws.Cell(1, 1).Value = "Boss";
      ws.Cell(1, 2).Value = "符卡名";
      ws.Cell(1, 3).Value = "创作者";
      wb.SaveAs(path);
    }

    var result = new ExcelImporter().ImportSpellCardTable(path);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("空");
  }

  [Test]
  public void ImportSpellCardTable_MissingColumns_ReturnsError()
  {
    var path = Path.Combine(_tempDir, "missing_cols.xlsx");
    using (var wb = new XLWorkbook())
    {
      var ws = wb.Worksheets.Add("Sheet1");
      ws.Cell(1, 1).Value = "Boss";
      ws.Cell(1, 2).Value = "符卡名";
      ws.Cell(2, 1).Value = "Boss1";
      ws.Cell(2, 2).Value = "Card1";
      wb.SaveAs(path);
    }

    var result = new ExcelImporter().ImportSpellCardTable(path);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("列缺失");
  }

  [Test]
  public void ImportSpellCardTable_SkipsEmptyRows()
  {
    var path = Path.Combine(_tempDir, "skip_empty.xlsx");
    CreateSpellCardWorkbook(
      path,
      new[]
      {
        new[] { "Boss1", "Card1", "Alice" },
        new[] { "", "", "" },
        new[] { "Boss1", "Card2", "Bob" },
      }
    );

    var result = new ExcelImporter().ImportSpellCardTable(path);

    result.IsSuccess.ShouldBeTrue();
    result.Data!.Count.ShouldBe(1);
    result.Data[0].SpellCards.Count.ShouldBe(2);
  }

  [Test]
  public void ImportSpellCardTable_FileNotFound_ReturnsError()
  {
    var path = Path.Combine(_tempDir, "nonexistent.xlsx");

    var result = new ExcelImporter().ImportSpellCardTable(path);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("导入失败");
  }

  // ==================== ImportAliasTable Tests ====================

  [Test]
  public void ImportAliasTable_ValidWorkbook_ReturnsAliases()
  {
    var path = Path.Combine(_tempDir, "alias.xlsx");
    CreateAliasWorkbook(path, new[] { new[] { "Alice", "Ali", "A" }, new[] { "Bob", "B", "Bo" } });

    var result = new ExcelImporter().ImportAliasTable(path);

    result.IsSuccess.ShouldBeTrue();
    result.Data!.Count.ShouldBe(2);
    result.Data[0].MainName.ShouldBe("Alice");
    result.Data[0].Aliases.Count.ShouldBe(2);
    result.Data[0].Aliases.ShouldContain("Ali");
    result.Data[0].Aliases.ShouldContain("A");
    result.Data[1].MainName.ShouldBe("Bob");
  }

  [Test]
  public void ImportAliasTable_MissingColumns_ReturnsError()
  {
    var path = Path.Combine(_tempDir, "alias_missing_cols.xlsx");
    using (var wb = new XLWorkbook())
    {
      var ws = wb.Worksheets.Add("Sheet1");
      ws.Cell(1, 1).Value = "主名";
      ws.Cell(2, 1).Value = "Alice";
      wb.SaveAs(path);
    }

    var result = new ExcelImporter().ImportAliasTable(path);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("列缺失");
  }

  [Test]
  public void ImportAliasTable_EmptyFile_ReturnsError()
  {
    var path = Path.Combine(_tempDir, "alias_empty.xlsx");
    using (var wb = new XLWorkbook())
    {
      wb.Worksheets.Add("Sheet1");
      wb.SaveAs(path);
    }

    var result = new ExcelImporter().ImportAliasTable(path);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("空");
  }

  [Test]
  public void ImportAliasTable_SkipsEmptyMainNameRows()
  {
    var path = Path.Combine(_tempDir, "alias_skip_empty.xlsx");
    CreateAliasWorkbook(
      path,
      new[] { new[] { "Alice", "Ali" }, new[] { "", "" }, new[] { "Bob", "B" } }
    );

    var result = new ExcelImporter().ImportAliasTable(path);

    result.IsSuccess.ShouldBeTrue();
    result.Data!.Count.ShouldBe(2);
  }

  [Test]
  public void ImportAliasTable_PreservesBossOrder()
  {
    var path = Path.Combine(_tempDir, "order.xlsx");
    CreateSpellCardWorkbook(
      path,
      new[]
      {
        new[] { "ZBoss", "Card1", "Alice" },
        new[] { "ABoss", "Card2", "Bob" },
        new[] { "MBoss", "Card3", "Charlie" },
      }
    );

    var result = new ExcelImporter().ImportSpellCardTable(path);

    result.IsSuccess.ShouldBeTrue();
    result.Data![0].Name.ShouldBe("ZBoss");
    result.Data![1].Name.ShouldBe("ABoss");
    result.Data![2].Name.ShouldBe("MBoss");
  }

  // ==================== Helper Methods ====================

  private static void CreateSpellCardWorkbook(string path, string[][] rows)
  {
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Sheet1");
    ws.Cell(1, 1).Value = "Boss";
    ws.Cell(1, 2).Value = "符卡名";
    ws.Cell(1, 3).Value = "创作者";
    for (var i = 0; i < rows.Length; i++)
    {
      for (var j = 0; j < rows[i].Length; j++)
        ws.Cell(i + 2, j + 1).Value = rows[i][j];
    }
    wb.SaveAs(path);
  }

  private static void CreateAliasWorkbook(string path, string[][] rows)
  {
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Sheet1");
    ws.Cell(1, 1).Value = "主名";
    ws.Cell(1, 2).Value = "别名1";
    ws.Cell(1, 3).Value = "别名2";
    for (var i = 0; i < rows.Length; i++)
    {
      for (var j = 0; j < rows[i].Length; j++)
        ws.Cell(i + 2, j + 1).Value = rows[i][j];
    }
    wb.SaveAs(path);
  }
}
