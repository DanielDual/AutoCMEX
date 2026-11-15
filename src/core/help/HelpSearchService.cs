namespace AutoCMEX.Core.Help;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 帮助检索：归一化 + 加权子串匹配 + 片段截取（纯函数、无状态、无 UI 依赖）。
/// </summary>
/// <remarks>
/// <para>
/// 权重见 <see cref="WeightTitleExact"/> 等常量：标题完全一致 100 / 前缀 80 / 包含 60，
/// 摘要或标签 40，H2/H3 20，正文 10；同分按导航全局顺序（<see cref="IHelpContent.Pages"/> 的顺序）。
/// </para>
/// <para>
/// 匹配是大小写不敏感的整体子串匹配：中文不依赖分词，英文忽略大小写。
/// 整句查不到时按空白拆词回退（每个词都命中才算命中，得分取最弱的一项），
/// 这样「AI 模型配置」这类多词查询也能命中。
/// </para>
/// <para>
/// 每页最多返回 <see cref="MaxHitsPerPage"/> 条，命中词用 <see cref="HighlightStart"/> 包裹；
/// 28 页线性扫描远小于 1 ms，页面数超过 100 再考虑倒排索引（本批不做）。
/// </para>
/// </remarks>
public sealed class HelpSearchService
{
  /// <summary>标题完全一致的权重。</summary>
  public const int WeightTitleExact = 100;

  /// <summary>标题前缀命中的权重。</summary>
  public const int WeightTitlePrefix = 80;

  /// <summary>标题包含命中的权重。</summary>
  public const int WeightTitleContains = 60;

  /// <summary>摘要或标签命中的权重。</summary>
  public const int WeightSummaryOrTag = 40;

  /// <summary>H2/H3 命中的权重。</summary>
  public const int WeightHeading = 20;

  /// <summary>正文命中的权重。</summary>
  public const int WeightBody = 10;

  /// <summary>每页最多返回的命中条数。</summary>
  public const int MaxHitsPerPage = 3;

  /// <summary>片段在命中词两侧各保留的字符数。</summary>
  public const int SnippetRadius = 30;

  /// <summary>高亮开始标记（BBCode 底色）。</summary>
  /// <remarks>
  /// v1 的搜索结果列表是 <c>ItemList</c>（不渲染 BBCode），界面上不显示命中高亮，
  /// 这两个标记目前只被检索单测断言、不参与主题色解析；将来若把结果列表换成
  /// RichTextLabel，这里的硬编码颜色必须改成 <c>HelpPlaceholders</c> 的语义占位符。
  /// </remarks>
  public const string HighlightStart = "[bgcolor=#ffd166]";

  /// <summary>高亮结束标记（v1 不在界面上使用，见 <see cref="HighlightStart"/> 的说明）。</summary>
  public const string HighlightEnd = "[/bgcolor]";

  private const char Ellipsis = '…';

  private readonly IHelpContent _content;

  /// <summary>构造检索服务。</summary>
  /// <param name="content">帮助内容（测试可注入替身）。</param>
  public HelpSearchService(IHelpContent content)
  {
    _content = content;
  }

  /// <summary>
  /// 检索帮助内容。
  /// </summary>
  /// <param name="query">查询词（去首尾空白；空输入返回空结果，由 UI 展示常用页）。</param>
  /// <param name="limit">返回条数上限（默认 50）。</param>
  /// <returns>按得分降序、同分按导航顺序排列的命中列表。</returns>
  public IReadOnlyList<HelpSearchHit> Search(string query, int limit = 50)
  {
    string needle = (query ?? string.Empty).Trim();
    if (needle.Length == 0 || limit <= 0)
    {
      return Array.Empty<HelpSearchHit>();
    }

    IReadOnlyList<HelpPage> pages = _content.Pages;
    var hits = Collect(needle, pages, limit);
    if (hits.Count > 0)
    {
      return hits;
    }

    string[] terms = needle.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (terms.Length <= 1)
    {
      return hits;
    }
    return CollectAllTerms(terms, pages, limit);
  }

