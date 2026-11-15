namespace AutoCMEX.Core.Help.Markdown;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 块级渲染器：把逐行正文转成 BBCode 与纯文本（标题、段落、列表、表格、代码块、提示块、图片、分隔线）。
/// </summary>
/// <remarks>
/// <para>
/// 严格按渲染契约的映射表实现；被拒绝的语法（H4 及更深标题、HTML、嵌套列表、页内锚点、代码块未闭合）
/// 一律抛 <see cref="HelpFormatException"/> 并带行号，由装配器转成「文件:行 + 原因」。
/// </para>
/// <para>
/// 图片行策略：资源缺失时整行不输出（BBCode 与纯文本都不输出），只累加缺失计数；
/// 计数由生成器汇总成一行普通消息，不是 warning。
/// </para>
/// </remarks>
public sealed class HelpBlockRenderer
{
  /// <summary>字号换算（正文/H2/H3 的绝对像素，随项目主题缩放变化）。</summary>
  private readonly HelpTypography _typography;

  /// <summary>
  /// 四种提示标签对应的底色占位符名：颜色归主题，生成物只留占位符。
  /// 无标签的引用块用 <see cref="HelpPlaceholders.TipInfo"/>。
  /// </summary>
  public static readonly IReadOnlyDictionary<string, string> TipBackgrounds = new Dictionary<
    string,
    string
  >(StringComparer.Ordinal)
  {
    ["提示"] = HelpPlaceholders.TipOk,
    ["注意"] = HelpPlaceholders.TipWarn,
    ["警告"] = HelpPlaceholders.TipError,
    ["技巧"] = HelpPlaceholders.TipInfo,
  };

  private const string Fence = "```";
  private const string HorizontalRule = "---";

  private readonly HelpInlineRenderer _inline;
  private readonly Func<string, bool> _imageExists;

  /// <summary>构造块级渲染器。</summary>
  /// <param name="inline">行内渲染器。</param>
  /// <param name="imageExists">图片存在性判定（参数相对 <c>src/assets/help</c>）。</param>
  public HelpBlockRenderer(
    HelpInlineRenderer inline,
    Func<string, bool> imageExists,
    HelpTypography? typography = null
  )
  {
    _inline = inline;
    _imageExists = imageExists;
    _typography = typography ?? HelpTypography.Default;
  }

  /// <summary>
  /// 渲染正文区（从 <paramref name="startIndex"/> 开始到文件末尾）。
  /// </summary>
  /// <param name="lines">整份文件按行拆分的结果。</param>
  /// <param name="startIndex">正文第一行的 0 起下标（front matter 之后）。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <returns>BBCode、纯文本、H2/H3 文本与缺失图片数。</returns>
  /// <exception cref="HelpFormatException">出现被拒绝的语法或结构不完整时抛出。</exception>
  public BlockRenderResult Render(IReadOnlyList<string> lines, int startIndex, string pageId)
  {
    var blocks = new List<string>();
    var plainBlocks = new List<string>();
    var headings = new List<string>();
    int missingImages = 0;
    int i = startIndex;
    while (i < lines.Count)
    {
      string raw = lines[i].TrimEnd();
      string trimmed = raw.Trim();
      if (trimmed.Length == 0)
      {
        i++;
        continue;
      }

      if (trimmed.StartsWith(Fence, StringComparison.Ordinal))
      {
        i = ReadFence(lines, i, blocks, plainBlocks);
        continue;
      }
      if (ContainsHtmlTag(trimmed))
      {
        throw new HelpFormatException(
          i + 1,
          "不支持 HTML 标签（帮助正文只允许渲染契约列出的语法）"
        );
      }
      if (trimmed[0] == '#')
      {
        i = ReadHeading(lines, i, pageId, blocks, plainBlocks, headings, ref missingImages);
        continue;
      }
      if (trimmed == HorizontalRule)
      {
        blocks.Add("[hr]");
        plainBlocks.Add(string.Empty);
        i++;
        continue;
      }
      if (trimmed[0] == '>')
      {
        i = ReadTip(lines, i, pageId, blocks, plainBlocks, ref missingImages);
        continue;
      }
      if (trimmed[0] == '|')
      {
        i = ReadTable(lines, i, pageId, blocks, plainBlocks, ref missingImages);
        continue;
      }
      if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && IsListMarker(trimmed))
      {
        throw new HelpFormatException(i + 1, "不支持嵌套列表（帮助正文只允许单层列表）");
      }
      if (IsListMarker(trimmed))
      {
        i = ReadList(lines, i, pageId, blocks, plainBlocks, ref missingImages);
        continue;
      }
      if (HelpImages.IsImageLine(trimmed))
      {
        i = ReadImageLine(lines, i, blocks, plainBlocks, ref missingImages);
        continue;
      }
      i = ReadParagraph(lines, i, pageId, blocks, plainBlocks, ref missingImages);
    }

