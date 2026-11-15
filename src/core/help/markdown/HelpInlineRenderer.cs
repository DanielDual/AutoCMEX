namespace AutoCMEX.Core.Help.Markdown;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 行内渲染器：把一行的 Markdown 行内语法转成 BBCode 与纯文本。
/// </summary>
/// <remarks>
/// <para>
/// 关键约束：RichTextLabel 不支持交叉嵌套（<c>[b]a[i]b[/b]c[/i]</c> 会被解成错乱结果）。
/// 因此这里先扫描出开/闭标记序列，再用栈发射：需要闭合一个不在栈顶的标签时，
/// 先把栈顶标签依次闭合，再闭合目标标签，然后按原顺序重新打开被闭合的标签，
/// 输出一定是顺序嵌套。
/// </para>
/// <para>
/// 正文中的 <c>[</c> 一律转成 <c>[lb]</c>，与 <c>StringEscapeHelper.EscapeBbcode</c> 同规则；
/// 这里不直接复用该工具，是为了让生成器工程只链接 <c>src/core/help/markdown/</c>（见技术规格的代码结构说明）。
/// </para>
/// </remarks>
public sealed class HelpInlineRenderer
{
  private const char Backtick = '`';
  private const char Star = '*';
  private const char Bracket = '[';
  private const char Bang = '!';
  private const char Backslash = '\\';

  private readonly HelpLinkResolver _links;
  private readonly Func<string, bool> _imageExists;

  /// <summary>构造行内渲染器。</summary>
  /// <param name="links">站内链接解析器。</param>
  /// <param name="imageExists">图片存在性判定（参数形如 <c>images/x.png</c>，相对 <c>src/assets/help</c>）。</param>
  public HelpInlineRenderer(HelpLinkResolver links, Func<string, bool> imageExists)
  {
    _links = links;
    _imageExists = imageExists;
  }

  /// <summary>
  /// 渲染一行（或一个段落合并后的一行）行内内容。
  /// </summary>
  /// <param name="text">原始 Markdown 文本。</param>
  /// <param name="pageId">当前页 id（站内链接基准 + 报错定位）。</param>
  /// <param name="line">出错时报告的行号。</param>
  /// <returns>BBCode 与纯文本两态结果。</returns>
  public InlineResult Render(string text, string pageId, int line)
  {
    int missingImages = 0;
    var tokens = new List<InlineToken>();
    Tokenize(text, pageId, line, tokens, ref missingImages);
    return Emit(tokens, missingImages);
  }

  /// <summary>把正文纯文本里的 <c>[</c> 转成 <c>[lb]</c>（BBCode 转义）。</summary>
  /// <param name="text">原始文本。</param>
  /// <returns>BBCode 安全文本。</returns>
  public static string EscapeBbcode(string text)
  {
    if (text.Length == 0 || text.IndexOf(Bracket) < 0)
    {
      return text;
    }
    return text.Replace("[", "[lb]");
  }

  /// <summary>把整段文本扫成行内标记序列。</summary>
  /// <param name="text">待扫描文本。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="line">出错行号。</param>
  /// <param name="tokens">输出：标记序列。</param>
  /// <param name="missingImages">输出：本段缺失图片数量累加。</param>
  private void Tokenize(
    string text,
    string pageId,
    int line,
    List<InlineToken> tokens,
    ref int missingImages
  )
  {
    var literal = new StringBuilder();
    var openMarks = new List<TagKind>();
    int i = 0;
    while (i < text.Length)
    {
      char c = text[i];
      if (c == Backslash && i + 1 < text.Length && IsEscapable(text[i + 1]))
      {
        literal.Append(text[i + 1]);
        i += 2;
        continue;
      }
      if (c == Backtick)
      {
        int close = text.IndexOf(Backtick, i + 1);
        if (close > i)
        {
          Flush(literal, tokens);
          tokens.Add(new InlineToken(InlineTokenKind.Code, text[(i + 1)..close], string.Empty));
          i = close + 1;
          continue;
        }
      }
      if (c == Bang && TryTokenizeImage(text, ref i, line, tokens, literal, ref missingImages))
      {
        continue;
      }
      if (
        c == Bracket
        && TryTokenizeLink(text, ref i, pageId, line, tokens, literal, ref missingImages)
      )
      {
        continue;
      }
      if (c == Star && TryTokenizeEmphasis(text, ref i, tokens, literal, openMarks))
      {
        continue;
      }
      literal.Append(c);
      i++;
    }
    Flush(literal, tokens);
  }

