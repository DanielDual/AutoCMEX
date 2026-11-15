namespace AutoCMEX.Core.Help;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// 帮助目录：由生成常量构建分区 → 有序页、id 索引与上一页/下一页关系。
/// </summary>
/// <remarks>
/// 构造期做降级校验而不是抛异常：未知分区、order 非法或重复、id 重复、缺少正文的页面
/// 一律跳过并记日志（内容问题不应让 UI 崩溃）。测试可注入替身页面，故本类不直接读生成常量，
/// 需要默认实现时调用 <see cref="FromGenerated"/>。
/// </remarks>
public sealed class HelpContentCatalog : IHelpContent
{
  /// <summary>默认打开页（「帮助中心」）。</summary>
  public const string DefaultPageId = "start/index";

  private readonly List<HelpPage> _pages = new();
  private readonly Dictionary<string, int> _indexById = new(StringComparer.Ordinal);
  private readonly IReadOnlyDictionary<string, string> _bbcode;
  private readonly IReadOnlyDictionary<string, string> _plainText;
  private readonly IReadOnlyDictionary<string, string> _hashes;

  /// <summary>构造目录并做降级校验。</summary>
  /// <param name="pages">页面元数据（顺序不限，构造后按分区顺序重排）。</param>
  /// <param name="bbcode">页面 id → BBCode 正文。</param>
  /// <param name="plainText">页面 id → 正文纯文本。</param>
  /// <param name="hashes">页面 id → 源 md 的 SHA-256。</param>
  /// <param name="log">日志；为空时用 <see cref="NullLog.Instance"/>。</param>
  public HelpContentCatalog(
    IReadOnlyList<HelpPage> pages,
    IReadOnlyDictionary<string, string> bbcode,
    IReadOnlyDictionary<string, string> plainText,
    IReadOnlyDictionary<string, string> hashes,
    ILog? log = null
  )
  {
    ILog logger = log ?? NullLog.Instance;
    _bbcode = bbcode;
    _plainText = plainText;
    _hashes = hashes;

    var sectionIndex = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int i = 0; i < HelpPage.SectionOrder.Count; i++)
    {
      sectionIndex[HelpPage.SectionOrder[i]] = i;
    }

    var seenIds = new HashSet<string>(StringComparer.Ordinal);
    var seenSlots = new HashSet<string>(StringComparer.Ordinal);
    foreach (var page in pages)
    {
      if (page == null)
      {
        continue;
      }
      if (page.Id.Length == 0)
      {
        logger.Warn("帮助内容：页面 id 为空，已跳过");
        continue;
      }
      if (!sectionIndex.ContainsKey(page.Section))
      {
        logger.Warn($"帮助内容：未知分区「{page.Section}」（页面 {page.Id}），已跳过");
        continue;
      }
      if (page.Order < 1)
      {
        logger.Warn($"帮助内容：页面 order 非法（页面 {page.Id}），已跳过");
        continue;
      }
      if (!seenIds.Add(page.Id))
      {
        logger.Warn($"帮助内容：页面 id 重复（页面 {page.Id}），已跳过");
        continue;
      }
      if (!seenSlots.Add(page.Section + "#" + page.Order))
      {
        logger.Warn($"帮助内容：分区内 order 重复（页面 {page.Id}），已跳过");
        continue;
      }
      if (!bbcode.ContainsKey(page.Id) || !hashes.ContainsKey(page.Id))
      {
        logger.Warn($"帮助内容：页面正文缺失（页面 {page.Id}），已跳过");
        continue;
      }
      _pages.Add(page);
    }

    _pages.Sort(
      (a, b) =>
      {
        int bySection = sectionIndex[a.Section].CompareTo(sectionIndex[b.Section]);
        return bySection != 0 ? bySection : a.Order.CompareTo(b.Order);
      }
    );
    for (int i = 0; i < _pages.Count; i++)
    {
      _indexById[_pages[i].Id] = i;
    }
  }

  /// <inheritdoc />
  public IReadOnlyList<HelpPage> Pages => _pages;

  /// <summary>用生成常量构造默认目录（运行时唯一入口）。</summary>
  /// <param name="log">日志；为空时用 <see cref="NullLog.Instance"/>。</param>
  /// <returns>默认目录。</returns>
  public static HelpContentCatalog FromGenerated(ILog? log = null) =>
    new(HelpContent.Pages, HelpContent.Bbcode, HelpContent.Plain, HelpContent.Hashes, log);

  /// <inheritdoc />
  public HelpPage? GetPage(string id) =>
    !string.IsNullOrEmpty(id) && _indexById.TryGetValue(id, out int index) ? _pages[index] : null;

  /// <inheritdoc />
  public string GetBbcode(string id) => TextOf(_bbcode, id);

  /// <inheritdoc />
  public string GetPlainText(string id) => TextOf(_plainText, id);

  /// <inheritdoc />
  public string GetHash(string id) => TextOf(_hashes, id);

  /// <summary>取页面在导航全局顺序里的下标。</summary>
  /// <param name="id">页面 id。</param>
  /// <returns>下标；不存在返回 -1。</returns>
  public int GetGlobalIndex(string id) =>
    !string.IsNullOrEmpty(id) && _indexById.TryGetValue(id, out int index) ? index : -1;

  /// <summary>取导航顺序中的上一页。</summary>
  /// <param name="id">当前页 id。</param>
  /// <returns>上一页；已是首页或 id 不存在时返回 null。</returns>
  public HelpPage? GetPrevious(string id)
  {
    int index = GetGlobalIndex(id);
    return index > 0 ? _pages[index - 1] : null;
  }

  /// <summary>取导航顺序中的下一页。</summary>
  /// <param name="id">当前页 id。</param>
  /// <returns>下一页；已是末页或 id 不存在时返回 null。</returns>
  public HelpPage? GetNext(string id)
  {
    int index = GetGlobalIndex(id);
    return index >= 0 && index + 1 < _pages.Count ? _pages[index + 1] : null;
  }

  /// <summary>取某个分区的全部页面（按 order 升序）。</summary>
  /// <param name="section">分区名。</param>
  /// <returns>该分区的页面列表；分区不存在时返回空列表。</returns>
  public IReadOnlyList<HelpPage> GetSectionPages(string section)
  {
    var result = new List<HelpPage>();
    foreach (var page in _pages)
    {
      if (string.Equals(page.Section, section, StringComparison.Ordinal))
      {
        result.Add(page);
      }
    }
    return result;
  }

  /// <summary>取默认页；缺失时退化为第一页。</summary>
  /// <returns>默认页；没有任何页面时返回 null。</returns>
  public HelpPage? GetDefaultPage() =>
    GetPage(DefaultPageId) ?? (_pages.Count > 0 ? _pages[0] : null);

  /// <summary>从 id → 文本字典里取值，缺失返回空串。</summary>
  /// <param name="source">字典。</param>
  /// <param name="id">页面 id。</param>
  /// <returns>文本或空串。</returns>
  private static string TextOf(IReadOnlyDictionary<string, string> source, string id) =>
    !string.IsNullOrEmpty(id) && source.TryGetValue(id, out string? value) ? value : string.Empty;
}
