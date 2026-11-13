namespace AutoCMEX;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Test.Drivers;
using AutoCMEX.UI.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class TestLogPanel : TestClass
{
  private LogPanel _panel = default!;
  private LogService _service = default!;
  private Mock<IRichTextLabel> _logView = default!;
  private Mock<IOptionButton> _levelFilter = default!;
  private Mock<IOptionButton> _moduleFilter = default!;
  private readonly List<Node> _toCleanup = new();

  public TestLogPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _panel = new LogPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    _logView = new Mock<IRichTextLabel>();
    _levelFilter = new Mock<IOptionButton>();
    _moduleFilter = new Mock<IOptionButton>();
    var pauseBtn = new Mock<IButton>();
    pauseBtn.SetupProperty(m => m.ToggleMode);
    var clearBtn = new Mock<IButton>();
    var logDirLabel = new Mock<ILabel>();

    _panel.FakeNodeTree(
      new()
      {
        ["%LogView"] = _logView.Object,
        ["%LevelFilter"] = _levelFilter.Object,
        ["%ModuleFilter"] = _moduleFilter.Object,
        ["%PauseBtn"] = pauseBtn.Object,
        ["%ClearBtn"] = clearBtn.Object,
        ["%LogDirLabel"] = logDirLabel.Object,
      }
    );

    var logCfg = new LogConfig { LogDirectory = System.IO.Path.GetTempPath() };
    _service = new LogService(logCfg, includeGodotConsole: false);
    _panel.FakeDependency<ILogService>(_service);
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }
    _toCleanup.Clear();
  }

  [Test]
  public void Panel_IsNotNull() => _panel.ShouldNotBeNull();

  [Test]
  public void LogView_IsNotNull() => _panel.LogView.ShouldNotBeNull();

  [Test]
  public void LevelFilter_HasFourOptions() =>
    _levelFilter.Verify(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(4));

  [Test]
  public void PauseBtn_IsToggleMode() => _panel.PauseBtn.ToggleMode.ShouldBeTrue();

  [Test]
  public void ClearBtn_IsNotNull() => _panel.ClearBtn.ShouldNotBeNull();

  /// <summary>
  /// 回归：首次出现的模块名可能来自写日志线程（WebSocket/录制线程池）。
  /// 写日志线程只能入队，控件（模块筛选下拉）必须等主线程 <c>_Process</c> 排空后再改。
  /// </summary>
  [Test]
  public async Task BackgroundThreadModule_IsRegisteredOnlyByMainThreadDrain()
  {
    // 后台线程写一条新模块的日志：此时若直接改控件，这里就已经被调用
    await Task.Run(() => _service.GetLogger("BackgroundOnly").Print("后台线程日志"));

    // 写日志线程不得直接改模块筛选下拉
    _moduleFilter.Verify(m => m.AddItem("BackgroundOnly", It.IsAny<int>()), Times.Never);

    // 主线程排空队列后才注册进下拉
    _panel._Process(0);

    _moduleFilter.Verify(m => m.AddItem("BackgroundOnly", It.IsAny<int>()), Times.Once);
  }

  /// <summary>
  /// 回归：主线程排空后同一模块只注册一次（队列里可能堆着同一模块的多条日志）。
  /// </summary>
  [Test]
  public async Task BackgroundThreadModule_IsRegisteredOncePerModule()
  {
    await Task.Run(() =>
    {
      var log = _service.GetLogger("RepeatModule");
      log.Print("第一条");
      log.Print("第二条");
    });

    _panel._Process(0);
    _panel._Process(0);

    _moduleFilter.Verify(m => m.AddItem("RepeatModule", It.IsAny<int>()), Times.Once);
  }

  /// <summary>
  /// 回归：<see cref="LogPanel.RefreshFromBuffer"/> 重画后，待处理队列里的同一批条目不得被下一帧
  /// <c>_Process</c> 再画一遍（修复前表现为时间戳相同的重复两行）。
  /// </summary>
  [Test]
  public void RefreshFromBuffer_ThenProcess_DoesNotRenderPendingEntriesTwice()
  {
    var log = _service.GetLogger("DuplicateProbe");
    log.Print("第一条");
    log.Print("第二条");
    log.Print("第三条");

    _logView.Invocations.Clear();

    // 重画一次后紧接着走下一帧的正常消费：队列里的这批条目必须已经被排空
    _panel.RefreshFromBuffer();
    _panel._Process(0);

    CountViewCalls(nameof(IRichTextLabel.AppendText))
      .ShouldBe(3, "三条待处理日志只能各画一行（修复前是 6 行）");
    CountViewCalls(nameof(IRichTextLabel.Clear)).ShouldBe(1);
  }

  /// <summary>
  /// 回归：主题变化的整段重建必须排到帧末（父控件在自己的 <c>theme_changed</c> 回调里解析主题项拿到的
  /// 仍是上一套值，同步重建会把旧主题色固化进 BBCode），且同一帧的多次通知只重画一轮。
  /// </summary>
  /// <remarks>
  /// 这里不手工顶替帧末调用，而是等真实的 <c>ProcessFrame</c>：若去掉合并标记
  /// （<see cref="LogPanel"/> 的 <c>_themeRefreshQueued</c>），两次通知会各排一次 deferred，
  /// 重画两轮，本用例随之变红。
  /// </remarks>
  [Test]
  public async Task ThemeChanged_DefersRebuildAndMergesNotifications()
  {
    var log = _service.GetLogger("ThemeProbe");
    log.Print("第一条");
    log.Print("第二条");

    _logView.Invocations.Clear();

    // 触发主题信号：此刻不得同步重画
    _panel.EmitSignal(Control.SignalName.ThemeChanged);
    CountViewCalls(nameof(IRichTextLabel.Clear)).ShouldBe(0, "主题变化不得在信号回调里同步重建");
    CountViewCalls(nameof(IRichTextLabel.AppendText)).ShouldBe(0);

    // 同一帧再发一次：只应排一次帧末重画（合并）
    _panel.EmitSignal(Control.SignalName.ThemeChanged);

    // 规则 30：CallDeferred 之后必须等帧再断言，不手工调 FlushThemeRefresh
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    CountViewCalls(nameof(IRichTextLabel.Clear)).ShouldBe(1, "同帧多次主题通知只应重画一轮");
    CountViewCalls(nameof(IRichTextLabel.AppendText)).ShouldBe(2);

    // 第二轮：上一轮重画必须把合并标记复位，否则这次通知不会被重新排队，帧末也就不会重画。
    // 即覆盖 FlushThemeRefresh 里的复位那一行（删掉它，下面的累计断言就会红）。
    _panel.EmitSignal(Control.SignalName.ThemeChanged);

    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    CountViewCalls(nameof(IRichTextLabel.Clear)).ShouldBe(2, "第二轮主题变化必须再重画一轮");
    CountViewCalls(nameof(IRichTextLabel.AppendText)).ShouldBe(4);
  }

  /// <summary>
  /// 回归：面板离场后，已经排到帧末的主题重画必须被挡掉
  /// （<see cref="LogPanel"/> 的 <c>_exiting</c> 守卫）。
  /// </summary>
  /// <remarks>
  /// 主题通知先把重画排到帧末，随后才离场：离场会退订 <c>theme_changed</c>，但已经排上的 deferred 照常执行。
  /// 若 <see cref="LogPanel.FlushThemeRefresh"/> 不检查 <c>_exiting</c>，帧末仍会整段重画一遍
  /// （Clear 1 次、AppendText 2 次），本用例随之变红。
  /// </remarks>
  [Test]
  public async Task ThemeChanged_AfterExit_DoesNotRebuild()
  {
    var logCfg = new LogConfig { LogDirectory = System.IO.Path.GetTempPath() };
    var service = new LogService(logCfg, includeGodotConsole: false);
    var driver = new LogPanelDriver(service);

    try
    {
      var log = service.GetLogger("ThemeExitProbe");
      log.Print("第一条");
      log.Print("第二条");

      driver.LogView.Invocations.Clear();

      // 先排帧末重画，再离场：真实离场语义就是退订主题信号（这里不重复调用 _ExitTree）
      driver.Panel.EmitSignal(Control.SignalName.ThemeChanged);
      driver.Panel._ExitTree();

      // 规则 30：CallDeferred 之后必须等帧再断言，不手工调 FlushThemeRefresh
      await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
      await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

      CountViewCalls(driver.LogView, nameof(IRichTextLabel.Clear))
        .ShouldBe(0, "离场后排到帧末的主题重画必须被 _exiting 挡掉");
      CountViewCalls(driver.LogView, nameof(IRichTextLabel.AppendText)).ShouldBe(0);
    }
    finally
    {
      driver.Dispose();
      service.Dispose();
    }
  }

  /// <summary>
  /// <see cref="LogPanel.FlushThemeRefresh"/> 的公开语义：调用即同步整段重画一轮。
  /// </summary>
  /// <remarks>
  /// 直接调用 Flush 时合并标记必为 <c>false</c>，本用例验证不了它的复位，故随后的等帧断言只说明
  /// 「没有额外排上 deferred」；标记的复位与同帧合并由
  /// <see cref="ThemeChanged_DefersRebuildAndMergesNotifications"/> 覆盖。
  /// </remarks>
  [Test]
  public async Task FlushThemeRefresh_RebuildsOnceWithoutLeavingAnotherPass()
  {
    var log = _service.GetLogger("ThemeProbeFlush");
    log.Print("第一条");
    log.Print("第二条");

    _logView.Invocations.Clear();

    _panel.FlushThemeRefresh();

    CountViewCalls(nameof(IRichTextLabel.Clear)).ShouldBe(1);
    CountViewCalls(nameof(IRichTextLabel.AppendText)).ShouldBe(2);

    // 没有排过 deferred：随后等帧不得出现第二次重画
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    CountViewCalls(nameof(IRichTextLabel.Clear)).ShouldBe(1);
    CountViewCalls(nameof(IRichTextLabel.AppendText)).ShouldBe(2);
  }

  /// <summary>
  /// 回归：<see cref="LogPanel.RefreshFromBuffer"/> 必须补画「只在待处理队列、已不在缓冲区快照里」的条目。
  /// </summary>
  /// <remarks>
  /// 这是「防丢行」分支唯一会被走到的路径：缓冲区容量为 1 时快照只含最后一条，
  /// 前两条只存在于队列里；若只按快照重建就会丢两行。
  /// </remarks>
  [Test]
  public void RefreshFromBuffer_ReplaysQueuedEntriesMissingFromBufferSnapshot()
  {
    // 独立实例：缓冲区容量 1，写入 3 条后快照里只剩最后一条
    var logCfg = new LogConfig
    {
      InMemoryBufferSize = 1,
      LogDirectory = System.IO.Path.GetTempPath(),
    };
    var service = new LogService(logCfg, includeGodotConsole: false);
    var driver = new LogPanelDriver(service);

    try
    {
      var log = service.GetLogger("EvictedProbe");
      log.Print("第一条");
      log.Print("第二条");
      log.Print("第三条");

      driver.LogView.Invocations.Clear();

      driver.Panel.RefreshFromBuffer();

      CountViewCalls(driver.LogView, nameof(IRichTextLabel.AppendText))
        .ShouldBe(3, "被挤出缓冲区的条目只能从待处理队列补画（只按快照会是 1 条）");
      CountViewCalls(driver.LogView, nameof(IRichTextLabel.Clear)).ShouldBe(1);
    }
    finally
    {
      driver.Dispose();
      service.Dispose();
    }
  }

  /// <summary>数本类共享视图上某个方法被调用了几次（AppendText / Clear 断言用）。</summary>
  /// <param name="methodName">方法名。</param>
  /// <returns>调用次数。</returns>
  private int CountViewCalls(string methodName) => CountViewCalls(_logView, methodName);

  /// <summary>数指定视图替身上某个方法被调用了几次（独立面板实例的断言用）。</summary>
  /// <param name="view">视图替身。</param>
  /// <param name="methodName">方法名。</param>
  /// <returns>调用次数。</returns>
  private static int CountViewCalls(Mock<IRichTextLabel> view, string methodName) =>
    view.Invocations.Count(call => call.Method.Name == methodName);
}
