namespace AutoCMEX.Core.Help;

using System;
using System.Text;
using AutoCMEX.UI;
using Godot;

/// <summary>
/// 帮助正文的语义颜色解析：把生成物里的占位符换成当前主题下的十六进制颜色。
/// </summary>
/// <remarks>
/// <para>
/// 颜色全部来自主题的 <see cref="ThemeCatalog.AppColorsType"/>（经 <see cref="AppPalette"/> 读取，
/// 因此切主题后读到的是新值），主题未定义时回退到内置的暗夜配色，
/// 保证「默认（Godot 原生）」主题下依然可读。
/// </para>
/// <para>
/// 输出统一为 <c>#RRGGBBAA</c>（9 字符），各主题长度一致：重渲染时正文长度不变，滚动位置不会跳。
/// 提示块底色由状态色（不透明）叠加透明度得到，透明度按窗口底色明度选：亮底用 0.18、暗底用 0.30。
/// </para>
/// </remarks>
public static class HelpThemeColors
{
  /// <summary>主题里链接色的颜色项名。</summary>
  public const string LinkColorName = ThemeCatalog.HelpLink;

  /// <summary>主题里代码块底色的颜色项名。</summary>
  public const string CodeBackgroundColorName = ThemeCatalog.HelpCodeBackground;

  /// <summary>主题里表头底色的颜色项名。</summary>
  public const string TableHeaderBackgroundColorName = ThemeCatalog.HelpTableHeaderBackground;

  /// <summary>主题里表格线颜色的颜色项名。</summary>
  public const string TableBorderColorName = ThemeCatalog.HelpBorder;

  /// <summary>暗夜兜底：链接色。</summary>
  public static readonly Color FallbackLink = new("#5b9dff");

  /// <summary>暗夜兜底：代码块底色。</summary>
  public static readonly Color FallbackCodeBackground = new("#1b1e26");

  /// <summary>暗夜兜底：表头底色。</summary>
  public static readonly Color FallbackTableHeaderBackground = new("#3a4150");

  /// <summary>暗夜兜底：表格线。</summary>
  public static readonly Color FallbackTableBorder = new("#4a5462");

  /// <summary>暗夜兜底：窗口底色（用于判断亮/暗底）。</summary>
  public static readonly Color FallbackWindowBackground = new("#1e1e24");

  /// <summary>亮底主题下提示块的透明度。</summary>
  public const float LightBackgroundAlpha = 0.18f;

  /// <summary>暗底主题下提示块的透明度。</summary>
  public const float DarkBackgroundAlpha = 0.30f;

  /// <summary>把正文里的全部占位符替换成当前主题的颜色。</summary>
  /// <param name="bbcode">含占位符的 BBCode。</param>
  /// <param name="themeRoot">用于解析主题链的节点（传面板自身即可）。</param>
  /// <returns>替换后的 BBCode；无占位符时原样返回。</returns>
  public static string Resolve(string bbcode, Node? themeRoot)
  {
    if (string.IsNullOrEmpty(bbcode) || bbcode.IndexOf(HelpPlaceholders.Delimiter) < 0)
    {
      return bbcode;
    }

    var builder = new StringBuilder(bbcode.Length + 64);
    int index = 0;
    while (index < bbcode.Length)
    {
      char current = bbcode[index];
      if (current != HelpPlaceholders.Delimiter)
      {
        builder.Append(current);
        index++;
        continue;
      }

      int end = bbcode.IndexOf(HelpPlaceholders.Delimiter, index + 1);
      if (end < 0)
      {
        // 落单的分隔符：原样保留，避免吞字。
        builder.Append(current);
        index++;
        continue;
      }

      string name = bbcode[(index + 1)..end];
      // Godot 的 ToHtml(true) 不带前导 #，BBCode 需要 #RRGGBBAA 形式。
      builder.Append('#').Append(ColorFor(name, themeRoot).ToHtml(true));
      index = end + 1;
    }

    return builder.ToString();
  }