  /// <summary>把累积的字面量刷成一个文本标记。</summary>
  /// <param name="literal">字面量缓冲。</param>
  /// <param name="tokens">标记序列。</param>
  private static void Flush(StringBuilder literal, List<InlineToken> tokens)
  {
    if (literal.Length == 0)
    {
      return;
    }
    tokens.Add(new InlineToken(InlineTokenKind.Text, literal.ToString(), string.Empty));
    literal.Clear();
  }

  /// <summary>判断反斜杠后面的字符是否是可转义字符（支持的最小集合）。</summary>
  /// <param name="c">被转义的字符。</param>
  /// <returns>可转义返回 true。</returns>
  private static bool IsEscapable(char c) =>
    c == Star || c == Bracket || c == ']' || c == Backtick || c == Bang || c == Backslash;

  /// <summary>尝试把 <c>![alt](images/x.png)</c> 扫成图片标记。</summary>
  /// <param name="text">整行文本。</param>
  /// <param name="index">扫描下标（成功时推进到图片之后）。</param>
  /// <param name="line">出错行号。</param>
  /// <param name="tokens">标记序列。</param>
  /// <param name="literal">字面量缓冲。</param>
  /// <param name="missingImages">缺失图片计数。</param>
  /// <returns>识别成功返回 true。</returns>
  private bool TryTokenizeImage(
    string text,
    ref int index,
    int line,
    List<InlineToken> tokens,
    StringBuilder literal,
    ref int missingImages
  )
  {
    if (!TryReadLinkTail(text, index + 1, out string alt, out string source, out int end))
    {
      return false;
    }
    Flush(literal, tokens);
    string? bbcode = HelpImages.Resolve(source, _imageExists, line);
    if (bbcode == null)
    {
      missingImages++;
    }
    else
    {
      tokens.Add(new InlineToken(InlineTokenKind.Raw, bbcode, alt));
    }
    index = end;
    return true;
  }

  /// <summary>尝试把 <c>[文字](目标)</c> 扫成链接标记（标签内允许再次行内渲染）。</summary>
  /// <param name="text">整行文本。</param>
  /// <param name="index">扫描下标（成功时推进到链接之后）。</param>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="line">出错行号。</param>
  /// <param name="tokens">标记序列。</param>
  /// <param name="literal">字面量缓冲。</param>
  /// <returns>识别成功返回 true。</returns>
  private bool TryTokenizeLink(
    string text,
    ref int index,
    string pageId,
    int line,
    List<InlineToken> tokens,
    StringBuilder literal,
    ref int missingImages
  )
  {
    if (!TryReadLinkTail(text, index, out string label, out string target, out int end))
    {
      return false;
    }
    if (label.Length == 0)
    {
      return false;
    }
    var resolved = _links.Resolve(pageId, target, line);
    Flush(literal, tokens);
    tokens.Add(new InlineToken(InlineTokenKind.LinkOpen, resolved.Url, string.Empty));
    Tokenize(label, pageId, line, tokens, ref missingImages);
    tokens.Add(new InlineToken(InlineTokenKind.LinkClose, string.Empty, string.Empty));
    index = end;
    return true;
  }

  /// <summary>
  /// 从 <paramref name="start"/> 处的 <c>[</c> 读出 <c>[label](target)</c> 的标签与目标。
  /// </summary>
  /// <param name="text">整行文本。</param>
  /// <param name="start"><c>[</c> 的下标。</param>
  /// <param name="label">输出：方括号内文本。</param>
  /// <param name="target">输出：圆括号内目标。</param>
  /// <param name="end">输出：右圆括号之后的下标。</param>
  /// <returns>结构完整返回 true。</returns>
  private static bool TryReadLinkTail(
    string text,
    int start,
    out string label,
    out string target,
    out int end
  )
  {
    label = string.Empty;
    target = string.Empty;
    end = start;
    if (start >= text.Length || text[start] != Bracket)
    {
      return false;
    }
    int close = text.IndexOf(']', start + 1);
    if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
    {
      return false;
    }
    int paren = text.IndexOf(')', close + 2);
    if (paren < 0)
    {
      return false;
    }
    label = text[(start + 1)..close];
    target = text[(close + 2)..paren];
    end = paren + 1;
    return true;
  }

