namespace AutoCMEX.UI;

using Chickensoft.GodotNodeInterfaces;
using Godot;

/// <summary>
/// 业务语义色（窗口底色 / 状态色 / 弱化文字 / 强调色 / 日志正文色）的读取入口。
/// </summary>
/// <remarks>
/// <para>
/// 这些颜色没有对应的 Control，统一放在主题的 <see cref="ThemeCatalog.AppColorsType"/> 自定义类型下；
/// 读取时必须经过本类，主题未提供该颜色时回退到调用方给的默认常量，
/// 从而保证「默认主题」观感与接入主题前完全一致。
/// </para>
/// <para>
/// 注意读取的是控件所在主题链（含根 <c>Window.Theme</c>），因此主题切换后无需重新绑定即可拿到新颜色。
/// </para>
/// </remarks>
public static class AppPalette
{
  /// <summary>
  /// 读取控件主题链中 <see cref="ThemeCatalog.AppColorsType"/> 下的语义色。
  /// </summary>
  /// <param name="control">用于解析主题链的控件；为 <c>null</c> 或已释放时返回 <paramref name="fallback"/>。</param>
  /// <param name="colorName">颜色项名，取 <see cref="ThemeCatalog"/> 中的颜色名常量。</param>
  /// <param name="fallback">主题未定义该颜色时的兜底色。</param>
  /// <returns>主题定义的颜色，或 <paramref name="fallback"/>。</returns>
  public static Color Get(Control control, string colorName, Color fallback)
  {
    if (control == null || !GodotObject.IsInstanceValid(control))
      return fallback;

    if (!control.HasThemeColor(colorName, ThemeCatalog.AppColorsType))
      return fallback;

    return control.GetThemeColor(colorName, ThemeCatalog.AppColorsType);
  }

  /// <summary>
  /// 读取节点所在主题链中 <see cref="ThemeCatalog.AppColorsType"/> 下的语义色。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 语义色挂在主题的自定义类型下，与具体是哪个控件无关：主题链只看节点所在的树位置，
  /// 因此传面板里的任意子控件都能取到根 <c>Window.Theme</c> 里的颜色。
  /// </para>
  /// <para>
  /// 调用方往往只有 <c>[Node]</c> 注入的接口适配器或可空的控件引用，故这里先把节点归一化成
  /// <see cref="Control"/>，再复用 <see cref="Get(Control, string, Color)"/> 的同一套解析逻辑。
  /// </para>
  /// </remarks>
  /// <param name="node">用于解析主题链的节点；为 <c>null</c>、已释放或不可归一化时返回 <paramref name="fallback"/>。</param>
  /// <param name="colorName">颜色项名，取 <see cref="ThemeCatalog"/> 中的颜色名常量。</param>
  /// <param name="fallback">主题未定义该颜色时的兜底色。</param>
  /// <returns>主题定义的颜色，或 <paramref name="fallback"/>。</returns>
  public static Color Get(Node? node, string colorName, Color fallback)
  {
    if (node == null || !GodotObject.IsInstanceValid(node))
      return fallback;

    var control = (node as Control) ?? ((node as INodeAdapter)?.TargetObj as Control);
    return control is null ? fallback : Get(control, colorName, fallback);
  }
}