  /// <summary>按单个查询子串收集命中（每页最多 <see cref="MaxHitsPerPage"/> 条）。</summary>
  /// <param name="needle">查询子串。</param>
  /// <param name="pages">页面集合。</param>
  /// <param name="limit">返回上限。</param>
  /// <returns>命中列表。</returns>
  private List<HelpSearchHit> Collect(string needle, IReadOnlyList<HelpPage> pages, int limit)
  {
    var all = new List<Candidate>();
    for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
    {
      var page = pages[pageIndex];
      var pageHits = new List<Candidate>();
      CollectTitle(page, pageIndex, needle, pageHits);
      CollectSummary(page, pageIndex, needle, pageHits);
      CollectHeadings(page, pageIndex, needle, pageHits);
      CollectBody(page, pageIndex, needle, pageHits);
      pageHits.Sort(CompareCandidate);
      int take = Math.Min(pageHits.Count, MaxHitsPerPage);
      for (int i = 0; i < take; i++)
      {
        all.Add(pageHits[i]);
      }
    }
    all.Sort(CompareCandidate);
    return Project(all, limit);
  }

  /// <summary>按多个查询词收集命中：每个词都要命中，得分取最弱的一项。</summary>
  /// <param name="terms">查询词。</param>
  /// <param name="pages">页面集合。</param>
  /// <param name="limit">返回上限。</param>
  /// <returns>命中列表。</returns>
  private List<HelpSearchHit> CollectAllTerms(
    IReadOnlyList<string> terms,
    IReadOnlyList<HelpPage> pages,
    int limit
  )
  {
    var all = new List<Candidate>();
    for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
    {
      var page = pages[pageIndex];
      int weakest = int.MaxValue;
      string snippet = string.Empty;
      string plain = string.Empty;
      bool matchedAll = true;
      foreach (var term in terms)
      {
        if (!TryBestMatch(page, term, out int score, out string hitSnippet, out string hitPlain))
        {
          matchedAll = false;
          break;
        }
        if (score < weakest)
        {
          weakest = score;
          snippet = hitSnippet;
          plain = hitPlain;
        }
      }
      if (!matchedAll)
      {
        continue;
      }
      all.Add(
        new Candidate(
          new HelpSearchHit(page.Id, page.Title, snippet, plain, weakest),
          pageIndex,
          MaxHitsPerPage
        )
      );
    }
    all.Sort(CompareCandidate);
    return Project(all, limit);
  }

  /// <summary>取单页对单个查询词的最佳命中（标题 &gt; 摘要/标签 &gt; H2H3 &gt; 正文）。</summary>
  /// <param name="page">页面。</param>
  /// <param name="term">查询词。</param>
  /// <param name="score">输出：得分。</param>
  /// <param name="snippet">输出：带高亮的片段。</param>
  /// <param name="plain">输出：无高亮的片段。</param>
  /// <returns>命中返回 true。</returns>
  private bool TryBestMatch(
    HelpPage page,
    string term,
    out int score,
    out string snippet,
    out string plain
  )
  {
    score = 0;
    snippet = string.Empty;
    plain = string.Empty;
    var candidates = new List<Candidate>();
    CollectTitle(page, 0, term, candidates);
    CollectSummary(page, 0, term, candidates);
    CollectHeadings(page, 0, term, candidates);
    CollectBody(page, 0, term, candidates);
    if (candidates.Count == 0)
    {
      return false;
    }
    candidates.Sort(CompareCandidate);
    score = candidates[0].Hit.Score;
    snippet = candidates[0].Hit.Snippet;
    plain = candidates[0].Hit.PlainSnippet;
    return true;
  }

  /// <summary>收集标题命中。</summary>
  /// <param name="page">页面。</param>
  /// <param name="pageIndex">页面在导航顺序里的下标。</param>
  /// <param name="needle">查询子串。</param>
  /// <param name="sink">结果收集。</param>
  private static void CollectTitle(
    HelpPage page,
    int pageIndex,
    string needle,
    List<Candidate> sink
  )
  {
    int index = page.Title.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
    if (index < 0)
    {
      return;
    }
    int score = WeightTitleContains;
    if (string.Equals(page.Title, needle, StringComparison.OrdinalIgnoreCase))
    {
      score = WeightTitleExact;
    }
    else if (index == 0)
    {
      score = WeightTitlePrefix;
    }
    sink.Add(
      new Candidate(
        new HelpSearchHit(
          page.Id,
          page.Title,
          BuildSnippet(page.Title, index, needle.Length, true),
          BuildSnippet(page.Title, index, needle.Length, false),
          score
        ),
        pageIndex,
        0
      )
    );
  }