  /// <summary>尝试把 <c>**粗体**</c> / <c>*斜体*</c> 的开闭标记识别出来。</summary>
  /// <param name="text">整行文本。</param>
  /// <param name="index">扫描下标（成功时推进到标记之后）。</param>
  /// <param name="tokens">标记序列。</param>
  /// <param name="literal">字面量缓冲。</param>
  /// <param name="openMarks">已打开的行内标记（决定当前星号是开还是闭）。</param>
  /// <returns>识别成功返回 true（否则按普通字符处理）。</returns>
  private static bool TryTokenizeEmphasis(
    string text,
    ref int index,
    List<InlineToken> tokens,
    StringBuilder literal,
    List<TagKind> openMarks
  )
  {
    bool bold = index + 1 < text.Length && text[index + 1] == Star;
    int width = bold ? 2 : 1;
    var kind = bold ? TagKind.Bold : TagKind.Italic;

    if (openMarks.Contains(kind))
    {
      Flush(literal, tokens);
      tokens.Add(
        bold
          ? new InlineToken(InlineTokenKind.BoldClose, string.Empty, string.Empty)
          : new InlineToken(InlineTokenKind.ItalicClose, string.Empty, string.Empty)
      );
      openMarks.Remove(kind);
      index += width;
      return true;
    }

    if (!HasClosingEmphasis(text, index + width, bold))
    {
      return false;
    }

    Flush(literal, tokens);
    tokens.Add(
      bold
        ? new InlineToken(InlineTokenKind.BoldOpen, string.Empty, string.Empty)
        : new InlineToken(InlineTokenKind.ItalicOpen, string.Empty, string.Empty)
    );
    openMarks.Add(kind);
    index += width;
    return true;
  }

  /// <summary>向后确认存在可闭合的强调标记，避免把孤立的 <c>*</c> 当成标记。</summary>
  /// <param name="text">整行文本。</param>
  /// <param name="start">查找起点。</param>
  /// <param name="bold">是否找 <c>**</c>。</param>
  /// <returns>找到匹配的闭合标记返回 true。</returns>
  private static bool HasClosingEmphasis(string text, int start, bool bold)
  {
    for (int i = start; i < text.Length; i++)
    {
      if (text[i] != Star)
      {
        continue;
      }
      bool nextStar = i + 1 < text.Length && text[i + 1] == Star;
      if (bold)
      {
        if (nextStar)
        {
          return true;
        }
        continue;
      }
      bool prevStar = i > 0 && text[i - 1] == Star;
      if (!prevStar && !nextStar)
      {
        return true;
      }
    }
    return false;
  }

  /// <summary>把标记序列发射成 BBCode 与纯文本（用栈保证顺序嵌套）。</summary>
  /// <param name="tokens">标记序列。</param>
  /// <param name="missingImages">缺失图片数量。</param>
  /// <returns>两态结果。</returns>
  private static InlineResult Emit(List<InlineToken> tokens, int missingImages)
  {
    var bbcode = new StringBuilder();
    var plain = new StringBuilder();
    var stack = new List<TagFrame>();
    foreach (var token in tokens)
    {
      switch (token.Kind)
      {
        case InlineTokenKind.Text:
          bbcode.Append(EscapeBbcode(token.Value));
          plain.Append(token.Value);
          break;
        case InlineTokenKind.Raw:
          bbcode.Append(token.Value);
          plain.Append(token.Plain);
          break;
        case InlineTokenKind.Code:
          bbcode.Append("[code]").Append(EscapeBbcode(token.Value)).Append("[/code]");
          plain.Append(token.Value);
          break;
        case InlineTokenKind.LinkOpen:
          // 链接要一眼看出可点：外层 [color] 写占位符，运行期由主题解析成链接色
          // （RichTextLabel 默认与正文同色）。关闭顺序固定 [/url][/color]。
          string open =
            "[color=" + HelpPlaceholders.Wrap(HelpPlaceholders.Link) + "][url=" + token.Value + "]";
          bbcode.Append(open);
          stack.Add(new TagFrame(TagKind.Link, open, "[/url][/color]"));
          break;
        case InlineTokenKind.LinkClose:
          CloseTag(stack, bbcode, TagKind.Link);
          break;
        case InlineTokenKind.BoldOpen:
          bbcode.Append("[b]");
          stack.Add(new TagFrame(TagKind.Bold, "[b]", "[/b]"));
          break;
        case InlineTokenKind.BoldClose:
          CloseTag(stack, bbcode, TagKind.Bold);
          break;
        case InlineTokenKind.ItalicOpen:
          bbcode.Append("[i]");
          stack.Add(new TagFrame(TagKind.Italic, "[i]", "[/i]"));
          break;
        case InlineTokenKind.ItalicClose:
          CloseTag(stack, bbcode, TagKind.Italic);
          break;
      }
    }
    for (int i = stack.Count - 1; i >= 0; i--)
    {
      bbcode.Append(stack[i].Close);
    }
    return new InlineResult(bbcode.ToString(), plain.ToString(), missingImages);
  }

