namespace AutoCMEX.UI;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 主窗口几何的接线路：启动时把 <c>ui_layout.json</c> 的记忆几何应用到真实窗口，退出时把当前几何写回模型。
/// </summary>
/// <remarks>
/// <para>
/// 只做「窗口 ↔ 模型」的搬运与最小校验：尺寸抬升到 <c>Window.MinSize</c>、离屏位置居中（见
/// <see cref="WindowPlacement"/>）。不碰 <c>MaxSize</c>——自由缩放上限由 <see cref="MainWindowSizing"/>
/// 负责解除，两者互不覆盖。
/// </para>
/// <para>
/// 最大化时的还原矩形不能直接读 <c>Window.Size</c>（各平台在最大化状态下返回值不一致），
/// 因此由 <see cref="Track"/> 在窗口处于普通态时持续记录，<see cref="Capture"/> 在最大化 / 最小化时
/// 回退到这份记录：下次启动仍能还原到用户最大化之前的位置。
/// </para>
/// <para>
/// 模型只在这里被写，且不调用 <c>TriggerAutoSave</c>：几何变化可经 <see cref="Track"/> 高频发生，
/// 落盘统一交给关窗时的 <c>SaveAll</c>/<c>DataManager.Dispose</c>（规则 23 的唯一持久化入口）。
/// </para>
/// </remarks>
public sealed class WindowLayoutState
{
  /// <summary>被接线的窗口适配器；<c>null</c> 时全链路空安全（无法适配 / 测试模式）。</summary>
  private readonly IWindow? _window;

  /// <summary>布局记忆模型（由构造参数的 <see cref="DataManager"/> 取出，避免长期持有可释放的管理器）。</summary>
  private readonly UiLayoutData _uiLayout;

  /// <summary>日志器（只用于记录应用 / 落点，便于排查多屏与最小尺寸夹紧问题）。</summary>
  private readonly ILog _log;

  /// <summary>屏幕可用区来源：默认读 <see cref="DisplayServer"/>，测试注入固定矩形。</summary>
  private readonly Func<IReadOnlyList<Rect2I>> _screens;

  /// <summary>最近一次普通态（非最大化 / 非最小化）下的窗口尺寸，用于最大化时取还原矩形。</summary>
  private Vector2I _normalSize;

  /// <summary>最近一次普通态下的窗口位置。</summary>
  private Vector2I _normalPosition;

  /// <summary><see cref="Apply"/> 结束时的尺寸快照，用于 <see cref="Capture"/> 判定几何是否被用户改过。</summary>
  private Vector2I _initialSize;

  /// <summary><see cref="Apply"/> 结束时的位置快照。</summary>
  private Vector2I _initialPosition;

  /// <summary><see cref="Apply"/> 结束时的最大化标志快照。</summary>
  private bool _initialMaximized;

  /// <summary>是否已建立初值快照；未 <see cref="Apply"/> 过时 <see cref="Capture"/> 直接按当前几何写。</summary>
  private bool _hasInitial;

  /// <summary>
  /// 创建接线器。
  /// </summary>
  /// <param name="window">主窗口适配器；无法适配时传 <c>null</c>，所有方法退化为空操作。</param>
  /// <param name="dataManager">布局数据的持有者；本类只取它的模型。</param>
  /// <param name="log">日志器。</param>
  /// <param name="screens">可选的屏幕可用区来源（测试注入固定矩形）；为空时读 <see cref="DisplayServer"/>。</param>
  public WindowLayoutState(
    IWindow? window,
    DataManager dataManager,
    ILog log,
    Func<IReadOnlyList<Rect2I>>? screens = null
  )
  {
    _window = window;
    _uiLayout = dataManager.UiLayout;
    _log = log;
    _screens = screens ?? LoadScreenUsables;
  }

