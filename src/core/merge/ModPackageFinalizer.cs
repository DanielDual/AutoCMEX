namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// mod zip 后处理结果。
/// </summary>
public sealed class ModPackageFinalizationResult
{
  /// <summary>是否成功（失败时原 zip 已按 fail-closed 改名备份）。</summary>
  public bool IsSuccess { get; init; }

  /// <summary>是否实际做了后处理（混淆关闭且保留工程文件时为 false：无操作直接成功）。</summary>
  public bool Processed { get; init; }

  /// <summary>被成功混淆替换的 <c>.lua</c> 条目数。</summary>
  public int ObfuscatedCount { get; init; }

  /// <summary>是否按 <c>IncludeLstges=false</c> 移除了 <c>&lt;输出名&gt;.lstgproj</c> 条目。</summary>
  public bool RemovedProjectFile { get; init; }

  /// <summary>失败原因（含失败条目与工具原因）；成功时为 <c>null</c>。</summary>
  public string? Error { get; init; }

  /// <summary>fail-closed 时 Sharp 原始 zip 的备份路径（<c>&lt;输出名&gt;.zip.unobfuscated</c>）；否则为 <c>null</c>。</summary>
  public string? BackupPath { get; init; }

  /// <summary>非致命警告（如未找到 .lua 条目、未找到工程文件条目）。</summary>
  public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Sharp 打包产物的 zip 后处理：在内存中逐条目重建 mod zip。
/// <list type="bullet">
/// <item><c>.lua</c> 条目：开启混淆时逐条替换为混淆后源码，条目名与顺序不变；</item>
/// <item><c>&lt;输出名&gt;.lstgproj</c> 条目：<b>只由「导出包含 .lstges 工程文件」决定</b>——
/// <c>includeLstges=true</c> 保留、<c>false</c> 删除，混淆开关不参与判断；</item>
/// <item>其余条目：按字节原样复制，保留 <c>FullName</c>（含反斜杠）与条目顺序。</item>
/// </list>
/// <para>
/// 全部条目处理成功才替换原 zip；任一失败即 fail-closed：不交付半混淆产物，把 Sharp 原始 zip
/// 改名为 <c>&lt;输出名&gt;.zip.unobfuscated</c>，并返回含失败文件与原因的错误。
/// 混淆关闭且保留工程文件时不做任何后处理（保持现状）。
/// </para>
/// </summary>
public sealed class ModPackageFinalizer
{
  private const string LuaExtension = ".lua";
  private const string ProjectExtension = ".lstgproj";
  private const string BackupExtension = ".unobfuscated";

  private readonly ILuaObfuscator _obfuscator;
  private readonly ILog _log;

  /// <summary>
  /// 创建 mod zip 后处理器。
  /// </summary>
  /// <param name="obfuscator">Lua 混淆器（生产用 <see cref="PrometheusLuaObfuscator"/>，测试可注入 fake）。</param>
  /// <param name="log">日志；为 <c>null</c> 时使用 <see cref="NullLog.Instance"/>。</param>
  public ModPackageFinalizer(ILuaObfuscator obfuscator, ILog? log = null)
  {
    _obfuscator = obfuscator ?? throw new ArgumentNullException(nameof(obfuscator));
    _log = log ?? NullLog.Instance;
  }