  /// <summary>闭合指定标签；途中位于其上方的标签先闭合再原序重开，保证顺序嵌套。</summary>
  /// <param name="stack">当前打开的标签栈。</param>
  /// <param name="bbcode">输出缓冲。</param>
  /// <param name="kind">要闭合的标签类型。</param>
  private static void CloseTag(List<TagFrame> stack, StringBuilder bbcode, TagKind kind)
  {
    int index = -1;
    for (int i = stack.Count - 1; i >= 0; i--)
    {
      if (stack[i].Kind == kind)
      {
        index = i;
        break;
      }
    }
    if (index < 0)
    {
      return;
    }
    for (int i = stack.Count - 1; i >= index; i--)
    {
      bbcode.Append(stack[i].Close);
    }
    var reopen = new List<TagFrame>();
    for (int i = index + 1; i < stack.Count; i++)
    {
      reopen.Add(stack[i]);
    }
    stack.RemoveRange(index, stack.Count - index);
    foreach (var frame in reopen)
    {
      bbcode.Append(frame.Open);
      stack.Add(frame);
    }
  }

  /// <summary>行内标签类型（仅用于栈匹配）。</summary>
  private enum TagKind
  {
    /// <summary>粗体。</summary>
    Bold,

    /// <summary>斜体。</summary>
    Italic,

    /// <summary>站内/外链。</summary>
    Link,
  }

  /// <summary>标记种类。</summary>
  private enum InlineTokenKind
  {
    /// <summary>字面文本（发射时转义）。</summary>
    Text,

    /// <summary>已渲染的 BBCode 片段（图片）。</summary>
    Raw,

    /// <summary>行内代码。</summary>
    Code,

    /// <summary>链接开始。</summary>
    LinkOpen,

    /// <summary>链接结束。</summary>
    LinkClose,

    /// <summary>粗体开始。</summary>
    BoldOpen,

    /// <summary>粗体结束。</summary>
    BoldClose,

    /// <summary>斜体开始。</summary>
    ItalicOpen,

    /// <summary>斜体结束。</summary>
    ItalicClose,
  }

  /// <summary>行内标记。</summary>
  private readonly struct InlineToken
  {
    /// <summary>构造标记。</summary>
    /// <param name="kind">种类。</param>
    /// <param name="value">BBCode 片段或原始文本。</param>
    /// <param name="plain">纯文本补充（图片 alt）。</param>
    public InlineToken(InlineTokenKind kind, string value, string plain)
    {
      Kind = kind;
      Value = value;
      Plain = plain;
    }

    /// <summary>种类。</summary>
    public InlineTokenKind Kind { get; }

    /// <summary>BBCode 片段或原始文本。</summary>
    public string Value { get; }

    /// <summary>纯文本补充。</summary>
    public string Plain { get; }
  }

  /// <summary>已打开的行内标签。</summary>
  private readonly struct TagFrame
  {
    /// <summary>构造标签帧。</summary>
    /// <param name="kind">标签类型。</param>
    /// <param name="open">开始标签原文（重开时复用）。</param>
    /// <param name="close">结束标签原文。</param>
    public TagFrame(TagKind kind, string open, string close)
    {
      Kind = kind;
      Open = open;
      Close = close;
    }

    /// <summary>标签类型。</summary>
    public TagKind Kind { get; }

    /// <summary>开始标签原文。</summary>
    public string Open { get; }

    /// <summary>结束标签原文。</summary>
    public string Close { get; }
  }
}

