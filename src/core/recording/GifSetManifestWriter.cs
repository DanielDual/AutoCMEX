namespace AutoCMEX.Core.Recording;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoCMEX.Models;

/// <summary>
/// 把一轮录制的报告写成 GIF 集清单（<c>manifest.json</c>），使自动录制的产物与手工整理的集同构，
/// 从而直接被 <c>GifSetService</c> 的导入链路消费。
/// </summary>
/// <remarks>
/// <para>
/// 只收<b>成功落盘</b>的卡：失败与未录制的卡没有产物，写进清单会立刻被集内自洽校验判为
/// 「清单声明的文件在集目录内不存在」。序号因此允许有洞——校验只要求序号为正且唯一，不要求连续。
/// </para>
/// <para>
/// 键名必须写 camelCase：<see cref="GifSetManifest"/> 的属性是 PascalCase，读侧大小写不敏感，
/// 所以「自己写、自己读」用哪种都过得去；但清单契约与文档示例都是 camelCase，也供外部工具读取，
/// 故写盘必须显式指定，不能依赖默认命名策略。
/// </para>
/// </remarks>
public static class GifSetManifestWriter
{
  /// <summary>写盘用序列化选项：camelCase 键名 + 不转义非 ASCII（中文符卡名保持可读）。</summary>
  private static readonly JsonSerializerOptions _writeOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  /// <summary>按报告组装清单。</summary>
  /// <param name="report">本轮报告。</param>
  /// <returns>清单；本轮没有任何成功落盘的卡时为 <c>null</c>。</returns>
  /// <exception cref="ArgumentNullException"><paramref name="report"/> 为 <c>null</c>。</exception>
  public static GifSetManifest? Build(RecordingReport report)
  {
    ArgumentNullException.ThrowIfNull(report);

    var entries = new List<GifSetEntry>();
    foreach (var card in report.Cards)
    {
      if (card.Status != RecordingCardStatus.Ok)
      {
        continue;
      }

      entries.Add(
        new GifSetEntry
        {
          Index = card.CombatOrdinal,
          SpellCardName = card.EntryName,
          FileName = card.FileName,
          Width = card.Width,
          Height = card.Height,
        }
      );
    }

    if (entries.Count == 0)
    {
      return null;
    }

    return new GifSetManifest
    {
      SetName = report.ModPackName,
      BossLabel = report.BossName ?? string.Empty,
      GeneratedAt = FormatGeneratedAt(report.GeneratedAt),
      Entries = entries,
    };
  }

  /// <summary>按报告组装清单并写进输出目录。</summary>
  /// <param name="outputDir">GIF 集输出目录（不存在时创建）。</param>
  /// <param name="report">本轮报告。</param>
  /// <returns>清单的绝对路径；本轮没有任何成功落盘的卡时不落盘并返回 <c>null</c>。</returns>
  /// <exception cref="IOException">写盘失败。</exception>
  /// <exception cref="UnauthorizedAccessException">输出目录不可写。</exception>
  public static string? Write(string outputDir, RecordingReport report)
  {
    var manifest = Build(report);
    if (manifest is null)
    {
      return null;
    }

    Directory.CreateDirectory(outputDir);
    var path = Path.Combine(outputDir, GifSetManifest.FileName);
    File.WriteAllText(path, JsonSerializer.Serialize(manifest, _writeOptions));
    return path;
  }

  /// <summary>
  /// 把报告的 ISO 8601 起始时间转成清单约定的 <c>yyyy-MM-dd HH:mm:ss</c>。
  /// </summary>
  /// <param name="generatedAt">报告里的本轮起始时间。</param>
  /// <returns>格式化后的时间；无法解析时原样返回（不编造时间）。</returns>
  private static string FormatGeneratedAt(string generatedAt) =>
    DateTime.TryParse(
      generatedAt,
      CultureInfo.InvariantCulture,
      DateTimeStyles.RoundtripKind,
      out var parsed
    )
      ? parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
      : generatedAt;
}
