namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Recording;
using AutoCMEX.Models;
using AutoCMEX.Test.Drivers;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 引擎进程运行器单测：启动参数串（含引号包裹与必带开关）、超时杀进程、
/// 非零退出、结果缺失、<c>job_id</c> 不符拒绝。
/// </summary>
/// <remarks>
/// 一律注入假进程，不真启游戏（真启需要引擎与 ffmpeg，且不能并发）。
/// </remarks>
public class GameProcessRunnerTest : TestClass
{
  private const string ModPackName = "sample_project";

  private string _root = string.Empty;
  private string _engineDir = string.Empty;
  private string _gameDir = string.Empty;
  private FakeEngineProcess _fake = new();
  private Mock<ILog> _log = new();

  public GameProcessRunnerTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_ProcessRunner_" + Guid.NewGuid().ToString("N")[..8]
    );
    _engineDir = Path.Combine(_root, "LuaSTGSub");
    _gameDir = Path.Combine(_engineDir, EngineLocator.GameDirName);
    Directory.CreateDirectory(_gameDir);
    File.WriteAllText(Path.Combine(_gameDir, EngineLocator.LaunchFileName), string.Empty);
    File.WriteAllText(Path.Combine(_gameDir, "LuaSTGSub.exe"), string.Empty);

    _fake = new FakeEngineProcess();
    _log = new Mock<ILog>();
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  /// <summary>构造运行器，进程一律由假进程代替。</summary>
  /// <returns>运行器。</returns>
  private GameProcessRunner CreateRunner() =>
    new(
      _log.Object,
      startInfo =>
      {
        _fake.StartInfo = startInfo;
        return _fake;
      }
    );

  /// <summary>写出一份录制任务。</summary>
  /// <param name="jobId">任务号。</param>
  /// <returns>任务描述。</returns>
  private RecordingJobSpec PrepareJob(string jobId = "rec_unit")
  {
    return RecordingJobWriter.WriteRecordJob(
      _engineDir,
      jobId,
      absoluteIndex: 11,
      interval: 3,
      maxFrame: 350,
      scale: 0.5,
      bossClass: "sample_enm1"
    );
  }

  /// <summary>写出一份枚举任务。</summary>
  /// <param name="jobId">任务号。</param>
  /// <returns>任务描述。</returns>
  private RecordingJobSpec PrepareEnumerateJob(string jobId = "enum_unit")
  {
    return RecordingJobWriter.WriteEnumerateJob(_engineDir, jobId);
  }

  /// <summary>跑一次任务（默认 5 秒超时，避免用例本身挂住）。</summary>
  /// <param name="spec">任务描述。</param>
  /// <param name="timeout">超时。</param>
  /// <param name="modPackName">工程包名。</param>
  /// <returns>运行结果。</returns>
  private Task<GameProcessOutcome> RunAsync(
    RecordingJobSpec spec,
    TimeSpan? timeout = null,
    string modPackName = ModPackName
  ) => CreateRunner().RunAsync(_engineDir, modPackName, spec, timeout ?? TimeSpan.FromSeconds(5));

  [Test]
  public async Task RunAsync_InvalidEngineDir_ReturnsStartupError()
  {
    var outcome = await CreateRunner()
      .RunAsync(
        Path.Combine(_root, "not_exists"),
        ModPackName,
        PrepareJob(),
        TimeSpan.FromSeconds(1)
      );

    outcome.StartupError.ShouldNotBeNull();
    outcome.StartupError!.ShouldContain("引擎目录不可用");
    outcome.RanToCompletion.ShouldBeFalse();
    _fake.StartInfo.ShouldBeNull();
  }

  [Test]
  public async Task RunAsync_ValidJob_BuildsMandatoryLaunchArguments()
  {
    var spec = PrepareJob();

    var outcome = await RunAsync(spec);

    outcome.StartupError.ShouldBeNull();
    var startInfo = _fake.StartInfo;
    startInfo.ShouldNotBeNull();

    // 整串被双引号包裹，避免包名含空格时被拆成多个参数
    startInfo!.Arguments.ShouldStartWith("\"");
    startInfo.Arguments.ShouldEndWith("\"");

    startInfo.Arguments.ShouldContain($"setting.mod='{ModPackName}'");
    startInfo.Arguments.ShouldContain($"setting.autocmex_job='autocmex/jobs/{spec.JobId}.json'");
    startInfo.Arguments.ShouldContain("setting.showcfg=false");
    startInfo.Arguments.ShouldContain("start_game=true");
    // 不无敌会让自机被弹幕撞死、卡提前结束
    startInfo.Arguments.ShouldContain("cheat=true");
    // 分辨率归玩家设置所有，覆盖会改坏画面比例
    startInfo.Arguments.ShouldNotContain("resx");
    startInfo.Arguments.ShouldNotContain("resy");
    startInfo.Arguments.ShouldNotContain("windowed");

    startInfo.WorkingDirectory.ShouldBe(_gameDir);
    startInfo.FileName.ShouldBe(Path.Combine(_gameDir, "LuaSTGSub.exe"));
  }

  [Test]
  public async Task RunAsync_ProcessNeverExits_KillsTreeAndReportsTimeout()
  {
    _fake.ExitsImmediately = false;

    var outcome = await RunAsync(PrepareJob(), TimeSpan.FromMilliseconds(150));

    outcome.TimedOut.ShouldBeTrue();
    outcome.Cancelled.ShouldBeFalse();
    outcome.RanToCompletion.ShouldBeFalse();
    _fake.KillCount.ShouldBe(1);
    // 进程被杀，游戏侧没机会写结果
    outcome.ResultError.ShouldBe("result_missing");
  }

  [Test]
  public async Task RunAsync_NonZeroExit_ReportsExitCodeAndEngineLogTail()
  {
    _fake.ExitCodeValue = 3;
    File.WriteAllLines(
      Path.Combine(_gameDir, GameProcessRunner.EngineLogFileName),
      Enumerable.Range(1, 60).Select(i => $"engine line {i}")
    );

    var outcome = await RunAsync(PrepareJob());

    outcome.RanToCompletion.ShouldBeTrue();
    outcome.ExitCode.ShouldBe(3);
    outcome.ResultError.ShouldBe("result_missing");

    outcome.EngineLogTail.ShouldNotBeNull();
    // 只回收尾部 40 行，用于失败诊断
    outcome.EngineLogTail!.ShouldContain("engine line 60");
    outcome.EngineLogTail!.ShouldContain("engine line 21");
    outcome.EngineLogTail!.ShouldNotContain("engine line 20");
    _fake.KillCount.ShouldBe(0);
  }

  [Test]
  public async Task RunAsync_StaleResultFromPreviousRun_IsIgnored()
  {
    var spec = PrepareJob();
    // 上一轮遗留的同名结果（进程根本没跑出结果时不应被当成本次结果）
    File.WriteAllText(
      RecordingJobWriter.GetResultAbsolutePath(_engineDir, spec),
      """{"job_id":"rec_previous","status":"ok","frames":350}"""
    );

    var outcome = await RunAsync(spec);

    outcome.Result.ShouldBeNull();
    outcome.ResultError.ShouldBe("result_missing");
  }

  [Test]
  public async Task RunAsync_ResultJobIdMismatch_RejectsResult()
  {
    var spec = PrepareJob();
    _fake.OnStart = () =>
      File.WriteAllText(
        RecordingJobWriter.GetResultAbsolutePath(_engineDir, spec),
        """{"job_id":"rec_other","status":"ok","frames":350}"""
      );

    var outcome = await RunAsync(spec);

    outcome.Result.ShouldBeNull();
    outcome.ResultError.ShouldNotBeNull();
    outcome.ResultError!.ShouldContain("job_id_mismatch");
  }

  [Test]
  public async Task RunAsync_MatchingResult_ReturnsParsedResult()
  {
    var spec = PrepareJob();
    _fake.OnStart = () =>
      File.WriteAllText(
        RecordingJobWriter.GetResultAbsolutePath(_engineDir, spec),
        $$"""
        {
          "job_id": "{{spec.JobId}}",
          "status": "ok",
          "boss_name": "测试Boss",
          "boss_class": "sample_enm1",
          "absolute_index": 11,
          "card_name": "千萃返",
          "task_name": "1790332930",
          "gif_path": "danmaku_recorder/output/1790332930.gif",
          "frames": 350,
          "interval": 3,
          "max_frame": 350,
          "complete": true,
          "success": true,
          "size": 65000000
        }
        """
      );

    var outcome = await RunAsync(spec);

    outcome.ResultError.ShouldBeNull();
    var result = outcome.Result;
    result.ShouldNotBeNull();
    result!.JobId.ShouldBe(spec.JobId);
    result.Status.ShouldBe("ok");
    result.TaskName.ShouldBe("1790332930");
    result.Frames.ShouldBe(350);
    result.Interval.ShouldBe(3);
    result.MaxFrame.ShouldBe(350);
    result.Complete.ShouldBe(true);
    result.Success.ShouldBe(true);
    result.Size.ShouldBe(65000000L);
  }

  [Test]
  public async Task RunAsync_JobFileNotWritten_ReturnsStartupError()
  {
    // 写盘后删除以模拟任务文件缺失：插件只会报含糊的 "job file not readable"，运行器须提前拦下
    var spec = RecordingJobWriter.WriteEnumerateJob(_engineDir, "enum_not_written");
    File.Delete(RecordingJobWriter.GetJobAbsolutePath(_engineDir, spec));

    var outcome = await CreateRunner()
      .RunAsync(_engineDir, ModPackName, spec, TimeSpan.FromSeconds(1));

    outcome.StartupError.ShouldNotBeNull();
    outcome.StartupError!.ShouldContain("任务文件不存在");
    _fake.StartInfo.ShouldBeNull();
  }

  [Test]
  public async Task RunAsync_EnumerateResultWithErrorStatus_RejectsResult()
  {
    // 进程可能以 0 退出、但插件自报失败（方案 §6.6），只有 status 能区分
    var spec = PrepareEnumerateJob();
    _fake.OnStart = () =>
      File.WriteAllText(
        RecordingJobWriter.GetResultAbsolutePath(_engineDir, spec),
        $$"""{"job_id":"{{spec.JobId}}","status":"error","error":"boss_not_found"}"""
      );

    var outcome = await RunAsync(spec);

    outcome.Result.ShouldBeNull();
    outcome.ResultError.ShouldNotBeNull();
    outcome.ResultError!.ShouldContain("status_error");
    outcome.ResultError!.ShouldContain("boss_not_found");
  }

  [Test]
  public async Task RunAsync_EnumerateResultWithoutCards_RejectsResult()
  {
    var spec = PrepareEnumerateJob();
    _fake.OnStart = () =>
      File.WriteAllText(
        RecordingJobWriter.GetResultAbsolutePath(_engineDir, spec),
        $$"""{"job_id":"{{spec.JobId}}","status":"ok","boss_name":"测试Boss","cards":[]}"""
      );

    var outcome = await RunAsync(spec);

    outcome.Result.ShouldBeNull();
    outcome.ResultError.ShouldNotBeNull();
    outcome.ResultError!.ShouldContain("cards_empty");
  }

  [Test]
  public async Task RunAsync_PackNameWithQuote_ReturnsStartupError()
  {
    var outcome = await RunAsync(PrepareJob(), modPackName: "bad'name");

    outcome.StartupError.ShouldNotBeNull();
    outcome.StartupError!.ShouldContain("工程包名不能包含");
    _fake.StartInfo.ShouldBeNull();
  }

  [Test]
  public void BuildArgumentString_AlwaysWrapsWholeStringInQuotes()
  {
    var args = GameProcessRunner.BuildArgumentString("My Pack", "autocmex/jobs/j.json");

    args.ShouldStartWith("\"");
    args.ShouldEndWith("\"");
    args.ShouldContain("setting.mod='My Pack'");
    args.ShouldContain("autocmex/jobs/j.json");
    args.ShouldContain("start_game=true");
    args.ShouldContain("cheat=true");
  }

  [Test]
  public void TryValidateLaunchValues_RejectsQuotesNewlinesAndEmpty()
  {
    GameProcessRunner
      .TryValidateLaunchValues("pack", "autocmex/jobs/j.json", out var ok)
      .ShouldBeTrue();
    ok.ShouldBeEmpty();

    GameProcessRunner
      .TryValidateLaunchValues(string.Empty, "j.json", out var emptyReason)
      .ShouldBeFalse();
    emptyReason.ShouldBe("工程包名为空");

    GameProcessRunner
      .TryValidateLaunchValues("pack", string.Empty, out var noJobReason)
      .ShouldBeFalse();
    noJobReason.ShouldBe("任务文件路径为空");

    GameProcessRunner
      .TryValidateLaunchValues("bad\"name", "j.json", out var quoteReason)
      .ShouldBeFalse();
    quoteReason.ShouldContain("双引号");

    GameProcessRunner
      .TryValidateLaunchValues("pack", "bad\npath.json", out var newlineReason)
      .ShouldBeFalse();
    newlineReason.ShouldContain("换行");
  }
}