    string bbcode = blocks.Count == 0 ? string.Empty : string.Join("\n\n", blocks).Trim();
    string plainText =
      plainBlocks.Count == 0 ? string.Empty : string.Join("\n", plainBlocks).Trim();
    return new BlockRenderResult(bbcode, plainText, headings, missingImages);
  }

  /// <summary>读取围栏代码块（不做语法高亮）。</summary>
  /// <param name="lines">整份文件行。</param>
  /// <param name="index">围栏起始下标。</param>
  /// <param name="blocks">BBCode 块输出。</param>
  /// <param name="plainBlocks">纯文本块输出。</param>
  /// <returns>代码块之后的下标。</returns>
  private static int ReadFence(
    IReadOnlyList<string> lines,
    int index,
    List<string> blocks,
    List<string> plainBlocks
  )
  {
    int lineNo = index + 1;
    var code = new List<string>();
    int i = index + 1;
    while (i < lines.Count && !lines[i].Trim().StartsWith(Fence, StringComparison.Ordinal))
    {
      code.Add(lines[i].TrimEnd());
      i++;
    }
    if (i >= lines.Count)
    {
      throw new HelpFormatException(lineNo, "代码块未闭合：缺少结束的 ```");
    }
    string body = string.Join("\n", code);
    // [bgcolor] 只染文字后面的底色；整块矩形底色用单格表格的 cell bg。
    // 不加 ratio 参数：实测 plain cell 的底色已铺满可用宽度，写 cell=1 反而引入额外比例语义。
    // bg/border 只写占位符，具体颜色由 HelpThemeColors 在运行期按主题解析。
    string background = HelpPlaceholders.Wrap(HelpPlaceholders.CodeBackground);
    blocks.Add(
      "[table=1][cell bg="
        + background
        + " border="
        + background
        + "][code]"
        + HelpInlineRenderer.EscapeBbcode(body)
        + "[/code][/cell][/table]"
    );
    plainBlocks.Add(body);
    return i + 1;
  }

  /// <summary>读取 H1/H2/H3 标题；H1 是页标题，不渲染。</summary>
  /// <param name="lines">整份文件行。</param>
  /// <param name="index">标题行下标。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="blocks">BBCode 块输出。</param>
  /// <param name="plainBlocks">纯文本块输出。</param>
  /// <param name="headings">H2/H3 文本收集。</param>
  /// <param name="missingImages">缺失图片计数。</param>
  /// <returns>标题行之后的下标。</returns>
  private int ReadHeading(
    IReadOnlyList<string> lines,
    int index,
    string pageId,
    List<string> blocks,
    List<string> plainBlocks,
    List<string> headings,
    ref int missingImages
  )
  {
    string trimmed = lines[index].Trim();
    int level = 0;
    while (level < trimmed.Length && trimmed[level] == '#')
    {
      level++;
    }
    if (level >= 4)
    {
      throw new HelpFormatException(index + 1, "不支持 H4 及更深的标题（帮助正文只允许 H2/H3）");
    }
    string text = trimmed[level..].Trim();
    if (text.Length == 0)
    {
      throw new HelpFormatException(index + 1, "标题内容为空");
    }
    if (level == 1)
    {
      return index + 1;
    }
    var inline = _inline.Render(text, pageId, index + 1);
    missingImages += inline.MissingImages;
    int size = level == 2 ? _typography.H2FontSize : _typography.H3FontSize;
    blocks.Add(
      "[font_size="
        + size.ToString(CultureInfo.InvariantCulture)
        + "][b]"
        + inline.Bbcode
        + "[/b][/font_size]"
    );
    plainBlocks.Add(inline.Plain);
    headings.Add(inline.Plain);
    return index + 1;
  }

  /// <summary>读取引用块（提示/注意/警告/技巧）。</summary>
  /// <param name="lines">整份文件行。</param>
  /// <param name="index">引用块起始下标。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="blocks">BBCode 块输出。</param>
  /// <param name="plainBlocks">纯文本块输出。</param>
  /// <param name="missingImages">缺失图片计数。</param>
  /// <returns>引用块之后的下标。</returns>
  private int ReadTip(
    IReadOnlyList<string> lines,
    int index,
    string pageId,
    List<string> blocks,
    List<string> plainBlocks,
    ref int missingImages
  )
  {
    int lineNo = index + 1;
    var parts = new List<string>();
    int i = index;
    while (i < lines.Count && lines[i].Trim().StartsWith('>'))
    {
      string text = lines[i].Trim()[1..].Trim();
      if (text.Length > 0)
      {
        parts.Add(text);
      }
      i++;
    }
    string content = string.Join(" ", parts);
    string label = string.Empty;
    string rest = content;
    if (content.StartsWith("**", StringComparison.Ordinal))
    {
      int end = content.IndexOf("**", 2, StringComparison.Ordinal);
      if (end > 2)
      {
        string candidate = content[2..end];
        if (TipBackgrounds.ContainsKey(candidate))
        {
          label = candidate;
          rest = content[(end + 2)..].TrimStart();
          if (rest.Length > 0 && (rest[0] == '：' || rest[0] == ':'))
          {
            rest = rest[1..].TrimStart();
          }
        }
      }
    }
    var inline = _inline.Render(rest, pageId, lineNo);
    missingImages += inline.MissingImages;
    // 有标签按标签取占位符；无标签的引用块一律用 TipInfo（与「技巧」同色）。
    string background = TipBackgrounds.TryGetValue(label, out string? placeholderName)
      ? HelpPlaceholders.Wrap(placeholderName)
      : HelpPlaceholders.Wrap(HelpPlaceholders.TipInfo);
    if (label.Length > 0)
    {
      blocks.Add(
        "[table=1][cell bg="
          + background
          + " border="
          + background
          + "][b]"
          + label
          + "[/b] "
          + inline.Bbcode
          + "[/cell][/table]"
      );
      plainBlocks.Add(label + " " + inline.Plain);
    }
    else
    {
      blocks.Add(
        "[table=1][cell bg="
          + background
          + " border="
          + background
          + "]"
          + inline.Bbcode
          + "[/cell][/table]"
      );
      plainBlocks.Add(inline.Plain);
    }
    return i;
  }

  /// <summary>读取表格（表头 + 数据行，不支持合并单元格）。</summary>
  /// <remarks>
  /// 整张表发射成「一行」BBCode：<c>[table]</c> 内只允许出现 cell，cell 之间插入换行可能被当成
  /// 单元格内容的一部分，因此 cell 之间不留空白。每个 cell 带 <c>border</c> 画表格线，
  /// 表头额外带 <c>bg</c> 铺满底色（<c>bgcolor</c> 只染文字后面，不能用）。
  /// <c>bg</c>/<c>border</c> 的取值是占位符，运行期由 <see cref="HelpThemeColors"/> 解析。
  /// </remarks>
  /// <param name="lines">整份文件行。</param>
  /// <param name="index">表头行下标。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="blocks">BBCode 块输出。</param>
  /// <param name="plainBlocks">纯文本块输出。</param>
  /// <param name="missingImages">缺失图片计数。</param>
  /// <returns>表格之后的下标。</returns>
  private int ReadTable(
    IReadOnlyList<string> lines,
    int index,
    string pageId,
    List<string> blocks,
    List<string> plainBlocks,
    ref int missingImages
  )
  {
    int lineNo = index + 1;
    if (index + 1 >= lines.Count || !IsTableSeparator(lines[index + 1]))
    {
      throw new HelpFormatException(lineNo, "表格缺少表头分隔行（形如 | --- | --- |）");
    }
    string[] header = SplitRow(lines[index]);
    int columns = header.Length;
    if (columns == 0)
    {
      throw new HelpFormatException(lineNo, "表格表头为空");
    }
    var rows = new List<string[]>();
    int i = index + 2;
    while (i < lines.Count)
    {
      string trimmed = lines[i].Trim();
      if (trimmed.Length == 0 || trimmed[0] != '|')
      {
        break;
      }
      string[] cells = SplitRow(lines[i]);
      if (cells.Length != columns)
      {
        throw new HelpFormatException(
          i + 1,
          $"表格列数不一致：表头 {columns} 列，本行 {cells.Length} 列"
        );
      }
      rows.Add(cells);
      i++;
    }

    string headerBackground = HelpPlaceholders.Wrap(HelpPlaceholders.TableHeaderBackground);
    string border = HelpPlaceholders.Wrap(HelpPlaceholders.TableBorder);
    var bbcode = new StringBuilder();
    bbcode.Append("[table=").Append(columns.ToString(CultureInfo.InvariantCulture)).Append(']');
    for (int c = 0; c < header.Length; c++)
    {
      var cell = _inline.Render(header[c], pageId, lineNo);
      missingImages += cell.MissingImages;
      bbcode
        .Append("[cell bg=")
        .Append(headerBackground)
        .Append(" border=")
        .Append(border)
        .Append("][b]")
        .Append(cell.Bbcode)
        .Append("[/b][/cell]");
      header[c] = cell.Plain;
    }
    var plain = new StringBuilder();
    plain.Append(string.Join(" | ", header));
    foreach (var row in rows)
    {
      var plainRow = new string[row.Length];
      for (int c = 0; c < row.Length; c++)
      {
        var cell = _inline.Render(row[c], pageId, lineNo);
        missingImages += cell.MissingImages;
        bbcode
          .Append("[cell border=")
          .Append(border)
          .Append(']')
          .Append(cell.Bbcode)
          .Append("[/cell]");
        plainRow[c] = cell.Plain;
      }
      plain.Append('\n').Append(string.Join(" | ", plainRow));
    }
    bbcode.Append("[/table]");
    blocks.Add(bbcode.ToString());
    plainBlocks.Add(plain.ToString());
    return i;
  }

  /// <summary>读取单层列表。</summary>
  /// <remarks>
  /// RichTextLabel 的 <c>[ul]</c> / <c>[ol]</c> 没有 <c>[li]</c> 子标签：块内每一行就是一个列表项，
  /// 所以这里把「开始标签 + 逐行项 + 结束标签」各自放在单独一行。
  /// </remarks>
  /// <param name="lines">整份文件行。</param>
  /// <param name="index">第一项下标。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="blocks">BBCode 块输出。</param>
  /// <param name="plainBlocks">纯文本块输出。</param>
  /// <param name="missingImages">缺失图片计数。</param>
  /// <returns>列表之后的下标。</returns>
  private int ReadList(
    IReadOnlyList<string> lines,
    int index,
    string pageId,
    List<string> blocks,
    List<string> plainBlocks,
    ref int missingImages
  )
  {
    int lineNo = index + 1;
    bool ordered =
      TryParseListMarker(lines[index].Trim(), out bool listOrdered, out _) && listOrdered;
    var items = new List<string>();
    int i = index;
    while (i < lines.Count)
    {
      string raw = lines[i];
      string trimmed = raw.Trim();
      if (trimmed.Length == 0)
      {
        break;
      }
      bool indented = raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t');
      if (TryParseListMarker(trimmed, out bool itemOrdered, out string itemText))
      {
        if (indented)
        {
          throw new HelpFormatException(i + 1, "不支持嵌套列表（帮助正文只允许单层列表）");
        }
        if (items.Count > 0 && itemOrdered != ordered)
        {
          throw new HelpFormatException(i + 1, "同一列表内不允许混用有序与无序标记");
        }
        items.Add(itemText);
        i++;
        continue;
      }
      if (indented && items.Count > 0)
      {
        items[^1] = items[^1] + " " + trimmed;
        i++;
        continue;
      }
      break;
    }

    string tag = ordered ? "ol" : "ul";
    var bbcode = new StringBuilder();
    bbcode.Append('[').Append(tag).Append(']').Append('\n');
    var plainItems = new List<string>();
    foreach (var item in items)
    {
      var inline = _inline.Render(item, pageId, lineNo);
      missingImages += inline.MissingImages;
      bbcode.Append(inline.Bbcode).Append('\n');
      plainItems.Add(inline.Plain);
    }
    bbcode.Append("[/").Append(tag).Append(']');
    blocks.Add(bbcode.ToString());
    plainBlocks.Add(string.Join("\n", plainItems));
    return i;
  }

  /// <summary>读取整行图片；资源缺失时整行不输出。</summary>
  /// <param name="lines">整份文件行。</param>
  /// <param name="index">图片行下标。</param>
  /// <param name="blocks">BBCode 块输出。</param>
  /// <param name="plainBlocks">纯文本块输出。</param>
  /// <param name="missingImages">缺失图片计数。</param>
  /// <returns>图片行之后的下标。</returns>
  private int ReadImageLine(
    IReadOnlyList<string> lines,
    int index,
    List<string> blocks,
    List<string> plainBlocks,
    ref int missingImages
  )
  {
    if (!HelpImages.TryParseLine(lines[index].Trim(), out string alt, out string source))
    {
      return index + 1;
    }
    string? bbcode = HelpImages.Resolve(source, _imageExists, index + 1);
    if (bbcode == null)
    {
      missingImages++;
      return index + 1;
    }
    blocks.Add(bbcode);
    plainBlocks.Add(alt);
    return index + 1;
  }

  /// <summary>读取一个段落（连续的非空、非块起始行）。</summary>
  /// <param name="lines">整份文件行。</param>
  /// <param name="index">段落起始下标。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="blocks">BBCode 块输出。</param>
  /// <param name="plainBlocks">纯文本块输出。</param>
  /// <param name="missingImages">缺失图片计数。</param>
  /// <returns>段落之后的下标。</returns>
  private int ReadParagraph(
    IReadOnlyList<string> lines,
    int index,
    string pageId,
    List<string> blocks,
    List<string> plainBlocks,
    ref int missingImages
  )
  {
    int lineNo = index + 1;
    var parts = new List<string>();
    int i = index;
    while (i < lines.Count)
    {
      string trimmed = lines[i].Trim();
      if (trimmed.Length == 0 || IsBlockStart(trimmed))
      {
        break;
      }
      parts.Add(trimmed);
      i++;
    }
    string text = JoinParagraph(parts);
    var inline = _inline.Render(text, pageId, lineNo);
    missingImages += inline.MissingImages;
    blocks.Add(inline.Bbcode);
    plainBlocks.Add(inline.Plain);
    return i;
  }

  /// <summary>判断一行是否是块级语法的起始行（用于切断段落）。</summary>
  /// <param name="trimmed">已去首尾空白的行文本。</param>
  /// <returns>是块起始返回 true。</returns>
  private static bool IsBlockStart(string trimmed)
  {
    if (trimmed.StartsWith(Fence, StringComparison.Ordinal))
    {
      return true;
    }
    if (trimmed == HorizontalRule)
    {
      return true;
    }
    if (trimmed[0] == '#' || trimmed[0] == '>' || trimmed[0] == '|')
    {
      return true;
    }
    if (IsListMarker(trimmed) || HelpImages.IsImageLine(trimmed))
    {
      return true;
    }
    return ContainsHtmlTag(trimmed);
  }

  /// <summary>判断是否是无序/有序列表项。</summary>
  /// <param name="text">行文本。</param>
  /// <returns>是列表项返回 true。</returns>
  private static bool IsListMarker(string text) => TryParseListMarker(text, out _, out _);

  /// <summary>解析列表项标记。</summary>
  /// <param name="text">已去首尾空白的行文本。</param>
  /// <param name="ordered">输出：是否为有序列表项。</param>
  /// <param name="content">输出：标记之后的正文。</param>
  /// <returns>是列表项返回 true。</returns>
  private static bool TryParseListMarker(string text, out bool ordered, out string content)
  {
    ordered = false;
    content = string.Empty;
    if (text.Length < 3)
    {
      return false;
    }
    char first = text[0];
    if ((first == '-' || first == '*' || first == '+') && text[1] == ' ')
    {
      content = text[2..].Trim();
      return content.Length > 0;
    }
    int digits = 0;
    while (digits < text.Length && char.IsAsciiDigit(text[digits]))
    {
      digits++;
    }
    if (digits == 0 || digits > 3 || digits + 1 >= text.Length)
    {
      return false;
    }
    if ((text[digits] != '.' && text[digits] != ')') || text[digits + 1] != ' ')
    {
      return false;
    }
    content = text[(digits + 2)..].Trim();
    ordered = true;
    return content.Length > 0;
  }

  /// <summary>判断表格的第二行是否是表头分隔行。</summary>
  /// <param name="line">行文本。</param>
  /// <returns>是分隔行返回 true。</returns>
  private static bool IsTableSeparator(string line)
  {
    string trimmed = line.Trim();
    if (trimmed.Length == 0 || trimmed[0] != '|')
    {
      return false;
    }
    foreach (var cell in SplitRow(line))
    {
      string text = cell.Trim();
      if (text.Length < 3)
      {
        return false;
      }
      if (text[0] == ':')
      {
        text = text[1..];
      }
      if (text.Length > 0 && text[^1] == ':')
      {
        text = text[..^1];
      }
      if (text.Length < 3)
      {
        return false;
      }
      foreach (var c in text)
      {
        if (c != '-')
        {
          return false;
        }
      }
    }
    return true;
  }

  /// <summary>按 <c>|</c> 切分表格行，去掉首尾竖线并逐格去空白。</summary>
  /// <param name="line">行文本。</param>
  /// <returns>单元格数组。</returns>
  private static string[] SplitRow(string line)
  {
    string text = line.Trim();
    if (text.Length > 0 && text[0] == '|')
    {
      text = text[1..];
    }
    if (text.Length > 0 && text[^1] == '|')
    {
      text = text[..^1];
    }
    if (text.Trim().Length == 0)
    {
      return Array.Empty<string>();
    }
    string[] parts = text.Split('|');
    for (int i = 0; i < parts.Length; i++)
    {
      parts[i] = parts[i].Trim();
    }
    return parts;
  }

  /// <summary>合并段落内的软换行：两侧都不是中日韩字符时插入一个空格。</summary>
  /// <param name="parts">段落的各行（已去空白）。</param>
  /// <returns>合并后的段文本。</returns>
  private static string JoinParagraph(IReadOnlyList<string> parts)
  {
    if (parts.Count == 1)
    {
      return parts[0];
    }
    var sb = new StringBuilder();
    foreach (var part in parts)
    {
      if (sb.Length > 0 && !(IsCjk(sb[^1]) || IsCjk(part[0])))
      {
        sb.Append(' ');
      }
      sb.Append(part);
    }
    return sb.ToString();
  }

  /// <summary>判断字符是否属于中日韩文字或全角标点。</summary>
  /// <param name="c">字符。</param>
  /// <returns>属于返回 true。</returns>
  private static bool IsCjk(char c) =>
    (c >= '\u4E00' && c <= '\u9FFF')
    || (c >= '\u3400' && c <= '\u4DBF')
    || (c >= '\u3000' && c <= '\u303F')
    || (c >= '\uFF00' && c <= '\uFFEF');

  /// <summary>检测行内是否出现 HTML 标签（帮助正文拒绝 HTML）。</summary>
  /// <param name="text">行文本。</param>
  /// <returns>出现标签返回 true。</returns>
  private static bool ContainsHtmlTag(string text)
  {
    for (int i = 0; i + 2 < text.Length; i++)
    {
      if (text[i] != '<')
      {
        continue;
      }
      int j = i + 1;
      if (text[j] == '/')
      {
        j++;
      }
      if (j >= text.Length || !char.IsAsciiLetter(text[j]))
      {
        continue;
      }
      while (j < text.Length && (char.IsAsciiLetterOrDigit(text[j]) || text[j] == '-'))
      {
        j++;
      }
      if (j < text.Length && (text[j] == '>' || text[j] == ' ' || text[j] == '/'))
      {
        return true;
      }
    }
    return false;
  }
}

/// <summary>
/// 正文渲染结果：BBCode、纯文本、H2/H3 文本与缺失图片数量。
/// </summary>
public sealed class BlockRenderResult
{
  /// <summary>构造结果。</summary>
  /// <param name="bbcode">BBCode 正文。</param>
  /// <param name="plainText">纯文本正文。</param>
  /// <param name="headings">H2/H3 文本，按出现顺序。</param>
  /// <param name="missingImages">缺失图片数量。</param>
  public BlockRenderResult(
    string bbcode,
    string plainText,
    IReadOnlyList<string> headings,
    int missingImages
  )
  {
    Bbcode = bbcode;
    PlainText = plainText;
    Headings = headings;
    MissingImages = missingImages;
  }

  /// <summary>BBCode 正文。</summary>
  public string Bbcode { get; }

  /// <summary>纯文本正文。</summary>
  public string PlainText { get; }

  /// <summary>H2/H3 文本。</summary>
  public IReadOnlyList<string> Headings { get; }

  /// <summary>缺失图片数量。</summary>
  public int MissingImages { get; }
}
