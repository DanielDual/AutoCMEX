namespace AutoCMEX.Core.Help;

using System.Collections.Generic;

/// <summary>
/// 生成期写入、渲染期替换的语义颜色占位符。
/// </summary>
/// <remarks>
/// <para>
/// BBCode 没有「取主题色」的能力，因此生成物里不写死颜色，只写占位符；
/// 运行期由 <see cref="HelpThemeColors"/> 按控件所在主题链解析成具体颜色。
/// </para>
/// <para>
/// 占位符用不可见控制字符 <c>U+0001</c> 包裹，避免与正文里的 <c>{}</c>、<c>[]</c>、
/// 代码块内容或中文标点冲突；正文出现该控制字符的概率为零，且生成器拒绝含控制字符的正文。
/// </para>
/// </remarks>
public static class HelpPlaceholders
{
  /// <summary>占位符分隔符（不可见控制字符 U+0001）。</summary>
  public const char Delimiter = '';

  /// <summary>链接文字颜色。</summary>
  public const string Link = "link";

  /// <summary>代码块矩形底色。</summary>
  public const string CodeBackground = "code_bg";

  /// <summary>表格表头底色。</summary>
  public const string TableHeaderBackground = "table_header_bg";

  /// <summary>表格线颜色。</summary>
  public const string TableBorder = "border";

  /// <summary>「提示」块底色。</summary>
  public const string TipOk = "tip_ok";

  /// <summary>「注意」块底色。</summary>
  public const string TipWarn = "tip_warn";

  /// <summary>「警告」块底色。</summary>
  public const string TipError = "tip_error";

  /// <summary>「技巧」块底色。</summary>
  public const string TipInfo = "tip_info";

  /// <summary>全部占位符名（解析与测试用）。</summary>
  public static readonly IReadOnlyList<string> All = new[]
  {
    Link,
    CodeBackground,
    TableHeaderBackground,
    TableBorder,
    TipOk,
    TipWarn,
    TipError,
    TipInfo,
  };

  /// <summary>把占位符名包成生成物里实际出现的记号。</summary>
  /// <param name="name">占位符名，取本类常量。</param>
  /// <returns>形如 <c>\u0001link\u0001</c> 的记号。</returns>
  public static string Wrap(string name) => string.Concat(Delimiter, name, Delimiter);
}
