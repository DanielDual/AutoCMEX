namespace AutoCMEX;

using System;
using AutoCMEX.Core.Help;
using AutoCMEX.Core.Help.Markdown;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 帮助 Markdown → BBCode 渲染契约单测：逐行对齐渲染契约的映射表，
/// 覆盖标题字号、行内标记、列表、表格、提示块、链接、图片、分隔线，
/// 以及 H4/HTML/嵌套列表/页内锚点的报错与行号。
/// </summary>
public class HelpMarkdownConverterTest : TestClass
{
  /// <summary>本组用例里「已存在」的页面 id 全集。</summary>
  private static readonly string[] PageIds =
  {
    "start/index",
    "start/getting-started",
    "merge/export",
    "guessing/overview",
  };

  /// <summary>front matter 占 8 行，正文第 10 行开始（第 9 行是空行）。</summary>
  private const int FirstBodyLine = 10;

  public HelpMarkdownConverterTest(Node testScene)
    : base(testScene) { }

  /// <summary>
  /// 把占位符名包成生成物里实际出现的记号（生成物只留占位符，颜色由主题在运行期解析）。
  /// </summary>
  /// <param name="name">占位符名，取 <see cref="HelpPlaceholders"/> 常量。</param>
  /// <returns>形如 <c>\u0001link\u0001</c> 的记号。</returns>
  private static string Ph(string name) => HelpPlaceholders.Wrap(name);

  /// <summary>拼出「单格表格 + 占位符底色」的期望 BBCode（代码块与提示块共用）。</summary>
  /// <param name="placeholderName">底色占位符名。</param>
  /// <param name="body">单元格内容（BBCode）。</param>
  /// <returns>期望的整块 BBCode。</returns>
  private static string SingleCellBbcode(string placeholderName, string body)
  {
    string background = Ph(placeholderName);
    return "[table=1][cell bg="
      + background
      + " border="
      + background
      + "]"
      + body
      + "[/cell][/table]";
  }

  /// <summary>
  /// 用固定 front matter 包装一段正文后转换，减少用例样板。
  /// </summary>
  /// <param name="body">正文（不含 front matter）。</param>
  /// <param name="imageExists">图片存在性判定，默认全部缺失。</param>
  /// <param name="id">页面 id。</param>
  /// <returns>单页产物。</returns>
  private static ConvertedPage Convert(
    string body,
    Func<string, bool>? imageExists = null,
    string id = "start/index",
    HelpTypography? typography = null
  )
  {
    string text =
      "---\nid: "
      + id
      + "\ntitle: 测试页\nsection: 开始\norder: 1\nsummary: 这是摘要\ntags: [标签一, 标签二]\n---\n\n"
      + body;
    var converter = new HelpMarkdownConverter(
      new HelpLinkResolver(PageIds),
      imageExists ?? (_ => false),
      typography
    );
    return converter.Convert(text, id + ".md", "hash-" + id);
  }

  [Test]
  public void FrontMatter_IsParsedIntoMetadata()
  {
    var page = Convert("正文");

    page.Id.ShouldBe("start/index");
    page.Title.ShouldBe("测试页");
    page.Section.ShouldBe("开始");
    page.Order.ShouldBe(1);
    page.Summary.ShouldBe("这是摘要");
    page.Tags.Count.ShouldBe(2);
    page.Tags[0].ShouldBe("标签一");
    page.Hash.ShouldBe("hash-start/index");
  }

  [Test]
  public void H1_IsNotRendered()
  {
    var page = Convert("# 测试页\n\n正文");

    page.Bbcode.ShouldNotContain("测试页");
    page.Bbcode.ShouldContain("正文");
  }

  [Test]
  public void H2AndH3_UseThemeRelativeFontSize()
  {
    // 缩放 1.0：正文 16，H2 = 16×1.4 = 22，H3 = 16×1.15 = 18；标题必须始终大于正文。
    var page = Convert("## 二级\n\n### 三级");

    page.Bbcode.ShouldContain("[font_size=22][b]二级[/b][/font_size]");
    page.Bbcode.ShouldContain("[font_size=18][b]三级[/b][/font_size]");
    page.Headings.Count.ShouldBe(2);
    page.Headings[0].ShouldBe("二级");
    page.Headings[1].ShouldBe("三级");
    HelpTypography.Default.H2FontSize.ShouldBeGreaterThan(HelpTypography.Default.BodyFontSize);
    HelpTypography.Default.H3FontSize.ShouldBeGreaterThan(HelpTypography.Default.BodyFontSize);
  }

  [Test]
  public void H2AndH3_FollowProjectThemeScale()
  {
    // 项目主题缩放 2.0：正文 32，H2 = 32×1.4 = 45，H3 = 32×1.15 = 37。
    // [font_size] 是绝对像素、不吃 default_theme_scale，写死 22/18 会让标题比正文还小。
    var typography = new HelpTypography(2.0);
    typography.BodyFontSize.ShouldBe(32);
    typography.H2FontSize.ShouldBe(45);
    typography.H3FontSize.ShouldBe(37);

    var page = Convert("## 二级\n\n### 三级", typography: typography);

    page.Bbcode.ShouldContain("[font_size=45][b]二级[/b][/font_size]");
    page.Bbcode.ShouldContain("[font_size=37][b]三级[/b][/font_size]");
    typography.H2FontSize.ShouldBeGreaterThan(typography.BodyFontSize);
    typography.H3FontSize.ShouldBeGreaterThan(typography.BodyFontSize);
  }