  /// <summary>
  /// 把模型里的窗口几何应用到窗口：尺寸先抬到最小尺寸，位置再保证落在可见区域内。
  /// </summary>
  /// <remarks>
  /// 四个几何字段任一为 <c>null</c>（从未记录）即整体跳过，保持窗口默认几何；
  /// 无论是否应用，结束时都建立初值快照，这样用户没动过窗口时 <see cref="Capture"/> 不产生写入。
  /// </remarks>
  public void Apply()
  {
    if (_window is null)
      return;

    _normalSize = _window.Size;
    _normalPosition = _window.Position;

    var width = _uiLayout.WindowWidth.Value;
    var height = _uiLayout.WindowHeight.Value;
    var x = _uiLayout.WindowX.Value;
    var y = _uiLayout.WindowY.Value;
    if (width is null || height is null || x is null || y is null)
    {
      SnapshotInitial();
      return;
    }

    var size = WindowPlacement.ClampSize(new Vector2I(width.Value, height.Value), _window.MinSize);
    var (position, finalSize) = WindowPlacement.EnsureOnScreen(
      new Vector2I(x.Value, y.Value),
      size,
      _screens()
    );

    // 先尺寸后位置：部分平台在改尺寸时会连带移动窗口，先定位会被覆盖
    _window.Size = finalSize;
    _window.Position = position;
    _normalSize = finalSize;
    _normalPosition = position;

    if (_uiLayout.WindowMaximized.Value)
      _window.Mode = Window.ModeEnum.Maximized;

    _log.Print(
      $"WindowLayoutState: apply size={finalSize} position={position} "
        + $"maximized={_uiLayout.WindowMaximized.Value}"
    );
    SnapshotInitial();
  }

  /// <summary>
  /// 窗口普通态下更新「还原矩形」：只在非最大化 / 非最小化时记录，供最大化退出时回写。
  /// </summary>
  public void Track()
  {
    if (_window is null || _window.Mode != Window.ModeEnum.Windowed)
      return;

    _normalSize = _window.Size;
    _normalPosition = _window.Position;
  }

  /// <summary>
  /// 把当前几何写回模型（不触发保存）；与初值完全相同时不写。
  /// </summary>
  public void Capture()
  {
    if (_window is null)
      return;

    var (size, position, maximized) = ResolveGeometry();
    if (
      _hasInitial
      && size == _initialSize
      && position == _initialPosition
      && maximized == _initialMaximized
    )
      return;

    _uiLayout.WindowWidth.Value = size.X;
    _uiLayout.WindowHeight.Value = size.Y;
    _uiLayout.WindowX.Value = position.X;
    _uiLayout.WindowY.Value = position.Y;
    _uiLayout.WindowMaximized.Value = maximized;

    // 写完即把快照推进到当前值：关窗通知与 _ExitTree 各会 Capture 一次，第二次不必重复写
    _initialSize = size;
    _initialPosition = position;
    _initialMaximized = maximized;
    _hasInitial = true;

    _log.Print($"WindowLayoutState: capture size={size} position={position} maximized={maximized}");
  }

  /// <summary>
  /// 按窗口当前模式决定要写回的几何：普通态用实时值，最大化 / 最小化用跟踪到的还原矩形。
  /// </summary>
  /// <returns>窗口尺寸、位置与「是否最大化」标志。</returns>
  private (Vector2I Size, Vector2I Position, bool Maximized) ResolveGeometry()
  {
    var mode = _window!.Mode;
    return mode switch
    {
      Window.ModeEnum.Maximized => (_normalSize, _normalPosition, true),
      Window.ModeEnum.Minimized => (_normalSize, _normalPosition, false),
      _ => (_window.Size, _window.Position, false),
    };
  }

  /// <summary>
  /// 建立初值快照：尺寸 / 位置取跟踪到的普通态矩形，最大化标志取窗口当前模式。
  /// </summary>
  private void SnapshotInitial()
  {
    _initialSize = _normalSize;
    _initialPosition = _normalPosition;
    _initialMaximized = _window is not null && _window.Mode == Window.ModeEnum.Maximized;
    _hasInitial = true;
  }

  /// <summary>
  /// 默认的屏幕可用区读取：遍历 <see cref="DisplayServer"/> 的所有屏幕（已排除任务栏等系统区域）。
  /// </summary>
  /// <returns>每块屏幕的可用区域；无屏幕时返回空数组。</returns>
  private static Rect2I[] LoadScreenUsables()
  {
    var count = DisplayServer.GetScreenCount();
    if (count <= 0)
      return Array.Empty<Rect2I>();

    var screens = new Rect2I[count];
    for (var index = 0; index < count; index++)
      screens[index] = DisplayServer.ScreenGetUsableRect(index);

    return screens;
  }
}
