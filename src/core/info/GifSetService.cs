namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;

/// <summary>
/// GIF 集导入结果；校验失败时携带逐条明细，便于界面完整列出原因。
/// </summary>
public sealed class GifSetImportResult
{
  /// <summary>是否导入成功。</summary>
  public bool IsSuccess { get; init; }

  /// <summary>成功时的集注册记录；失败时为 null。</summary>
  public GifSetRecord? Set { get; init; }

  /// <summary>失败概述（单行）。</summary>
  public string ErrorMessage { get; init; } = string.Empty;

  /// <summary>失败明细（逐条，供界面逐行展示）。</summary>
  public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();

  /// <summary>构造成功结果。</summary>
  /// <param name="set">导入成功的集记录。</param>
  /// <returns>成功结果。</returns>
  public static GifSetImportResult Success(GifSetRecord set) =>
    new() { IsSuccess = true, Set = set };

  /// <summary>构造失败结果。</summary>
  /// <param name="message">失败概述。</param>
  /// <param name="details">失败明细。</param>
  /// <returns>失败结果。</returns>
  public static GifSetImportResult Error(string message, IReadOnlyList<string> details) =>
    new()
    {
      IsSuccess = false,
      ErrorMessage = message,
      Details = details,
    };

  /// <summary>拼成可直接展示的多行文本（概述 + 明细）。</summary>
  /// <returns>展示文本；成功时为空串。</returns>
  public string ToDisplayText()
  {
    if (IsSuccess)
      return string.Empty;

    return Details.Count == 0 ? ErrorMessage : ErrorMessage + "\n" + string.Join("\n", Details);
  }
}

/// <summary>
/// 符卡 GIF 集服务：导入（文件夹 / 压缩包）、严格集内自洽校验、多集注册与当前集切换。
/// </summary>
/// <remarks>
/// <para>
/// GIF 集对应「导入的工程」，<b>与猜测模块的 Boss 无关</b>：不做任何序号对齐校验；发布标题用的
/// 符卡名、序号、文件名一律取自集内 <c>manifest.json</c>。
/// </para>
/// <para>
/// 校验为<b>集内自洽</b>：清单存在且可解析、条目非空、序号为正且唯一、符卡名非空、文件名为集根
/// 下的纯文件名且与磁盘文件一一对应（不得有清单未声明的 <c>.gif</c>）。任一条不符即整体拒绝、
/// 列出明细并<b>不落库</b>。
/// </para>
/// <para>
/// 布局：文件夹导入<b>就地引用</b>原目录（避免数百 MB 重复拷贝）；压缩包导入解压到
/// <c>{数据目录}/gif_sets/{setId}/</c> 并引用该目录（校验失败则回滚删除）。
/// </para>
/// </remarks>
public class GifSetService
{
  /// <summary>托管集目录名（压缩包导入的解压目标，位于数据目录下）。</summary>
  public const string ManagedSetsDirName = "gif_sets";

  private const string GifExtension = ".gif";

  /// <summary>路径分隔符（跨平台都视为非法文件名字符，防清单写出路径逃逸）。</summary>
  private static readonly char[] PathSeparators = ['/', '\\'];

  private readonly DataManager _dataManager;
  private readonly string _managedSetsDir;
  private readonly ILog _log;

  /// <summary>使用默认日志器创建服务。</summary>
  /// <param name="dataManager">数据管理器（提供 InfoConfig 的集注册表）。</param>
  /// <param name="dataDir">数据目录（托管集目录的父目录）。</param>
  public GifSetService(DataManager dataManager, string dataDir)
    : this(dataManager, dataDir, AppLogs.GetOrCreate().GetLogger(nameof(GifSetService))) { }

  /// <summary>使用指定日志器创建服务。</summary>
  /// <param name="dataManager">数据管理器（提供 InfoConfig 的集注册表）。</param>
  /// <param name="dataDir">数据目录（托管集目录的父目录）。</param>
  /// <param name="log">日志器。</param>
  public GifSetService(DataManager dataManager, string dataDir, ILog log)
  {
    _dataManager = dataManager;
    _managedSetsDir = Path.Combine(dataDir, ManagedSetsDirName);
    _log = log;
  }

  /// <summary>集注册表（按导入顺序）；供界面绑定刷新。</summary>
  public AutoList<GifSetRecord> Sets => _dataManager.InfoConfig.GifSets;