  /// <summary>按占位符名取颜色；未知名回退正文色。</summary>
  /// <param name="placeholderName">占位符名，取 <see cref="HelpPlaceholders"/> 常量。</param>
  /// <param name="themeRoot">主题链解析节点。</param>
  /// <returns>该语义对应的颜色。</returns>
  public static Color ColorFor(string placeholderName, Node? themeRoot) =>
    placeholderName switch
    {
      HelpPlaceholders.Link => Link(themeRoot),
      HelpPlaceholders.CodeBackground => CodeBackground(themeRoot),
      HelpPlaceholders.TableHeaderBackground => TableHeaderBackground(themeRoot),
      HelpPlaceholders.TableBorder => TableBorder(themeRoot),
      HelpPlaceholders.TipOk => TipBackground(ThemeCatalog.StatusOk, themeRoot),
      HelpPlaceholders.TipWarn => TipBackground(ThemeCatalog.StatusWarn, themeRoot),
      HelpPlaceholders.TipError => TipBackground(ThemeCatalog.StatusError, themeRoot),
      HelpPlaceholders.TipInfo => TipBackground(ThemeCatalog.Accent, themeRoot),
      _ => AppPalette.Get(themeRoot, ThemeCatalog.LogText, Colors.White),
    };

  /// <summary>链接文字颜色。</summary>
  /// <param name="themeRoot">主题链解析节点。</param>
  /// <returns>链接色；未定义时用暗夜兜底。</returns>
  public static Color Link(Node? themeRoot) =>
    AppPalette.Get(themeRoot, LinkColorName, FallbackLink);

  /// <summary>代码块矩形底色。</summary>
  /// <param name="themeRoot">主题链解析节点。</param>
  /// <returns>底色；未定义时用暗夜兜底。</returns>
  public static Color CodeBackground(Node? themeRoot) =>
    AppPalette.Get(themeRoot, CodeBackgroundColorName, FallbackCodeBackground);

  /// <summary>表头底色。</summary>
  /// <param name="themeRoot">主题链解析节点。</param>
  /// <returns>底色；未定义时用暗夜兜底。</returns>
  public static Color TableHeaderBackground(Node? themeRoot) =>
    AppPalette.Get(themeRoot, TableHeaderBackgroundColorName, FallbackTableHeaderBackground);

  /// <summary>表格线颜色。</summary>
  /// <param name="themeRoot">主题链解析节点。</param>
  /// <returns>线色；未定义时用暗夜兜底。</returns>
  public static Color TableBorder(Node? themeRoot) =>
    AppPalette.Get(themeRoot, TableBorderColorName, FallbackTableBorder);

  /// <summary>提示块底色：状态色叠加透明度，透明度按窗口底色明度决定。</summary>
  /// <param name="statusColorName">状态色名，取 <see cref="ThemeCatalog"/> 常量。</param>
  /// <param name="themeRoot">主题链解析节点。</param>
  /// <returns>带透明度的提示块底色。</returns>
  public static Color TipBackground(string statusColorName, Node? themeRoot)
  {
    Color status = AppPalette.Get(themeRoot, statusColorName, FallbackStatus(statusColorName));
    Color window = AppPalette.Get(
      themeRoot,
      ThemeCatalog.WindowBackground,
      FallbackWindowBackground
    );
    float luminance = 0.2126f * window.R + 0.7152f * window.G + 0.0722f * window.B;
    float alpha = luminance > 0.5f ? LightBackgroundAlpha : DarkBackgroundAlpha;
    return new Color(status.R, status.G, status.B, alpha);
  }

  /// <summary>状态色的暗夜兜底（与主题缺项时的观感一致）。</summary>
  /// <param name="statusColorName">状态色名。</param>
  /// <returns>兜底色。</returns>
  public static Color FallbackStatus(string statusColorName) =>
    statusColorName switch
    {
      ThemeCatalog.StatusOk => new Color("#9fd37f"),
      ThemeCatalog.StatusWarn => new Color("#f5b45a"),
      ThemeCatalog.StatusError => new Color("#f07b7b"),
      ThemeCatalog.Accent => new Color("#e8642c"),
      _ => Colors.White,
    };
}
