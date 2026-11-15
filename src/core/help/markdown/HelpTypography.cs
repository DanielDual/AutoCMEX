namespace AutoCMEX.Core.Help.Markdown;

using System;

/// <summary>
/// 帮助正文的字号换算：BBCode 的 <c>[font_size]</c> 是绝对像素、不受项目主题缩放影响，
/// 因此标题字号必须按「实际生效的正文字号 × 倍率」预先算好，不能写死。
/// </summary>
/// <remarks>
/// 换算依据：Godot 默认主题正文字号 16 px；项目 <c>project.godot</c> 的
/// <c>gui/theme/default_theme_scale</c> 会把实际正文字号放大（AutoCMEX 当前为 2.0，即 32 px）。
/// 标题倍率为渲染契约约定的 H2 ×1.4、H3 ×1.15，四舍五入到整数像素。
/// </remarks>
public sealed class HelpTypography
{
  /// <summary>Godot 默认主题的正文字号（像素）。</summary>
  public const int BaseFontSize = 16;

  /// <summary>H2 相对正文字号的倍率（渲染契约）。</summary>
  public const double H2Scale = 1.4;

  /// <summary>H3 相对正文字号的倍率（渲染契约）。</summary>
  public const double H3Scale = 1.15;

  /// <summary>主题缩放为 1.0 时的默认换算（单元测试与不读项目配置的调用方使用）。</summary>
  public static readonly HelpTypography Default = new(1.0);

  /// <summary>构造字号换算。</summary>
  /// <param name="themeScale">项目主题缩放（<c>gui/theme/default_theme_scale</c>，必须为正数）。</param>
  /// <exception cref="ArgumentOutOfRangeException">缩放小于等于 0 时抛出。</exception>
  public HelpTypography(double themeScale)
  {
    if (themeScale <= 0 || double.IsNaN(themeScale) || double.IsInfinity(themeScale))
    {
      throw new ArgumentOutOfRangeException(nameof(themeScale), "主题缩放必须是正数");
    }

    ThemeScale = themeScale;
    BodyFontSize = ToPixels(BaseFontSize * themeScale);
    H2FontSize = ToPixels(BodyFontSize * H2Scale);
    H3FontSize = ToPixels(BodyFontSize * H3Scale);
  }

  /// <summary>项目主题缩放。</summary>
  public double ThemeScale { get; }

  /// <summary>实际生效的正文字号（像素）。</summary>
  public int BodyFontSize { get; }

  /// <summary>H2 字号（像素）。</summary>
  public int H2FontSize { get; }

  /// <summary>H3 字号（像素）。</summary>
  public int H3FontSize { get; }

  private static int ToPixels(double value) =>
    (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