  /// <summary>收集摘要命中。</summary>
  /// <param name="page">页面。</param>
  /// <param name="pageIndex">页面下标。</param>
  /// <param name="needle">查询子串。</param>
  /// <param name="sink">结果收集。</param>
  private static void CollectSummary(
    HelpPage page,
    int pageIndex,
    string needle,
    List<Candidate> sink
  )
  {
    int index = page.Summary.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
    if (index >= 0)
    {
      sink.Add(
        new Candidate(
          new HelpSearchHit(
            page.Id,
            page.Title,
            BuildSnippet(page.Summary, index, needle.Length, true),
            BuildSnippet(page.Summary, index, needle.Length, false),
            WeightSummaryOrTag
          ),
          pageIndex,
          1
        )
      );
      return;
    }
    foreach (var tag in page.Tags)
    {
      index = tag.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
      if (index < 0)
      {
        continue;
      }
      sink.Add(
        new Candidate(
          new HelpSearchHit(
            page.Id,
            page.Title,
            BuildSnippet(tag, index, needle.Length, true),
            BuildSnippet(tag, index, needle.Length, false),
            WeightSummaryOrTag
          ),
          pageIndex,
          1
        )
      );
      return;
    }
  }

  /// <summary>收集 H2/H3 命中。</summary>
  /// <param name="page">页面。</param>
  /// <param name="pageIndex">页面下标。</param>
  /// <param name="needle">查询子串。</param>
  /// <param name="sink">结果收集。</param>
  private static void CollectHeadings(
    HelpPage page,
    int pageIndex,
    string needle,
    List<Candidate> sink
  )
  {
    foreach (var heading in page.Headings)
    {
      int index = heading.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
      if (index < 0)
      {
        continue;
      }
      sink.Add(
        new Candidate(
          new HelpSearchHit(
            page.Id,
            page.Title,
            BuildSnippet(heading, index, needle.Length, true),
            BuildSnippet(heading, index, needle.Length, false),
            WeightHeading
          ),
          pageIndex,
          2
        )
      );
      return;
    }
  }

  /// <summary>收集正文命中。</summary>
  /// <param name="page">页面。</param>
  /// <param name="pageIndex">页面下标。</param>
  /// <param name="needle">查询子串。</param>
  /// <param name="sink">结果收集。</param>
  private void CollectBody(HelpPage page, int pageIndex, string needle, List<Candidate> sink)
  {
    string body = Normalize(_content.GetPlainText(page.Id));
    int index = body.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
    if (index < 0)
    {
      return;
    }
    sink.Add(
      new Candidate(
        new HelpSearchHit(
          page.Id,
          page.Title,
          BuildSnippet(body, index, needle.Length, true),
          BuildSnippet(body, index, needle.Length, false),
          WeightBody
        ),
        pageIndex,
        3
      )
    );
  }

  /// <summary>把候选排序投影成命中列表。</summary>
  /// <param name="candidates">候选（已排序）。</param>
  /// <param name="limit">条数上限。</param>
  /// <returns>命中列表。</returns>
  private static List<HelpSearchHit> Project(List<Candidate> candidates, int limit)
  {
    int take = Math.Min(candidates.Count, limit);
    var hits = new List<HelpSearchHit>(take);
    for (int i = 0; i < take; i++)
    {
      hits.Add(candidates[i].Hit);
    }
    return hits;
  }

  /// <summary>候选排序：得分降序 → 导航顺序升序 → 页内命中位置升序。</summary>
  /// <param name="a">左值。</param>
  /// <param name="b">右值。</param>
  /// <returns>比较结果。</returns>
  private static int CompareCandidate(Candidate a, Candidate b)
  {
    int byScore = b.Hit.Score.CompareTo(a.Hit.Score);
    if (byScore != 0)
    {
      return byScore;
    }
    int byPage = a.PageIndex.CompareTo(b.PageIndex);
    return byPage != 0 ? byPage : a.WhereOrder.CompareTo(b.WhereOrder);
  }

