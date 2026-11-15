namespace AutoCMEX.Core.Help.Markdown;

using System;
using System.Collections.Generic;

/// <summary>
/// 链接解析：站内相对 <c>.md</c> 链接 → <c>help://&lt;id&gt;</c>，外链保留 <c>http(s)://</c>，其余一律报错。
/// </summary>
/// <remarks>
/// 生成期就解析完站内链接，目标页不存在即失败——避免发布后出现死链。
/// 页内锚点（<c>#anchor</c>）不受支持（RichTextLabel 没有 anchor），出现即报错。
/// </remarks>
public sealed class HelpLinkResolver
{
  /// <summary>站内链接协议前缀；运行时按它分派到页面跳转。</summary>
  public const string HelpScheme = "help://";

  private readonly HashSet<string> _pageIds;

  /// <summary>用页面 id 全集构造解析器。</summary>
  /// <param name="pageIds">全部页面 id（装配期已知的集合）。</param>
  public HelpLinkResolver(IEnumerable<string> pageIds)
  {
    _pageIds = new HashSet<string>(pageIds, StringComparer.Ordinal);
  }

  /// <summary>判断页面 id 是否存在于本次装配的页面集合中。</summary>
  /// <param name="id">页面 id。</param>
  /// <returns>存在返回 true。</returns>
  public bool Contains(string id) => _pageIds.Contains(id);

  /// <summary>
  /// 解析一个链接目标。
  /// </summary>
  /// <param name="pageId">当前页 id（相对链接的基准）。</param>
  /// <param name="rawTarget">Markdown 括号里的原始目标。</param>
  /// <param name="line">出错时报告的行号。</param>
  /// <returns>解析结果：站内 <c>help://&lt;id&gt;</c> 或原样外链。</returns>
  /// <exception cref="HelpFormatException">目标为空、协议不支持、含锚点、越界或目标页不存在时抛出。</exception>
  public ResolvedLink Resolve(string pageId, string rawTarget, int line)
  {
    string target = rawTarget.Trim();
    if (target.Length == 0)
    {
      throw new HelpFormatException(line, "链接目标为空");
    }

    if (target.StartsWith(HelpScheme, StringComparison.Ordinal))
    {
      string id = target[HelpScheme.Length..].Trim();
      if (!_pageIds.Contains(id))
      {
        throw new HelpFormatException(line, $"站内链接目标不存在：{id}");
      }
      return new ResolvedLink(HelpScheme + id, true);
    }

    if (
      target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
      || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    )
    {
      return new ResolvedLink(target, false);
    }

    if (target[0] == '#')
    {
      throw new HelpFormatException(line, "不支持页内锚点链接（RichTextLabel 无 anchor）");
    }

    if (target.Contains('#'))
    {
      throw new HelpFormatException(line, $"不支持带锚点的链接：{target}");
    }

    if (target.Contains("://", StringComparison.Ordinal))
    {
      throw new HelpFormatException(
        line,
        $"不支持的链接协议：{target}（只允许站内相对 .md 与 http(s)）"
      );
    }

    if (target[0] == '/')
    {
      throw new HelpFormatException(line, $"站内链接必须使用相对路径，不能以 / 开头：{target}");
    }

    if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
    {
      throw new HelpFormatException(line, $"站内链接必须以 .md 结尾：{target}");
    }

    string relativeId = Normalize(pageId, target[..^3], line);
    if (!_pageIds.Contains(relativeId))
    {
      throw new HelpFormatException(line, $"站内链接目标不存在：{relativeId}（来自 {target}）");
    }
    return new ResolvedLink(HelpScheme + relativeId, true);
  }

  /// <summary>把「当前页目录 + 相对路径」归一成页面 id，并拦截越出帮助根目录的路径。</summary>
  /// <param name="pageId">当前页 id。</param>
  /// <param name="relative">去掉 <c>.md</c> 后缀的相对路径。</param>
  /// <param name="line">出错行号。</param>
  /// <returns>归一后的页面 id。</returns>
  private static string Normalize(string pageId, string relative, int line)
  {
    var segments = new List<string>();
    int slash = pageId.LastIndexOf('/');
    if (slash > 0)
    {
      segments.AddRange(pageId[..slash].Split('/'));
    }
    foreach (var segment in relative.Split('/'))
    {
      if (segment.Length == 0 || segment == ".")
      {
        continue;
      }
      if (segment == "..")
      {
        if (segments.Count == 0)
        {
          throw new HelpFormatException(line, $"相对链接越出帮助根目录：{relative}");
        }
        segments.RemoveAt(segments.Count - 1);
        continue;
      }
      segments.Add(segment);
    }
    return string.Join('/', segments);
  }
}

/// <summary>
/// 链接解析结果。
/// </summary>
public sealed class ResolvedLink
{
  /// <summary>构造解析结果。</summary>
  /// <param name="url">写进 BBCode <c>[url=...]</c> 的目标。</param>
  /// <param name="isInternal">是否为站内页面跳转。</param>
  public ResolvedLink(string url, bool isInternal)
  {
    Url = url;
    IsInternal = isInternal;
  }

  /// <summary>写进 BBCode <c>[url=...]</c> 的目标（站内为 <c>help://&lt;id&gt;</c>）。</summary>
  public string Url { get; }

  /// <summary>是否为站内页面跳转（<c>help://</c>）。</summary>
  public bool IsInternal { get; }
}