/// <summary>
/// 行内渲染结果：BBCode 与纯文本两态，外加缺失图片计数。
/// </summary>
public sealed class InlineResult
{
  /// <summary>构造结果。</summary>
  /// <param name="bbcode">BBCode 文本。</param>
  /// <param name="plain">纯文本。</param>
  /// <param name="missingImages">本段缺失图片数量。</param>
  public InlineResult(string bbcode, string plain, int missingImages)
  {
    Bbcode = bbcode;
    Plain = plain;
    MissingImages = missingImages;
  }

  /// <summary>BBCode 文本。</summary>
  public string Bbcode { get; }

  /// <summary>纯文本（剥掉全部行内标记）。</summary>
  public string Plain { get; }

  /// <summary>本段缺失图片数量。</summary>
  public int MissingImages { get; }
}

/// <summary>
/// 图片引用解析：只接受 <c>images/文件名</c>，映射到 <c>src/assets/help/images/文件名</c>。
/// </summary>
/// <remarks>
/// 资源缺失时返回 null（调用方整行不输出），不报错、不产生占位。
/// </remarks>
internal static class HelpImages
{
  /// <summary>运行时资源前缀。</summary>
  public const string ResourcePrefix = "res://src/assets/help/";

  /// <summary>作者书写时固定使用的相对目录前缀。</summary>
  public const string AuthorPrefix = "images/";

  /// <summary>判断一整行是否只由一个图片引用构成（<c>![alt](images/x.png)</c>）。</summary>
  /// <param name="trimmed">已去首尾空白的行文本。</param>
  /// <returns>是图片行返回 true。</returns>
  public static bool IsImageLine(string trimmed) => TryParseLine(trimmed, out _, out _);

  /// <summary>解析一整行的图片引用；结构不完整时返回 false（按普通段落处理）。</summary>
  /// <param name="trimmed">已去首尾空白的行文本。</param>
  /// <param name="alt">输出：alt 文本。</param>
  /// <param name="source">输出：作者写的图片路径。</param>
  /// <returns>是图片行返回 true。</returns>
  public static bool TryParseLine(string trimmed, out string alt, out string source)
  {
    alt = string.Empty;
    source = string.Empty;
    if (trimmed.Length < 5 || trimmed[0] != '!' || trimmed[1] != '[')
    {
      return false;
    }
    int close = trimmed.IndexOf(']', 2);
    if (close < 0 || close + 1 >= trimmed.Length || trimmed[close + 1] != '(')
    {
      return false;
    }
    if (trimmed[^1] != ')')
    {
      return false;
    }
    alt = trimmed[2..close];
    source = trimmed[(close + 2)..^1];
    return source.Length > 0;
  }

  /// <summary>
  /// 把作者写的图片路径解析为 BBCode 图片标签。
  /// </summary>
  /// <param name="source">作者写的路径，如 <c>images/x.png</c>。</param>
  /// <param name="imageExists">存在性判定（参数相对 <c>src/assets/help</c>）。</param>
  /// <param name="line">出错行号。</param>
  /// <returns>存在时返回 <c>[img]res://src/assets/help/images/x.png[/img]</c>，缺失返回 null。</returns>
  /// <exception cref="HelpFormatException">路径不在 <c>images/</c> 下或不合法时抛出。</exception>
  public static string? Resolve(string source, Func<string, bool> imageExists, int line)
  {
    string text = source.Trim();
    if (text.StartsWith("./", StringComparison.Ordinal))
    {
      text = text[2..];
    }
    if (!text.StartsWith(AuthorPrefix, StringComparison.Ordinal))
    {
      throw new HelpFormatException(line, $"图片路径必须以 images/ 开头：{source}");
    }
    string name = text[AuthorPrefix.Length..];
    if (
      name.Length == 0
      || name.Contains('/')
      || name.Contains('\\')
      || name.Contains("..", StringComparison.Ordinal)
      || name[0] == '.'
    )
    {
      throw new HelpFormatException(line, $"图片路径不合法（只允许 images/文件名）：{source}");
    }
    if (!imageExists(text))
    {
      return null;
    }
    return "[img]" + ResourcePrefix + text + "[/img]";
  }
}
