namespace AutoCMEX.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// 可切换界面主题的固定清单，以及主题资源解析 / 加载 / 语义色读取的唯一入口。
/// </summary>
/// <remarks>
/// <para>
/// 清单是硬编码的（不做目录扫描）：下拉顺序、显示名与资源路径都是界面契约，新增主题必须同步改这里。
/// 第一项恒为 <see cref="DefaultId"/>，它不加载任何资源，保证「默认」与接入主题前的观感完全一致。
/// </para>
/// <para>
/// 所有方法只做纯解析，不碰场景树，因此可以在无窗口的单元测试里覆盖全部分支；
/// 真正修改界面的动作由 <see cref="ThemeApplier"/> 通过委托注入。
/// </para>
/// </remarks>
public static class ThemeCatalog
{
  /// <summary>「默认（Godot 原生）」的稳定 ID：不加载主题资源，界面回到引擎默认外观。</summary>
  public const string DefaultId = "default";

  /// <summary>
  /// 主题中承载业务语义色的自定义类型名。
  /// </summary>
  /// <remarks>
  /// 它不对应任何 Control，只作为 <see cref="Theme.SetColor"/> 的类型容器，
  /// 供窗口底色与状态色（日志 / 录制 / GIF / WebSocket）通过 <see cref="AppPalette"/> 读取。
  /// </remarks>
  public const string AppColorsType = "AppColors";

  /// <summary>主窗口底色（<see cref="AppColorsType"/> 下的颜色项名）。</summary>
  public const string WindowBackground = "window_background";

  /// <summary>成功状态色（<see cref="AppColorsType"/> 下的颜色项名）。</summary>
  public const string StatusOk = "status_ok";

  /// <summary>警告状态色（<see cref="AppColorsType"/> 下的颜色项名）。</summary>
  public const string StatusWarn = "status_warn";

  /// <summary>错误状态色（<see cref="AppColorsType"/> 下的颜色项名）。</summary>
  public const string StatusError = "status_error";

  /// <summary>弱化文字色（<see cref="AppColorsType"/> 下的颜色项名）。</summary>
  public const string MutedText = "muted_text";

  /// <summary>强调色（<see cref="AppColorsType"/> 下的颜色项名）。</summary>
  public const string Accent = "accent";

  /// <summary>日志正文色（<see cref="AppColorsType"/> 下的颜色项名）。</summary>
  public const string LogText = "log_text";

  /// <summary>主题清单的固定顺序；下标 0 恒为 <see cref="DefaultId"/>。</summary>
  private static readonly ThemeOption[] Catalog =
  {
    new(DefaultId, "默认（Godot 原生）", false, null),
    new("dark-default", "暗夜（默认）", false, "res://src/assets/themes/dark-default.tres"),
    new("light-default", "浅色（默认）", true, "res://src/assets/themes/light-default.tres"),
    new("indigo-night", "靛蓝夜色", false, "res://src/assets/themes/indigo-night.tres"),
    new("high-contrast", "高对比", false, "res://src/assets/themes/high-contrast.tres"),
  };

  /// <summary>可选择的主题清单（顺序固定，第一项是默认项）。</summary>
  public static IReadOnlyList<ThemeOption> Options { get; } = Array.AsReadOnly(Catalog);

  /// <summary>默认主题项，供解析失败时回退。</summary>
  private static readonly ThemeOption DefaultOption = Catalog[0];

  /// <summary>
  /// 把持久化的主题 ID 解析成清单里的主题项。
  /// </summary>
  /// <remarks>
  /// <c>null</c>、空白或未知 ID 一律回退到默认项并给出非空错误说明，不抛异常；
  /// 调用方（如设置页）可据此提示「存档主题不可用」，同时保证界面仍能启动。
  /// </remarks>
  /// <param name="themeId">存档或下拉选中的主题 ID，允许为 <c>null</c>。</param>
  /// <param name="option">解析出的主题项；解析失败时为默认项。</param>
  /// <param name="error">成功时为 <c>null</c>，失败时为可直接展示给用户的回退原因。</param>
  /// <returns>ID 命中清单时为 <c>true</c>；否则为 <c>false</c>（此时 <paramref name="option"/> 为默认项）。</returns>
  public static bool TryResolve(string? themeId, out ThemeOption option, out string? error)
  {
    option = DefaultOption;
    error = null;

    var trimmed = themeId?.Trim();
    if (string.IsNullOrEmpty(trimmed))
    {
      error = "未指定界面主题，已回退到默认主题。";
      return false;
    }

    foreach (var candidate in Options)
    {
      if (string.Equals(candidate.Id, trimmed, StringComparison.Ordinal))
      {
        option = candidate;
        return true;
      }
    }

    error = $"未知的界面主题「{trimmed}」，已回退到默认主题。";
    return false;
  }

  /// <summary>
  /// 加载主题项对应的 <see cref="Theme"/> 资源。
  /// </summary>
  /// <remarks>
  /// <see cref="ThemeOption.ResourcePath"/> 为 <c>null</c> 时视为默认项：不加载资源，
  /// 返回 <c>true</c> 且 <paramref name="theme"/> 为 <c>null</c>，由调用方恢复引擎默认外观。
  /// 路径空白、资源不存在、加载抛异常或资源不是 <see cref="Theme"/> 时返回 <c>false</c>，
  /// 错误信息包含路径便于定位；此方法不抛异常。
  /// </remarks>
  /// <param name="option">待加载的主题项。</param>
  /// <param name="theme">加载成功的主题；默认项或失败时为 <c>null</c>。</param>
  /// <param name="error">成功时为 <c>null</c>，失败时为包含资源路径的非空错误说明。</param>
  /// <returns>默认项或加载成功时为 <c>true</c>，否则为 <c>false</c>。</returns>
  public static bool TryLoad(ThemeOption option, out Theme? theme, out string? error)
  {
    theme = null;

    if (option.ResourcePath == null)
    {
      error = null;
      return true;
    }

    var path = option.ResourcePath;
    if (string.IsNullOrWhiteSpace(path))
    {
      error = $"主题「{option.Id}」的资源路径为空：[{path}]，无法加载。";
      return false;
    }

    Theme? loaded;
    try
    {
      if (!ResourceLoader.Exists(path))
      {
        error = $"主题资源不存在：{path}（主题「{option.Id}」）。";
        return false;
      }

      loaded = ResourceLoader.Load<Theme>(path);
    }
    catch (Exception exception)
    {
      error = $"主题加载失败：{path}（主题「{option.Id}」）：{exception.Message}";
      return false;
    }

    if (loaded == null)
    {
      error = $"主题资源不是有效的 Theme：{path}（主题「{option.Id}」）。";
      return false;
    }

    theme = loaded;
    error = null;
    return true;
  }

  /// <summary>
  /// 解析主题定义的主窗口底色。
  /// </summary>
  /// <param name="theme">已加载的主题；为 <c>null</c>（默认主题）时直接返回 <paramref name="fallback"/>。</param>
  /// <param name="fallback">主题未定义底色时的兜底色（通常是启动时捕获的引擎清屏色）。</param>
  /// <returns>主题 <see cref="AppColorsType"/> 下的 <see cref="WindowBackground"/>；缺失时返回 <paramref name="fallback"/>。</returns>
  public static Color ResolveWindowBackground(Theme? theme, Color fallback)
  {
    if (theme == null || !theme.HasColor(WindowBackground, AppColorsType))
      return fallback;

    return theme.GetColor(WindowBackground, AppColorsType);
  }
}
