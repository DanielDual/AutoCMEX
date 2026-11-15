namespace AutoCMEX;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutoCMEX.Core.Help;
using AutoCMEX.Core.Help.Markdown;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 帮助内容一致性测试：现场读取 <c>docs/help</c> 重新装配一遍，与编译期常量逐页比对
/// （页数 / id 集合与顺序 / 每页元数据 / 每页 SHA-256 / 每页 BBCode / 每页纯文本）。
/// </summary>
/// <remarks>
/// 这是「内容与实现漂移」的兜底：改了 md 但没有重新构建时，本组测试必然失败。
/// 判定口径必须与生成器一致——图片存在性用 <c>src/assets/help</c> 下的真实文件判定。
/// </remarks>
public partial class HelpContentIntegrityTest : TestClass
{
  /// <summary>帮助正文根目录（测试进程工作目录即仓库根）。</summary>
  private const string DocsRoot = "docs/help";

  /// <summary>帮助图片资源根目录。</summary>
  private const string AssetsRoot = "src/assets/help";

  /// <summary>线上内容的页数（内容规范，新增页面需同步本断言与文档）。</summary>
  private const int ExpectedPageCount = 28;

  /// <summary>各分区页数，顺序与 <see cref="HelpContent.SectionOrder"/> 一致。</summary>
  private static readonly int[] ExpectedSectionCounts = { 4, 5, 6, 4, 2, 4, 3 };

  /// <summary>编译期生成的正则：裸十六进制颜色（<c>#RRGGBB</c> 或 <c>#RRGGBBAA</c>）。</summary>
  [GeneratedRegex("#[0-9a-fA-F]{8}\\b|#[0-9a-fA-F]{6}\\b", RegexOptions.CultureInvariant)]
  private static partial Regex BareHexColor();

  private HelpPageSetBuildResult _result = default!;

  public HelpContentIntegrityTest(Node testScene)
    : base(testScene) { }

  /// <summary>每次用例前重新装配一遍 <c>docs/help</c>。</summary>
  /// <remarks>
  /// 字号换算必须与生成器一致：生成器读 <c>project.godot</c> 的
  /// <c>gui/theme/default_theme_scale</c>，这里读同一个项目设置，
  /// 否则标题字号不同会让「逐页 BBCode 相等」误报。
  /// </remarks>
  [Setup]
  public void Setup()
  {
    var scaleSetting = ProjectSettings.GetSetting("gui/theme/default_theme_scale", 1.0);
    double themeScale = scaleSetting.VariantType is Variant.Type.Float or Variant.Type.Int
      ? scaleSetting.AsDouble()
      : 1.0;
    var builder = new HelpPageSetBuilder(
      relative => File.Exists(Path.Combine(AssetsRoot, relative)),
      new HelpTypography(themeScale)
    );
    _result = builder.Build(DocsRoot);
  }

  [Test]
  public void Content_AssemblesWithoutDiagnostics()
  {
    _result.Errors.ShouldBeEmpty(
      "docs/help 存在非法内容，首个问题："
        + (_result.Errors.Count > 0 ? _result.Errors[0].ToString() : "-")
    );
    _result.Success.ShouldBeTrue();
  }

  [Test]
  public void Content_Has28PagesInSevenSections()
  {
    _result.Pages.Count.ShouldBe(ExpectedPageCount);
    HelpContent.Pages.Length.ShouldBe(ExpectedPageCount);
    HelpContent.SectionOrder.Length.ShouldBe(7);

    for (int i = 0; i < HelpContent.SectionOrder.Length; i++)
    {
      string section = HelpContent.SectionOrder[i];
      _result.Pages.Count(page => page.Section == section).ShouldBe(ExpectedSectionCounts[i]);
    }
  }

  [Test]
  public void GeneratedPageIds_MatchDocsInOrder()
  {
    var expectedIds = _result.Pages.Select(page => page.Id).ToArray();
    var actualIds = HelpContent.Pages.Select(page => page.Id).ToArray();

    actualIds.ShouldBe(
      expectedIds,
      "生成常量的页面 id 顺序与 docs/help 不一致，请重新 dotnet build。"
    );
  }

