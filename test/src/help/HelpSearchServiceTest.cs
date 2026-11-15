namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Help;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 帮助检索单测：标题命中优先、中文子串、英文大小写不敏感、多词回退、无结果、
/// 条数截断、片段长度与高亮标记、每页命中上限。
/// </summary>
public class HelpSearchServiceTest : TestClass
{
  /// <summary>第 1 页的标签（常量数组参数会被 CA1861 提示，提为静态字段）。</summary>
  private static readonly string[] IndexTags = { "手册", "总览" };

  /// <summary>第 1 页的 H2/H3。</summary>
  private static readonly string[] IndexHeadings = { "使用路径" };

  /// <summary>第 2 页的标签。</summary>
  private static readonly string[] WebSocketTags = { "群聊", "连接" };

  /// <summary>第 2 页的 H2/H3。</summary>
  private static readonly string[] WebSocketHeadings = { "连接状态" };

  /// <summary>第 3 页的标签。</summary>
  private static readonly string[] AutoHostTags = { "托管", "Koishi" };

  /// <summary>第 3 页的 H2/H3。</summary>
  private static readonly string[] AutoHostHeadings = { "前置条件" };

  public HelpSearchServiceTest(Node testScene)
    : base(testScene) { }

  /// <summary>
  /// 内存替身内容：只关心检索需要的最小字段。
  /// </summary>
  private sealed class FakeContent : IHelpContent
  {
    private readonly List<HelpPage> _pages = new();
    private readonly Dictionary<string, string> _plain = new(StringComparer.Ordinal);

    /// <summary>追加一页。</summary>
    /// <param name="id">页面 id。</param>
    /// <param name="title">标题。</param>
    /// <param name="summary">摘要。</param>
    /// <param name="tags">标签。</param>
    /// <param name="headings">H2/H3。</param>
    /// <param name="body">正文纯文本。</param>
    /// <returns>自身，便于链式构造。</returns>
    public FakeContent Add(
      string id,
      string title,
      string summary,
      string[] tags,
      string[] headings,
      string body
    )
    {
      _pages.Add(new HelpPage(id, title, "开始", _pages.Count + 1, summary, tags, headings));
      _plain[id] = body;
      return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<HelpPage> Pages => _pages;

    /// <inheritdoc />
    public HelpPage? GetPage(string id) => _pages.Find(page => page.Id == id);

    /// <inheritdoc />
    public string GetBbcode(string id) => string.Empty;

    /// <inheritdoc />
    public string GetPlainText(string id) =>
      _plain.TryGetValue(id, out var text) ? text : string.Empty;

    /// <inheritdoc />
    public string GetHash(string id) => string.Empty;
  }

  /// <summary>造一个两页内容：标题命中页 + 正文命中页。</summary>
  /// <returns>检索服务。</returns>
  private static HelpSearchService CreateService()
  {
    var content = new FakeContent()
      .Add(
        "start/index",
        "帮助中心",
        "本手册说明 AutoCMEX 的使用方法。",
        IndexTags,
        IndexHeadings,
        "建议先读快速开始，再读术语与活动流程。"
      )
      .Add(
        "run/websocket",
        "WebSocket 状态面板",
        "连接状态与消息收发统计。",
        WebSocketTags,
        WebSocketHeadings,
        "Client 模式是否已连接，Server 模式是否运行中。"
      )
      .Add(
        "guessing/auto-host",
        "群聊托管猜测",
        "Koishi 插件把群消息交给程序判定。",
        AutoHostTags,
        AutoHostHeadings,
        "需要 AI 模型已配置，并且 WebSocket 已连接，插件已安装并在控制台启用。"
      );
    return new HelpSearchService(content);
  }

  [Test]
  public void TitleHit_RanksAboveBodyHit()
  {
    var hits = CreateService().Search("WebSocket");

    hits.Count.ShouldBeGreaterThan(0);
    hits[0].PageId.ShouldBe("run/websocket");
    hits[0].Score.ShouldBe(HelpSearchService.WeightTitlePrefix);
  }

  [Test]
  public void ChineseSubstring_Matches()
  {
    var hits = CreateService().Search("流程");

    hits.ShouldNotBeEmpty();
    hits[0].PageId.ShouldBe("start/index");
  }

  [Test]
  public void EnglishMatch_IsCaseInsensitive()
  {
    var hits = CreateService().Search("websocket");

    hits.ShouldNotBeEmpty();
    hits[0].PageId.ShouldBe("run/websocket");
  }

  [Test]
  public void MultiWordQuery_FallsBackToAllTerms()
  {
    var hits = CreateService().Search("AI 模型");

    hits.ShouldNotBeEmpty();
    hits[0].PageId.ShouldBe("guessing/auto-host");
  }

  [Test]
  public void NoResult_ReturnsEmpty()
  {
    CreateService().Search("这个词一定不存在").ShouldBeEmpty();
  }

  [Test]
  public void EmptyQuery_ReturnsEmpty()
  {
    CreateService().Search("   ").ShouldBeEmpty();
  }

  [Test]
  public void NonPositiveLimit_ReturnsEmpty()
  {
    CreateService().Search("连接", 0).ShouldBeEmpty();
  }

  [Test]
  public void Limit_Truncates()
  {
    var hits = CreateService().Search("连接", 1);

    hits.Count.ShouldBe(1);
  }

  [Test]
  public void Snippet_HasHighlightMarkersAndBoundedLength()
  {
    var hits = CreateService().Search("连接");

    hits.ShouldNotBeEmpty();
    bool sawHighlight = false;
    foreach (var hit in hits)
    {
      hit.Snippet.ShouldContain(HelpSearchService.HighlightStart);
      hit.Snippet.ShouldContain(HelpSearchService.HighlightEnd);
      hit.PlainSnippet.ShouldNotContain(HelpSearchService.HighlightStart);
      hit.Snippet.Length.ShouldBeLessThanOrEqualTo(HelpSearchService.SnippetRadius * 2 + 32);
      sawHighlight = true;
    }
    sawHighlight.ShouldBeTrue();
  }

  [Test]
  public void PerPageHits_AreCapped()
  {
    var hits = CreateService().Search("连接");

    var perPage = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var hit in hits)
    {
      perPage.TryGetValue(hit.PageId, out int count);
      perPage[hit.PageId] = count + 1;
    }
    foreach (var pair in perPage)
    {
      pair.Value.ShouldBeLessThanOrEqualTo(HelpSearchService.MaxHitsPerPage);
    }
  }

  [Test]
  public void TagHit_UsesSummaryWeight()
  {
    var hits = CreateService().Search("Koishi");

    hits.ShouldNotBeEmpty();
    hits[0].Score.ShouldBe(HelpSearchService.WeightSummaryOrTag);
  }
}
