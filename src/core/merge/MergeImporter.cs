namespace AutoCMEX.Core.Merge;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AutoCMEX.Models;

/// <summary>
/// 一次创作者包导入的结果：包元数据 + 其符卡对应表条目（供合并前编辑）。
/// </summary>
public sealed class CreatorImportResult
{
  /// <summary>导入的创作者包元数据（已写入持久化前）。</summary>
  public CreatorPackage? Package { get; init; }

  /// <summary>该包内抽取出的符卡对应表条目（顺序即检测顺序）。</summary>
  public System.Collections.Generic.List<SpellCardMappingEntry> Cards { get; init; } = new();

  /// <summary>失败描述；成功为 null。</summary>
  public string? Error { get; init; }

  /// <summary>是否成功。</summary>
  public bool IsSuccess => Error == null && Package != null;
}

/// <summary>
/// 解包创作者 zip，定位其中的 .lstges 工程文件并解析，生成包元数据与符卡条目。
/// 创作者名从包名推导（可由用户编辑）。
/// </summary>
public static class MergeImporter
{
  /// <summary>
  /// 从 zip 导入创作者包。
  /// </summary>
  /// <param name="zipPath">创作者包 zip 路径。</param>
  public static CreatorImportResult ImportZip(string zipPath)
  {
    if (!File.Exists(zipPath))
      return new CreatorImportResult { Error = $"zip 不存在：{zipPath}" };

    var tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Import_" + Guid.NewGuid().ToString("N")[..8]
    );
    try
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
      ZipFile.ExtractToDirectory(zipPath, tempDir);

      var packageName = Path.GetFileNameWithoutExtension(zipPath);
      var doc = LoadPackageDoc(tempDir);
      if (doc == null)
        return new CreatorImportResult { Error = "zip 内未找到可解析的 .lstges 工程文件" };

      var cards = SpellCardExtractor
        .Extract(doc)
        .Select(
          (card, idx) =>
            new SpellCardMappingEntry
            {
              Name = card.Name,
              IsNonSpell = new(card.IsNonSpell),
              Creator = new(packageName),
              PackageName = packageName,
              SourceCardIndex = idx,
            }
        )
        .ToList();

      var pkg = new CreatorPackage
      {
        PackageName = packageName,
        CreatorName = new(packageName),
        SourcePath = new(zipPath),
        IsDeleted = new(false),
      };

      return new CreatorImportResult { Package = pkg, Cards = cards };
    }
    catch (Exception ex)
    {
      return new CreatorImportResult { Error = $"导入失败：{ex.Message}" };
    }
    finally
    {
      try
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
      catch
      { /* 忽略清理错误 */
      }
    }
  }

  /// <summary>
  /// 在解包目录中定位并解析 .lstges。
  /// </summary>
  private static LstgesDocument? LoadPackageDoc(string dir)
  {
    var file = Directory
      .EnumerateFiles(dir, "*.lstges", SearchOption.AllDirectories)
      .FirstOrDefault();
    if (file == null)
      return null;
    return LstgesParser.LoadFile(file, out var error) ?? null;
  }
}
