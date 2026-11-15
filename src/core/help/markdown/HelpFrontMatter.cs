namespace AutoCMEX.Core.Help.Markdown;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// 帮助页正文的 front matter（固定六字段）解析与校验。
/// </summary>
/// <remarks>
/// 六字段为 <c>id</c> / <c>title</c> / <c>section</c> / <c>order</c> / <c>summary</c> / <c>tags</c>。
/// 缺失、未知、重复、类型不符都会抛 <see cref="HelpFormatException"/> 并带行号；
/// 解析成功后通过 <c>bodyStartIndex</c> 告诉调用方正文字段从哪里开始。
/// </remarks>
public sealed partial class HelpFrontMatter
{
  /// <summary>允许的 front matter 字段，顺序即错误提示中的展示顺序。</summary>
  public static readonly IReadOnlyList<string> Keys = new[]
  {
    "id",
    "title",
    "section",
    "order",
    "summary",
    "tags",
  };

  private const string Delimiter = "---";

  /// <summary>编译期生成的正则：页面 id 的字符与层级约束。</summary>
  [GeneratedRegex(
    "^[a-z0-9]([a-z0-9-]*[a-z0-9])?(/[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$",
    RegexOptions.CultureInvariant
  )]
  private static partial Regex IdPattern();

  private HelpFrontMatter(
    string id,
    string title,
    string section,
    int order,
    string summary,
    IReadOnlyList<string> tags,
    int idLine
  )
  {
    Id = id;
    Title = title;
    Section = section;
    Order = order;
    Summary = summary;
    Tags = tags;
    IdLine = idLine;
  }

  /// <summary>页面唯一键（与相对路径一致，由装配器另行校验）。</summary>
  public string Id { get; }

  /// <summary>导航与顶栏标题。</summary>
  public string Title { get; }

  /// <summary>分区名。</summary>
  public string Section { get; }

  /// <summary>分区内序号（从 1 起）。</summary>
  public int Order { get; }

  /// <summary>搜索摘要。</summary>
  public string Summary { get; }

  /// <summary>搜索加权标签。</summary>
  public IReadOnlyList<string> Tags { get; }

  /// <summary><c>id</c> 字段所在行号（1 起），用于「id 与路径不一致」的报错定位。</summary>
  public int IdLine { get; }

  /// <summary>
  /// 解析整份文件开头的 front matter。
  /// </summary>
  /// <param name="lines">整份文件按行拆分的结果（<c>\r</c> 已由拆分器去掉）。</param>
  /// <param name="bodyStartIndex">输出：正文第一行的 0 起下标。</param>
  /// <returns>解析出的元数据。</returns>
  /// <exception cref="HelpFormatException">缺少分隔符、未闭合、字段非法或缺失时抛出。</exception>
  public static HelpFrontMatter Parse(IReadOnlyList<string> lines, out int bodyStartIndex)
  {
    if (lines.Count == 0 || lines[0].Trim() != Delimiter)
    {
      throw new HelpFormatException(1, "缺少 front matter：文件第一行必须是 ---");
    }

    int close = -1;
    for (int i = 1; i < lines.Count; i++)
    {
      if (lines[i].Trim() == Delimiter)
      {
        close = i;
        break;
      }
    }
    if (close < 0)
    {
      throw new HelpFormatException(1, "front matter 未闭合：缺少结束的 ---");
    }

    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    var keyLines = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int i = 1; i < close; i++)
    {
      string text = lines[i].Trim();
      if (text.Length == 0)
      {
        continue;
      }
      int colon = text.IndexOf(':');
      if (colon <= 0)
      {
        throw new HelpFormatException(i + 1, $"front matter 行格式应为 key: value；实际：{text}");
      }
      string key = text[..colon].Trim();
      string value = text[(colon + 1)..].Trim();
      if (!IsKnownKey(key))
      {
        throw new HelpFormatException(
          i + 1,
          $"未知的 front matter 字段：{key}（允许：id、title、section、order、summary、tags）"
        );
      }
      if (values.ContainsKey(key))
      {
        throw new HelpFormatException(i + 1, $"front matter 字段重复：{key}");
      }
      values[key] = value;
      keyLines[key] = i + 1;
    }

    foreach (var key in Keys)
    {
      if (!values.ContainsKey(key))
      {
        throw new HelpFormatException(1, $"front matter 缺少字段：{key}");
      }
    }

    string id = values["id"];
    if (id.Length == 0)
    {
      throw new HelpFormatException(keyLines["id"], "front matter 字段 id 不能为空");
    }
    if (!IdPattern().IsMatch(id))
    {
      throw new HelpFormatException(
        keyLines["id"],
        $"id 只能由小写字母、数字、连字符组成，并用 / 分隔分区（如 merge/export）；实际：{id}"
      );
    }

    string title = values["title"];
    if (title.Length == 0)
    {
      throw new HelpFormatException(keyLines["title"], "front matter 字段 title 不能为空");
    }

    string section = values["section"];
    if (section.Length == 0)
    {
      throw new HelpFormatException(keyLines["section"], "front matter 字段 section 不能为空");
    }

    string summary = values["summary"];
    if (summary.Length == 0)
    {
      throw new HelpFormatException(keyLines["summary"], "front matter 字段 summary 不能为空");
    }

    if (
      !int.TryParse(
        values["order"],
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int order
      )
      || order < 1
    )
    {
      throw new HelpFormatException(
        keyLines["order"],
        $"order 必须是大于 0 的整数；实际：{values["order"]}"
      );
    }

    var tags = ParseTags(values["tags"], keyLines["tags"]);
    bodyStartIndex = close + 1;
    return new HelpFrontMatter(id, title, section, order, summary, tags, keyLines["id"]);
  }

  /// <summary>判断字段名是否在允许的六个字段内。</summary>
  /// <param name="key">字段名。</param>
  /// <returns>允许返回 true。</returns>
  private static bool IsKnownKey(string key)
  {
    foreach (var known in Keys)
    {
      if (string.Equals(known, key, StringComparison.Ordinal))
      {
        return true;
      }
    }
    return false;
  }

  /// <summary>解析 <c>tags: [a, b]</c> 形式；空数组允许。</summary>
  /// <param name="value">字段原始值。</param>
  /// <param name="line">字段所在行号。</param>
  /// <returns>去空白后的标签列表。</returns>
  private static IReadOnlyList<string> ParseTags(string value, int line)
  {
    string text = value.Trim();
    if (text.Length < 2 || text[0] != '[' || text[^1] != ']')
    {
      throw new HelpFormatException(line, $"tags 必须是 [a, b] 形式；实际：{value}");
    }
    string inner = text[1..^1].Trim();
    if (inner.Length == 0)
    {
      return Array.Empty<string>();
    }
    var tags = new List<string>();
    foreach (var part in inner.Split(','))
    {
      string tag = part.Trim();
      if (tag.Length == 0)
      {
        throw new HelpFormatException(line, "tags 中存在空标签");
      }
      tags.Add(tag);
    }
    return tags;
  }
}
