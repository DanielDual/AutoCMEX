namespace AutoCMEX.Helpers;

/// <summary>
/// 字符串转义工具类，提供 CSV 和 BBCode 转义方法。
/// </summary>
public static class StringEscapeHelper
{
  /// <summary>
  /// 对 CSV 字段进行转义，处理逗号、双引号和换行符。
  /// </summary>
  /// <param name="field">原始字段值。</param>
  /// <returns>转义后的 CSV 安全字符串。</returns>
  public static string EscapeCsv(string field)
  {
    if (string.IsNullOrEmpty(field))
      return string.Empty;
    if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
      return $"\"{field.Replace("\"", "\"\"")}\"";
    return field;
  }

  /// <summary>
  /// 对 BBCode 字符串进行转义，将 <c>[</c> 替换为 <c>[lb]</c> 以避免 Godot RichTextLabel 误解析。
  /// </summary>
  /// <param name="text">原始文本。</param>
  /// <returns>转义后的 BBCode 安全字符串。</returns>
  public static string EscapeBbcode(string text)
  {
    if (string.IsNullOrEmpty(text))
      return string.Empty;
    return text.Replace("[", "[lb]");
  }
}