  /// <summary>把换行与制表符归一成空格（长度不变，命中下标仍可直接用）。</summary>
  /// <param name="text">原文。</param>
  /// <returns>归一后的文本。</returns>
  private static string Normalize(string text)
  {
    if (text.Length == 0)
    {
      return text;
    }
    var sb = new StringBuilder(text.Length);
    foreach (var c in text)
    {
      sb.Append(c is '\n' or '\r' or '\t' ? ' ' : c);
    }
    return sb.ToString();
  }

  /// <summary>截取命中词前后各 <see cref="SnippetRadius"/> 字的片段。</summary>
  /// <param name="text">源文本（长度不变归一后的文本）。</param>
  /// <param name="matchIndex">命中词起点。</param>
  /// <param name="matchLength">命中词长度。</param>
  /// <param name="highlight">是否用 <see cref="HighlightStart"/> 包裹命中词。</param>
  /// <returns>片段文本。</returns>
  private static string BuildSnippet(string text, int matchIndex, int matchLength, bool highlight)
  {
    int start = Math.Max(0, matchIndex - SnippetRadius);
    int end = Math.Min(text.Length, matchIndex + matchLength + SnippetRadius);
    var sb = new StringBuilder(end - start + 16);
    if (start > 0)
    {
      sb.Append(Ellipsis);
    }
    sb.Append(text, start, matchIndex - start);
    if (highlight)
    {
      sb.Append(HighlightStart);
    }
    sb.Append(text, matchIndex, matchLength);
    if (highlight)
    {
      sb.Append(HighlightEnd);
    }
    sb.Append(text, matchIndex + matchLength, end - (matchIndex + matchLength));
    if (end < text.Length)
    {
      sb.Append(Ellipsis);
    }
    return sb.ToString();
  }

  /// <summary>检索候选（携带排序所需的页下标与页内位置）。</summary>
  private readonly struct Candidate
  {
    /// <summary>构造候选。</summary>
    /// <param name="hit">命中结果。</param>
    /// <param name="pageIndex">页面在导航顺序里的下标。</param>
    /// <param name="whereOrder">页内命中位置（标题 0 / 摘要 1 / H2H3 2 / 正文 3）。</param>
    public Candidate(HelpSearchHit hit, int pageIndex, int whereOrder)
    {
      Hit = hit;
      PageIndex = pageIndex;
      WhereOrder = whereOrder;
    }

    /// <summary>命中结果。</summary>
    public HelpSearchHit Hit { get; }

    /// <summary>页面在导航顺序里的下标。</summary>
    public int PageIndex { get; }

    /// <summary>页内命中位置。</summary>
    public int WhereOrder { get; }
  }
}

/// <summary>
/// 检索命中：一条命中对应「某页的某处文本」。
/// </summary>
public sealed class HelpSearchHit
{
  /// <summary>构造命中。</summary>
  /// <param name="pageId">页面 id。</param>
  /// <param name="title">页面标题。</param>
  /// <param name="snippet">带高亮标记的片段（供 RichTextLabel 渲染）。</param>
  /// <param name="plainSnippet">无高亮标记的片段（供 ItemList 等纯文本控件显示）。</param>
  /// <param name="score">得分（越大越靠前）。</param>
  public HelpSearchHit(string pageId, string title, string snippet, string plainSnippet, int score)
  {
    PageId = pageId;
    Title = title;
    Snippet = snippet;
    PlainSnippet = plainSnippet;
    Score = score;
  }

  /// <summary>页面 id（点击结果时据此开页）。</summary>
  public string PageId { get; }

  /// <summary>页面标题。</summary>
  public string Title { get; }

  /// <summary>带 <c>[bgcolor]</c> 高亮标记的片段。</summary>
  public string Snippet { get; }

  /// <summary>无高亮标记的片段。</summary>
  public string PlainSnippet { get; }

  /// <summary>得分。</summary>
  public int Score { get; }
}
