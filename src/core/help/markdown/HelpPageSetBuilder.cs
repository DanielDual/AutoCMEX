namespace AutoCMEX.Core.Help.Markdown;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 页面集合装配器：扫描 <c>docs/help/</c>、校验元数据与链接、逐页转换，并渲染成 C# 生成文件。
/// </summary>
/// <remarks>
/// <para>
/// 校验项：id 与路径一致、分区属于 <see cref="SectionOrder"/>、分区内 order 从 1 起连续且不重复、
/// 站内链接目标存在、正文不出现被拒绝语法。任一失败都只记录 <see cref="HelpDiagnostic"/>，不抛异常，
/// 以便一次把所有问题都报出来（生成器据此非零退出、不写产物）。
/// </para>
/// <para>
/// 分区顺序在这里定义，是生成期与运行时的唯一来源（运行时经 <c>HelpContent.SectionOrder</c> 读取）。
/// </para>
/// </remarks>
public sealed class HelpPageSetBuilder
{
  /// <summary>导航分区顺序（与页面 front matter 的 section 取值一致）。</summary>
  public static readonly IReadOnlyList<string> SectionOrder = new[]
  {
    "开始",
    "整合",
    "猜测",
    "信息",
    "录制",
    "设置",
    "运行与排障",
  };

  private readonly Func<string, bool> _imageExists;

  /// <summary>字号换算。</summary>
  private readonly HelpTypography _typography;

  /// <summary>构造装配器。</summary>
  /// <param name="imageExists">图片存在性判定（参数相对 <c>src/assets/help</c>）。</param>
  /// <param name="typography">字号换算；缺省为缩放 1.0（生成器按项目主题缩放传入）。</param>
  public HelpPageSetBuilder(Func<string, bool> imageExists, HelpTypography? typography = null)
  {
    _imageExists = imageExists;
    _typography = typography ?? HelpTypography.Default;
  }

  /// <summary>
  /// 从磁盘装配全部页面。
  /// </summary>
  /// <param name="docsRoot">帮助内容根目录（仓库内为 <c>docs/help</c>）。</param>
  /// <returns>装配结果：页面集合、诊断与缺失图片数。</returns>
  public HelpPageSetBuildResult Build(string docsRoot)
  {
    var errors = new List<HelpDiagnostic>();
    if (!Directory.Exists(docsRoot))
    {
      errors.Add(new HelpDiagnostic(docsRoot, 0, "帮助内容目录不存在"));
      return new HelpPageSetBuildResult(Array.Empty<ConvertedPage>(), errors, 0);
    }

    var files = new List<string>();
    foreach (var path in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories))
    {
      string relative = Path.GetRelativePath(docsRoot, path).Replace('\\', '/');
      string[] segments = relative.Split('/');
      if (segments.Length != 2)
      {
        continue;
      }
      if (segments[0].StartsWith('_') || segments[1].StartsWith('_'))
      {
        continue;
      }
      files.Add(path);
    }
    files.Sort(StringComparer.Ordinal);
    if (files.Count == 0)
    {
      errors.Add(
        new HelpDiagnostic(docsRoot, 0, "未找到任何帮助页面（期望 docs/help/<分区>/<页面>.md）")
      );
      return new HelpPageSetBuildResult(Array.Empty<ConvertedPage>(), errors, 0);
    }