  /// <summary>
  /// 对 Sharp 产出的 mod zip 做后处理（混淆 .lua + 按选项控制工程文件条目）。
  /// </summary>
  /// <param name="zipPath">Sharp 产出的 <c>&lt;OutputDir&gt;/&lt;OutputName&gt;.zip</c> 完整路径。</param>
  /// <param name="obfuscateLua">是否混淆 <c>.lua</c> 条目。</param>
  /// <param name="includeLstges">是否保留 <c>&lt;OutputName&gt;.lstgproj</c> 条目（只看该开关）。</param>
  /// <returns>后处理结果；失败时 <see cref="ModPackageFinalizationResult.IsSuccess"/> 为 <c>false</c>。</returns>
  public ModPackageFinalizationResult Finalize(
    string zipPath,
    bool obfuscateLua,
    bool includeLstges
  )
  {
    if (string.IsNullOrWhiteSpace(zipPath))
      return Failure("mod zip 路径为空", processed: false, warnings: Array.Empty<string>());
    if (!File.Exists(zipPath))
      return Failure(
        $"未找到 mod zip：{zipPath}",
        processed: false,
        warnings: Array.Empty<string>()
      );

    bool needRemoveProject = !includeLstges;
    if (!obfuscateLua && !needRemoveProject)
    {
      _log.Print("ModPackageFinalizer: 混淆关闭且保留工程文件，跳过后处理。");
      return new ModPackageFinalizationResult { IsSuccess = true, Processed = false };
    }

    var outputName = Path.GetFileNameWithoutExtension(zipPath);
    var projectEntryName = outputName + ProjectExtension;
    var warnings = new List<string>();
    var tempPath = zipPath + ".tmp";
    var obfuscatedCount = 0;
    var removedProjectFile = false;

    try
    {
      if (File.Exists(tempPath))
        File.Delete(tempPath);

      using (var source = ZipFile.OpenRead(zipPath))
      {
        using var targetStream = new FileStream(
          tempPath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None
        );
        using var target = new ZipArchive(targetStream, ZipArchiveMode.Create);

        foreach (var entry in source.Entries)
        {
          if (IsProjectEntry(entry.FullName, projectEntryName))
          {
            if (needRemoveProject)
            {
              removedProjectFile = true;
              continue;
            }
            CopyEntry(entry, target, replacement: null);
            continue;
          }

          if (
            obfuscateLua
            && entry.FullName.EndsWith(LuaExtension, StringComparison.OrdinalIgnoreCase)
          )
          {
            var sourceBytes = ReadEntryBytes(entry);
            var obfuscated = _obfuscator.Obfuscate(sourceBytes, entry.FullName);
            if (!obfuscated.IsSuccess || obfuscated.Output == null)
            {
              throw new ModPackageFinalizeException(
                $"混淆失败：{entry.FullName}：{obfuscated.Error ?? "未知错误"}"
              );
            }
            CopyEntry(entry, target, replacement: obfuscated.Output);
            obfuscatedCount++;
            continue;
          }

          CopyEntry(entry, target, replacement: null);
        }
      }

      if (needRemoveProject && !removedProjectFile)
      {
        warnings.Add(
          $"未在 mod zip 中找到工程文件条目 {projectEntryName}（可能 Sharp 未把工程文件打包进 zip）"
        );
      }
      if (obfuscateLua && obfuscatedCount == 0)
        warnings.Add("mod zip 中没有找到 .lua 条目，未混淆任何脚本");

      // 全部条目成功：替换原 zip（同目录临时文件，减少跨卷移动风险）。
      File.Move(tempPath, zipPath, overwrite: true);
      _log.Print(
        $"ModPackageFinalizer: rebuilt {zipPath} (obfuscated={obfuscatedCount}, "
          + $"removedProject={removedProjectFile})."
      );
      return new ModPackageFinalizationResult
      {
        IsSuccess = true,
        Processed = true,
        ObfuscatedCount = obfuscatedCount,
        RemovedProjectFile = removedProjectFile,
        Warnings = warnings,
      };
    }
    catch (Exception ex)
    {
      TryDeleteFile(tempPath);
      var error =
        ex is ModPackageFinalizeException ? ex.Message : $"mod zip 后处理失败：{ex.Message}";
      var backupPath = BackupOriginalZip(zipPath, ref error);
      _log.Warn($"ModPackageFinalizer: {error}");
      return new ModPackageFinalizationResult
      {
        IsSuccess = false,
        Processed = true,
        ObfuscatedCount = obfuscatedCount,
        RemovedProjectFile = removedProjectFile,
        Error = error,
        BackupPath = backupPath,
        Warnings = warnings,
      };
    }
  }

  /// <summary>判断条目是否为 <c>&lt;输出名&gt;.lstgproj</c>（zip 条目可能用反斜杠，归一后比较）。</summary>
  private static bool IsProjectEntry(string fullName, string projectEntryName)
  {
    var normalized = fullName.Replace('\\', '/').TrimStart('/');
    return string.Equals(normalized, projectEntryName, StringComparison.OrdinalIgnoreCase)
      || normalized.EndsWith("/" + projectEntryName, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>复制单条目：保留 FullName / 外部属性 / 时间戳；给了替换字节则替换内容，否则按字节原样复制。</summary>
  private static void CopyEntry(ZipArchiveEntry source, ZipArchive target, byte[]? replacement)
  {
    var created = target.CreateEntry(source.FullName, CompressionLevel.Optimal);
    try
    {
      created.ExternalAttributes = source.ExternalAttributes;
    }
    catch
    { /* 个别条目属性不可写时忽略，不影响内容 */
    }
    try
    {
      created.LastWriteTime = source.LastWriteTime;
    }
    catch
    { /* 时间戳越界（如 1980 年前）时保留新条目默认值 */
    }

    using var output = created.Open();
    if (replacement != null)
    {
      output.Write(replacement, 0, replacement.Length);
      return;
    }
    using var input = source.Open();
    input.CopyTo(output);
  }

  /// <summary>读取 zip 条目原始字节（内存中完成，不解压整个 zip 到磁盘）。</summary>
  private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
  {
    using var input = entry.Open();
    using var memory = new MemoryStream();
    input.CopyTo(memory);
    return memory.ToArray();
  }

  /// <summary>
  /// fail-closed 收尾：把 Sharp 原始 zip 改名为 <c>&lt;输出名&gt;.zip.unobfuscated</c>，不留预期文件名成品。
  /// </summary>
  private static string? BackupOriginalZip(string zipPath, ref string error)
  {
    try
    {
      var backupPath = zipPath + BackupExtension;
      if (File.Exists(backupPath))
        File.Delete(backupPath);
      File.Move(zipPath, backupPath);
      return backupPath;
    }
    catch (Exception ex)
    {
      error += $"（原 zip 备份改名失败：{ex.Message}；原 zip 仍是未混淆版本，仍在 {zipPath}）";
      return null;
    }
  }

  private static void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch
    { /* 临时文件清理失败不影响 fail-closed 结论 */
    }
  }

  private static ModPackageFinalizationResult Failure(
    string error,
    bool processed,
    IReadOnlyList<string> warnings
  ) =>
    new()
    {
      IsSuccess = false,
      Processed = processed,
      Error = error,
      Warnings = warnings,
    };

  /// <summary>后处理中可预期的失败（带面向用户的原因），用于与意外异常区分。</summary>
  private sealed class ModPackageFinalizeException : Exception
  {
    public ModPackageFinalizeException(string message)
      : base(message) { }
  }
}
