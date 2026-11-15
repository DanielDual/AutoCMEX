namespace AutoCMEX.Core.Help.Markdown;

using System;
using System.Collections.Generic;

/// <summary>
/// 单页转换产物：元数据 + BBCode 正文 + 检索用纯文本 + 源文件哈希。
/// </summary>
/// <remarks>
/// 由 <see cref="HelpMarkdownConverter"/> 产出、由 <see cref="HelpPageSetBuilder"/> 汇总；
/// 运行时不经手该类型，生成的 <c>HelpContent</c> 常量由它序列化而来。
/// </remarks>
public sealed class ConvertedPage
{
  /// <summary>页面唯一键，等于相对 <c>docs/help/</c> 去掉 <c>.md</c> 的路径，如 <c>merge/export</c>。</summary>
  public string Id { get; init; } = string.Empty;

  /// <summary>导航与顶栏标题（front matter <c>title</c>）。</summary>
  public string Title { get; init; } = string.Empty;

  /// <summary>分区名（front matter <c>section</c>），取值见 <see cref="HelpPageSetBuilder.SectionOrder"/>。</summary>
  public string Section { get; init; } = string.Empty;

  /// <summary>分区内序号（front matter <c>order</c>，从 1 起连续）。</summary>
  public int Order { get; init; }

  /// <summary>搜索摘要（front matter <c>summary</c>）。</summary>
  public string Summary { get; init; } = string.Empty;

  /// <summary>搜索加权标签（front matter <c>tags</c>）。</summary>
  public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

  /// <summary>H2/H3 标题纯文本，按出现顺序；供「本页目录」与搜索加权。</summary>
  public IReadOnlyList<string> Headings { get; init; } = Array.Empty<string>();

  /// <summary>渲染后的 BBCode 正文（不含页标题）。</summary>
  public string Bbcode { get; init; } = string.Empty;

  /// <summary>BBCode 剥离后的正文纯文本，供检索与片段截取。</summary>
  public string PlainText { get; init; } = string.Empty;

  /// <summary>源 md 文件字节的 SHA-256（小写十六进制），供一致性测试发现内容漂移。</summary>
  public string Hash { get; init; } = string.Empty;

  /// <summary>源文件路径（仅用于诊断与日志）。</summary>
  public string SourcePath { get; init; } = string.Empty;

  /// <summary>本页因资源缺失而整行跳过的图片数量（构建统计，不参与运行时）。</summary>
  public int MissingImageCount { get; init; }
}
