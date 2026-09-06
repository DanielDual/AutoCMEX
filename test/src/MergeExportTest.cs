namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AutoCMEX.Core.Merge;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
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

  // ==================== MergeEngine 导出选项落地 ====================

  private const string EngineTemplate =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  private const string EnginePackage =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"符「卡」\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n";

  private string WriteEngineData(string relative, string content)
  {
    var path = Path.Combine(_tempDir, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  private string CreateEngineZip()
  {
    var lstgesPath = WriteEngineData("pkg/root.lstges", EnginePackage);
    var zipPath = Path.Combine(_tempDir, "CMEX23_A.zip");
    using (var fs = File.Create(zipPath))
    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
      zip.CreateEntryFromFile(lstgesPath, "root.lstges");
    return zipPath;
  }

  private DataManager BuildEngineDataManager(bool includeLstges)
  {
    var templatePath = WriteEngineData("tmpl/root.lstgproj", EngineTemplate);
    var zipPath = CreateEngineZip();

    var dm = new DataManager(Path.Combine(_tempDir, "userdata"), new AesEncryptor("test-key"));
    dm.LoadAll();

    var import = MergeImporter.ImportZip(zipPath);
    import.IsSuccess.ShouldBeTrue();
    dm.CreatorPackages.Add(import.Package!);
    foreach (var card in import.Cards)
      dm.MergeConfig.Mapping.Add(card);

    dm.MergeConfig.TemplatePath.Value = templatePath;
    dm.MergeConfig.OutputDir.Value = Path.Combine(_tempDir, "out");
    dm.MergeConfig.IncludeLstges.Value = includeLstges;
    dm.MergeConfig.OutputName.Value = "mod";
    return dm;
  }

  [Test]
  public void MergeEngine_IncludeLstgesTrue_DeliversProjectFile()
  {
    var dm = BuildEngineDataManager(includeLstges: true);
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();

    result.Error.ShouldBeNull();
    result.IsSuccess.ShouldBeTrue();
    engine.MergedProjectPath.ShouldNotBeNullOrEmpty();
    File.Exists(engine.MergedProjectPath).ShouldBeTrue();

    var delivered = Path.Combine(dm.MergeConfig.OutputDir.Value, "mod.lstgproj");
    File.Exists(delivered).ShouldBeTrue();
  }

  [Test]
  public void MergeEngine_IncludeLstgesFalse_SkipsDeliverButKeepsWorkArtifact()
  {
    var dm = BuildEngineDataManager(includeLstges: false);
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, false, false);
    var result = engine.BuildAndMerge();

    result.IsSuccess.ShouldBeTrue();
    engine.MergedProjectPath.ShouldNotBeNullOrEmpty();
    File.Exists(engine.MergedProjectPath).ShouldBeTrue();

    var delivered = Path.Combine(dm.MergeConfig.OutputDir.Value, "mod.lstgproj");
    File.Exists(delivered).ShouldBeFalse();
  }

  [Test]
  public void MergeEngine_ExportMapping_WritesCsv_ReadableByImporter()
  {
    var dm = BuildEngineDataManager(includeLstges: true);
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var content = engine.ExportMapping(dm.MergeConfig.OutputDir.Value);

    content.ShouldNotBeEmpty();
    content.ShouldContain("Boss,符卡名,创作者");

    var csv = Path.Combine(dm.MergeConfig.OutputDir.Value, "spellcard_mapping.csv");
    File.Exists(csv).ShouldBeTrue();

    var result = new CsvImporter().ImportSpellCardTable(csv);
    result.IsSuccess.ShouldBeTrue();
  }
}