  [Test]
  public void Typography_RejectsNonPositiveScale()
  {
    Should.Throw<ArgumentOutOfRangeException>(() => new HelpTypography(0));
    Should.Throw<ArgumentOutOfRangeException>(() => new HelpTypography(-1));
    Should.Throw<ArgumentOutOfRangeException>(() => new HelpTypography(double.NaN));
    Should.Throw<ArgumentOutOfRangeException>(() => new HelpTypography(double.PositiveInfinity));
  }

  [Test]
  public void H4_ThrowsWithLineNumber()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("#### 太深"));

    exception.Line.ShouldBe(FirstBodyLine);
    exception.Message.ShouldContain("H4");
  }

  [Test]
  public void Paragraph_EscapesBracketToLb()
  {
    var page = Convert("列表显示为「[编号] 原文」。");

    page.Bbcode.ShouldBe("列表显示为「[lb]编号] 原文」。");
  }

  [Test]
  public void BoldAndItalic_UseStackTags()
  {
    var page = Convert("**粗体**与*斜体*");

    page.Bbcode.ShouldBe("[b]粗体[/b]与[i]斜体[/i]");
  }

  [Test]
  public void CrossNestedEmphasis_ClosesSequentially()
  {
    var page = Convert("**a *b** c*");

    page.Bbcode.ShouldBe("[b]a [i]b[/i][/b][i] c[/i]");
  }

  [Test]
  public void LoneAsterisk_StaysLiteral()
  {
    var page = Convert("两数相乘 2 * 3");

    page.Bbcode.ShouldBe("两数相乘 2 * 3");
  }

  [Test]
  public void InlineCode_UsesCodeTag()
  {
    var page = Convert("执行 `AutoCMEX.exe` 即可");

    page.Bbcode.ShouldBe("执行 [code]AutoCMEX.exe[/code] 即可");
    page.PlainText.ShouldBe("执行 AutoCMEX.exe 即可");
  }

  [Test]
  public void CodeFence_BecomesParagraphWithCode()
  {
    var page = Convert("```json\n{ \"a\": [1] }\n```");

    page.Bbcode.ShouldBe(
      SingleCellBbcode(HelpPlaceholders.CodeBackground, "[code]{ \"a\": [lb]1] }[/code]")
    );
  }

  [Test]
  public void UnclosedCodeFence_Throws()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("```json\n{}"));

    exception.Line.ShouldBe(FirstBodyLine);
    exception.Message.ShouldContain("未闭合");
  }

  [Test]
  public void UnorderedList_UsesUlTag()
  {
    var page = Convert("- 第一项\n- 第二项");

    page.Bbcode.ShouldBe("[ul]\n第一项\n第二项\n[/ul]");
  }

  [Test]
  public void OrderedList_UsesOlTag()
  {
    var page = Convert("1. 第一步\n2. 第二步");

    page.Bbcode.ShouldBe("[ol]\n第一步\n第二步\n[/ol]");
  }

  [Test]
  public void ListItems_SupportInlineMarkup()
  {
    var page = Convert("- 见 [快速开始](getting-started.md)\n- **加粗** 的项");

    page.Bbcode.ShouldContain(
      "[color="
        + Ph(HelpPlaceholders.Link)
        + "][url=help://start/getting-started]快速开始[/url][/color]"
    );
    page.Bbcode.ShouldContain("[b]加粗[/b]");
  }

  [Test]
  public void NestedList_ThrowsWithLineNumber()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("- 一级\n  - 二级"));

    exception.Line.ShouldBe(FirstBodyLine + 1);
    exception.Message.ShouldContain("嵌套列表");
  }

  [Test]
  public void Table_UsesTableCellsAndHeaderBackground()
  {
    var page = Convert("| 阶段 | 内容 |\n| --- | --- |\n| 创作 | 制作作品 |");

    string border = Ph(HelpPlaceholders.TableBorder);
    page.Bbcode.ShouldContain("[table=2]");
    page.Bbcode.ShouldContain(
      "[cell bg="
        + Ph(HelpPlaceholders.TableHeaderBackground)
        + " border="
        + border
        + "][b]阶段[/b][/cell]"
    );
    page.Bbcode.ShouldContain(
      "[cell border=" + border + "]创作[/cell][cell border=" + border + "]制作作品[/cell]"
    );
    page.Bbcode.ShouldEndWith("[/table]");
  }

  [Test]
  public void TableWithoutSeparator_Throws()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("| 阶段 |\n| 创作 |"));

    exception.Line.ShouldBe(FirstBodyLine);
    exception.Message.ShouldContain("分隔行");
  }

  [Test]
  public void TableColumnMismatch_Throws()
  {
    var exception = Should.Throw<HelpFormatException>(() =>
      Convert("| A | B |\n| --- | --- |\n| 1 |")
    );

    exception.Line.ShouldBe(FirstBodyLine + 2);
    exception.Message.ShouldContain("列数不一致");
  }

  [Test]
  public void TipBlocks_UsePlaceholderPerLabel()
  {
    // 提示→TipOk、注意→TipWarn、警告→TipError、技巧→TipInfo（颜色由主题解析，生成物只留占位符）。
    var note = Convert("> **注意**：切换 Boss 后需重新通知。");
    note.Bbcode.ShouldBe(
      SingleCellBbcode(HelpPlaceholders.TipWarn, "[b]注意[/b] 切换 Boss 后需重新通知。")
    );

    var warning = Convert("> **警告**：导入会清空当前表。");
    warning.Bbcode.ShouldBe(
      SingleCellBbcode(HelpPlaceholders.TipError, "[b]警告[/b] 导入会清空当前表。")
    );

    var tip = Convert("> **提示**：可以先备份。");
    tip.Bbcode.ShouldBe(SingleCellBbcode(HelpPlaceholders.TipOk, "[b]提示[/b] 可以先备份。"));

    var trick = Convert("> **技巧**：用快捷键。");
    trick.Bbcode.ShouldBe(SingleCellBbcode(HelpPlaceholders.TipInfo, "[b]技巧[/b] 用快捷键。"));
  }

  [Test]
  public void TipBlock_WithoutLabel_UsesInfoPlaceholder()
  {
    // 无标签的引用块按「信息」处理，同样不写死颜色。
    var page = Convert("> 只是一段引用。");

    page.Bbcode.ShouldBe(SingleCellBbcode(HelpPlaceholders.TipInfo, "只是一段引用。"));
  }

  [Test]
  public void InternalLink_ResolvesToHelpScheme()
  {
    var page = Convert("见 [导出合并工程](../merge/export.md)。");

    page.Bbcode.ShouldBe(
      "见 [color="
        + Ph(HelpPlaceholders.Link)
        + "][url=help://merge/export]导出合并工程[/url][/color]。"
    );
  }

  [Test]
  public void ExternalLink_IsKept()
  {
    var page = Convert("见 [官网](https://example.com/a?b=1)。");

    page.Bbcode.ShouldContain(
      "[color=" + Ph(HelpPlaceholders.Link) + "][url=https://example.com/a?b=1]官网[/url][/color]"
    );
  }

  [Test]
  public void AnchorLink_Throws()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("[跳转](merge/export.md#top)"));

    exception.Message.ShouldContain("锚点");
  }

  [Test]
  public void MissingInternalTarget_Throws()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("[跳转](nope/missing.md)"));

    exception.Message.ShouldContain("目标不存在");
  }

  [Test]
  public void Image_WhenAssetExists_EmitsImgTag()
  {
    var page = Convert("![总览](images/overview.png)", rel => rel == "images/overview.png");

    page.Bbcode.ShouldBe("[img]res://src/assets/help/images/overview.png[/img]");
    page.MissingImageCount.ShouldBe(0);
  }

  [Test]
  public void Image_WhenAssetMissing_DropsWholeLine()
  {
    var page = Convert("前一段\n\n![总览](images/overview.png)\n\n后一段");

    page.Bbcode.ShouldBe("前一段\n\n后一段");
    page.PlainText.ShouldNotContain("总览");
    page.MissingImageCount.ShouldBe(1);
  }

  [Test]
  public void Image_OutsideImagesDir_Throws()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("![图](pics/a.png)"));

    exception.Message.ShouldContain("images/");
  }

  [Test]
  public void HorizontalRule_BecomesHr()
  {
    var page = Convert("上\n\n---\n\n下");

    page.Bbcode.ShouldBe("上\n\n[hr]\n\n下");
  }

  [Test]
  public void Html_Throws()
  {
    var exception = Should.Throw<HelpFormatException>(() => Convert("正文 <b>加粗</b>"));

    exception.Line.ShouldBe(FirstBodyLine);
    exception.Message.ShouldContain("HTML");
  }

  [Test]
  public void PlainText_StripsInlineMarkup()
  {
    var page = Convert("**粗**、*斜*、`码` 与 [链接](../merge/export.md)");

    page.PlainText.ShouldBe("粗、斜、码 与 链接");
  }

  [Test]
  public void SplitLines_HandlesCrlfAndBom()
  {
    var lines = HelpMarkdownConverter.SplitLines("\uFEFFa\r\nb\n");

    lines.Count.ShouldBe(3);
    lines[0].ShouldBe("a");
    lines[1].ShouldBe("b");
    lines[2].ShouldBe(string.Empty);
  }

  [Test]
  public void EmptyTags_AreAllowed()
  {
    string text =
      "---\nid: start/index\ntitle: 空标签\nsection: 开始\norder: 1\nsummary: 摘要\ntags: []\n---\n\n正文";
    var converter = new HelpMarkdownConverter(new HelpLinkResolver(PageIds), _ => false);

    var page = converter.Convert(text, "start/index.md", "hash");

    page.Tags.ShouldBeEmpty();
  }
}
