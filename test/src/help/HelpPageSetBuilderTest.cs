namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using AutoCMEX.Core.Help;
using AutoCMEX.Core.Help.Markdown;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 帮助页面集合装配单测：六字段校验、id 与路径一致、order 连续性与重复、
/// 链接目标存在性、图片缺失不报错、根目录 README 与下划线文件不参与装配、生成源码形态。
/// </summary>
public class HelpPageSetBuilderTest : TestClass
{
  /// <summary>每个分区目录名对应的 section 字段值。</summary>
  private static readonly Dictionary<string, string> SectionByDirectory = new()
  {
    ["start"] = "开始",
    ["merge"] = "整合",
    ["guessing"] = "猜测",
    ["info"] = "信息",
    ["recording"] = "录制",
    ["settings"] = "设置",
    ["run"] = "运行与排障",
  };

  private string _root = string.Empty;

  public HelpPageSetBuilderTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(Path.GetTempPath(), "AutoCMEX_Help_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_root);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  /// <summary>
  /// 写一页到临时目录：默认按 id 推导 section 与 order=1，允许逐项覆盖。
  /// </summary>
  /// <param name="id">页面 id（同时决定落盘路径）。</param>
  /// <param name="body">正文。</param>
  /// <param name="order">order 字段。</param>
  /// <param name="idField">覆盖 front matter 里的 id（用于制造 id 与路径不一致）。</param>
  /// <param name="section">覆盖 section 字段。</param>
  /// <param name="omitField">要省略的字段名。</param>
  /// <param name="summaryLine">完整替换 summary 行。</param>
  private void WritePage(
    string id,
    string body = "正文",
    int order = 1,
    string? idField = null,
    string? section = null,
    string? omitField = null,
    string? summaryLine = null
  )
  {
    string directory = id.Split('/')[0];
    string resolvedSection = section ?? SectionByDirectory[directory];
    var lines = new List<string>
    {
      "---",
      "id: " + (idField ?? id),
      "title: 标题",
      "section: " + resolvedSection,
      "order: " + order.ToString(System.Globalization.CultureInfo.InvariantCulture),
      summaryLine ?? "summary: 摘要",
      "tags: [标签一, 标签二]",
      "---",
      string.Empty,
      body,
    };
    if (omitField != null)
    {
      lines.RemoveAll(line => line.StartsWith(omitField + ":", StringComparison.Ordinal));
    }
    string path = Path.Combine(_root, id + ".md");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, string.Join("\n", lines));
  }

  /// <summary>用「图片全部存在」的判定跑一次装配。</summary>
  /// <param name="imageExists">图片存在性判定。</param>
  /// <returns>装配结果。</returns>
  private HelpPageSetBuildResult Build(Func<string, bool>? imageExists = null) =>
    new HelpPageSetBuilder(imageExists ?? (_ => false)).Build(_root);

  [Test]
  public void ValidPages_AreSortedBySectionThenOrder()
  {
    WritePage("start/index", order: 1);
    WritePage("start/getting-started", order: 2);
    WritePage("merge/overview", order: 1);

    var result = Build();

    result.Success.ShouldBeTrue();
    result.Pages.Count.ShouldBe(3);
    result.Pages[0].Id.ShouldBe("start/index");
    result.Pages[1].Id.ShouldBe("start/getting-started");
    result.Pages[2].Id.ShouldBe("merge/overview");
  }

  [Test]
  public void MissingField_IsReportedWithFileAndReason()
  {
    WritePage("start/index", omitField: "summary");

    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors.Count.ShouldBe(1);
    result.Errors[0].FilePath.ShouldBe("start/index.md");
    result.Errors[0].ToString().ShouldContain("缺少字段：summary");
  }

  [Test]
  public void ReservedControlChar_IsReportedWithLine()
  {
    WritePage("start/index", body: "第一行\n第二行含保留字符 \u0001link\u0001");

    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors[0].FilePath.ShouldBe("start/index.md");
    result.Errors[0].ToString().ShouldContain("U+0001");
  }

  [Test]
  public void IdMismatch_IsReported()
  {
    WritePage("start/index", idField: "start/other");

    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors[0].ToString().ShouldContain("id 与文件路径不一致");
    result.Errors[0].Line.ShouldBeGreaterThan(0);
  }

