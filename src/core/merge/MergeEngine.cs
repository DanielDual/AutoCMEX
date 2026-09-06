namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// 整合引擎的服务门面：在 DataManager 模型层与纯逻辑引擎（Merger/导出器）之间搭桥，
/// 供 UI（MergePanel）调用。负责把模板与创作者包解析为引擎文档、重建合并映射、执行合并，
/// 并导出对应表。真正的外调 Sharp Cli 编译打包与 Lua 混淆属阶段3-导出子项。
/// </summary>
public class MergeEngine
{
  private readonly DataManager _dm;
  private readonly string _templatePath;
  private readonly bool _includeLstges;
  private readonly bool _obfuscateLua;
  private readonly ILog _log;

  public MergeEngine(DataManager dm, string templatePath, bool includeLstges, bool obfuscateLua)
  {
    _dm = dm;
    _templatePath = templatePath;
    _includeLstges = includeLstges;
    _obfuscateLua = obfuscateLua;
    _log = AppLogs.GetOrCreate().GetLogger(nameof(MergeEngine));
  }

  /// <summary>
  /// 解析模板的 Boss 显示名作为对应表的共通 Boss 名；失败回退「共同Boss」。
  /// </summary>
  public static string ResolveCommonBossName(DataManager dm)
  {
    var template = dm.MergeConfig.TemplatePath.Value;
    if (string.IsNullOrEmpty(template))
      return "共同Boss";
    var doc = LstgesParser.LoadFile(template, out _);
    if (doc == null)
      return "共同Boss";
    var boss = doc.FindAll(n => n.Type == ".Boss.BossDefine, ").FirstOrDefault();
    if (boss == null)
      return "共同Boss";
    var name = boss.GetAttr("Displayed name");
    return string.IsNullOrEmpty(name) ? "共同Boss" : name;
  }

  /// <summary>
  /// 合并全部创作者包进模板（按映射顺序注入），据此生成合并产物；成功时按导出选项落地工程文件。
  /// </summary>
  /// <returns>引擎合并结果（含冲突/错误），并附带 <see cref="MergeResult.Merged"/>。</returns>
  public MergeResult BuildAndMerge()
  {
    var template = LstgesParser.LoadFile(_templatePath, out var templateError);
    if (template == null)
    {
      _log.Warn($"MergeEngine: template parse failed: {templateError}");
      return new MergeResult { Error = templateError ?? "模板解析失败" };
    }

    // 取非删除包，按顺序编号（映射按包名回查下标）
    var packageNames = new List<string>();
    var packageDocs = new List<CreatorPackageDoc>();
    foreach (var pkg in _dm.CreatorPackages)
    {
      if (pkg.IsDeleted.Value)
        continue;
      var doc = ReloadPackageDoc(pkg.SourcePath.Value, out _);
      if (doc == null)
      {
        _log.Warn($"MergeEngine: package '{pkg.PackageName}' source unreadable, skipped.");
        continue;
      }
      packageNames.Add(pkg.PackageName);
      packageDocs.Add(new CreatorPackageDoc(pkg.PackageName, doc));
    }

    var indexByName = packageNames
      .Select((name, idx) => (name, idx))
      .ToDictionary(x => x.name, x => x.idx);

    var entries = new List<MergeMappingEntry>();
    foreach (var m in _dm.MergeConfig.Mapping)
    {
      if (!indexByName.TryGetValue(m.PackageName, out var pkgIdx))
      {
        return new MergeResult { Error = $"映射引用了未导入的创作者包：{m.PackageName}" };
      }
      entries.Add(new MergeMappingEntry(pkgIdx, m.SourceCardIndex, m.Creator.Value));
    }

    var options = new MergeOptions
    {
      AutoRenameResources = _dm.MergeConfig.AutoRenameConflicts.Value,
    };
    var result = new Merger().Merge(template, packageDocs, entries, options);
    if (result.IsSuccess)
      PersistMergedProject(result.Merged!);

    _log.Print(
      $"MergeEngine: merged {entries.Count} entries into template, conflicts={result.Conflicts.Count}."
    );
    return result;
  }

  private string _workProjectPath = string.Empty;

  /// <summary>
  /// 供 Sharp Cli 编译打包的合并工程文件完整路径（工作中间产物，始终可用）。
  /// </summary>
  public string MergedProjectPath => _workProjectPath;

