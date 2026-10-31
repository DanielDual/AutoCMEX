namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// GIF 集服务单测：严格集内自洽校验的失败路径必须报错且不落库，成功路径完成注册与当前集切换。
/// </summary>
public class GifSetServiceTest : TestClass
{
  private string _root = string.Empty;
  private string _dataDir = string.Empty;
  private string _workDir = string.Empty;
  private DataManager _dataManager = default!;
  private GifSetService _service = default!;

  public GifSetServiceTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(Path.GetTempPath(), "AutoCMEX_Test_" + Guid.NewGuid().ToString("N")[..8]);
    _dataDir = Path.Combine(_root, "data");
    _workDir = Path.Combine(_root, "work");
    Directory.CreateDirectory(_dataDir);
    Directory.CreateDirectory(_workDir);

    _dataManager = new DataManager(
      _dataDir,
      new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_dataDir)),
      new Mock<ILog>().Object
    );
    _service = new GifSetService(_dataManager, _dataDir, new Mock<ILog>().Object);
  }

  [Cleanup]
  public void Cleanup()
  {
    _dataManager?.Dispose();

    if (Directory.Exists(_root))
      Directory.Delete(_root, true);
  }

  // ==================== 文件夹导入：成功路径 ====================

  [Test]
  public void ImportFolder_ValidSet_RegistersAndAutoSelects()
  {
    var folder = CreateSetDir("sample_exp", (1, "非符", "1.gif"), (2, "符卡 A", "2.gif"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeTrue();
    result.Set.ShouldNotBeNull();
    result.Set!.SetName.Value.ShouldBe("SamplePkg");
    result.Set.EntryCount.Value.ShouldBe(2);
    result.Set.RootPath.Value.ShouldBe(Path.GetFullPath(folder));
    _service.Sets.Count.ShouldBe(1);

    // 首个集导入后自动选中，栏内不会停在「未选中」空态
    _dataManager.InfoConfig.ActiveGifSetId.Value.ShouldBe(result.Set.Id.Value);
    _service.GetActiveSet()!.Id.Value.ShouldBe(result.Set.Id.Value);
  }

  [Test]
  public void ImportFolder_EntryPathIsResolvedUnderSetRoot()
  {
    var folder = CreateSetDir("sample_exp", (1, "非符", "1.gif"));
    var set = _service.ImportFolder(folder).Set!;

    var path = _service.GetEntryPath(set, set.Manifest.Entries[0]);

    path.ShouldBe(Path.Combine(Path.GetFullPath(folder), "1.gif"));
    File.Exists(path).ShouldBeTrue();
  }

  [Test]
  public void ImportFolder_SamePathTwice_UpdatesInPlaceWithoutDuplicate()
  {
    var folder = CreateSetDir("sample_exp", (1, "非符", "1.gif"));
    var first = _service.ImportFolder(folder);

    // 追加一张后再导入同一目录：应原地更新而不是新增一条
    WriteGif(folder, "2.gif");
    WriteManifest(folder, "SamplePkg", (1, "非符", "1.gif"), (2, "符卡 A", "2.gif"));
    var second = _service.ImportFolder(folder);

    second.IsSuccess.ShouldBeTrue();
    _service.Sets.Count.ShouldBe(1);
    first.Set!.Id.Value.ShouldBe(second.Set!.Id.Value);
    second.Set.EntryCount.Value.ShouldBe(2);
  }

  // ==================== 文件夹导入：校验失败（必须不落库） ====================

  [Test]
  public void ImportFolder_ManifestMissing_Rejected()
  {
    var folder = Path.Combine(_workDir, "no_manifest");
    Directory.CreateDirectory(folder);
    WriteGif(folder, "1.gif");

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("manifest.json");
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_BrokenJson_Rejected()
  {
    var folder = Path.Combine(_workDir, "broken");
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, GifSetManifest.FileName), "{ not json ");

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("解析失败");
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_NoEntries_Rejected()
  {
    var folder = Path.Combine(_workDir, "empty_entries");
    Directory.CreateDirectory(folder);
    WriteManifest(folder, "SamplePkg");

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("未声明任何条目");
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_DeclaredFileMissing_Rejected()
  {
    var folder = Path.Combine(_workDir, "missing_file");
    Directory.CreateDirectory(folder);
    WriteManifest(folder, "SamplePkg", (1, "非符", "1.gif"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail => detail.Contains("不存在"));
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_UndeclaredGif_Rejected()
  {
    var folder = CreateSetDir("extra_gif", (1, "非符", "1.gif"));
    WriteGif(folder, "2.gif");

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail =>
      detail.Contains("2.gif") && detail.Contains("未在清单中声明")
    );
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_DuplicateIndex_Rejected()
  {
    var folder = CreateSetDir("dup_index", (1, "非符", "1.gif"), (1, "符卡 A", "2.gif"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail => detail.Contains("重复"));
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_NonPositiveIndex_Rejected()
  {
    var folder = CreateSetDir("bad_index", (0, "非符", "1.gif"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail => detail.Contains("序号必须为正整数"));
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_EmptySpellCardName_Rejected()
  {
    var folder = CreateSetDir("empty_name", (1, "  ", "1.gif"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail => detail.Contains("符卡名为空"));
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_NonGifFile_Rejected()
  {
    var folder = Path.Combine(_workDir, "non_gif");
    Directory.CreateDirectory(folder);
    File.WriteAllBytes(Path.Combine(folder, "1.png"), new byte[] { 1, 2, 3 });
    WriteManifest(folder, "SamplePkg", (1, "非符", "1.png"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail => detail.Contains("不是 .gif 文件"));
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_PathEscapeFileName_Rejected()
  {
    var folder = CreateSetDir("escape", (1, "非符", "1.gif"));
    WriteManifest(folder, "SamplePkg", (1, "非符", "../1.gif"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail => detail.Contains("必须是集根目录下的纯文件名"));
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_FileReusedByTwoEntries_Rejected()
  {
    var folder = CreateSetDir("reuse", (1, "非符", "1.gif"), (2, "符卡 A", "1.gif"));

    var result = _service.ImportFolder(folder);

    result.IsSuccess.ShouldBeFalse();
    result.Details.ShouldContain(detail => detail.Contains("被清单内多个条目引用"));
    _service.Sets.ShouldBeEmpty();
  }

  [Test]
  public void ImportFolder_MissingDirectory_Rejected()
  {
    var result = _service.ImportFolder(Path.Combine(_workDir, "not_exists"));

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("集目录不存在");
    _service.Sets.ShouldBeEmpty();
  }

  // ==================== 压缩包导入 ====================

  [Test]
  public void ImportZip_ValidSet_ExtractsIntoManagedDirAndRegisters()
  {
    var source = CreateSetDir("zip_src", (1, "非符", "1.gif"), (2, "符卡 A", "2.gif"));
    var zip = ZipDir(source, "sample_pkg");

    var result = _service.ImportZip(zip);

    result.IsSuccess.ShouldBeTrue();
    result.Set!.RootPath.Value.ShouldBe(
      Path.Combine(_dataDir, GifSetService.ManagedSetsDirName, result.Set.Id.Value)
    );
    File.Exists(_service.GetEntryPath(result.Set, result.Set.Manifest.Entries[1])).ShouldBeTrue();
    _service.Sets.Count.ShouldBe(1);
  }

  [Test]
  public void ImportZip_InvalidSet_RollsBackExtractedDir()
  {
    var source = CreateSetDir("zip_bad", (1, "非符", "1.gif"));
    // 清单声明 2.gif 但压缩包内没有 → 校验失败，解压目录必须回滚
    WriteManifest(source, "SamplePkg", (1, "非符", "1.gif"), (2, "符卡 A", "2.gif"));
    var zip = ZipDir(source, "bad_pkg");

    var result = _service.ImportZip(zip);

    result.IsSuccess.ShouldBeFalse();
    _service.Sets.ShouldBeEmpty();

    var managed = Path.Combine(_dataDir, GifSetService.ManagedSetsDirName);
    (!Directory.Exists(managed) || Directory.GetDirectories(managed).Length == 0).ShouldBeTrue();
  }

  [Test]
  public void ImportZip_SingleTopLevelFolder_ResolvesSetRoot()
  {
    var source = CreateSetDir("zip_nested", (1, "非符", "1.gif"));
    var topLevel = Path.Combine(_workDir, "zip_nested_top");
    Directory.CreateDirectory(topLevel);
    var inner = Path.Combine(topLevel, "SamplePkg");
    CopyDir(source, inner);
    var zip = ZipDir(topLevel, "nested_pkg");

    var result = _service.ImportZip(zip);

    result.IsSuccess.ShouldBeTrue();
    result.Set!.RootPath.Value.ShouldEndWith("SamplePkg");
    _service.GetActiveSet()!.Id.Value.ShouldBe(result.Set.Id.Value);
  }

  [Test]
  public void ImportZip_MissingFile_Rejected()
  {
    var result = _service.ImportZip(Path.Combine(_workDir, "not_exists.zip"));

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("压缩包不存在");
    _service.Sets.ShouldBeEmpty();
  }

  // ==================== 当前集切换 ====================

  [Test]
  public void SetActiveSet_UnknownId_ReturnsFalseAndKeepsActive()
  {
    var folder = CreateSetDir("sample_exp", (1, "非符", "1.gif"));
    var set = _service.ImportFolder(folder).Set!;

    _service.SetActiveSet("not-exists").ShouldBeFalse();
    _service.GetActiveSet()!.Id.Value.ShouldBe(set.Id.Value);
  }

  [Test]
  public void SetActiveSet_SwitchesActiveSet()
  {
    var first = _service.ImportFolder(CreateSetDir("set_a", (1, "非符", "1.gif"))).Set!;
    var second = _service.ImportFolder(CreateSetDir("set_b", (1, "符卡 B", "1.gif"))).Set!;

    _service.SetActiveSet(second.Id.Value).ShouldBeTrue();
    _service.GetActiveSet()!.Id.Value.ShouldBe(second.Id.Value);

    _service.SetActiveSet(first.Id.Value).ShouldBeTrue();
    _service.GetActiveSet()!.Id.Value.ShouldBe(first.Id.Value);
  }

  [Test]
  public void GetActiveSet_StaleActiveId_FallsBackToFirstSet()
  {
    var set = _service.ImportFolder(CreateSetDir("set_a", (1, "非符", "1.gif"))).Set!;
    _dataManager.InfoConfig.ActiveGifSetId.Value = "stale-id";

    var active = _service.GetActiveSet();

    active!.Id.Value.ShouldBe(set.Id.Value);
    _dataManager.InfoConfig.ActiveGifSetId.Value.ShouldBe(set.Id.Value);
  }

  [Test]
  public void GetActiveSet_NoSets_ReturnsNull()
  {
    _service.GetActiveSet().ShouldBeNull();
  }

  // ==================== 夹具工具 ====================

  private string CreateSetDir(
    string folderName,
    params (int Index, string Name, string FileName)[] entries
  )
  {
    var folder = Path.Combine(_workDir, folderName);
    Directory.CreateDirectory(folder);

    foreach (var (_, _, fileName) in entries)
      WriteGif(folder, fileName);

    WriteManifest(folder, "SamplePkg", entries);
    return folder;
  }

  private static void WriteGif(string folder, string fileName) =>
    File.WriteAllBytes(Path.Combine(folder, fileName), new byte[] { 0x47, 0x49, 0x46, 0x38 });

  private static void WriteManifest(
    string folder,
    string setName,
    params (int Index, string Name, string FileName)[] entries
  )
  {
    var manifest = new GifSetManifest
    {
      SetName = setName,
      BossLabel = "SampleBoss",
      GeneratedAt = "2026-09-25 12:00:00",
      Entries = entries
        .Select(entry => new GifSetEntry
        {
          Index = entry.Index,
          SpellCardName = entry.Name,
          FileName = entry.FileName,
          Width = 640,
          Height = 480,
        })
        .ToList(),
    };

    File.WriteAllText(
      Path.Combine(folder, GifSetManifest.FileName),
      JsonSerializer.Serialize(
        manifest,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
      )
    );
  }

  private string ZipDir(string sourceDir, string zipName)
  {
    var zipPath = Path.Combine(_workDir, zipName + ".zip");
    if (File.Exists(zipPath))
      File.Delete(zipPath);

    ZipFile.CreateFromDirectory(sourceDir, zipPath);
    return zipPath;
  }

  private static void CopyDir(string sourceDir, string targetDir)
  {
    Directory.CreateDirectory(targetDir);
    foreach (var file in Directory.EnumerateFiles(sourceDir))
      File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
  }
}