  /// <summary>
  /// 取当前选中的集；未选中或选中项失效时回退到第一个集并写回选中项。
  /// </summary>
  /// <returns>当前集；没有任何集时为 null。</returns>
  public GifSetRecord? GetActiveSet()
  {
    var sets = Sets;
    if (sets.Count == 0)
      return null;

    var activeId = _dataManager.InfoConfig.ActiveGifSetId.Value ?? string.Empty;
    var active = sets.FirstOrDefault(set =>
      string.Equals(set.Id.Value, activeId, StringComparison.Ordinal)
    );
    if (active is not null)
      return active;

    active = sets[0];
    _dataManager.InfoConfig.ActiveGifSetId.Value = active.Id.Value;
    return active;
  }

  /// <summary>切换当前选中的集。</summary>
  /// <param name="setId">目标集 Id。</param>
  /// <returns>切换是否成功（Id 不存在时为 false）。</returns>
  public bool SetActiveSet(string setId)
  {
    var target = Sets.FirstOrDefault(set =>
      string.Equals(set.Id.Value, setId, StringComparison.Ordinal)
    );
    if (target is null)
    {
      _log.Warn($"GifSetService.SetActiveSet: set '{setId}' not found.");
      return false;
    }

    _dataManager.InfoConfig.ActiveGifSetId.Value = setId;
    _dataManager.TriggerAutoSave();
    return true;
  }

  /// <summary>
  /// 取集内某条目的 GIF 绝对路径；只取纯文件名再与集根拼接，天然限制在集根目录内。
  /// </summary>
  /// <param name="set">集记录。</param>
  /// <param name="entry">清单条目。</param>
  /// <returns>GIF 文件的绝对路径。</returns>
  public string GetEntryPath(GifSetRecord set, GifSetEntry entry)
  {
    var fileName = Path.GetFileName(entry.FileName ?? string.Empty);
    return Path.Combine(set.RootPath.Value ?? string.Empty, fileName);
  }

  /// <summary>
  /// 导入文件夹形式的 GIF 集（就地引用，不复制文件）。
  /// </summary>
  /// <param name="folderPath">集根目录（内含 manifest.json 与各 GIF）。</param>
  /// <returns>导入结果；校验不通过时不落库。</returns>
  public GifSetImportResult ImportFolder(string folderPath)
  {
    var fullRoot = SafeGetFullPath(folderPath);
    var failure = ValidateSet(fullRoot, out var manifest);
    if (failure is not null)
    {
      _log.Warn($"GifSetService.ImportFolder: rejected '{fullRoot}' - {failure.ErrorMessage}");
      return failure;
    }

    var record = Register(new DirectoryInfo(fullRoot).Name, fullRoot, manifest!);
    _log.Print($"GifSetService.ImportFolder: imported '{fullRoot}' as set '{record.Id.Value}'.");
    return GifSetImportResult.Success(record);
  }

  /// <summary>
  /// 导入压缩包形式的 GIF 集（解压到托管集目录后引用）。
  /// </summary>
  /// <remarks>
  /// 解压目标即最终集目录（与最终位置同卷，避免跨卷搬运数百 MB）；校验失败或解压异常时回滚
  /// 删除本次解压出的目录，保证不残留、不落库。
  /// </remarks>
  /// <param name="zipPath">压缩包路径。</param>
  /// <returns>导入结果；校验不通过时不落库。</returns>
  public GifSetImportResult ImportZip(string zipPath)
  {
    if (!File.Exists(zipPath))
    {
      _log.Warn($"GifSetService.ImportZip: zip not found '{zipPath}'.");
      return GifSetImportResult.Error("压缩包不存在。", new[] { zipPath });
    }

    var setId = Guid.NewGuid().ToString("N")[..8];
    var extractDir = Path.Combine(_managedSetsDir, setId);

    try
    {
      Directory.CreateDirectory(_managedSetsDir);
      Directory.CreateDirectory(extractDir);
      ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
    }
    catch (Exception ex)
    {
      _log.Err($"GifSetService.ImportZip: extract failed '{zipPath}' - {ex.Message}");
      RollbackExtract(extractDir);
      return GifSetImportResult.Error($"解压失败：{ex.Message}", new[] { zipPath });
    }

    var setRoot = ResolveSetRoot(extractDir);
    var failure = ValidateSet(setRoot, out var manifest);
    if (failure is not null)
    {
      _log.Warn($"GifSetService.ImportZip: rejected '{zipPath}' - {failure.ErrorMessage}");
      RollbackExtract(extractDir);
      return failure;
    }

    var record = Register(setId, setRoot, manifest!);
    _log.Print(
      $"GifSetService.ImportZip: imported '{zipPath}' into '{setRoot}' as set '{record.Id.Value}'."
    );
    return GifSetImportResult.Success(record);
  }

