namespace AutoCMEX;

using System;
using AutoCMEX.Core.Logging;
using AutoCMEX.UI;
using AutoCMEX.UI.Main;
using Chickensoft.GameTools.Displays;
using Godot;
#if RUN_TESTS
using System.Reflection;
using Chickensoft.GoDotTest;
using Chickensoft.GodotNodeInterfaces;
#endif

// This entry-point file is responsible for determining if we should run tests.

/// <summary>
/// 全局日志服务访问点。在 <see cref="Main._Ready"/> 中初始化。
/// </summary>
public static class AppLogs
{
  private static ILogService? _service;

  /// <summary>当前日志服务实例。未初始化时返回 <c>null</c>。</summary>
  public static ILogService? Current => _service;

  /// <summary>
  /// 初始化全局日志服务（幂等）。重复调用将返回已存在实例。
  /// </summary>
  /// <param name="config">可选配置。为空时使用默认配置（系统临时目录）。</param>
  /// <returns>初始化后的服务实例。</returns>
  public static ILogService Initialize(LogConfig? config = null)
  {
    if (_service != null)
      return _service;
    _service = new LogService(config);
    return _service;
  }

  /// <summary>
  /// 关闭并释放日志服务。
  /// </summary>
  public static void Shutdown()
  {
    if (_service == null)
      return;
    try
    {
      _service.Shutdown();
    }
    finally
    {
      _service = null;
    }
  }

  /// <summary>
  /// 获取或创建当前实例的兜底 <see cref="ILogService"/>。
  /// 适合测试或无 Main 入口的场景。
  /// </summary>
  public static ILogService GetOrCreate()
  {
    if (_service != null)
      return _service;
    return Initialize();
  }

  /// <summary>
  /// 临时替换日志服务（仅供测试使用）。返回原始实例以便恢复。
  /// </summary>
  public static ILogService? Swap(ILogService? replacement)
  {
    var previous = _service;
    _service = replacement;
    return previous;
  }
}

public partial class Main : Node2D
{
  public Vector2I DesignResolution => Display.UHD4k;
#if RUN_TESTS
  public TestEnvironment Environment = default!;
#endif

  public override void _Ready()
  {
    // Correct any erroneous scaling and guess sensible defaults.
    GetWindow().LookGood(WindowScaleBehavior.UIFixed, DesignResolution);

    // LookGood 会把 MaxSize 限制为屏幕逻辑分辨率的 75%（游戏窗口推荐值），这里解除该上限以支持自由缩放
    MainWindowSizing.AllowFreeResize(GetWindow());

    // 初始化全局日志服务（位于系统临时目录，详见 LogConfig）
    var logService = AppLogs.Initialize();
    var startupLog = logService.GetLogger("Main");
    startupLog.Print($"Application starting. Log dir: {logService.Config.LogDirectory}");

#if RUN_TESTS
    // If this is a debug build, use GoDotTest to examine the
    // command line arguments and determine if we should run tests.
    Environment = TestEnvironment.From(OS.GetCmdlineArgs());
    if (Environment.ShouldRunTests)
    {
      RuntimeContext.IsTesting = true;
      // 关闭日志服务由测试驱动释放；这里先记录一条
      startupLog.Print("Running test suite.");
      CallDeferred("RunTests");
      return;
    }
#endif

    // If we don't need to run tests, we can just switch to the main scene.
    startupLog.Print("Switching to main scene.");
    CallDeferred("RunScene");
  }

  public override void _Notification(int what)
  {
    // Godot 退出通知：WM_CLOSE_REQUEST 与 EXIT_TREE 时先刷盘再关闭日志
    if (what == NotificationWMCloseRequest || what == NotificationExitTree)
    {
      // 顺序不可颠倒：DataManager 落盘过程要写日志，日志服务必须还活着
      FlushPendingSave();
      ShutdownLogs();
    }
    base._Notification(what);
  }

  /// <summary>
  /// 触发主窗口的末次保存，避免防抖窗口（1500ms）内的改动随关窗丢失。
  /// </summary>
  /// <remarks>
  /// Main 不持有 DataManager，经 <see cref="AutoCMEX.UI.Main.MainWindow.FlushPendingSave"/> 的静态入口转发；
  /// 无主窗口实例（如测试模式）时为空操作，重复调用不会重复写盘。
  /// </remarks>
  private static void FlushPendingSave()
  {
    try
    {
      MainWindow.FlushPendingSave();
    }
    catch (Exception ex)
    {
      GD.PrintErr($"[Main] Failed to flush pending saves: {ex.Message}");
    }
  }

  /// <summary>
  /// 关闭日志服务，确保缓存落盘。
  /// </summary>
  private static void ShutdownLogs()
  {
    try
    {
      var svc = AppLogs.Current;
      svc?.GetLogger("Main").Print("Application shutting down. Flushing logs.");
      AppLogs.Shutdown();
    }
    catch (Exception ex)
    {
      GD.PrintErr($"[Main] Failed to shutdown logs: {ex.Message}");
    }
  }

#if RUN_TESTS
  private void RunTests() =>
    _ = GoTest.RunTests(Assembly.GetExecutingAssembly(), this, Environment);
#endif

  private void RunScene() => GetTree().ChangeSceneToFile("res://src/ui/main/MainWindow.tscn");
}
