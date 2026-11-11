namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// <see cref="WindowLayoutState"/> 的单元测试：窗口几何应用、最大化还原矩形与「未改动不写」。
/// </summary>
/// <remarks>
/// 窗口与屏幕都用替身注入（<see cref="IWindow"/> Mock + 固定屏幕矩形），不依赖真实显示器：
/// 真实窗口在无头测试环境里改 Mode 会走平台调用，结果不稳定；这里只验证接线逻辑本身。
/// </remarks>
public class WindowLayoutStateTest : TestClass
{
  /// <summary>单主屏（1920×1080 可用区）。</summary>
  private static readonly Rect2I[] PrimaryScreen = { new(0, 0, 1920, 1080) };

  /// <summary>待删除的临时数据根目录。</summary>
  private readonly List<string> _tempRoots = new();

  public WindowLayoutStateTest(Node testScene)
    : base(testScene) { }

  /// <summary>清理临时数据目录。</summary>
  [Cleanup]
  public void Cleanup()
  {
    foreach (var root in _tempRoots)
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }

    _tempRoots.Clear();
  }

  // ==================== 夹具 ====================

  /// <summary>建一份临时目录里的 <see cref="DataManager"/>（数据目录为 root/data）。</summary>
  private DataManager CreateDataManager()
  {
    var root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_WindowLayout_" + Guid.NewGuid().ToString("N")[..8]
    );
    _tempRoots.Add(root);

    var dataManager = new DataManager(Path.Combine(root, "data"), new AesEncryptor("test-key"));
    dataManager.LoadAll();
    return dataManager;
  }

  /// <summary>建一个可读写 Size/Position/Mode/MinSize 的窗口替身。</summary>
  private static Mock<IWindow> NewWindowMock(Vector2I size, Vector2I position, Vector2I minSize)
  {
    var window = new Mock<IWindow>();
    window.SetupProperty(w => w.Size, size);
    window.SetupProperty(w => w.Position, position);
    window.SetupProperty(w => w.MinSize, minSize);
    window.SetupProperty(w => w.Mode, Window.ModeEnum.Windowed);
    return window;
  }

  /// <summary>按固定屏幕列表建接线路。</summary>
  private static WindowLayoutState NewState(IWindow? window, DataManager dataManager) =>
    new(window, dataManager, NullLog.Instance, () => PrimaryScreen);

  // ==================== Apply ====================

  /// <summary>有记录时尺寸与位置都还原。</summary>
  [Test]
  public void Apply_RestoresSizeAndPosition()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 1200;
      dataManager.UiLayout.WindowHeight.Value = 800;
      dataManager.UiLayout.WindowX.Value = 100;
      dataManager.UiLayout.WindowY.Value = 50;

      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      NewState(window.Object, dataManager).Apply();

      window.Object.Size.ShouldBe(new Vector2I(1200, 800));
      window.Object.Position.ShouldBe(new Vector2I(100, 50));
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>记忆尺寸小于最小尺寸时抬到最小尺寸。</summary>
  [Test]
  public void Apply_BelowMinSize_EnlargesToMinSize()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 640;
      dataManager.UiLayout.WindowHeight.Value = 480;
      dataManager.UiLayout.WindowX.Value = 100;
      dataManager.UiLayout.WindowY.Value = 100;

      var window = NewWindowMock(
        new Vector2I(800, 600),
        new Vector2I(0, 0),
        new Vector2I(1024, 768)
      );
      NewState(window.Object, dataManager).Apply();

      window.Object.Size.ShouldBe(new Vector2I(1024, 768));
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>记忆位置离屏时居中到主屏。</summary>
  [Test]
  public void Apply_OffScreen_CentersOnPrimaryScreen()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 800;
      dataManager.UiLayout.WindowHeight.Value = 600;
      dataManager.UiLayout.WindowX.Value = 5000;
      dataManager.UiLayout.WindowY.Value = 4000;

      var window = NewWindowMock(Vector2I.Zero, Vector2I.Zero, Vector2I.Zero);
      NewState(window.Object, dataManager).Apply();

      window.Object.Position.ShouldBe(new Vector2I(560, 240));
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>从未记录过几何时保持窗口当前几何不动。</summary>
  [Test]
  public void Apply_MissingGeometry_KeepsCurrentGeometry()
  {
    var dataManager = CreateDataManager();
    try
    {
      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      NewState(window.Object, dataManager).Apply();

      window.Object.Size.ShouldBe(new Vector2I(800, 600));
      window.Object.Position.ShouldBe(new Vector2I(10, 20));
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>记录里带最大化标志时应用后窗口进入最大化。</summary>
  [Test]
  public void Apply_MaximizedRecord_SetsMaximizedMode()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 1200;
      dataManager.UiLayout.WindowHeight.Value = 800;
      dataManager.UiLayout.WindowX.Value = 100;
      dataManager.UiLayout.WindowY.Value = 50;
      dataManager.UiLayout.WindowMaximized.Value = true;

      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      NewState(window.Object, dataManager).Apply();

      window.Object.Mode.ShouldBe(Window.ModeEnum.Maximized);
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>拿不到窗口（适配失败 / 测试模式）时全链路空安全，不改模型。</summary>
  [Test]
  public void NoWindow_DoesNotThrowAndLeavesModelUntouched()
  {
    var dataManager = CreateDataManager();
    try
    {
      var state = NewState(null, dataManager);
      Should.NotThrow(() =>
      {
        state.Apply();
        state.Track();
        state.Capture();
      });

      dataManager.UiLayout.WindowWidth.Value.ShouldBeNull();
      dataManager.UiLayout.WindowX.Value.ShouldBeNull();
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  // ==================== Capture ====================

  /// <summary>用户没动窗口时 Capture 不写模型（保持「从未记录」）。</summary>
  [Test]
  public void Capture_WithoutChange_DoesNotWrite()
  {
    var dataManager = CreateDataManager();
    try
    {
      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      var state = NewState(window.Object, dataManager);
      state.Apply();

      state.Capture();

      dataManager.UiLayout.WindowWidth.Value.ShouldBeNull();
      dataManager.UiLayout.WindowHeight.Value.ShouldBeNull();
      dataManager.UiLayout.WindowX.Value.ShouldBeNull();
      dataManager.UiLayout.WindowY.Value.ShouldBeNull();
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>用户改了尺寸 / 位置时 Capture 写回模型。</summary>
  [Test]
  public void Capture_UserResize_WritesNewGeometry()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 1200;
      dataManager.UiLayout.WindowHeight.Value = 800;
      dataManager.UiLayout.WindowX.Value = 100;
      dataManager.UiLayout.WindowY.Value = 50;

      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      var state = NewState(window.Object, dataManager);
      state.Apply();

      window.Object.Size = new Vector2I(1400, 900);
      window.Object.Position = new Vector2I(200, 60);
      state.Capture();

      dataManager.UiLayout.WindowWidth.Value.ShouldBe(1400);
      dataManager.UiLayout.WindowHeight.Value.ShouldBe(900);
      dataManager.UiLayout.WindowX.Value.ShouldBe(200);
      dataManager.UiLayout.WindowY.Value.ShouldBe(60);
      dataManager.UiLayout.WindowMaximized.Value.ShouldBeFalse();
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>最大化时写回的是跟踪到的还原矩形，而不是最大化后的尺寸，且标志为 true。</summary>
  [Test]
  public void Capture_Maximized_WritesTrackedRestoreRect()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 1200;
      dataManager.UiLayout.WindowHeight.Value = 800;
      dataManager.UiLayout.WindowX.Value = 100;
      dataManager.UiLayout.WindowY.Value = 50;

      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      var state = NewState(window.Object, dataManager);
      state.Apply();

      // 模拟用户最大化：窗口尺寸变成屏幕大小，普通态矩形仍是刚应用的 1200×800@(100,50)
      window.Object.Mode = Window.ModeEnum.Maximized;
      window.Object.Size = new Vector2I(1920, 1080);
      window.Object.Position = Vector2I.Zero;
      state.Capture();

      dataManager.UiLayout.WindowWidth.Value.ShouldBe(1200);
      dataManager.UiLayout.WindowHeight.Value.ShouldBe(800);
      dataManager.UiLayout.WindowX.Value.ShouldBe(100);
      dataManager.UiLayout.WindowY.Value.ShouldBe(50);
      dataManager.UiLayout.WindowMaximized.Value.ShouldBeTrue();
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>最大化期间收到 WM_SIZE_CHANGED 时 Track 不覆盖还原矩形。</summary>
  [Test]
  public void Track_WhenMaximized_KeepsRestoreRect()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 1200;
      dataManager.UiLayout.WindowHeight.Value = 800;
      dataManager.UiLayout.WindowX.Value = 100;
      dataManager.UiLayout.WindowY.Value = 50;

      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      var state = NewState(window.Object, dataManager);
      state.Apply();

      window.Object.Mode = Window.ModeEnum.Maximized;
      window.Object.Size = new Vector2I(1920, 1080);
      window.Object.Position = Vector2I.Zero;
      state.Track();
      state.Capture();

      dataManager.UiLayout.WindowWidth.Value.ShouldBe(1200);
      dataManager.UiLayout.WindowHeight.Value.ShouldBe(800);
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>最小化时写还原矩形，且最大化标志为 false。</summary>
  [Test]
  public void Capture_Minimized_WritesRestoreRectWithoutMaximizedFlag()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.WindowWidth.Value = 1200;
      dataManager.UiLayout.WindowHeight.Value = 800;
      dataManager.UiLayout.WindowX.Value = 100;
      dataManager.UiLayout.WindowY.Value = 50;
      dataManager.UiLayout.WindowMaximized.Value = true;

      var window = NewWindowMock(new Vector2I(800, 600), new Vector2I(10, 20), Vector2I.Zero);
      var state = NewState(window.Object, dataManager);
      state.Apply();

      window.Object.Mode = Window.ModeEnum.Minimized;
      state.Capture();

      dataManager.UiLayout.WindowWidth.Value.ShouldBe(1200);
      dataManager.UiLayout.WindowHeight.Value.ShouldBe(800);
      dataManager.UiLayout.WindowMaximized.Value.ShouldBeFalse();
    }
    finally
    {
      dataManager.Dispose();
    }
  }
}