  /// <summary>
  /// 严格集内自洽校验。
  /// </summary>
  /// <param name="rootPath">集根目录。</param>
  /// <param name="manifest">校验通过时的清单；失败时为 null。</param>
  /// <returns>校验失败的结果；通过时为 null。</returns>
  private GifSetImportResult? ValidateSet(string rootPath, out GifSetManifest? manifest)
  {
    manifest = null;

    if (!Directory.Exists(rootPath))
      return GifSetImportResult.Error("集目录不存在。", new[] { rootPath });

    var manifestPath = Path.Combine(rootPath, GifSetManifest.FileName);
    if (!File.Exists(manifestPath))
      return GifSetImportResult.Error(
        $"集根目录缺少 {GifSetManifest.FileName}。",
        new[] { manifestPath }
      );

    try
    {
      var json = File.ReadAllText(manifestPath);
      manifest = JsonSerializer.Deserialize<GifSetManifest>(
        json,
        GifSetManifest.CreateReadOptions()
      );
    }
    catch (Exception ex)
    {
      return GifSetImportResult.Error(
        $"{GifSetManifest.FileName} 解析失败：{ex.Message}",
        new[] { manifestPath }
      );
    }

    if (manifest is null)
      return GifSetImportResult.Error(
        $"{GifSetManifest.FileName} 内容为空或格式不正确。",
        new[] { manifestPath }
      );

    manifest.Entries ??= new List<GifSetEntry>();
    if (manifest.Entries.Count == 0)
      return GifSetImportResult.Error(
        $"{GifSetManifest.FileName} 未声明任何条目。",
        new[] { manifestPath }
      );

    var details = new List<string>();
    var declaredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var seenIndexes = new HashSet<int>();

    for (var i = 0; i < manifest.Entries.Count; i++)
    {
      var entry = manifest.Entries[i];
      if (entry is null)
      {
        details.Add($"entries[{i}]：条目为空。");
        continue;
      }

      var indexText = entry.Index > 0 ? entry.Index.ToString(CultureInfo.InvariantCulture) : "?";
      var label = $"entries[{i}]（序号 {indexText}）";

      if (entry.Index <= 0)
        details.Add($"{label}：序号必须为正整数，实际为 {entry.Index}。");
      else if (!seenIndexes.Add(entry.Index))
        details.Add($"{label}：序号 {entry.Index} 在清单内重复。");

      if (string.IsNullOrWhiteSpace(entry.SpellCardName))
        details.Add($"{label}：符卡名为空（发布标题需要）。");

      var fileName = entry.FileName ?? string.Empty;
      if (string.IsNullOrWhiteSpace(fileName))
      {
        details.Add($"{label}：文件名为空。");
        continue;
      }

      // 必须是集根目录下的纯 GIF 文件名（拒绝路径分隔符与上级目录引用）
      if (
        !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
        || fileName.IndexOfAny(PathSeparators) >= 0
      )
      {
        details.Add($"{label}：文件名“{fileName}”必须是集根目录下的纯文件名。");
        continue;
      }

      if (!fileName.EndsWith(GifExtension, StringComparison.OrdinalIgnoreCase))
        details.Add($"{label}：文件名“{fileName}”不是 {GifExtension} 文件。");

      if (!declaredFiles.Add(fileName))
      {
        details.Add($"{label}：文件名“{fileName}”被清单内多个条目引用。");
        continue;
      }

      if (!File.Exists(Path.Combine(rootPath, fileName)))
        details.Add($"{label}：清单声明的文件在集目录内不存在：{fileName}");
    }

    foreach (var actual in EnumerateGifFiles(rootPath))
    {
      if (!declaredFiles.Contains(Path.GetFileName(actual)))
        details.Add($"集目录内的 {Path.GetFileName(actual)} 未在清单中声明。");
    }

    if (details.Count > 0)
    {
      _log.Warn(
        $"GifSetService.ValidateSet: '{rootPath}' failed self-consistency check, "
          + $"{details.Count} issue(s)."
      );
      return GifSetImportResult.Error("GIF 集校验未通过，未导入。", details);
    }

    return null;
  }