  [Test]
  public void GeneratedMetadata_MatchesDocs()
  {
    for (int i = 0; i < _result.Pages.Count; i++)
    {
      var expected = _result.Pages[i];
      var actual = HelpContent.Pages[i];
      actual.Id.ShouldBe(expected.Id);
      actual.Title.ShouldBe(expected.Title, "页面 " + expected.Id + " 的标题不一致");
      actual.Section.ShouldBe(expected.Section, "页面 " + expected.Id + " 的分区不一致");
      actual.Order.ShouldBe(expected.Order, "页面 " + expected.Id + " 的 order 不一致");
      actual.Summary.ShouldBe(expected.Summary, "页面 " + expected.Id + " 的摘要不一致");
      actual.Tags.ShouldBe(expected.Tags, "页面 " + expected.Id + " 的 tags 不一致");
      actual.Headings.ShouldBe(expected.Headings, "页面 " + expected.Id + " 的标题列表不一致");
    }
  }

  [Test]
  public void GeneratedHashes_MatchDocs()
  {
    foreach (var page in _result.Pages)
    {
      HelpContent
        .Hashes.ContainsKey(page.Id)
        .ShouldBeTrue("生成常量缺少页面 " + page.Id + " 的哈希");
      HelpContent
        .Hashes[page.Id]
        .ShouldBe(
          page.Hash,
          "页面 " + page.Id + " 的源文件哈希与 docs/help 不一致，请重新 dotnet build。"
        );
    }
  }

  [Test]
  public void GeneratedBbcode_MatchesDocs()
  {
    foreach (var page in _result.Pages)
    {
      HelpContent
        .Bbcode.ContainsKey(page.Id)
        .ShouldBeTrue("生成常量缺少页面 " + page.Id + " 的正文");
      HelpContent
        .Bbcode[page.Id]
        .ShouldBe(
          page.Bbcode,
          "页面 " + page.Id + " 的 BBCode 与 docs/help 不一致，请重新 dotnet build。"
        );
    }
  }

  /// <summary>
  /// 生成物里不得出现写死的十六进制颜色：颜色归主题，生成期只写占位符。
  /// </summary>
  /// <remarks>
  /// 正则对全部页面跑：出现 <c>#RRGGBB</c> / <c>#RRGGBBAA</c> 即失败，提示改为
  /// <see cref="HelpPlaceholders"/> 里的语义占位符（解析见 <see cref="HelpThemeColors"/>）。
  /// </remarks>
  [Test]
  public void GeneratedBbcode_HasNoBareHexColors()
  {
    foreach (var page in HelpContent.Pages)
    {
      string bbcode = HelpContent.Bbcode[page.Id];
      var match = BareHexColor().Match(bbcode);
      match.Success.ShouldBeFalse(
        "页面 "
          + page.Id
          + " 的生成 BBCode 出现写死颜色 "
          + match.Value
          + "，请改用 HelpPlaceholders 占位符后重新 dotnet build。"
      );
    }
  }

  [Test]
  public void GeneratedPlainText_MatchesDocs()
  {
    foreach (var page in _result.Pages)
    {
      HelpContent
        .Plain.ContainsKey(page.Id)
        .ShouldBeTrue("生成常量缺少页面 " + page.Id + " 的纯文本");
      HelpContent
        .Plain[page.Id]
        .ShouldBe(
          page.PlainText,
          "页面 " + page.Id + " 的纯文本与 docs/help 不一致，请重新 dotnet build。"
        );
    }
  }

  [Test]
  public void Catalog_BuildsFromGeneratedWithAllPages()
  {
    var catalog = HelpContentCatalog.FromGenerated();

    catalog.Pages.Count.ShouldBe(ExpectedPageCount);
    catalog.GetDefaultPage()!.Id.ShouldBe(HelpContentCatalog.DefaultPageId);
    foreach (var section in HelpContent.SectionOrder)
    {
      catalog.GetSectionPages(section).Count.ShouldBeGreaterThan(0);
    }
  }
}
