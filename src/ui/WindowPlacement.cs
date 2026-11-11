namespace AutoCMEX.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// 主窗口几何的纯计算策略：尺寸抬升到最小尺寸、判定窗口是否仍可见、离屏时居中到主屏。
/// </summary>
/// <remarks>
/// <para>
/// 刻意不碰 <see cref="Window"/> / <see cref="DisplayServer"/>：屏幕可用区由调用方以
/// <see cref="Rect2I"/> 列表注入，因此本类可以在无窗口的单元测试里覆盖全部边界（规则 25）。
/// </para>
/// <para>
/// 判据是「与任一屏幕可用区的交集宽高都不小于 <see cref="MinVisibleExtent"/>」：
/// 只露出一条边的小窗口用户既看不见标题栏也拖不回来，按离屏处理。
/// </para>
/// </remarks>
public static class WindowPlacement
{
  /// <summary>判定「窗口仍可被用户找到」所需的最小可见宽高（像素，单边）。</summary>
  public const int MinVisibleExtent = 64;

  /// <summary>
  /// 把窗口尺寸抬到不小于最小尺寸（只放大不缩小，不做上限）。
  /// </summary>
  /// <param name="size">记忆 / 当前窗口尺寸。</param>
  /// <param name="minSize">窗口最小尺寸（通常来自 <c>Window.MinSize</c>）。</param>
  /// <returns>单边都不小于 <paramref name="minSize"/> 的尺寸。</returns>
  public static Vector2I ClampSize(Vector2I size, Vector2I minSize) =>
    new(Mathf.Max(size.X, minSize.X), Mathf.Max(size.Y, minSize.Y));

  /// <summary>
  /// 判断窗口是否仍在某块屏幕的可用区内留下足够的可操作面积。
  /// </summary>
  /// <param name="position">窗口左上角坐标（可为负，副屏在左侧时为负）。</param>
  /// <param name="size">窗口尺寸。</param>
  /// <param name="screenUsables">各屏幕的可用区域（已排除任务栏等）；空列表时返回 <c>false</c>。</param>
  /// <returns>与任一屏幕可用区的交集宽高均不小于 <see cref="MinVisibleExtent"/> 时为 <c>true</c>。</returns>
  public static bool IsVisibleEnough(
    Vector2I position,
    Vector2I size,
    IReadOnlyList<Rect2I> screenUsables
  )
  {
    var window = new Rect2I(position, size);
    foreach (var usable in screenUsables)
    {
      var overlap = window.Intersection(usable);
      if (overlap.Size.X >= MinVisibleExtent && overlap.Size.Y >= MinVisibleExtent)
        return true;
    }

    return false;
  }

  /// <summary>
  /// 把窗口居中到指定屏幕可用区。
  /// </summary>
  /// <param name="usable">目标屏幕可用区。</param>
  /// <param name="size">窗口尺寸。</param>
  /// <returns>居中后的窗口左上角坐标（窗口比屏幕大时为负偏移，由调用方自行决定是否再收敛）。</returns>
  public static Vector2I CenterIn(Rect2I usable, Vector2I size) =>
    usable.Position + new Vector2I((usable.Size.X - size.X) / 2, (usable.Size.Y - size.Y) / 2);

  /// <summary>
  /// 保证窗口落在可见区域：仍可见时原样返回，离屏时居中到第一块屏幕。
  /// </summary>
  /// <param name="position">记忆的窗口位置。</param>
  /// <param name="size">窗口尺寸。</param>
  /// <param name="screenUsables">各屏幕可用区；空列表时原样返回（由调用方兜底）。</param>
  /// <returns>可直接应用的窗口位置与尺寸。</returns>
  public static (Vector2I Position, Vector2I Size) EnsureOnScreen(
    Vector2I position,
    Vector2I size,
    IReadOnlyList<Rect2I> screenUsables
  )
  {
    if (screenUsables.Count == 0 || IsVisibleEnough(position, size, screenUsables))
      return (position, size);

    return (CenterIn(screenUsables[0], size), size);
  }
}
