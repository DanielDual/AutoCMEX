namespace AutoCMEX.Core.Help;

using System;
using System.Collections.Generic;

/// <summary>
/// 帮助内容的最小只读契约：面板与测试只依赖该接口，便于注入替身。
/// </summary>
public interface IHelpContent
{
  /// <summary>全部页面，已按分区顺序与分区内 order 排序。</summary>
  IReadOnlyList<HelpPage> Pages { get; }

  /// <summary>按 id 取页面；不存在时返回 null。</summary>
  /// <param name="id">页面 id。</param>
  /// <returns>页面或 null。</returns>
  HelpPage? GetPage(string id);

  /// <summary>按 id 取 BBCode 正文；不存在时返回空串。</summary>
  /// <param name="id">页面 id。</param>
  /// <returns>BBCode 正文。</returns>
  string GetBbcode(string id);

  /// <summary>按 id 取检索用纯文本；不存在时返回空串。</summary>
  /// <param name="id">页面 id。</param>
  /// <returns>正文纯文本。</returns>
  string GetPlainText(string id);

  /// <summary>按 id 取源 md 的 SHA-256；不存在时返回空串。</summary>
  /// <param name="id">页面 id。</param>
  /// <returns>小写十六进制哈希。</returns>
  string GetHash(string id);
}