  /// <summary>
  /// 注册集记录；同一路径重复导入时原地更新，避免产生重复条目。
  /// </summary>
  /// <param name="setId">预生成的集 Id（压缩包导入用；文件夹导入用目录名作展示回退）。</param>
  /// <param name="rootPath">集根目录绝对路径。</param>
  /// <param name="manifest">已校验通过的清单。</param>
  /// <returns>注册后的集记录。</returns>
  private GifSetRecord Register(string setId, string rootPath, GifSetManifest manifest)
  {
    var sets = Sets;
    var existing = sets.FirstOrDefault(set =>
      string.Equals(set.RootPath.Value, rootPath, StringComparison.OrdinalIgnoreCase)
    );

    var record = existing ?? new GifSetRecord();
    record.Id.Value = existing?.Id.Value ?? setId;
    record.SetName.Value = string.IsNullOrWhiteSpace(manifest.SetName)
      ? new DirectoryInfo(rootPath).Name
      : manifest.SetName;
    record.RootPath.Value = rootPath;
    record.ImportedAt.Value = DateTime.Now.ToString(
      "yyyy-MM-dd HH:mm:ss",
      CultureInfo.InvariantCulture
    );
    record.EntryCount.Value = manifest.Entries.Count;
    record.Manifest = manifest;

    if (existing is null)
      sets.Add(record);

    // 首个集导入后自动选中，避免栏内出现「未选中」空态
    var activeId = _dataManager.InfoConfig.ActiveGifSetId.Value ?? string.Empty;
    if (
      string.IsNullOrEmpty(activeId)
      || !sets.Any(set => string.Equals(set.Id.Value, activeId, StringComparison.Ordinal))
    )
    {
      _dataManager.InfoConfig.ActiveGifSetId.Value = record.Id.Value;
    }

    _dataManager.TriggerAutoSave();
    return record;
  }

  /// <summary>
  /// 解析解压目录中的真实集根（压缩包若只含一层顶层目录，则取该目录）。
  /// </summary>
  /// <param name="extractedDir">解压目录。</param>
  /// <returns>集根目录绝对路径。</returns>
  private static string ResolveSetRoot(string extractedDir)
  {
    if (File.Exists(Path.Combine(extractedDir, GifSetManifest.FileName)))
      return extractedDir;

    var subDirs = Directory.GetDirectories(extractedDir);
    if (subDirs.Length == 1 && Directory.GetFiles(extractedDir).Length == 0)
      return subDirs[0];

    return extractedDir;
  }

  /// <summary>
  /// 回滚本次解压出的托管目录（仅限本服务在托管集目录下自建的目录）。
  /// </summary>
  /// <param name="extractDir">待回滚的目录。</param>
  private void RollbackExtract(string extractDir)
  {
    if (!Directory.Exists(extractDir))
      return;

    try
    {
      Directory.Delete(extractDir, recursive: true);
      _log.Print($"GifSetService.RollbackExtract: removed '{extractDir}'.");
    }
    catch (Exception ex)
    {
      _log.Err($"GifSetService.RollbackExtract: failed to remove '{extractDir}' - {ex.Message}");
    }
  }

  /// <summary>枚举集根目录下第一层的 <c>.gif</c> 文件（不递归子目录）。</summary>
  /// <param name="rootPath">集根目录。</param>
  /// <returns>GIF 文件的完整路径序列。</returns>
  private static IEnumerable<string> EnumerateGifFiles(string rootPath)
  {
    foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.TopDirectoryOnly))
    {
      if (file.EndsWith(GifExtension, StringComparison.OrdinalIgnoreCase))
        yield return file;
    }
  }

  /// <summary>把路径转为绝对路径；无效应返回原值，避免在校验前抛异常。</summary>
  /// <param name="path">输入路径。</param>
  /// <returns>绝对路径。</returns>
  private static string SafeGetFullPath(string path)
  {
    try
    {
      return Path.GetFullPath(path);
    }
    catch (Exception)
    {
      return path;
    }
  }
}
