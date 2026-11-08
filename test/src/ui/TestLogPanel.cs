namespace AutoCMEX;

using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
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

    var logView = new Mock<IRichTextLabel>();
    _levelFilter = new Mock<IOptionButton>();
    _moduleFilter = new Mock<IOptionButton>();
    var pauseBtn = new Mock<IButton>();
    pauseBtn.SetupProperty(m => m.ToggleMode);
    var clearBtn = new Mock<IButton>();
    var logDirLabel = new Mock<ILabel>();

    _panel.FakeNodeTree(
      new()
      {
        ["%LogView"] = logView.Object,
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
  /// 回归（S7）：首次出现的模块名可能来自写日志线程（WebSocket/录制线程池）。
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
  /// 回归（S7）：主线程排空后同一模块只注册一次（队列里可能堆着同一模块的多条日志）。
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
}
