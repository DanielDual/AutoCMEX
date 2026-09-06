namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Merge;
using AutoCMEX.Core.Storage;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 阶段1 导出相关单元测试：对应表三列 CSV 导出与 Sharp Cli 参数构造。
/// </summary>
public class MergeExportTest : TestClass
{
  private string _tempDir = string.Empty;

  public MergeExportTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_MergeExport_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_tempDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  // ==================== SpellCardMappingExporter Tests ====================

  private string CsvPath() => Path.Combine(_tempDir, "mapping.csv");

  [Test]
  public void Exporter_WritesThreeColumns_HeaderAndRows()
  {
    var rows = new List<SpellCardMappingRow>
    {
      new("神技「XXA」", "Alice", false),
      new(string.Empty, "Alice", true), // 非符
      new("弹幕「ZZB」", "Bob", false),
    };

    var content = new SpellCardMappingExporter().Export("共同Boss", rows, CsvPath());

    content.ShouldContain("Boss,符卡名,创作者");
    content.ShouldContain("共同Boss,神技「XXA」,Alice");
    content.ShouldContain("共同Boss,非符,Alice"); // 非符按默认名表示
    content.ShouldContain("共同Boss,弹幕「ZZB」,Bob");
  }

  [Test]
  public void Exporter_WritesFileToDisk()
  {
    var rows = new List<SpellCardMappingRow> { new("卡A", "Alice", false) };
    new SpellCardMappingExporter().Export("Boss", rows, CsvPath());

    File.Exists(CsvPath()).ShouldBeTrue();
  }

  [Test]
  public void Exporter_CsvReadableByImporter_GuessingRoundTrip()
  {
    var rows = new List<SpellCardMappingRow>
    {
      new("卡A", "Alice", false),
      new("卡B", "Bob", false),
    };
    new SpellCardMappingExporter().Export("共同Boss", rows, CsvPath());

    // 用现有 CsvImporter 读回，验证猜测模块可导入
    var result = new CsvImporter().ImportSpellCardTable(CsvPath());
    result.IsSuccess.ShouldBeTrue();
    result.Data!.Count.ShouldBe(1);
    result.Data[0].Name.ShouldBe("共同Boss");
    result.Data[0].SpellCards.Count.ShouldBe(2);
    result.Data[0].SpellCards[0].Name.Value.ShouldBe("卡A");
    result.Data[0].SpellCards[0].Creator.Value.ShouldBe("Alice");
    result.Data[0].SpellCards[1].Name.Value.ShouldBe("卡B");
    result.Data[0].SpellCards[1].Creator.Value.ShouldBe("Bob");
  }

  [Test]
  public void Exporter_EmptyRows_Throws()
  {
    var exporter = new SpellCardMappingExporter();
    Should.Throw<ArgumentException>(() =>
      exporter.Export("Boss", new List<SpellCardMappingRow>(), CsvPath())
    );
  }

  [Test]
  public void Exporter_EmptyBossName_Throws()
  {
    var rows = new List<SpellCardMappingRow> { new("卡A", "Alice", false) };
    var exporter = new SpellCardMappingExporter();
    Should.Throw<ArgumentException>(() => exporter.Export("", rows, CsvPath()));
  }

  // ==================== SharpCliInvoker Tests ====================

  [Test]
  public void CliInvoker_BuildArguments_MatchesExpectedOrder()
  {
    var args = SharpCliInvoker.BuildArguments(
      "C:/proj/merged.lstgproj",
      "C:/out",
      "cmex22",
      "LuaSTGPlusLib.dll"
    );

    args.Length.ShouldBe(7);
    args[0].ShouldBe("C:/proj/merged.lstgproj");
    args[1].ShouldBe("-d");
    args[2].ShouldBe("C:/out");
    args[3].ShouldBe("-n");
    args[4].ShouldBe("cmex22");
    args[5].ShouldBe("-p");
    args[6].ShouldBe("LuaSTGPlusLib.dll");
  }
}
