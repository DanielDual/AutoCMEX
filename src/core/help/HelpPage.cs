namespace AutoCMEX.Core.Help;

using System;
using System.Collections.Generic;

/// <summary>
/// 帮助页面只读模型（由生成常量构造，运行时不再解析 Markdown）。
/// </summary>
public sealed class HelpPage
{
  /// <summary>构造页面模型。</summary>
  /// <param name="id">页面唯一键（相对 <c>docs/help/</c> 去掉 <c>.md</c>）。</param>
  /// <param name="title">导航与顶栏标题。</param>
  /// <param name="section">分区名。</param>
  /// <param name="order">分区内序号（从 1 起）。</param>
  /// <param name="summary">搜索摘要。</param>
  /// <param name="tags">搜索加权标签。</param>
  /// <param name="headings">H2/H3 文本（「本页目录」与搜索加权）。</param>
  public HelpPage(
    string id,
    string title,
    string section,
    int order,
    string summary,
    IReadOnlyList<string> tags,
    IReadOnlyList<string> headings
  )
  {
    Id = id;
    Title = title;
    Section = section;
    Order = order;
    Summary = summary;
    Tags = tags;
    Headings = headings;
  }

  /// <summary>页面唯一键，如 <c>merge/export</c>。</summary>
  public string Id { get; }

  /// <summary>导航与顶栏标题。</summary>
  public string Title { get; }

  /// <summary>分区名（导航分区顺序见 <see cref="SectionOrder"/>）。</summary>
  public string Section { get; }

  /// <summary>分区内序号（从 1 起连续）。</summary>
  public int Order { get; }

  /// <summary>搜索摘要。</summary>
  public string Summary { get; }

  /// <summary>搜索加权标签。</summary>
  public IReadOnlyList<string> Tags { get; }

  /// <summary>H2/H3 标题文本，按出现顺序。</summary>
  public IReadOnlyList<string> Headings { get; }

  /// <summary>
  /// 导航分区顺序（来自生成常量 <c>HelpContent.SectionOrder</c>，与内容同一份来源）。
  /// </summary>
  public static IReadOnlyList<string> SectionOrder => HelpContent.SectionOrder;
}
