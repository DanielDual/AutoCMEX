namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;
using CsvHelper;
using CsvHelper.Configuration;

/// <summary>对应表的一行：符卡名 + Creator + 是否非符。</summary>
public readonly record struct SpellCardMappingRow(string Name, string Creator, bool IsNonSpell);

/// <summary>
/// 导出「符卡—创作者对应表」为猜测模块可导入的三列 CSV（Boss、符卡名、创作者）。
/// 输出与现有 <c>CsvImporter.ImportSpellCardTable</c> 兼容，用户可在猜测模块用现有导入器导入。
/// </summary>
public class SpellCardMappingExporter
{
  private static readonly string DefaultNonSpellName = "非符";

  private readonly ILog _log;

  public SpellCardMappingExporter() =>
    _log = AppLogs.GetOrCreate().GetLogger(nameof(SpellCardMappingExporter));

  /// <summary>
  /// 导出对应表到 CSV 文件。
  /// </summary>
  /// <param name="bossName">完整项目包中的共通 Boss 名。</param>
  /// <param name="rows">按映射顺序排列的对应表行。</param>
  /// <param name="outputPath">输出 CSV 文件路径。</param>
  /// <param name="nonSpellName">非符在导出表中的表示（默认"非符"）。</param>
  /// <returns>生成的 CSV 文件内容（用于往返校验）；写入失败时返回空串。</returns>
  public string Export(
    string bossName,
    IReadOnlyList<SpellCardMappingRow> rows,
    string outputPath
  ) => Export(bossName, rows, outputPath, DefaultNonSpellName);

  /// <summary>
  /// 导出对应表到 CSV 文件（可指定非符表示名）。
  /// </summary>
  /// <param name="bossName">完整项目包中的共通 Boss 名。</param>
  /// <param name="rows">按映射顺序排列的对应表行。</param>
  /// <param name="outputPath">输出 CSV 文件路径。</param>
  /// <param name="nonSpellName">非符在导出表中的表示（默认"非符"）。</param>
  /// <returns>生成的 CSV 文件内容（用于往返校验）。</returns>
  public string Export(
    string bossName,
    IReadOnlyList<SpellCardMappingRow> rows,
    string outputPath,
    string nonSpellName
  )
  {
    if (string.IsNullOrWhiteSpace(bossName))
      throw new ArgumentException("Boss 名不能为空", nameof(bossName));
    if (rows == null || rows.Count == 0)
      throw new ArgumentException("对应表至少需要一行", nameof(rows));

    var outputDir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
      Directory.CreateDirectory(outputDir);

    // 先生成内容，再一次性写文件，避免句柄占用时读取
    using var textWriter = new StringWriter();
    using (
      var csv = new CsvWriter(
        textWriter,
        new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true }
      )
    )
    {
      csv.WriteField("Boss");
      csv.WriteField("符卡名");
      csv.WriteField("创作者");
      csv.NextRecord();

      foreach (var row in rows)
      {
        csv.WriteField(bossName);
        csv.WriteField(row.IsNonSpell ? nonSpellName : row.Name);
        csv.WriteField(row.Creator);
        csv.NextRecord();
      }
    }

    var content = textWriter.ToString();
    File.WriteAllText(outputPath, content);

    _log.Print($"SpellCardMappingExporter: exported {rows.Count} rows to {outputPath}.");
    return content;
  }
}
