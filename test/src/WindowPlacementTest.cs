namespace AutoCMEX;

using AutoCMEX.UI;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="WindowPlacement"/> 的纯计算测试：尺寸抬升、可见性判据与离屏居中。
/// </summary>
/// <remarks>
/// 这些函数刻意不依赖真实窗口 / 显示器，屏幕可用区由用例注入，因此能覆盖
/// 「窗口比最小尺寸小」「整块离屏」「只露一条边」「左侧副屏负坐标」「拿不到屏幕列表」等真实边界。
/// </remarks>
public class WindowPlacementTest : TestClass
{
  /// <summary>单主屏（1920×1080 可用区）。</summary>
  private static readonly Rect2I[] PrimaryScreen = { new(0, 0, 1920, 1080) };

  /// <summary>左侧副屏（负 X 起点）+ 主屏。</summary>
  private static readonly Rect2I[] PrimaryAndLeftScreens =
  {
    new(0, 0, 1920, 1080),
    new(-1920, 0, 1920, 1080),
  };

  /// <summary>空屏幕列表（模拟拿不到显示器信息）。</summary>
  private static readonly Rect2I[] NoScreens = System.Array.Empty<Rect2I>();

  public WindowPlacementTest(Node testScene)
    : base(testScene) { }

  // ==================== ClampSize ====================

  /// <summary>小于最小尺寸时两轴都抬到最小尺寸。</summary>
  [Test]
  public void ClampSize_BelowMinSize_EnlargesBothAxes() =>
    WindowPlacement
      .ClampSize(new Vector2I(800, 600), new Vector2I(1024, 768))
      .ShouldBe(new Vector2I(1024, 768));

  /// <summary>大于最小尺寸时保持原值（只抬不压）。</summary>
  [Test]
  public void ClampSize_AboveMinSize_KeepsSize() =>
    WindowPlacement
      .ClampSize(new Vector2I(1600, 900), new Vector2I(1024, 768))
      .ShouldBe(new Vector2I(1600, 900));

  /// <summary>只在一轴上小于最小尺寸时只抬那一轴。</summary>
  [Test]
  public void ClampSize_OneAxisBelowMinSize_EnlargesOnlyThatAxis() =>
    WindowPlacement
      .ClampSize(new Vector2I(1200, 600), new Vector2I(1024, 768))
      .ShouldBe(new Vector2I(1200, 768));

  /// <summary>最小尺寸为零（无下限）时原样返回。</summary>
  [Test]
  public void ClampSize_ZeroMinSize_KeepsSize() =>
    WindowPlacement
      .ClampSize(new Vector2I(800, 600), Vector2I.Zero)
      .ShouldBe(new Vector2I(800, 600));

  // ==================== IsVisibleEnough ====================

  /// <summary>完全落在屏幕可用区内时可见。</summary>
  [Test]
  public void IsVisibleEnough_FullyOnScreen_ReturnsTrue() =>
    WindowPlacement
      .IsVisibleEnough(new Vector2I(100, 100), new Vector2I(800, 600), PrimaryScreen)
      .ShouldBeTrue();

  /// <summary>部分露出但交集宽高都不小于 64 像素时仍视为可见（允许用户拖回来）。</summary>
  [Test]
  public void IsVisibleEnough_PartiallyOnScreenAboveThreshold_ReturnsTrue() =>
    WindowPlacement
      .IsVisibleEnough(new Vector2I(1820, 100), new Vector2I(800, 600), PrimaryScreen)
      .ShouldBeTrue();

  /// <summary>只露一条小于 64 像素的边时视为离屏（用户找不到标题栏）。</summary>
  [Test]
  public void IsVisibleEnough_SliverBelowThreshold_ReturnsFalse() =>
    WindowPlacement
      .IsVisibleEnough(new Vector2I(1900, 100), new Vector2I(800, 600), PrimaryScreen)
      .ShouldBeFalse();

  /// <summary>整块在屏幕右下外侧时不可见。</summary>
  [Test]
  public void IsVisibleEnough_OffScreen_ReturnsFalse() =>
    WindowPlacement
      .IsVisibleEnough(new Vector2I(3000, 2000), new Vector2I(800, 600), PrimaryScreen)
      .ShouldBeFalse();

  /// <summary>左侧副屏用负坐标：落在副屏内时可见。</summary>
  [Test]
  public void IsVisibleEnough_NegativeSecondaryScreen_ReturnsTrue() =>
    WindowPlacement
      .IsVisibleEnough(new Vector2I(-1800, 100), new Vector2I(800, 600), PrimaryAndLeftScreens)
      .ShouldBeTrue();

  /// <summary>空屏幕列表时没有可见依据，返回 <c>false</c>（由调用方兜底）。</summary>
  [Test]
  public void IsVisibleEnough_NoScreens_ReturnsFalse() =>
    WindowPlacement
      .IsVisibleEnough(new Vector2I(100, 100), new Vector2I(800, 600), NoScreens)
      .ShouldBeFalse();

  // ==================== CenterIn ====================

  /// <summary>居中到主屏：位置为可用区起点加上两侧余量的一半。</summary>
  [Test]
  public void CenterIn_PrimaryScreen_CentersWindow() =>
    WindowPlacement
      .CenterIn(new Rect2I(0, 0, 1920, 1080), new Vector2I(800, 600))
      .ShouldBe(new Vector2I(560, 240));

  /// <summary>居中到负坐标的副屏时保留屏幕起点偏移。</summary>
  [Test]
  public void CenterIn_NegativeScreen_KeepsScreenOffset() =>
    WindowPlacement
      .CenterIn(new Rect2I(-1920, 0, 1920, 1080), new Vector2I(800, 600))
      .ShouldBe(new Vector2I(-1360, 240));

  // ==================== EnsureOnScreen ====================

  /// <summary>仍可见时原样返回位置与尺寸。</summary>
  [Test]
  public void EnsureOnScreen_Visible_KeepsPositionAndSize()
  {
    var (position, size) = WindowPlacement.EnsureOnScreen(
      new Vector2I(100, 100),
      new Vector2I(800, 600),
      PrimaryScreen
    );

    position.ShouldBe(new Vector2I(100, 100));
    size.ShouldBe(new Vector2I(800, 600));
  }

  /// <summary>离屏时居中到第一块屏幕。</summary>
  [Test]
  public void EnsureOnScreen_OffScreen_CentersOnFirstScreen()
  {
    var (position, size) = WindowPlacement.EnsureOnScreen(
      new Vector2I(5000, 4000),
      new Vector2I(800, 600),
      PrimaryScreen
    );

    position.ShouldBe(new Vector2I(560, 240));
    size.ShouldBe(new Vector2I(800, 600));
  }

  /// <summary>拿不到屏幕列表时返回原值，由调用方决定兜底。</summary>
  [Test]
  public void EnsureOnScreen_NoScreens_ReturnsInputUnchanged()
  {
    var (position, size) = WindowPlacement.EnsureOnScreen(
      new Vector2I(5000, 4000),
      new Vector2I(800, 600),
      NoScreens
    );

    position.ShouldBe(new Vector2I(5000, 4000));
    size.ShouldBe(new Vector2I(800, 600));
  }
}
