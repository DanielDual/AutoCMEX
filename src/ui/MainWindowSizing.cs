namespace AutoCMEX.UI;

using Godot;

/// <summary>
/// 主窗口缩放策略：解除 <see cref="Chickensoft.GameTools.Displays.Display.LookGood"/> 施加的尺寸上限，
/// 让主窗口可以自由拉伸（含最大化）。
/// </summary>
public static class MainWindowSizing
{
  /// <summary>
  /// 允许主窗口自由缩放：取消目标窗口的「不可缩放」标记，并清空最大尺寸。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 必须在 <c>LookGood</c> 之后调用：<c>LookGood</c> 在收尾阶段才写入 <c>MinSize</c>/<c>MaxSize</c>，
  /// 提前设置会被它覆盖。
  /// </para>
  /// <para>
  /// GameTools 把 <c>MaxSize</c> 限制为屏幕逻辑分辨率的 75%，这只是「游戏窗口推荐值」，
  /// 并非显示缩放要求，因此可以安全解除。
  /// </para>
  /// <para>
  /// 不变式：只改 <c>MaxSize</c> 与 <c>Unresizable</c>，
  /// 不动 <c>Size</c>/<c>Position</c>/<c>MinSize</c>/<c>Mode</c>/<c>ContentScale*</c>。
  /// </para>
  /// </remarks>
  /// <param name="window">目标窗口，通常是主窗口。</param>
  public static void AllowFreeResize(Window window)
  {
    window.Unresizable = false;
    // Godot 语义：MaxSize 为 0 表示不限制
    window.MaxSize = Vector2I.Zero;
  }
}
