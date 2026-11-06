namespace AutoCMEX;

using AutoCMEX.UI;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="MainWindowSizing"/> 单元测试：自由缩放策略只解除尺寸上限与「不可缩放」标记，
/// 不碰初始尺寸与最小尺寸。每条用例都使用未入树的真实 <see cref="Window"/>，用完后立即释放。
/// </summary>
public class MainWindowSizingTest : TestClass
{
  public MainWindowSizingTest(Node testScene)
    : base(testScene) { }

  /// <summary>MaxSize 被清空为零（Godot 语义：无上限）。</summary>
  [Test]
  public void AllowFreeResize_ClearsMaxSize()
  {
    var window = new Window();
    try
    {
      window.MaxSize = new Vector2I(1440, 810);

      MainWindowSizing.AllowFreeResize(window);

      window.MaxSize.ShouldBe(Vector2I.Zero);
    }
    finally
    {
      window.Free();
    }
  }

  /// <summary>「不可缩放」标记被清除，窗口重新允许拉伸。</summary>
  [Test]
  public void AllowFreeResize_EnablesResizing()
  {
    var window = new Window();
    try
    {
      window.Unresizable = true;

      MainWindowSizing.AllowFreeResize(window);

      window.Unresizable.ShouldBeFalse();
    }
    finally
    {
      window.Free();
    }
  }

  /// <summary>最小尺寸保持不变（仍由 LookGood 设定的下限生效）。</summary>
  [Test]
  public void AllowFreeResize_KeepsMinSize()
  {
    var window = new Window();
    try
    {
      window.MinSize = new Vector2I(480, 270);

      MainWindowSizing.AllowFreeResize(window);

      window.MinSize.ShouldBe(new Vector2I(480, 270));
    }
    finally
    {
      window.Free();
    }
  }

  /// <summary>初始尺寸与位置保持不变（不重置 LookGood 算出的窗口大小）。</summary>
  [Test]
  public void AllowFreeResize_DoesNotMoveOrResize()
  {
    var window = new Window();
    try
    {
      window.Size = new Vector2I(1440, 810);
      window.Position = new Vector2I(120, 80);

      MainWindowSizing.AllowFreeResize(window);

      window.Size.ShouldBe(new Vector2I(1440, 810));
      window.Position.ShouldBe(new Vector2I(120, 80));
    }
    finally
    {
      window.Free();
    }
  }

  /// <summary>幂等：连续调用两次不抛异常，且结果一致。</summary>
  [Test]
  public void AllowFreeResize_IsIdempotent()
  {
    var window = new Window();
    try
    {
      window.Unresizable = true;
      window.MaxSize = new Vector2I(1440, 810);

      MainWindowSizing.AllowFreeResize(window);
      MainWindowSizing.AllowFreeResize(window);

      window.Unresizable.ShouldBeFalse();
      window.MaxSize.ShouldBe(Vector2I.Zero);
    }
    finally
    {
      window.Free();
    }
  }
}
