namespace AutoCMEX.Models;

using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 符卡 GIF 集清单（<c>manifest.json</c>）：导入校验与发布取名的唯一依据。
/// </summary>
/// <remarks>
/// <para>
/// 清单是 GIF 集自身携带的元数据，<b>与猜测板块的 Boss 无关</b>：序号、符卡名、文件名、
/// 尺寸全部以清单为准，不查询 <c>SpellCard</c> 表。清单由录制/整理 GIF 集的一方产出。
/// </para>
/// <para>
/// 文件名固定为 <see cref="FileName"/>，位于集根目录。键名使用 camelCase
/// （<c>setName</c> / <c>bossLabel</c> / <c>generatedAt</c> / <c>entries</c>，条目为
/// <c>index</c> / <c>spellCardName</c> / <c>fileName</c> / <c>width</c> / <c>height</c>）；
/// 读取时大小写不敏感、允许注释与尾随逗号，以容忍外部工具产出的细微差异。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// {
///   "setName": "示例集",
///   "bossLabel": "SampleBoss",
///   "generatedAt": "2026-09-25 12:00:00",
///   "entries": [
///     { "index": 1, "spellCardName": "非符", "fileName": "1.gif", "width": 640, "height": 480 }
///   ]
/// }
/// </code>
/// </example>
public class GifSetManifest
{
  /// <summary>清单文件名（位于集根目录）。</summary>
  public const string FileName = "manifest.json";

  /// <summary>集名；为空时由导入方回退为集目录名。</summary>
  public string SetName { get; set; } = string.Empty;

  /// <summary>集自述来源（如工程名或 Boss 名），仅用于展示，<b>不参与任何对齐校验</b>。</summary>
  public string BossLabel { get; set; } = string.Empty;

  /// <summary>清单生成时间（原样展示，不解析）。</summary>
  public string GeneratedAt { get; set; } = string.Empty;

  /// <summary>逐张符卡 GIF 的条目；序号在集内唯一。</summary>
  public List<GifSetEntry> Entries { get; set; } = new();

  /// <summary>
  /// 创建读取清单用的 JSON 选项（大小写不敏感、允许注释与尾随逗号）。
  /// </summary>
  /// <returns>读取清单专用的 <see cref="JsonSerializerOptions"/>。</returns>
  public static JsonSerializerOptions CreateReadOptions() =>
    new()
    {
      PropertyNameCaseInsensitive = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
    };
}

/// <summary>
/// 清单中的单张符卡 GIF 条目。
/// </summary>
public class GifSetEntry
{
  /// <summary>序号（集内唯一；发布标题「序号. 符卡名」的序号来源）。</summary>
  public int Index { get; set; }

  /// <summary>符卡名（发布标题用；来自清单，不来自猜测表）。</summary>
  public string SpellCardName { get; set; } = string.Empty;

  /// <summary>GIF 文件名（相对集根目录；必须与磁盘上文件一致）。</summary>
  public string FileName { get; set; } = string.Empty;

  /// <summary>GIF 像素宽（展示用；不参与校验）。</summary>
  public int Width { get; set; }

  /// <summary>GIF 像素高（展示用；不参与校验）。</summary>
  public int Height { get; set; }
}
