namespace AutoCMEX.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// 一次主题应用的结果：是否成功、实际生效的主题 ID、失败原因。
/// </summary>
/// <param name="Ok">主题是否按请求生效；未知 ID 或资源加载失败时为 <c>false</c>，但界面已回退到默认主题。</param>
/// <param name="AppliedId">实际生效的主题 ID；失败与默认回退时均为 <see cref="ThemeCatalog.DefaultId"/>。</param>
/// <param name="Error">成功时为 <c>null</c>，失败时为可直接写日志 / 提示的非空原因。</param>
public sealed record ThemeApplyResult(bool Ok, string AppliedId, string? Error);

/// <summary>
/// 把主题 ID 变成实际界面外观的应用器：解析 ID → 加载主题 → 通过注入的委托改界面。
/// </summary>
/// <remarks>
/// <para>
/// 本类刻意不持有窗口 / 场景节点：主题与窗口底色分别通过 <see cref="Action{T}"/> 注入，
/// 因此可以在无窗口的单元测试里覆盖全部成功 / 失败分支。
/// </para>
/// <para>
/// 不变式：任何失败（未知 ID、路径不存在、资源不是 <see cref="Theme"/>）都回退到「无主题 + 默认底色」，
/// 绝不留下半套主题。重复调用只重复应用同一结果，不会累积副作用。
/// </para>
/// </remarks>
public sealed class ThemeApplier
{
  private readonly Color _defaultBackground;
  private readonly Action<Theme?> _applyTheme;
  private readonly Action<Color> _applyBackground;
  private readonly IReadOnlyList<ThemeOption> _options;

  /// <summary>
  /// 构造应用器。
  /// </summary>
  /// <param name="defaultBackground">默认主题（或应用失败）时使用的窗口底色，通常为启动时捕获的引擎清屏色。</param>
  /// <param name="applyTheme">应用主题的动作；参数为 <c>null</c> 表示恢复引擎默认外观。</param>
  /// <param name="applyBackground">应用窗口底色的动作。</param>
  /// <param name="options">可解析的主题清单；为 <c>null</c> 时使用 <see cref="ThemeCatalog.Options"/>（测试可注入自定义清单）。</param>
  public ThemeApplier(
    Color defaultBackground,
    Action<Theme?> applyTheme,
    Action<Color> applyBackground,
    IReadOnlyList<ThemeOption>? options = null
  )
  {
    _defaultBackground = defaultBackground;
    _applyTheme = applyTheme ?? throw new ArgumentNullException(nameof(applyTheme));
    _applyBackground = applyBackground ?? throw new ArgumentNullException(nameof(applyBackground));
    _options = options ?? ThemeCatalog.Options;
  }

  /// <summary>
  /// 应用指定主题。
  /// </summary>
  /// <remarks>
  /// <c>null</c>、空白或 <see cref="ThemeCatalog.DefaultId"/> → 默认外观且 <c>Ok = true</c>；
  /// 清单内的非默认 ID → 加载成功则应用主题与主题底色，加载失败则回退默认并返回原因；
  /// 未知 ID → 回退默认且 <c>Ok = false</c>。此方法不抛异常（注入的委托自身异常除外）。
  /// </remarks>
  /// <param name="themeId">主题 ID，允许为 <c>null</c>（视为默认）。</param>
  /// <returns>应用结果；失败时界面已回退到默认主题。</returns>
  public ThemeApplyResult Apply(string? themeId)
  {
    var trimmed = themeId?.Trim();
    var resolved = FindById(trimmed);

    if (resolved != null && resolved.ResourcePath != null)
      return ApplyResourceTheme(resolved);

    ApplyDefault();

    if (
      resolved != null
      || string.IsNullOrEmpty(trimmed)
      || string.Equals(trimmed, ThemeCatalog.DefaultId, StringComparison.Ordinal)
    )
      return new ThemeApplyResult(true, ThemeCatalog.DefaultId, null);

    return new ThemeApplyResult(
      false,
      ThemeCatalog.DefaultId,
      $"未知的界面主题「{trimmed}」，已应用默认主题。"
    );
  }

  /// <summary>在清单中按 ID 精确查找主题项；未命中（含空 ID）时返回 <c>null</c>。</summary>
  /// <param name="themeId">已去除首尾空白的主题 ID。</param>
  /// <returns>命中的主题项，或 <c>null</c>。</returns>
  private ThemeOption? FindById(string? themeId)
  {
    if (string.IsNullOrEmpty(themeId))
      return null;

    foreach (var option in _options)
    {
      if (string.Equals(option.Id, themeId, StringComparison.Ordinal))
        return option;
    }

    return null;
  }

  /// <summary>加载并应用带资源路径的主题项；失败时先回退默认外观再返回原因。</summary>
  /// <param name="option">清单内的非默认主题项。</param>
  /// <returns>应用结果。</returns>
  private ThemeApplyResult ApplyResourceTheme(ThemeOption option)
  {
    if (!ThemeCatalog.TryLoad(option, out var theme, out var error))
    {
      ApplyDefault();
      return new ThemeApplyResult(false, ThemeCatalog.DefaultId, error);
    }

    _applyTheme(theme);
    _applyBackground(ThemeCatalog.ResolveWindowBackground(theme, _defaultBackground));
    return new ThemeApplyResult(true, option.Id, null);
  }

  /// <summary>恢复默认外观：清空主题并把窗口底色还原成默认底色。</summary>
  private void ApplyDefault()
  {
    _applyTheme(null);
    _applyBackground(_defaultBackground);
  }
}
