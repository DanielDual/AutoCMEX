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
/// 导出相关单元测试：对应表三列 CSV 导出与 Sharp Cli 参数构造。
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
      "proj/merged.lstgproj",
      "out",
      "samp",
      "LuaSTGPlusLib.dll"
    );

    args.Length.ShouldBe(7);
    args[0].ShouldBe("proj/merged.lstgproj");
    args[1].ShouldBe("-d");
    args[2].ShouldBe("out");
    args[3].ShouldBe("-n");
    args[4].ShouldBe("samp");
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
    var zipPath = Path.Combine(_tempDir, "sample_pkg_a.zip");
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

  // ==================== MergeEngine 物理资源复制 ====================

  private const string EngineTemplateWithResources =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"tmpl_img.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n";

  private const string EnginePackageWithResource =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"符「卡」\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n"
    + "1,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"pkg_img.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Patch, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"pkg_script.lua\",\"EditWindow\":\"luaFile\"}],\"AttributeCount\":1}\n";

  private const string EnginePackageExcludedArchive =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"pkg_img.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"shared/\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"sh.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"符「卡」\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n";

  private string CreateEngineZipWithResource(string zipName, string lstgesText)
  {
    var lstgesPath = WriteEngineData("pkgw/root.lstges", lstgesText);
    var imgPath = WriteEngineData("pkgw/pkg_img.png", "PNG-fake");
    var shPath = WriteEngineData("pkgw/sh.png", "PNG-shared");
    var scPath = WriteEngineData("pkgw/pkg_script.lua", "lua-script");
    var zipPath = Path.Combine(_tempDir, zipName);
    using (var fs = File.Create(zipPath))
    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
    {
      zip.CreateEntryFromFile(lstgesPath, "root.lstges");
      zip.CreateEntryFromFile(imgPath, "pkg_img.png");
      zip.CreateEntryFromFile(shPath, "sh.png");
      zip.CreateEntryFromFile(scPath, "pkg_script.lua");
    }
    return zipPath;
  }

  private DataManager BuildEngineDataManagerResources(bool includeLstges)
  {
    var templatePath = WriteEngineData("tmplr/root.lstgproj", EngineTemplateWithResources);
    WriteEngineData("tmplr/tmpl_img.png", "PNG-template");
    var zipPath = CreateEngineZipWithResource("sample_pkg_r.zip", EnginePackageWithResource);

    var dm = new DataManager(Path.Combine(_tempDir, "userdata_r"), new AesEncryptor("test-key"));
    dm.LoadAll();

    var import = MergeImporter.ImportZip(zipPath);
    import.IsSuccess.ShouldBeTrue();
    dm.CreatorPackages.Add(import.Package!);
    foreach (var card in import.Cards)
      dm.MergeConfig.Mapping.Add(card);

    dm.MergeConfig.TemplatePath.Value = templatePath;
    dm.MergeConfig.OutputDir.Value = Path.Combine(_tempDir, "out_r");
    dm.MergeConfig.IncludeLstges.Value = includeLstges;
    dm.MergeConfig.OutputName.Value = "mod";
    return dm;
  }

  [Test]
  public void MergeEngine_CopiesReferencedResources_ToWorkAndOutputDirs()
  {
    var dm = BuildEngineDataManagerResources(includeLstges: true);
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();

    result.IsSuccess.ShouldBeTrue();
    result.Error.ShouldBeNull();

    // 物理资源必须随合并 .lstgproj 落在同一目录（Sharp 按相对基准找文件打包）
    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    File.Exists(Path.Combine(workDir, "tmpl_img.png")).ShouldBeTrue(); // 模板自身资源随迁
    File.Exists(Path.Combine(workDir, "pkg_img.png")).ShouldBeTrue(); // 包资源随迁
    File.Exists(Path.Combine(workDir, "pkg_script.lua")).ShouldBeTrue(); // Patch 导入的脚本随迁

    // 输出目录（includeLstges=true）也随迁
    var outDir = dm.MergeConfig.OutputDir.Value;
    File.Exists(Path.Combine(outDir, "tmpl_img.png")).ShouldBeTrue();
    File.Exists(Path.Combine(outDir, "pkg_img.png")).ShouldBeTrue();
    File.Exists(Path.Combine(outDir, "pkg_script.lua")).ShouldBeTrue();
  }

  [Test]
  public void MergeEngine_ExcludedArchiveResource_NotCopied()
  {
    var templatePath = WriteEngineData("tmplx/root.lstgproj", EngineTemplateWithResources);
    var zipPath = CreateEngineZipWithResource("sample_pkg_x.zip", EnginePackageExcludedArchive);

    var dm = new DataManager(Path.Combine(_tempDir, "userdata_x"), new AesEncryptor("test-key"));
    dm.LoadAll();
    var import = MergeImporter.ImportZip(zipPath);
    dm.CreatorPackages.Add(import.Package!);
    foreach (var card in import.Cards)
      dm.MergeConfig.Mapping.Add(card);

    dm.MergeConfig.TemplatePath.Value = templatePath;
    dm.MergeConfig.OutputDir.Value = Path.Combine(_tempDir, "out_x");
    dm.MergeConfig.IncludeLstges.Value = true;
    dm.MergeConfig.OutputName.Value = "mod";
    // 配置清单排除 shared/ 归档空间（命中则资源不检测/不导入/不复制）
    dm.MergeConfig.ExcludedArchiveSpaces.Add("shared/");

    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();
    result.IsSuccess.ShouldBeTrue();

    // 命中排除集的资源不注入 → 合并工程无该 LoadImage → 物理文件不复制
    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    File.Exists(Path.Combine(workDir, "sh.png")).ShouldBeFalse();
    // 但包自身资源（未排除）仍随迁
    File.Exists(Path.Combine(workDir, "pkg_img.png")).ShouldBeTrue();
  }

  private const string EnginePackagePatchArchive =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"custom/\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".General.Patch, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"pkg_script.lua\",\"EditWindow\":\"luaFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"符「卡」\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n";

  [Test]
  public void MergeEngine_CopiesPatchScript_WhenArchiveNotExcluded()
  {
    // 创作者包自定义归档（custom/ 未命中模板、不在配置清单）下的 Patch（脚本导入）节点：
    // 脚本必须像图片等其它资源一样被物理复制到工作目录与输出目录（否则 Sharp 打包缺脚本，导出包无法运行）。
    var templatePath = WriteEngineData("tmplp/root.lstgproj", EngineTemplateWithResources);
    var zipPath = CreateEngineZipWithResource("sample_pkg_p.zip", EnginePackagePatchArchive);

    var dm = new DataManager(Path.Combine(_tempDir, "userdata_p"), new AesEncryptor("test-key"));
    dm.LoadAll();
    var import = MergeImporter.ImportZip(zipPath);
    import.IsSuccess.ShouldBeTrue();
    dm.CreatorPackages.Add(import.Package!);
    foreach (var card in import.Cards)
      dm.MergeConfig.Mapping.Add(card);

    dm.MergeConfig.TemplatePath.Value = templatePath;
    dm.MergeConfig.OutputDir.Value = Path.Combine(_tempDir, "out_p");
    dm.MergeConfig.IncludeLstges.Value = true;
    dm.MergeConfig.OutputName.Value = "mod";

    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();
    result.IsSuccess.ShouldBeTrue();

    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    File.Exists(Path.Combine(workDir, "pkg_script.lua")).ShouldBeTrue(); // Patch 脚本随迁到工作目录
    var outDir = dm.MergeConfig.OutputDir.Value;
    File.Exists(Path.Combine(outDir, "pkg_script.lua")).ShouldBeTrue(); // 输出目录同样随迁
  }

  [Test]
  public void MergeEngine_CopiesPatchScript_IntoArchiveSubdir_WhenNotExcluded()
  {
    // 贴合真机场景：创作者包 Patch（脚本导入）引用 sample_exp/special.lua（脚本物理位于包 zip 的
    // sample_exp/ 子目录），归档 sample_exp/ 未命中模板、不在配置清单。脚本必须像其它资源一样随迁到
    // 工作/输出目录的 sample_exp/ 子目录（否则导出包缺脚本，运行时无法加载）。
    var templatePath = WriteEngineData("tmplh/root.lstgproj", EngineTemplateWithResources);
    const string pkgText =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "2,{\"$type\":\".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"sample_exp/\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "3,{\"$type\":\".General.Patch, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"sample_exp/special.lua\",\"EditWindow\":\"luaFile\"}],\"AttributeCount\":1}\n"
      + "1,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"符「卡」\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "2,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n";
    var lstgesPath = WriteEngineData("pktemp/root.lstges", pkgText);
    var scriptPath = WriteEngineData("pktemp/sample_exp/special.lua", "lua-script");
    var zipPath = Path.Combine(_tempDir, "sample_pkg_h.zip");
    using (var fs = File.Create(zipPath))
    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
    {
      zip.CreateEntryFromFile(lstgesPath, "root.lstges");
      zip.CreateEntryFromFile(scriptPath, "sample_exp/special.lua");
    }

    var dm = new DataManager(Path.Combine(_tempDir, "userdata_h"), new AesEncryptor("test-key"));
    dm.LoadAll();
    var import = MergeImporter.ImportZip(zipPath);
    import.IsSuccess.ShouldBeTrue();
    dm.CreatorPackages.Add(import.Package!);
    foreach (var card in import.Cards)
      dm.MergeConfig.Mapping.Add(card);

    dm.MergeConfig.TemplatePath.Value = templatePath;
    dm.MergeConfig.OutputDir.Value = Path.Combine(_tempDir, "out_h");
    dm.MergeConfig.IncludeLstges.Value = true;
    dm.MergeConfig.OutputName.Value = "mod";

    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();
    result.IsSuccess.ShouldBeTrue();

    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    File.Exists(Path.Combine(workDir, "sample_exp", "special.lua")).ShouldBeTrue(); // 子目录脚本随迁到工作目录
    var outDir = dm.MergeConfig.OutputDir.Value;
    File.Exists(Path.Combine(outDir, "sample_exp", "special.lua")).ShouldBeTrue(); // 输出目录 sample_exp/ 子目录出现
  }

  // ==================== 冲突自动改名（Phase 1 双改端到端） ====================

  private const string TwoPackageTemplate =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  /// <summary>同名资源（<c>res/shared.png</c>）位于顶层文件夹内的创作者包工程文本。</summary>
  private static string TwoPackageText(string folder, string card) =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + folder
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"res/shared.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + card
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[],\"AttributeCount\":0}\n";

  /// <summary>
  /// 写一个含 <c>root.lstges</c>（引用 <c>res/shared.png</c>）的创作者包 zip；
  /// <paramref name="includeImage"/> 为 false 时故意不放物理图片，用于验证缺失资源警告。
  /// </summary>
  private string CreateTwoPackageZip(
    string packageName,
    string folder,
    string card,
    bool includeImage = true
  )
  {
    var srcDir = "two_" + packageName;
    var lstgesPath = WriteEngineData(srcDir + "/root.lstges", TwoPackageText(folder, card));
    var zipPath = Path.Combine(_tempDir, packageName + ".zip");
    using (var fs = File.Create(zipPath))
    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
    {
      zip.CreateEntryFromFile(lstgesPath, "root.lstges");
      if (includeImage)
      {
        var imgPath = WriteEngineData(srcDir + "/res/shared.png", "PNG-" + packageName);
        zip.CreateEntryFromFile(imgPath, "res/shared.png");
      }
    }
    return zipPath;
  }

  /// <summary>
  /// 构造两个包含同名资源文件的创作者包（包名 pkg_alice / pkg_bob）；
  /// <paramref name="includeAliceImage"/> / <paramref name="includeBobImage"/> 可控是否放入物理图片。
  /// </summary>
  private DataManager BuildTwoPackageDataManager(
    string userDataName,
    bool autoRename,
    MergeAlgorithm algorithm,
    bool includeAliceImage = true,
    bool includeBobImage = true
  )
  {
    var templatePath = WriteEngineData("twotmpl/root.lstgproj", TwoPackageTemplate);
    var zipA = CreateTwoPackageZip("pkg_alice", "resAlice", "卡A", includeAliceImage);
    var zipB = CreateTwoPackageZip("pkg_bob", "resBob", "卡B", includeBobImage);

    var dm = new DataManager(Path.Combine(_tempDir, userDataName), new AesEncryptor("test-key"));
    dm.LoadAll();
    foreach (var zipPath in new[] { zipA, zipB })
    {
      var import = MergeImporter.ImportZip(zipPath);
      import.IsSuccess.ShouldBeTrue();
      dm.CreatorPackages.Add(import.Package!);
      foreach (var card in import.Cards)
        dm.MergeConfig.Mapping.Add(card);
    }

    dm.MergeConfig.TemplatePath.Value = templatePath;
    dm.MergeConfig.OutputDir.Value = Path.Combine(_tempDir, "out_" + userDataName);
    dm.MergeConfig.IncludeLstges.Value = true;
    dm.MergeConfig.OutputName.Value = "mod";
    dm.MergeConfig.AutoRenameConflicts.Value = autoRename;
    dm.MergeConfig.Algorithm.Value = algorithm;
    return dm;
  }

  /// <summary>收集合并工程内全部资源引用（相对路径，正斜杠）。</summary>
  private static List<string> CollectResourceRefs(LstgesDocument doc)
  {
    var refs = new List<string>();
    foreach (var node in doc.Nodes)
    {
      if (node.Type == null || !ResourceDetector.ResourceTypes.Contains(node.Type))
        continue;
      var path = node.GetAttrAt(0);
      if (string.IsNullOrWhiteSpace(path))
        continue;
      foreach (var part in path.Split('|', StringSplitOptions.RemoveEmptyEntries))
        refs.Add(part.Trim().Replace('\\', '/'));
    }
    return refs;
  }

  /// <summary>断言合并 .lstgproj 内每条资源引用都能在指定目录找到物理文件。</summary>
  private static void AssertMergedRefsResolve(LstgesDocument merged, string dir, string label)
  {
    var refs = CollectResourceRefs(merged);
    refs.ShouldNotBeEmpty();
    foreach (var rel in refs)
      File.Exists(Path.Combine(dir, rel)).ShouldBeTrue($"{label} 缺少合并工程引用的资源：{rel}");
  }

  [Test]
  public void MergeEngine_TwoPackagesSharedResource_AutoRename_DoubleCopiesAndRefsResolve()
  {
    var dm = BuildTwoPackageDataManager(
      "ud_two_on_ex",
      autoRename: true,
      MergeAlgorithm.Extractive
    );
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();

    result.Error.ShouldBeNull();
    result.IsSuccess.ShouldBeTrue();

    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    var outDir = dm.MergeConfig.OutputDir.Value;

    // 工作目录与输出目录都同时存在两个改名后的文件（<包名>_<原名>），原文件名不再落盘
    var renamed = new[] { "res/pkg_alice_shared.png", "res/pkg_bob_shared.png" };
    foreach (var rel in renamed)
    {
      File.Exists(Path.Combine(workDir, rel)).ShouldBeTrue($"工作目录缺少改名后文件 {rel}");
      File.Exists(Path.Combine(outDir, rel)).ShouldBeTrue($"输出目录缺少改名后文件 {rel}");
    }
    File.Exists(Path.Combine(workDir, "res/shared.png")).ShouldBeFalse();
    File.Exists(Path.Combine(outDir, "res/shared.png")).ShouldBeFalse();

    // 合并 .lstgproj 内资源引用逐条能在工作目录 / 输出目录找到
    var merged = LstgesParser.LoadFile(engine.MergedProjectPath, out var parseError);
    parseError.ShouldBeNull();
    merged.ShouldNotBeNull();
    var refs = CollectResourceRefs(merged!);
    refs.ShouldContain("res/pkg_alice_shared.png");
    refs.ShouldContain("res/pkg_bob_shared.png");
    AssertMergedRefsResolve(merged!, workDir, "工作目录");
    AssertMergedRefsResolve(merged!, outDir, "输出目录");

    // 自动改名后不应再报缺失资源；冲突列表仍应记录该资源冲突
    result
      .Warnings.Any(w => w.Contains("缺失"))
      .ShouldBeFalse(string.Join(" | ", result.Warnings));
    result
      .Conflicts.Any(c => c.Kind == MergeConflictKind.Resource && c.Name == "shared.png")
      .ShouldBeTrue();
    result.ResourcePlacements.Count.ShouldBe(2);
  }

  [Test]
  public void MergeEngine_TwoPackagesSharedResource_RenameDisabled_KeepsOriginalAndListsConflict()
  {
    var dm = BuildTwoPackageDataManager(
      "ud_two_off_ex",
      autoRename: false,
      MergeAlgorithm.Extractive
    );
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();

    result.Error.ShouldBeNull();
    result.IsSuccess.ShouldBeTrue();

    // 冲突列表包含该资源冲突（裸文件名），物理文件保留原名且不出现改名后文件
    result
      .Conflicts.Any(c => c.Kind == MergeConflictKind.Resource && c.Name == "shared.png")
      .ShouldBeTrue();
    result.ResourcePlacements.ShouldBeEmpty();

    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    var outDir = dm.MergeConfig.OutputDir.Value;
    File.Exists(Path.Combine(workDir, "res/shared.png")).ShouldBeTrue();
    File.Exists(Path.Combine(outDir, "res/shared.png")).ShouldBeTrue();
    File.Exists(Path.Combine(workDir, "res/pkg_alice_shared.png")).ShouldBeFalse();
    File.Exists(Path.Combine(workDir, "res/pkg_bob_shared.png")).ShouldBeFalse();
    File.Exists(Path.Combine(outDir, "res/pkg_alice_shared.png")).ShouldBeFalse();
    File.Exists(Path.Combine(outDir, "res/pkg_bob_shared.png")).ShouldBeFalse();
  }

  [Test]
  public void MergeEngine_TopFolderCarry_AutoRename_DoubleCopiesAndRefsResolve()
  {
    var dm = BuildTwoPackageDataManager(
      "ud_two_on_tfc",
      autoRename: true,
      MergeAlgorithm.TopFolderCarry
    );
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();

    result.Error.ShouldBeNull();
    result.IsSuccess.ShouldBeTrue();

    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    var outDir = dm.MergeConfig.OutputDir.Value;
    var renamed = new[] { "res/pkg_alice_shared.png", "res/pkg_bob_shared.png" };
    foreach (var rel in renamed)
    {
      File.Exists(Path.Combine(workDir, rel)).ShouldBeTrue($"工作目录缺少改名后文件 {rel}");
      File.Exists(Path.Combine(outDir, rel)).ShouldBeTrue($"输出目录缺少改名后文件 {rel}");
    }
    File.Exists(Path.Combine(workDir, "res/shared.png")).ShouldBeFalse();
    File.Exists(Path.Combine(outDir, "res/shared.png")).ShouldBeFalse();

    var merged = LstgesParser.LoadFile(engine.MergedProjectPath, out var parseError);
    parseError.ShouldBeNull();
    merged.ShouldNotBeNull();
    var refs = CollectResourceRefs(merged!);
    refs.ShouldContain("res/pkg_alice_shared.png");
    refs.ShouldContain("res/pkg_bob_shared.png");
    AssertMergedRefsResolve(merged!, workDir, "工作目录");
    AssertMergedRefsResolve(merged!, outDir, "输出目录");

    result.Warnings.Any(w => w.Contains("缺失")).ShouldBeFalse(string.Join(" | ", result.Warnings));
    result.ResourcePlacements.Count.ShouldBe(2);
  }

  [Test]
  public void MergeEngine_TopFolderCarry_RenameDisabled_KeepsOriginal()
  {
    var dm = BuildTwoPackageDataManager(
      "ud_two_off_tfc",
      autoRename: false,
      MergeAlgorithm.TopFolderCarry
    );
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();

    result.Error.ShouldBeNull();
    result.IsSuccess.ShouldBeTrue();
    result
      .Conflicts.Any(c => c.Kind == MergeConflictKind.Resource && c.Name == "shared.png")
      .ShouldBeTrue();
    result.ResourcePlacements.ShouldBeEmpty();

    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    File.Exists(Path.Combine(workDir, "res/shared.png")).ShouldBeTrue();
    File.Exists(Path.Combine(workDir, "res/pkg_alice_shared.png")).ShouldBeFalse();
    File.Exists(Path.Combine(workDir, "res/pkg_bob_shared.png")).ShouldBeFalse();
  }

  [Test]
  public void MergeEngine_AutoRename_MissingPhysicalImage_AddsMissingWarning()
  {
    // 正向覆盖（code-reviewer W1）：其中一包只有 .lstges 引用、缺物理图片时，
    // 自动改名链路必须把「自动改名资源缺失」作为非致命警告返回（先前只有「不缺失」负向断言）。
    var dm = BuildTwoPackageDataManager(
      "ud_two_missing_ex",
      autoRename: true,
      MergeAlgorithm.Extractive,
      includeBobImage: false
    );
    var engine = new MergeEngine(dm, dm.MergeConfig.TemplatePath.Value, true, false);
    var result = engine.BuildAndMerge();

    result.Error.ShouldBeNull();
    result.IsSuccess.ShouldBeTrue();

    // 文案级正向断言：警告前缀 + 包名 + 源相对路径，便于用户定位到具体包与文件
    result.Warnings.ShouldContain(
      w => w.Contains("自动改名资源缺失"),
      string.Join(" | ", result.Warnings)
    );
    result.Warnings.ShouldContain(w => w.Contains("pkg_bob") && w.Contains("res/shared.png"));

    // 缺失不阻塞导出：另一个包的改名资源照常落盘，缺图那份不落盘
    var workDir = Path.GetDirectoryName(engine.MergedProjectPath)!;
    var outDir = dm.MergeConfig.OutputDir.Value;
    File.Exists(Path.Combine(workDir, "res/pkg_alice_shared.png")).ShouldBeTrue();
    File.Exists(Path.Combine(outDir, "res/pkg_alice_shared.png")).ShouldBeTrue();
    File.Exists(Path.Combine(workDir, "res/pkg_bob_shared.png")).ShouldBeFalse();
  }
}
