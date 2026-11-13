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
/// 并导出对应表。真正的外调 Sharp Cli 编译打包与 Lua 混淆属导出子项。
/// </summary>
public class MergeEngine
{
  private readonly DataManager _dm;
  private readonly string _templatePath;
  private readonly bool _includeLstges;
  private readonly bool _obfuscateLua;
  private readonly ILog _log;

  /// <summary>
  /// 创建整合引擎门面。
  /// </summary>
  /// <param name="dm">数据管理器。</param>
  /// <param name="templatePath">模板工程路径。</param>
  /// <param name="includeLstges">是否把合并工程交付到输出目录。</param>
  /// <param name="obfuscateLua">是否混淆 Lua（当前仅透传给导出流程）。</param>
  /// <param name="log">日志；为 <c>null</c> 时使用 <see cref="NullLog.Instance"/>（生产/测试可显式注入）。</param>
  public MergeEngine(
    DataManager dm,
    string templatePath,
    bool includeLstges,
    bool obfuscateLua,
    ILog? log = null
  )
  {
    _dm = dm;
    _templatePath = templatePath;
    _includeLstges = includeLstges;
    _obfuscateLua = obfuscateLua;
    _log = log ?? NullLog.Instance;
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
    var creatorNames = new List<string>();
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
      var creator = pkg.CreatorName?.Value?.Trim() ?? string.Empty;
      creatorNames.Add(string.IsNullOrWhiteSpace(creator) ? pkg.PackageName : creator);
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
      ForcePerformAction = _dm.MergeConfig.ForcePerformAction.Value,
      GroupByCreatorFolders = _dm.MergeConfig.GroupByCreatorFolders.Value,
      ExcludedArchiveSpaces = _dm.MergeConfig.ExcludedArchiveSpaces,
    };
    MergeResult result =
      _dm.MergeConfig.Algorithm.Value == MergeAlgorithm.TopFolderCarry
        ? new TopFolderCarryMerger(_log).Merge(template, packageDocs, entries, options)
        : new Merger(_log).Merge(template, packageDocs, entries, options, creatorNames);
    if (result.IsSuccess)
    {
      PersistMergedProject(result.Merged!);

      // 物理资源必须随合并 .lstgproj 落在同一目录（Sharp 依相对基准找文件打包）。
      // 工作目录（Sharp 输入）始终复制；只要配置了输出目录也会复制（与是否交付 .lstgproj 无关）。
      // 自动改名资源的落盘计划随结果传入，避免按改名后路径在源包找不到源文件而漏拷。
      var missing = new List<string>();
      missing.AddRange(
        CopyResources(
          result.Merged!,
          Path.GetDirectoryName(_workProjectPath) ?? string.Empty,
          result.ResourcePlacements
        )
      );
      if (_dm.MergeConfig.OutputDir.Value.Length > 0)
      {
        missing.AddRange(
          CopyResources(result.Merged!, _dm.MergeConfig.OutputDir.Value, result.ResourcePlacements)
        );
      }

      // 缺失资源并入合并警告，由 MergePanel 在冲突行之后以 [警告] 展示（不新增弹窗）。
      result.Warnings.AddRange(missing.Distinct());
    }

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
  /// <returns>导出的 CSV 内容；无映射时返回空串，写入失败时抛异常。</returns>
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
    return new SpellCardMappingExporter(_log).Export(ResolveCommonBossName(_dm), rows, outPath);
  }

  /// <summary>
  /// 把合并工程实际引用的物理资源按相对路径基准复制到目标目录（工作目录/输出目录），
  /// 使 Sharp 打包时能找到资源文件。源：各源包解压目录（优先）+ 模板工程目录。
  /// 命中排除集的资源不在合并工程中，故不涉及复制。
  /// <para>
  /// 先处理 <paramref name="placements"/>（自动改名资源）：按包名建立「包名 → 解压目录」映射，
  /// 从对应源包目录取原始相对路径、写到改名后的目标相对路径；已被 placement 处理的目标路径
  /// 在后续「按合并工程引用搜索」循环中跳过，避免改名后路径在源目录找不到而误报缺失。
  /// </para>
  /// 返回未找到的资源描述列表（普通缺失为相对路径，自动改名缺失带包名与源路径）。
  /// </summary>
  private List<string> CopyResources(
    LstgesDocument merged,
    string targetDir,
    IReadOnlyList<ResourcePlacement> placements
  )
  {
    var missing = new List<string>();
    if (string.IsNullOrEmpty(targetDir) || merged == null)
      return missing;

    // 收集合并工程实际引用的相对路径（资源节点首个属性，可含 | 分隔的多路径）
    var referenced = new HashSet<string>();
    foreach (var node in merged.Nodes)
    {
      if (node.Type == null || !ResourceDetector.ResourceTypes.Contains(node.Type))
        continue;
      var path0 = node.GetAttrAt(0);
      if (string.IsNullOrWhiteSpace(path0))
        continue;
      foreach (var part in path0.Split('|', StringSplitOptions.RemoveEmptyEntries))
      {
        var rel = NormalizeRelative(part.Trim());
        if (rel.Length > 0)
          referenced.Add(rel);
      }
    }
    if (referenced.Count == 0 && placements.Count == 0)
      return missing;

    // 源目录：各非删除包解压目录（优先，保证导入资源取创作者实际文件）+ 模板工程目录（模板自身资源）。
    // 另建「包名 → 解压目录」映射供自动改名资源按包取源文件（不用包索引，避免不可读包错位）。
    var sources = new List<string>();
    var archives = new List<PackageWork>();
    var packageDirs = new Dictionary<string, string>(StringComparer.Ordinal);
    var copiedCount = 0;
    try
    {
      foreach (var pkg in _dm.CreatorPackages)
      {
        if (pkg.IsDeleted.Value)
          continue;
        var work = ExtractPackageToTemp(pkg.SourcePath.Value);
        if (work.TempDir != null)
        {
          sources.Add(work.TempDir);
          archives.Add(work);
          packageDirs[pkg.PackageName] = work.TempDir;
        }
        else
        {
          work.Dispose();
        }
      }
      var templateDir = Path.GetDirectoryName(_templatePath);
      if (!string.IsNullOrEmpty(templateDir))
        sources.Add(templateDir);

      // 先处理自动改名资源：按包名映射取源文件，落到改名后的目标相对路径。
      var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var placement in placements)
      {
        var srcRel = NormalizeRelative(placement.SourceRelativePath);
        var dstRel = NormalizeRelative(placement.DestinationRelativePath);
        if (srcRel.Length == 0 || dstRel.Length == 0)
          continue;

        // 该目标路径已由 placement 负责：后续引用搜索循环跳过，避免按改名后路径重复找源文件。
        handled.Add(dstRel);

        if (!packageDirs.TryGetValue(placement.PackageName ?? string.Empty, out var packageDir))
        {
          missing.Add(
            $"自动改名资源缺失：未找到创作者包 \"{placement.PackageName}\" 的解压目录（源 {srcRel}）"
          );
          continue;
        }

        var candidate = Path.Combine(packageDir, srcRel);
        if (!File.Exists(candidate))
        {
          missing.Add(
            $"自动改名资源缺失：包 \"{placement.PackageName}\" 内未找到 {srcRel}（目标 {dstRel}）"
          );
          continue;
        }
        CopyToTarget(candidate, targetDir, dstRel);
        copiedCount++;
      }

      foreach (var rel in referenced)
      {
        if (handled.Contains(rel))
          continue;
        var copied = false;
        foreach (var src in sources)
        {
          var candidate = Path.Combine(src, rel);
          if (!File.Exists(candidate))
            continue;
          CopyToTarget(candidate, targetDir, rel);
          copied = true;
          copiedCount++;
          break;
        }
        if (!copied)
          missing.Add($"缺失资源文件：{rel}");
      }
    }
    finally
    {
      foreach (var a in archives)
        a.Dispose();
    }

    if (missing.Count > 0)
      _log.Warn($"MergeEngine: missing resource files while copying: {string.Join(", ", missing)}");
    else
      _log.Print($"MergeEngine: copied {copiedCount} referenced resources to {targetDir}.");
    return missing;
  }

  /// <summary>
  /// 把 <paramref name="sourcePath"/> 复制到 <paramref name="targetDir"/> 下的
  /// <paramref name="relativePath"/>（自动创建目标子目录）。
  /// </summary>
  private static void CopyToTarget(string sourcePath, string targetDir, string relativePath)
  {
    var dest = Path.Combine(targetDir, relativePath);
    var destDir = Path.GetDirectoryName(dest);
    if (!string.IsNullOrEmpty(destDir))
      Directory.CreateDirectory(destDir);
    File.Copy(sourcePath, dest, overwrite: true);
  }

  /// <summary>把资源相对路径归一为「相对工程根、无 ./ 或前导 /」形式。</summary>
  private static string NormalizeRelative(string value)
  {
    var s = value.Trim().Replace('\\', '/');
    while (s.StartsWith("./") || s.StartsWith("/"))
      s = s.Substring(s.StartsWith("/") ? 1 : 2);
    return s;
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

    // 解压目录的生命周期须持续到解析完成（旧 FindLstges 已移除，不再提前删除），
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
