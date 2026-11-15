namespace AutoCMEX.Core.Help.Markdown;

using System;
using System.Collections.Generic;

/// <summary>
/// 单页 Markdown 转 BBCode 的主入口：front matter 解析 + 块级扫描 + 行内替换。
/// </summary>
/// <remarks>
/// 转换器无状态、不读文件：源文本、源路径与哈希都由调用方传入，
/// 以便生成器（读磁盘）与单测（内存字符串）共用同一份逻辑。
/// </remarks>
public sealed class HelpMarkdownConverter
{
  private readonly HelpBlockRenderer _blocks;

  /// <summary>构造转换器。</summary>
  /// <param name="links">站内链接解析器（需已知道全部页面 id）。</param>
  /// <param name="imageExists">图片存在性判定（参数相对 <c>src/assets/help</c>）。</param>
  /// <param name="typography">字号换算；缺省为缩放 1.0（生成器按项目主题缩放传入）。</param>
  public HelpMarkdownConverter(
    HelpLinkResolver links,
    Func<string, bool> imageExists,
    HelpTypography? typography = null
  )
  {
    var inline = new HelpInlineRenderer(links, imageExists);
    _blocks = new HelpBlockRenderer(inline, imageExists, typography);
  }

  /// <summary>
  /// 把一份完整的 md 文件（含 front matter）转成单页产物。
  /// </summary>
  /// <param name="text">文件全文。</param>
  /// <param name="sourcePath">源文件路径（仅用于诊断）。</param>
  /// <param name="hash">源文件字节的 SHA-256（小写十六进制）。</param>
  /// <returns>单页产物。</returns>
  /// <exception cref="HelpFormatException">front matter 非法或正文出现被拒绝语法时抛出。</exception>
  public ConvertedPage Convert(string text, string sourcePath, string hash)
  {
    var lines = SplitLines(text);
    var front = HelpFrontMatter.Parse(lines, out int bodyStart);
    var body = _blocks.Render(lines, bodyStart, front.Id);
    return new ConvertedPage
    {
      Id = front.Id,
      Title = front.Title,
      Section = front.Section,
      Order = front.Order,
      Summary = front.Summary,
      Tags = front.Tags,
      Headings = body.Headings,
      Bbcode = body.Bbcode,
      PlainText = body.PlainText,
      Hash = hash,
      SourcePath = sourcePath,
      MissingImageCount = body.MissingImages,
    };
  }

  /// <summary>
  /// 按行拆分文本：兼容 <c>\r\n</c>，并去掉文件开头的 BOM。
  /// </summary>
  /// <param name="text">文件全文。</param>
  /// <returns>逐行文本（不含换行符）。</returns>
  public static IReadOnlyList<string> SplitLines(string text)
  {
    string source = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    var lines = new List<string>();
    int start = 0;
    for (int i = 0; i < source.Length; i++)
    {
      if (source[i] != '\n')
      {
        continue;
      }
      int end = i > start && source[i - 1] == '\r' ? i - 1 : i;
      lines.Add(source[start..end]);
      start = i + 1;
    }
    lines.Add(source[start..]);
    return lines;
  }
}