  /// <summary>
  /// 把合并后的工程文件持久化。始终写入临时工作工程（供 Sharp 编译打包）；
  /// 仅当「包含 .lstges」开关开启时才把工程文件交付到输出目录（“不提供工程文件”时跳过交付）。
  /// 混淆开关属 Sharp 或后续混淆步骤，此处仅记录为导出选项的一部分。
  /// </summary>
  private void PersistMergedProject(LstgesDocument merged)
  {
    var name = string.IsNullOrWhiteSpace(_dm.MergeConfig.OutputName.Value)
      ? "mod"
      : _dm.MergeConfig.OutputName.Value;

    // 工作中间工程：始终写入临时目录，保证 Sharp 编译有输入文件（不因 includeLstges 而缺失）。
    var workDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Merge_{Guid.NewGuid():N}");
    Directory.CreateDirectory(workDir);
    _workProjectPath = Path.Combine(workDir, name + ".lstgproj");
    File.WriteAllText(_workProjectPath, merged.Serialize());

    if (!_includeLstges)
    {
      _log.Print(
        $"MergeEngine: merged project kept as work artifact (includeLstges=false), obfuscate={_obfuscateLua}."
      );
      return;
    }

    var outputDir = _dm.MergeConfig.OutputDir.Value;
    if (string.IsNullOrEmpty(outputDir))
      return;
    Directory.CreateDirectory(outputDir);
    File.WriteAllText(Path.Combine(outputDir, name + ".lstgproj"), merged.Serialize());
    _log.Print(
      $"MergeEngine: delivered merged project to {outputDir} (obfuscate={_obfuscateLua})."
    );
  }

  /// <summary>
  /// 导出「符卡—创作者对应表」三列 CSV（供猜测模块导入）。
  /// </summary>
  /// <returns>导出的 CSV 内容；失败返回空串。</returns>
  public string ExportMapping(string outputDir)
  {
    var mapping = _dm.MergeConfig.Mapping;
    if (mapping.Count == 0)
      return string.Empty;

    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
      Directory.CreateDirectory(outputDir);

    var rows = mapping
      .Select(e => new SpellCardMappingRow(e.Name, e.Creator.Value, e.IsNonSpell.Value))
      .ToList();
    var outPath = Path.Combine(outputDir, "spellcard_mapping.csv");
    return new SpellCardMappingExporter().Export(ResolveCommonBossName(_dm), rows, outPath);
  }

  private static LstgesDocument? ReloadPackageDoc(string sourcePath, out string? error)
  {
    error = null;
    if (string.IsNullOrEmpty(sourcePath))
    {
      error = "源路径为空";
      return null;
    }
    var import = MergeImporter.ImportZip(sourcePath);
    if (!import.IsSuccess)
    {
      error = import.Error;
      return null;
    }

    // 解压目录的生命周期须持续到解析完成（FindLstges 不再删除），
    // 否则返回的 .lstges 路径在 LoadFile 前已指向已删除目录。
    using var work = ExtractPackageToTemp(sourcePath);
    if (work.TempDir == null)
    {
      error = "解压创作者包失败";
      return null;
    }

    var lstges = Directory
      .EnumerateFiles(work.TempDir, "*.lstges", SearchOption.AllDirectories)
      .FirstOrDefault();
    if (lstges == null)
      return LstgesParser.LoadFile(sourcePath, out error);
    return LstgesParser.LoadFile(lstges, out error);
  }

  /// <summary>
  /// 解压创作者 zip 到临时目录；<see cref="IDisposable"/> 释放时删除解压目录。
  /// </summary>
  private sealed record PackageWork(string? TempDir, string CleanupDir) : IDisposable
  {
    public void Dispose()
    {
      try
      {
        if (!string.IsNullOrEmpty(CleanupDir) && Directory.Exists(CleanupDir))
          Directory.Delete(CleanupDir, true);
      }
      catch
      { /* 忽略清理错误 */
      }
    }
  }

  private static PackageWork ExtractPackageToTemp(string zipPath)
  {
    if (!File.Exists(zipPath))
      return new PackageWork(null, string.Empty);
    var tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Reload_",
      Guid.NewGuid().ToString("N")[..6]
    );
    try
    {
      System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempDir);
      return new PackageWork(tempDir, tempDir);
    }
    catch
    {
      try
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
      catch
      { /* 忽略 */
      }
      return new PackageWork(null, string.Empty);
    }
  }
}