  [Test]
  public void UnknownSection_IsReported()
  {
    WritePage("start/index", section: "未知分区");

    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors[0].ToString().ShouldContain("未知分区");
  }

  [Test]
  public void OrderGap_IsReported()
  {
    WritePage("start/index", order: 1);
    WritePage("start/getting-started", order: 3);

    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors[0].ToString().ShouldContain("缺少 2");
  }

  [Test]
  public void OrderDuplicate_IsReported()
  {
    WritePage("start/index", order: 1);
    WritePage("start/getting-started", order: 1);

    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors[0].ToString().ShouldContain("order 重复");
  }

  [Test]
  public void LinkToMissingPage_IsReported()
  {
    WritePage("start/index", body: "见 [不存在](merge/none.md)。");

    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors[0].ToString().ShouldContain("站内链接目标不存在");
  }

  [Test]
  public void ResolvableLink_Passes()
  {
    WritePage("start/index", body: "见 [整合概览](../merge/overview.md)。");
    WritePage("merge/overview");

    var result = Build();

    result.Success.ShouldBeTrue();
    result
      .Pages[0]
      .Bbcode.ShouldContain(
        "[color="
          + HelpPlaceholders.Wrap(HelpPlaceholders.Link)
          + "][url=help://merge/overview]整合概览[/url][/color]"
      );
  }

  [Test]
  public void MissingImage_DoesNotReportError_AndDropsLine()
  {
    WritePage("start/index", body: "前段\n\n![图](images/a.png)\n\n后段");

    var result = Build();

    result.Success.ShouldBeTrue();
    result.MissingImageCount.ShouldBe(1);
    result.Pages[0].Bbcode.ShouldNotContain("[img]");
    result.Pages[0].Bbcode.ShouldNotContain("前段\n\n[img]");
  }

  [Test]
  public void ExistingImage_IsRendered()
  {
    WritePage("start/index", body: "![图](images/a.png)");

    var result = Build(rel => rel == "images/a.png");

    result.Success.ShouldBeTrue();
    result.MissingImageCount.ShouldBe(0);
    result.Pages[0].Bbcode.ShouldBe("[img]res://src/assets/help/images/a.png[/img]");
  }

  [Test]
  public void RootReadmeAndUnderscoreFiles_AreIgnored()
  {
    WritePage("start/index");
    File.WriteAllText(
      Path.Combine(_root, "README.md"),
      "---\nid: readme\ntitle: 说明\nsection: 开始\norder: 9\nsummary: 维护说明\ntags: []\n---\n\n正文"
    );
    File.WriteAllText(
      Path.Combine(_root, "start", "_notes.md"),
      "---\nid: start/_notes\ntitle: 笔记\nsection: 开始\norder: 8\nsummary: 笔记\ntags: []\n---\n\n正文"
    );

    var result = Build();

    result.Success.ShouldBeTrue();
    result.Pages.Count.ShouldBe(1);
    result.Pages[0].Id.ShouldBe("start/index");
  }

  [Test]
  public void MissingDocsRoot_IsReported()
  {
    var result = new HelpPageSetBuilder(_ => false).Build(Path.Combine(_root, "not-exist"));

    result.Success.ShouldBeFalse();
    result.Errors[0].Reason.ShouldContain("目录不存在");
  }

  [Test]
  public void EmptyDocsRoot_IsReported()
  {
    var result = Build();

    result.Success.ShouldBeFalse();
    result.Errors[0].Reason.ShouldContain("未找到任何帮助页面");
  }

  [Test]
  public void RenderSource_EmitsAutoGeneratedHeaderAndAllConstants()
  {
    WritePage("start/index", body: "## 小节\n\n正文");

    var result = Build();
    string source = HelpPageSetBuilder.RenderSource(result);

    source.ShouldStartWith("// <auto-generated />");
    source.ShouldContain("public static partial class HelpContent");
    source.ShouldContain("public static readonly string[] SectionOrder");
    source.ShouldContain("public static readonly HelpPage[] Pages");
    source.ShouldContain("IReadOnlyDictionary<string, string> Bbcode");
    source.ShouldContain("IReadOnlyDictionary<string, string> Plain");
    source.ShouldContain("IReadOnlyDictionary<string, string> Hashes");
    source.ShouldContain("@\"start/index\"");
  }
}