    var parsed = new List<ParsedFile>();
    var seenIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var path in files)
    {
      string relative = Path.GetRelativePath(docsRoot, path).Replace('\\', '/');
      byte[] bytes;
      try
      {
        bytes = File.ReadAllBytes(path);
      }
      catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
      {
        errors.Add(new HelpDiagnostic(relative, 0, "读取失败：" + exception.Message));
        continue;
      }

      string text = Decode(bytes);
      int delimiterIndex = text.IndexOf(HelpPlaceholders.Delimiter);
      if (delimiterIndex >= 0)
      {
        int delimiterLine = 1;
        for (int i = 0; i < delimiterIndex; i++)
        {
          if (text[i] == '\n')
            delimiterLine++;
        }
        errors.Add(
          new HelpDiagnostic(
            relative,
            delimiterLine,
            "正文包含保留控制字符 U+0001（运行时用作颜色占位符的定界符，正文禁止出现）"
          )
        );
        continue;
      }

      string expectedId = relative[..^3];
      string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
      try
      {
        var lines = HelpMarkdownConverter.SplitLines(text);
        var front = HelpFrontMatter.Parse(lines, out _);
        if (!string.Equals(front.Id, expectedId, StringComparison.Ordinal))
        {
          errors.Add(
            new HelpDiagnostic(
              relative,
              front.IdLine,
              $"front matter id 与文件路径不一致：id={front.Id}，按路径应为 {expectedId}"
            )
          );
          continue;
        }
        if (!ContainsSection(front.Section))
        {
          errors.Add(
            new HelpDiagnostic(
              relative,
              front.IdLine,
              $"未知分区：{front.Section}（允许：{string.Join('、', SectionOrder)}）"
            )
          );
          continue;
        }
        if (!seenIds.Add(front.Id))
        {
          errors.Add(new HelpDiagnostic(relative, front.IdLine, $"页面 id 重复：{front.Id}"));
          continue;
        }
        parsed.Add(new ParsedFile(relative, hash, text, front));
      }
      catch (HelpFormatException exception)
      {
        errors.Add(new HelpDiagnostic(relative, exception.Line, exception.Message));
      }
    }

    CheckSectionOrders(parsed, errors);

    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var page in parsed)
    {
      ids.Add(page.Front.Id);
    }
    var converter = new HelpMarkdownConverter(new HelpLinkResolver(ids), _imageExists, _typography);
    var pages = new List<ConvertedPage>();
    int missingImageCount = 0;
    foreach (var page in parsed)
    {
      try
      {
        var converted = converter.Convert(page.Text, page.RelativePath, page.Hash);
        missingImageCount += converted.MissingImageCount;
        pages.Add(converted);
      }
      catch (HelpFormatException exception)
      {
        errors.Add(new HelpDiagnostic(page.RelativePath, exception.Line, exception.Message));
      }
    }

    pages.Sort(
      (a, b) =>
      {
        int bySection = SectionIndexOf(a.Section).CompareTo(SectionIndexOf(b.Section));
        return bySection != 0 ? bySection : a.Order.CompareTo(b.Order);
      }
    );
    return new HelpPageSetBuildResult(pages, errors, missingImageCount);
  }

  /// <summary>
  /// 把装配结果渲染成 <c>HelpContent.Generated.cs</c> 的源码文本（确定性输出，可用于 --verify 比对）。
  /// </summary>
  /// <param name="result">装配结果（必须无诊断，否则应视为调用方错误）。</param>
  /// <returns>C# 源文件全文（LF 换行）。</returns>
  public static string RenderSource(HelpPageSetBuildResult result)
  {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("// 本文件由 tools/HelpContentGenerator 生成，请勿手工编辑。");
    sb.AppendLine("// 内容源：docs/help/**/*.md；改 md 后重新 dotnet build 即自动更新。");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("namespace AutoCMEX.Core.Help;");
    sb.AppendLine();
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine(
      "/// 帮助内容的编译期常量：页面元数据、BBCode 正文、检索纯文本与源文件 SHA-256。"
    );
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public static partial class HelpContent");
    sb.AppendLine("{");
    sb.AppendLine("  /// <summary>导航分区顺序（生成期固定）。</summary>");
    sb.AppendLine("  public static readonly string[] SectionOrder = new string[]");
    sb.AppendLine("  {");
    foreach (var section in SectionOrder)
    {
      sb.Append("    ").Append(Str(section)).AppendLine(",");
    }
    sb.AppendLine("  };");

    sb.AppendLine();
    sb.AppendLine("  /// <summary>全部帮助页面元数据，已按分区顺序与分区内 order 排序。</summary>");
    sb.AppendLine("  public static readonly HelpPage[] Pages = new HelpPage[]");
    sb.AppendLine("  {");
    foreach (var page in result.Pages)
    {
      sb.AppendLine("    new HelpPage(");
      sb.Append("      ").Append(Str(page.Id)).AppendLine(",");
      sb.Append("      ").Append(Str(page.Title)).AppendLine(",");
      sb.Append("      ").Append(Str(page.Section)).AppendLine(",");
      sb.Append("      ").Append(page.Order.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
      sb.Append("      ").Append(Str(page.Summary)).AppendLine(",");
      sb.Append("      ").Append(StrArray(page.Tags)).AppendLine(",");
      sb.Append("      ").Append(StrArray(page.Headings)).AppendLine();
      sb.AppendLine("    ),");
    }
    sb.AppendLine("  };");

    AppendDictionary(
      sb,
      "Bbcode",
      "每页 BBCode 正文（页面 id → 正文）。",
      result.Pages,
      p => p.Bbcode
    );
    AppendDictionary(
      sb,
      "Plain",
      "每页检索用纯文本（页面 id → 正文纯文本）。",
      result.Pages,
      p => p.PlainText
    );
    AppendDictionary(
      sb,
      "Hashes",
      "每页源 md 的 SHA-256（页面 id → 小写十六进制）。",
      result.Pages,
      p => p.Hash
    );

    sb.AppendLine("}");
    // 统一成 LF：生成结果与构建平台无关，--verify 才能稳定比对（Windows 的 AppendLine 是 CRLF）。
    return sb.ToString().Replace("\r\n", "\n");
  }

  /// <summary>追加一个 id → 文本的只读字典字段。</summary>
  /// <param name="sb">输出缓冲。</param>
  /// <param name="name">字段名。</param>
  /// <param name="summary">XML 注释文本。</param>
  /// <param name="pages">页面集合。</param>
  /// <param name="selector">取值函数。</param>
  private static void AppendDictionary(
    StringBuilder sb,
    string name,
    string summary,
    IReadOnlyList<ConvertedPage> pages,
    Func<ConvertedPage, string> selector
  )
  {
    sb.AppendLine();
    sb.Append("  /// <summary>").Append(summary).AppendLine("</summary>");
    sb.Append("  public static readonly IReadOnlyDictionary<string, string> ")
      .Append(name)
      .AppendLine(" = new Dictionary<string, string>(StringComparer.Ordinal)");
    sb.AppendLine("  {");
    foreach (var page in pages)
    {
      sb.Append("    [")
        .Append(Str(page.Id))
        .Append("] = ")
        .Append(Str(selector(page)))
        .AppendLine(",");
    }
    sb.AppendLine("  };");
  }

  /// <summary>把字符串渲染成不会破坏生成文件结构的 C# verbatim 字面量。</summary>
  /// <param name="value">原始字符串。</param>
  /// <returns>verbatim 字面量文本（内部引号成对转义）。</returns>
  private static string Str(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";

  /// <summary>把字符串数组渲染成 C# 数组字面量。</summary>
  /// <param name="values">字符串集合。</param>
  /// <returns>数组字面量文本。</returns>
  private static string StrArray(IReadOnlyList<string> values)
  {
    if (values.Count == 0)
    {
      return "Array.Empty<string>()";
    }
    var sb = new StringBuilder("new string[] { ");
    for (int i = 0; i < values.Count; i++)
    {
      if (i > 0)
      {
        sb.Append(", ");
      }
      sb.Append(Str(values[i]));
    }
    sb.Append(" }");
    return sb.ToString();
  }

  /// <summary>校验每个分区的 order 从 1 起连续且不重复。</summary>
  /// <param name="parsed">已解析的页面。</param>
  /// <param name="errors">诊断输出。</param>
  private static void CheckSectionOrders(
    IReadOnlyList<ParsedFile> parsed,
    List<HelpDiagnostic> errors
  )
  {
    foreach (var section in SectionOrder)
    {
      var orders = new List<int>();
      var owners = new List<string>();
      foreach (var page in parsed)
      {
        if (!string.Equals(page.Front.Section, section, StringComparison.Ordinal))
        {
          continue;
        }
        orders.Add(page.Front.Order);
        owners.Add(page.RelativePath);
      }
      if (orders.Count == 0)
      {
        continue;
      }
      var seen = new HashSet<int>();
      for (int i = 0; i < orders.Count; i++)
      {
        if (!seen.Add(orders[i]))
        {
          errors.Add(
            new HelpDiagnostic(owners[i], 0, $"分区「{section}」内 order 重复：{orders[i]}")
          );
        }
      }
      for (int expected = 1; expected <= orders.Count; expected++)
      {
        if (!seen.Contains(expected))
        {
          errors.Add(
            new HelpDiagnostic(
              String.Join("、", owners),
              0,
              $"分区「{section}」的 order 必须从 1 起连续，缺少 {expected}"
            )
          );
        }
      }
    }
  }

  /// <summary>分区名是否合法。</summary>
  /// <param name="section">分区名。</param>
  /// <returns>合法返回 true。</returns>
  private static bool ContainsSection(string section) => SectionIndexOf(section) >= 0;

  /// <summary>分区名在固定顺序里的下标。</summary>
  /// <param name="section">分区名。</param>
  /// <returns>下标；不存在返回 -1。</returns>
  private static int SectionIndexOf(string section)
  {
    for (int i = 0; i < SectionOrder.Count; i++)
    {
      if (string.Equals(SectionOrder[i], section, StringComparison.Ordinal))
      {
        return i;
      }
    }
    return -1;
  }

  /// <summary>把文件字节解码成文本（UTF-8，去 BOM）。</summary>
  /// <param name="bytes">文件字节。</param>
  /// <returns>文本。</returns>
  private static string Decode(byte[] bytes)
  {
    string text = new UTF8Encoding(false, false).GetString(bytes);
    return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
  }

  /// <summary>单个已解析文件（front matter 已通过校验，正文尚未转换）。</summary>
  private sealed class ParsedFile
  {
    /// <summary>构造已解析文件。</summary>
    /// <param name="relativePath">相对 <c>docs/help</c> 的路径。</param>
    /// <param name="hash">源文件 SHA-256。</param>
    /// <param name="text">文件全文。</param>
    /// <param name="front">front matter。</param>
    public ParsedFile(string relativePath, string hash, string text, HelpFrontMatter front)
    {
      RelativePath = relativePath;
      Hash = hash;
      Text = text;
      Front = front;
    }

    /// <summary>相对路径（诊断与生成物来源）。</summary>
    public string RelativePath { get; }

    /// <summary>源文件 SHA-256。</summary>
    public string Hash { get; }

    /// <summary>文件全文。</summary>
    public string Text { get; }

    /// <summary>front matter。</summary>
    public HelpFrontMatter Front { get; }
  }
}

/// <summary>
/// 页面集合装配结果。
/// </summary>
public sealed class HelpPageSetBuildResult
{
  /// <summary>构造装配结果。</summary>
  /// <param name="pages">已排序的页面集合。</param>
  /// <param name="errors">诊断集合。</param>
  /// <param name="missingImageCount">缺失图片总数。</param>
  public HelpPageSetBuildResult(
    IReadOnlyList<ConvertedPage> pages,
    IReadOnlyList<HelpDiagnostic> errors,
    int missingImageCount
  )
  {
    Pages = pages;
    Errors = errors;
    MissingImageCount = missingImageCount;
  }

  /// <summary>已按分区顺序与分区内 order 排序的页面集合。</summary>
  public IReadOnlyList<ConvertedPage> Pages { get; }

  /// <summary>诊断集合；非空即表示装配失败，生成器应打印后非零退出。</summary>
  public IReadOnlyList<HelpDiagnostic> Errors { get; }

  /// <summary>缺失图片总数（只统计被整行跳过的图片）。</summary>
  public int MissingImageCount { get; }

  /// <summary>是否成功（无诊断且至少一页）。</summary>
  public bool Success => Errors.Count == 0 && Pages.Count > 0;
}
