namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
/// 录制编排器单测：引擎目录校验、任务写出 → 进程 → 结果校验的串接与卡表序号派生（P1/P2），
/// 以及一轮批量录制的编排、沙箱隔离、归集与取消（P3）。
/// </summary>
/// <remarks>
/// 一律注入假进程，不真启游戏（真启需要引擎与 ffmpeg，且同一引擎目录不可并发）。
/// </remarks>
public class RecordingOrchestratorTest : TestClass
{
  private const string ModPackName = "sample_project";

  private string _root = string.Empty;
  private string _engineDir = string.Empty;
  private string _gameDir = string.Empty;
  private string _outputDir = string.Empty;
  private string _packPath = string.Empty;
  private FakeEngineProcess _fake = new();
  private readonly List<FakeEngineProcess> _fakes = new();
  private int _startedProcesses;
  private Mock<ILog> _log = new();

  public RecordingOrchestratorTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Orchestrator_" + Guid.NewGuid().ToString("N")[..8]
    );
    _engineDir = Path.Combine(_root, "LuaSTGSub");
    _gameDir = Path.Combine(_engineDir, EngineLocator.GameDirName);
    _outputDir = Path.Combine(_root, "gifset");
    Directory.CreateDirectory(_gameDir);
    File.WriteAllText(Path.Combine(_gameDir, EngineLocator.LaunchFileName), string.Empty);
    File.WriteAllText(Path.Combine(_gameDir, "LuaSTGSub.exe"), string.Empty);

    _fake = new FakeEngineProcess();
    _log = new Mock<ILog>();
    _fakes.Clear();
    _startedProcesses = 0;
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  [Test]
  public async Task RunAsync_InvalidEngineDir_FailsBeforeTouchingDisk()
  {
    var sandboxRoot = Path.Combine(_root, "sandboxes");
    var request = new RecordingRequest(
      Path.Combine(_root, "not_exists"),
      Path.Combine(_root, "not_exists.zip"),
      _outputDir,
      new RecordingConfig { Parallelism = new(4), SandboxRoot = new(sandboxRoot) }
    );

    var result = await CreateOrchestrator().RunAsync(request);

    result.Succeeded.ShouldBeFalse();
    result.Error.ShouldContain("引擎目录不可用");
    Directory.Exists(sandboxRoot).ShouldBeFalse(); // 前置检查没过就不建沙箱
    Directory.Exists(_outputDir).ShouldBeFalse(); // 也不该留下输出目录
  }

  /// <summary>构造编排器，进程一律由假进程代替。</summary>
  /// <returns>编排器。</returns>
  private RecordingOrchestrator CreateOrchestrator() =>
    new(
      _log.Object,
      new GameProcessRunner(
        _log.Object,
        startInfo =>
        {
          _fake.StartInfo = startInfo;
          return _fake;
        }
      )
    );

  /// <summary>
  /// 用 CMEX 刚写出的任务文件名（即任务号）落一份结果，模拟游戏运行期写出结果文件。
  /// </summary>
  /// <param name="buildJson">按任务号生成结果 JSON。</param>
  private void WriteResultForCurrentJob(Func<string, string> buildJson)
  {
    var jobFile = Directory.GetFiles(RecordingJobWriter.GetJobsDir(_engineDir), "*.json").Single();
    var jobId = Path.GetFileNameWithoutExtension(jobFile);
    File.WriteAllText(
      Path.Combine(RecordingJobWriter.GetResultsDir(_engineDir), $"{jobId}.json"),
      buildJson(jobId)
    );
  }

  /// <summary>造一张战斗阶段的卡（默认取序号 2 的符卡）。</summary>
  /// <param name="absoluteIndex">绝对下标。</param>
  /// <param name="name">卡名（非符与对话阶段为空串）。</param>
  /// <param name="t3">卡最长时长（秒）。</param>
  /// <param name="combatOrdinal">战斗阶段序号。</param>
  /// <returns>目标卡。</returns>
  private static RecordingCardOption CreateCard(
    int absoluteIndex = 3,
    string name = "符卡·一",
    double t3 = 30.0,
    int combatOrdinal = 2
  ) =>
    new(
      absoluteIndex,
      name,
      IsSpellCard: name.Length > 0,
      IsCombat: true,
      t3,
      combatOrdinal,
      0,
      name
    );

  /// <summary>
  /// 为最近一次写出的录制任务落一份结果，并按需造出录制器产物文件（模拟游戏运行期的产出）。
  /// </summary>
  /// <param name="frames">帧数。</param>
  /// <param name="interval">抽帧间隔。</param>
  /// <param name="complete">是否录满被截断。</param>
  /// <param name="success">录制器是否自报成功。</param>
  /// <param name="width">产物宽。</param>
  /// <param name="height">产物高。</param>
  /// <param name="cardName">结果里的卡名（用于名字核对）。</param>
  /// <param name="taskName">录制器任务名（产物文件名）。</param>
  /// <param name="createGif">是否真造出产物文件。</param>
  private void WriteRecordResult(
    int frames = 200,
    int interval = 3,
    bool complete = false,
    bool success = true,
    int width = 640,
    int height = 480,
    string cardName = "符卡·一",
    string taskName = "task_unit",
    bool createGif = true
  )
  {
    var jobFile = Directory
      .GetFiles(RecordingJobWriter.GetJobsDir(_engineDir), "*.json")
      .OrderByDescending(file => file)
      .First();
    var jobId = Path.GetFileNameWithoutExtension(jobFile);
    var gifRelativePath = $"{GifSetBuilder.RecorderOutputDirName}/{taskName}.gif";

    if (createGif)
    {
      var recorderDir = Path.Combine(
        _gameDir,
        GifSetBuilder.RecorderOutputDirName.Replace('/', Path.DirectorySeparatorChar)
      );
      Directory.CreateDirectory(recorderDir);
      SyntheticGif.WriteFile(Path.Combine(recorderDir, $"{taskName}.gif"), width, height);
    }

    File.WriteAllText(
      Path.Combine(RecordingJobWriter.GetResultsDir(_engineDir), $"{jobId}.json"),
      $$"""
      {
        "job_id": "{{jobId}}",
        "status": "ok",
        "absolute_index": 3,
        "card_name": "{{cardName}}",
        "task_name": "{{taskName}}",
        "gif_path": "{{gifRelativePath}}",
        "frames": {{frames}},
        "interval": {{interval}},
        "complete": {{(complete ? "true" : "false")}},
        "success": {{(success ? "true" : "false")}},
        "size": 1024
      }
      """
    );
  }

  [Test]
  public async Task EnumerateAsync_InvalidEngineDir_ReturnsErrorWithoutStartingProcess()
  {
    var outcome = await CreateOrchestrator()
      .EnumerateAsync(Path.Combine(_root, "not_exists"), ModPackName);

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("引擎目录不可用");
    outcome.Cards.ShouldBeEmpty();
    _fake.StartInfo.ShouldBeNull();
  }

  [Test]
  public async Task EnumerateAsync_ValidJob_WritesEnumerateJobAndLaunches()
  {
    _fake.OnStart = () =>
      WriteResultForCurrentJob(jobId =>
        $$"""{"job_id":"{{jobId}}","status":"ok","cards":[{"absolute_index":1,"name":"","is_sc":false,"is_combat":false,"t3":1.0}]}"""
      );

    await CreateOrchestrator().EnumerateAsync(_engineDir, ModPackName);

    // 任务先落盘再启动，且阶段为枚举
    var jobFile = Directory.GetFiles(RecordingJobWriter.GetJobsDir(_engineDir), "*.json").Single();
    File.ReadAllText(jobFile).ShouldContain("enumerate");

    var arguments = _fake.StartInfo!.Arguments;
    arguments.ShouldContain($"setting.mod='{ModPackName}'");
    arguments.ShouldContain("setting.autocmex_job='autocmex/jobs/");
    arguments.ShouldContain("start_game=true");
    arguments.ShouldContain("cheat=true");
  }

  [Test]
  public async Task EnumerateAsync_CardsWithDialogues_DerivesOrdinalsAndNames()
  {
    _fake.OnStart = () =>
      WriteResultForCurrentJob(jobId =>
        $$"""
          {
            "job_id": "{{jobId}}",
            "status": "ok",
            "boss_name": "测试Boss",
            "boss_class": "sample_enm1",
            "cards": [
              {"absolute_index":1,"name":"","is_sc":false,"is_combat":false,"t3":15.0},
              {"absolute_index":2,"name":"","is_sc":false,"is_combat":true,"t3":20.0},
              {"absolute_index":3,"name":"符卡·一","is_sc":true,"is_combat":true,"t3":30.0},
              {"absolute_index":4,"name":"","is_sc":false,"is_combat":false,"t3":10.0},
              {"absolute_index":5,"name":"","is_sc":false,"is_combat":true,"t3":25.0}
            ]
          }
          """
      );

    var outcome = await CreateOrchestrator().EnumerateAsync(_engineDir, ModPackName);

    outcome.Succeeded.ShouldBeTrue();
    outcome.Error.ShouldBeNull();
    outcome.BossName.ShouldBe("测试Boss");
    outcome.BossClass.ShouldBe("sample_enm1");
    outcome.Cards.Count.ShouldBe(5);
    // 对话阶段不计数：5 张里只有 3 张是战斗阶段
    RecordingOrchestrator.CountCombat(outcome.Cards).ShouldBe(3);

    var dialogue = outcome.Cards[0];
    dialogue.IsCombat.ShouldBeFalse();
    dialogue.CombatOrdinal.ShouldBe(GifNaming.NoOrdinal);
    dialogue.NonSpellOrdinal.ShouldBe(GifNaming.NoOrdinal);
    dialogue.EntryName.ShouldBeEmpty();

    var firstNonSpell = outcome.Cards[1];
    firstNonSpell.CombatOrdinal.ShouldBe(1);
    firstNonSpell.NonSpellOrdinal.ShouldBe(1);
    firstNonSpell.EntryName.ShouldBe("普通攻击 1");

    var spell = outcome.Cards[2];
    spell.IsSpellCard.ShouldBeTrue();
    spell.CombatOrdinal.ShouldBe(2);
    spell.NonSpellOrdinal.ShouldBe(GifNaming.NoOrdinal);
    spell.EntryName.ShouldBe("符卡·一");
    spell.T3Seconds.ShouldBe(30.0);

    var secondNonSpell = outcome.Cards[4];
    secondNonSpell.CombatOrdinal.ShouldBe(3);
    secondNonSpell.NonSpellOrdinal.ShouldBe(2);
    secondNonSpell.EntryName.ShouldBe("普通攻击 2");
  }

  [Test]
  public async Task EnumerateAsync_PluginReportsError_ReturnsStatusErrorWithLogTail()
  {
    _fake.OnStart = () =>
      WriteResultForCurrentJob(jobId =>
        $$"""{"job_id":"{{jobId}}","status":"error","error":"boss_not_found"}"""
      );
    File.WriteAllLines(
      Path.Combine(_gameDir, GameProcessRunner.EngineLogFileName),
      new[] { "engine line 1", "engine line 2" }
    );

    var outcome = await CreateOrchestrator().EnumerateAsync(_engineDir, ModPackName);

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("status_error");
    outcome.Error!.ShouldContain("boss_not_found");
    outcome.Cards.ShouldBeEmpty();
    // 失败原因要能连带 engine.log 尾部一起展示
    outcome.EngineLogTail.ShouldNotBeNull();
    outcome.EngineLogTail!.ShouldContain("engine line 2");
  }

  [Test]
  public async Task EnumerateAsync_ProcessNeverExits_ReportsTimeoutAndKillsTree()
  {
    _fake.ExitsImmediately = false;

    var outcome = await CreateOrchestrator()
      .EnumerateAsync(_engineDir, ModPackName, timeout: TimeSpan.FromMilliseconds(150));

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("枚举超时");
    _fake.KillCount.ShouldBe(1);
  }

  [Test]
  public async Task EnumerateAsync_TokenCancelled_ReportsCancelledAndKillsTree()
  {
    _fake.ExitsImmediately = false;
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var outcome = await CreateOrchestrator()
      .EnumerateAsync(_engineDir, ModPackName, cts.Token, TimeSpan.FromSeconds(5));

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("取消");
    _fake.KillCount.ShouldBe(1);
  }

  [Test]
  public void CardTimeout_UsesSmallerOfCardLengthAndFrameSpan()
  {
    // min(60, 350×3/60 = 17.5) + 45 + 350×0.3 = 167.5 秒
    RecordingOrchestrator
      .CardTimeout(CreateCard(t3: 60), interval: 3, maxFrame: 350)
      .TotalSeconds.ShouldBe(167.5, 0.001);

    // 长卡也按「录满帧数」封顶，不会无限等
    RecordingOrchestrator
      .CardTimeout(CreateCard(t3: 600), interval: 3, maxFrame: 350)
      .TotalSeconds.ShouldBe(167.5, 0.001);

    // 短卡按卡长算：min(10, 17.5) + 45 + 105 = 160 秒
    RecordingOrchestrator
      .CardTimeout(CreateCard(t3: 10), interval: 3, maxFrame: 350)
      .TotalSeconds.ShouldBe(160, 0.001);
  }

  [Test]
  public async Task RecordCardAsync_FirstAttemptCompletes_PlacesGifUnderOrdinal()
  {
    _fake.OnStart = () => WriteRecordResult(frames: 200, interval: 3, complete: false);

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    outcome.Succeeded.ShouldBeTrue();
    outcome.Error.ShouldBeNull();
    // 本层只录不归集：产物留在引擎目录的录制器输出里，集内命名与宽高由 RunAsync 归集时定
    outcome.RecorderGifAbsolutePath.ShouldBe(
      GifSetBuilder.GetRecorderGifAbsolutePath(
        _engineDir,
        $"{GifSetBuilder.RecorderOutputDirName}/task_unit.gif"
      )
    );
    File.Exists(outcome.RecorderGifAbsolutePath).ShouldBeTrue();
    outcome.Frames.ShouldBe(200);
    outcome.Interval.ShouldBe(3);
    outcome.Complete.ShouldBeTrue();
    outcome.Attempts.ShouldBe(1);
    outcome.Runs.ShouldBe(1);
    Directory.Exists(_outputDir).ShouldBeFalse();

    // 任务先落盘再启动，阶段为录制且不演前序阶段
    var jobFile = Directory.GetFiles(RecordingJobWriter.GetJobsDir(_engineDir), "*.json").Single();
    var jobText = File.ReadAllText(jobFile);
    jobText.ShouldContain("\"phase\": \"record\"");
    jobText.ShouldContain("\"include_previous\": false");
    _fake.StartInfo!.Arguments.ShouldContain($"setting.mod='{ModPackName}'");
  }

  [Test]
  public async Task RecordCardAsync_FirstTruncated_ReRecordsAndAdoptsSecond()
  {
    var run = 0;
    _fake.OnStart = () =>
    {
      run++;
      if (run == 1)
      {
        WriteRecordResult(
          frames: 350,
          interval: 3,
          complete: true,
          width: 640,
          height: 480,
          taskName: "task_first"
        );
      }
      else
      {
        WriteRecordResult(
          frames: 210,
          interval: 5,
          complete: false,
          width: 800,
          height: 600,
          taskName: "task_second"
        );
      }
    };

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    run.ShouldBe(2);
    outcome.Succeeded.ShouldBeTrue();
    outcome.Attempts.ShouldBe(2);
    outcome.Runs.ShouldBe(2);
    outcome.Interval.ShouldBe(5);
    outcome.Frames.ShouldBe(210);
    // 方案 §6.3.4：凡触发重录的卡一律记 complete=false
    outcome.Complete.ShouldBeFalse();
    // 采用第二次的产物；首次的截断件留在录制器目录里，随沙箱一起丢弃
    outcome.RecorderGifAbsolutePath.ShouldBe(
      GifSetBuilder.GetRecorderGifAbsolutePath(
        _engineDir,
        $"{GifSetBuilder.RecorderOutputDirName}/task_second.gif"
      )
    );
    GifSetBuilder.ReadGifSize(outcome.RecorderGifAbsolutePath).ShouldBe((800, 600));
  }

  [Test]
  public async Task RecordCardAsync_BothAttemptsTruncated_AdoptsSecondAndMarksIncomplete()
  {
    var run = 0;
    _fake.OnStart = () =>
    {
      run++;
      WriteRecordResult(frames: 350, interval: run == 1 ? 3 : 5, complete: true);
    };

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    run.ShouldBe(2);
    outcome.Succeeded.ShouldBeTrue();
    outcome.Attempts.ShouldBe(2);
    outcome.Frames.ShouldBe(350);
    outcome.Interval.ShouldBe(5);
    outcome.Complete.ShouldBeFalse();
  }

  [Test]
  public async Task RecordCardAsync_AttemptWithoutResult_RetriesOnceThenReportsError()
  {
    // 两次都不落结果文件（进程没跑起来或卡在跳卡）
    _fake.OnStart = () => { };

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("result_missing");
    outcome.Attempts.ShouldBe(1);
    outcome.Runs.ShouldBe(2);
    outcome.RecorderGifAbsolutePath.ShouldBeEmpty();
    Directory.Exists(_outputDir).ShouldBeFalse();
  }

  [Test]
  public async Task RecordCardAsync_ProductUnusable_RetriesAndFails()
  {
    _fake.OnStart = () => WriteRecordResult(frames: 0, success: false);

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("product_unusable");
    outcome.Runs.ShouldBe(2);
  }

  [Test]
  public async Task RecordCardAsync_CardNameMismatch_RetriesAndFails()
  {
    // 跳到邻卡：录制器回报的名字与目标卡不符，产物不能算这一张的
    _fake.OnStart = () => WriteRecordResult(cardName: "别的符卡");

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("card_name_mismatch");
    outcome.Runs.ShouldBe(2);
  }

  [Test]
  public async Task RecordCardAsync_GifNotOnDisk_RetriesAndFails()
  {
    // 录制器自报成功但产物没落下来
    _fake.OnStart = () => WriteRecordResult(createGif: false);

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("gif_missing");
    outcome.Runs.ShouldBe(2);
  }

  [Test]
  public async Task RecordCardAsync_SecondAttemptFails_ReportsErrorWithoutGif()
  {
    var run = 0;
    _fake.OnStart = () =>
    {
      run++;
      if (run == 1)
      {
        WriteRecordResult(frames: 350, complete: true);
      }
    };

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("首次截断后重录失败");
    outcome.Attempts.ShouldBe(2);
    // 首次 1 次 + 第二次失败重试 1 次 + 重试前各 1 次
    outcome.Runs.ShouldBe(3);
    outcome.RecorderGifAbsolutePath.ShouldBeEmpty();
    Directory.Exists(_outputDir).ShouldBeFalse();
  }

  [Test]
  public async Task RecordCardAsync_TokenCancelled_ReportsCancelledAndKillsTree()
  {
    _fake.ExitsImmediately = false;
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(
        _engineDir,
        ModPackName,
        CreateCard(),
        new RecordingConfig(),
        cts.Token,
        TimeSpan.FromSeconds(5)
      );

    outcome.Succeeded.ShouldBeFalse();
    outcome.Cancelled.ShouldBeTrue();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("取消");
    outcome.Runs.ShouldBe(1);
    _fake.KillCount.ShouldBe(1);
  }

  [Test]
  public async Task RecordCardAsync_NonCombatCard_RejectedWithoutStartingProcess()
  {
    var dialogue = CreateCard(combatOrdinal: 0) with
    {
      Name = string.Empty,
      IsSpellCard = false,
      IsCombat = false,
    };

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, dialogue, new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("不是战斗阶段");
    outcome.Runs.ShouldBe(0);
    _fake.StartInfo.ShouldBeNull();
  }

  [Test]
  public async Task RecordCardAsync_InvalidEngineDir_RejectedWithoutStartingProcess()
  {
    var outcome = await CreateOrchestrator()
      .RecordCardAsync(
        Path.Combine(_root, "not_exists"),
        ModPackName,
        CreateCard(),
        new RecordingConfig()
      );

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("引擎目录不可用");
    _fake.StartInfo.ShouldBeNull();
  }

  [Test]
  public async Task RunAsync_TwoWorkers_RecordsEveryCardAndWritesReport()
  {
    var packPath = PrepareSandboxReadyEngine();
    var cards = CreateCardTable();
    var sandboxRoot = Path.Combine(_root, "sandboxes");

    var result = await CreateBulkOrchestrator(SimulateEngine(cards))
      .RunAsync(CreateRequest(packPath, sandboxRoot, parallelism: 2));

    result.Succeeded.ShouldBeTrue();
    result.Error.ShouldBeNull();
    result.Warning.ShouldBeNull();
    result.OutputDir.ShouldBe(_outputDir);

    var report = result.Report;
    report.ModPackName.ShouldBe(ModPackName);
    report.BossName.ShouldBe("测试Boss");
    report.BossClass.ShouldBe("sample_enm1");
    report.Parallelism.ShouldBe(2);
    report.WorkersStarted.ShouldBe(2);
    report.Cancelled.ShouldBeFalse();
    report.GeneratedAt.ShouldNotBeEmpty();
    // 对话阶段不计数：4 张卡里只有 3 张是战斗阶段
    report.TotalCombat.ShouldBe(3);
    report.Succeeded.ShouldBe(3);
    report.Failed.ShouldBe(0);

    // 报告按卡表顺序，与「谁先录完」无关
    report.Cards.Count.ShouldBe(3);
    report.Cards.Select(card => card.CombatOrdinal).ShouldBe(new[] { 1, 2, 3 });
    report
      .Cards.Select(card => card.EntryName)
      .ShouldBe(new[] { "普通攻击 1", "符卡·一", "符卡·二" });
    report.Cards[0].AbsoluteIndex.ShouldBe(2);
    report.Cards[0].Width.ShouldBe(640);
    report.Cards[0].Height.ShouldBe(480);
    report.Cards[0].Frames.ShouldBe(200);
    report.Cards[0].Fps.ShouldBe(20, 0.001);
    report.Cards[0].DurationSeconds.ShouldBe(10, 0.001);
    report.Cards[0].Complete.ShouldBeTrue(); // 一遍过
    foreach (var card in report.Cards)
    {
      card.Status.ShouldBe(RecordingCardStatus.Ok);
      card.FileName.ShouldBe($"{card.CombatOrdinal}.gif");
      card.Attempts.ShouldBe(1);
      card.Runs.ShouldBe(1);
      card.Error.ShouldBeNull();
      File.Exists(Path.Combine(_outputDir, card.FileName)).ShouldBeTrue();
    }

    File.Exists(Path.Combine(_outputDir, RecordingOrchestrator.ReportFileName)).ShouldBeTrue();
    File.ReadAllText(Path.Combine(_outputDir, RecordingOrchestrator.ReportFileName))
      .ShouldContain("\"succeeded\": 3");

    // 1 次枚举 + 3 次录制，没有多余的重录
    _startedProcesses.ShouldBe(4);
    // worker 1 复用枚举沙箱 → 2 个 worker 只占 2 份沙箱
    _fakes.Select(fake => fake.StartInfo!.WorkingDirectory).Distinct().Count().ShouldBe(2);

    // 真引擎目录全程只读：运行期目录与录制器目录都不该在源目录里出现
    Directory.Exists(RecordingJobWriter.GetRuntimeDir(_engineDir)).ShouldBeFalse();
    Directory.Exists(Path.Combine(_gameDir, GifSetBuilder.RecorderOutputDirName)).ShouldBeFalse();

    SandboxDirs(sandboxRoot).ShouldBeEmpty();
  }

  [Test]
  public async Task RunAsync_NoCombatCards_FailsWithoutRecording()
  {
    var packPath = PrepareSandboxReadyEngine();
    var cards = new List<RecordingCardInfo>
    {
      new()
      {
        AbsoluteIndex = 1,
        Name = string.Empty,
        IsCombat = false,
        T3Seconds = 15,
      },
    };
    var sandboxRoot = Path.Combine(_root, "sandboxes");

    var result = await CreateBulkOrchestrator(SimulateEngine(cards))
      .RunAsync(CreateRequest(packPath, sandboxRoot, parallelism: 2));

    result.Succeeded.ShouldBeFalse();
    result.Error.ShouldNotBeNull();
    result.Error!.ShouldContain("没有可录制的战斗阶段");
    result.Report.TotalCombat.ShouldBe(0);
    result.Report.Cards.ShouldBeEmpty();
    _startedProcesses.ShouldBe(1); // 只起了枚举那一次
    Directory.Exists(_outputDir).ShouldBeFalse();
    SandboxDirs(sandboxRoot).ShouldBeEmpty();
  }

  [Test]
  public async Task RunAsync_OneCardFails_OtherCardsStillCollected()
  {
    var packPath = PrepareSandboxReadyEngine();
    var cards = CreateCardTable();
    var sandboxRoot = Path.Combine(_root, "sandboxes");
    // 只有序号 1（绝对下标 2 的「普通攻击 1」）回插件错误，其余照常录成
    var simulate = SimulateEngine(
      cards,
      (card, jobId) =>
        card.AbsoluteIndex == 2
          ? new RecordingJobResult
          {
            JobId = jobId,
            Status = RecordingJobStatus.Error,
            Error = "boss_not_found",
          }
          : null
    );

    var result = await CreateBulkOrchestrator(simulate)
      .RunAsync(CreateRequest(packPath, sandboxRoot, parallelism: 2));

    result.Succeeded.ShouldBeTrue(); // 单卡失败不中断整轮
    result.Report.TotalCombat.ShouldBe(3);
    result.Report.Succeeded.ShouldBe(2);
    result.Report.Failed.ShouldBe(1);

    var failed = result.Report.Cards[0];
    failed.Status.ShouldBe(RecordingCardStatus.Failed);
    failed.Error.ShouldNotBeNull();
    failed.Error!.ShouldContain("boss_not_found");
    failed.FileName.ShouldBeEmpty();

    // 失败卡不留产物，其余卡照常归集，报告仍落盘
    File.Exists(Path.Combine(_outputDir, "1.gif")).ShouldBeFalse();
    File.Exists(Path.Combine(_outputDir, "2.gif")).ShouldBeTrue();
    File.Exists(Path.Combine(_outputDir, "3.gif")).ShouldBeTrue();
    File.Exists(Path.Combine(_outputDir, RecordingOrchestrator.ReportFileName)).ShouldBeTrue();
    SandboxDirs(sandboxRoot).ShouldBeEmpty();
  }

  [Test]
  public async Task RunAsync_EnumerationFails_ReportsReasonWithEngineLogTail()
  {
    var packPath = PrepareSandboxReadyEngine();
    var sandboxRoot = Path.Combine(_root, "sandboxes");

    var result = await CreateBulkOrchestrator(
        (_, startInfo, _) =>
        {
          var engineDir = Path.GetDirectoryName(startInfo.WorkingDirectory)!;
          var spec = ReadNewestJob(engineDir);
          File.WriteAllLines(
            Path.Combine(startInfo.WorkingDirectory, GameProcessRunner.EngineLogFileName),
            new[] { "engine line 1", "engine line 2" }
          );
          WriteJobResult(
            engineDir,
            spec,
            new RecordingJobResult
            {
              JobId = spec.JobId,
              Status = RecordingJobStatus.Error,
              Error = "boss_not_found",
            }
          );
        }
      )
      .RunAsync(CreateRequest(packPath, sandboxRoot, parallelism: 2));

    result.Succeeded.ShouldBeFalse();
    result.Error.ShouldNotBeNull();
    result.Error!.ShouldContain("枚举卡表失败");
    result.Error!.ShouldContain("boss_not_found");
    // 失败原因要能连带 engine.log 尾部一起展示
    result.EngineLogTail.ShouldNotBeNull();
    result.EngineLogTail!.ShouldContain("engine line 2");
    Directory.Exists(_outputDir).ShouldBeFalse();
    SandboxDirs(sandboxRoot).ShouldBeEmpty();
  }

  [Test]
  public async Task RunAsync_CancelledWhileRecording_KeepsCollectedWorkAndCleansSandboxes()
  {
    var packPath = PrepareSandboxReadyEngine();
    var cards = CreateCardTable();
    var sandboxRoot = Path.Combine(_root, "sandboxes");
    using var cts = new CancellationTokenSource();

    var running = CreateBulkOrchestrator(SimulateEngine(cards, hangOnRecord: true))
      .RunAsync(CreateRequest(packPath, sandboxRoot, parallelism: 1), null, cts.Token);

    // 等第一次录制真的起了进程再取消，保证取消落在「录制中」而不是领卡前
    await WaitUntilAsync(() => _startedProcesses >= 2);
    cts.Cancel();
    var result = await running;

    result.Succeeded.ShouldBeTrue();
    result.Cancelled.ShouldBeTrue();
    result.Report.Cancelled.ShouldBeTrue();
    result.Report.TotalCombat.ShouldBe(3);
    result.Report.Succeeded.ShouldBe(0);
    result.Report.Failed.ShouldBe(0);
    foreach (var card in result.Report.Cards)
    {
      // 没轮到与正在录的一律记「未录制」，失败数保持 0
      card.Status.ShouldBe(RecordingCardStatus.Unrecorded);
      card.Error.ShouldNotBeNull();
    }

    // 在跑的进程树被杀掉，沙箱清理干净，报告仍然落盘（但不该有产物）
    _fakes.Sum(fake => fake.KillCount).ShouldBeGreaterThan(0);
    SandboxDirs(sandboxRoot).ShouldBeEmpty();
    File.Exists(Path.Combine(_outputDir, RecordingOrchestrator.ReportFileName)).ShouldBeTrue();
    Directory.GetFiles(_outputDir, "*.gif").ShouldBeEmpty();
  }

  [Test]
  public async Task RunAsync_CollectFails_ReportsCardFailureAndWarningOnly()
  {
    var packPath = PrepareSandboxReadyEngine();
    var cards = new List<RecordingCardInfo>
    {
      new()
      {
        AbsoluteIndex = 1,
        Name = "符卡·一",
        IsSpellCard = true,
        IsCombat = true,
        T3Seconds = 30,
      },
    };
    var sandboxRoot = Path.Combine(_root, "sandboxes");
    // 把输出目录的位置先占成一个文件：归集与写报告都会失败，但整轮不算失败
    File.WriteAllText(_outputDir, "occupied");

    var result = await CreateBulkOrchestrator(SimulateEngine(cards))
      .RunAsync(CreateRequest(packPath, sandboxRoot, parallelism: 1));

    result.Succeeded.ShouldBeTrue();
    result.Warning.ShouldNotBeNull();
    result.Warning!.ShouldContain("写报告失败");
    result.Report.Succeeded.ShouldBe(0);
    result.Report.Failed.ShouldBe(1);
    result.Report.Cards[0].Status.ShouldBe(RecordingCardStatus.Failed);
    result.Report.Cards[0].Error.ShouldNotBeNull();
    result.Report.Cards[0].Error!.ShouldContain("归集产物失败");
    SandboxDirs(sandboxRoot).ShouldBeEmpty();
  }

  /// <summary>
  /// 把引擎目录补齐成沙箱清单要求的最小骨架（插件、包、用户数据、启动文件与 exe）并放好工程包。
  /// </summary>
  /// <param name="packName">工程包名（不含扩展名）。</param>
  /// <returns>工程包绝对路径。</returns>
  /// <remarks>
  /// RunAsync 全程在沙箱里跑，故源目录只需满足 <see cref="RecordingSandbox"/> 的清单校验：
  /// 缺 <c>plugins/autocmex</c>、引擎 exe 或工程包都会在建沙箱时被拒。
  /// </remarks>
  private string PrepareSandboxReadyEngine(string packName = ModPackName)
  {
    WriteGameFile("packages/script/core.lua", "core");
    WriteGameFile("plugins/plugins.json", "[]");
    WriteGameFile("plugins/autocmex/main.lua", "autocmex");
    WriteGameFile("plugins/danmaku_recorder_1.0.0/recorder.lua", "recorder");
    WriteGameFile("userdata/setting.json", "{}");
    File.WriteAllText(Path.Combine(_gameDir, "d3dcompiler_47.dll"), "dll");

    var modDir = Path.Combine(_gameDir, "mod");
    Directory.CreateDirectory(modDir);
    _packPath = Path.Combine(modDir, $"{packName}.zip");
    File.WriteAllText(_packPath, "zip");
    return _packPath;
  }

  /// <summary>在引擎的 <c>game/</c> 下写一个文件（自动建目录）。</summary>
  /// <param name="relativePath">相对 <c>game/</c> 的路径（正斜杠）。</param>
  /// <param name="content">文件内容。</param>
  private void WriteGameFile(string relativePath, string content)
  {
    var path = Path.Combine(_gameDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
  }

  /// <summary>
  /// 造一个批量录制用的编排器：每次启动进程都新建一个假进程（并行时不共享），并按启动顺序回调。
  /// </summary>
  /// <param name="onStart">启动回调：本次的假进程、启动信息、启动序号（1 基）。</param>
  /// <returns>编排器。</returns>
  private RecordingOrchestrator CreateBulkOrchestrator(
    Action<FakeEngineProcess, ProcessStartInfo, int> onStart
  ) =>
    new(
      _log.Object,
      new GameProcessRunner(
        _log.Object,
        startInfo =>
        {
          var fake = new FakeEngineProcess { StartInfo = startInfo };
          var index = Interlocked.Increment(ref _startedProcesses);
          fake.OnStart = () => onStart(fake, startInfo, index);
          lock (_fakes)
          {
            _fakes.Add(fake);
          }
          return fake;
        }
      )
    );

  /// <summary>
  /// 造一个「照做」的假引擎：枚举阶段回卡表，录制阶段落结果并造出产物——一切都写在它自己的工作目录
  /// （即它分到的那个沙箱）里，与真引擎的运行方式一致。
  /// </summary>
  /// <param name="cards">枚举阶段要回的卡表。</param>
  /// <param name="recordResult">
  /// 录制阶段的自定结果：参数为目标卡与本次任务号；返回 <c>null</c> 的卡按默认的「一遍录成」处理。
  /// 不传则所有卡都按默认处理。
  /// </param>
  /// <param name="hangOnRecord">录制阶段是否永不退出（用于验证取消时杀进程树）。</param>
  /// <returns>可直接交给 <see cref="CreateBulkOrchestrator"/> 的启动回调。</returns>
  private static Action<FakeEngineProcess, ProcessStartInfo, int> SimulateEngine(
    IReadOnlyList<RecordingCardInfo> cards,
    Func<RecordingCardInfo, string, RecordingJobResult?>? recordResult = null,
    bool hangOnRecord = false
  ) =>
    (fake, startInfo, _) =>
    {
      var gameDir = startInfo.WorkingDirectory;
      var engineDir = Path.GetDirectoryName(gameDir)!;
      var spec = ReadNewestJob(engineDir);
      if (spec.Phase == RecordingJobPhase.Enumerate)
      {
        WriteJobResult(
          engineDir,
          spec,
          new RecordingJobResult
          {
            JobId = spec.JobId,
            Status = RecordingJobStatus.Ok,
            BossName = "测试Boss",
            BossClass = "sample_enm1",
            Cards = cards.ToList(),
          }
        );
        return;
      }

      if (hangOnRecord)
      {
        fake.ExitsImmediately = false;
        return;
      }

      var card = cards.Single(info => info.AbsoluteIndex == spec.AbsoluteIndex);
      var custom = recordResult?.Invoke(card, spec.JobId);
      if (custom != null)
      {
        WriteJobResult(engineDir, spec, custom);
        return;
      }

      var gifRelativePath = $"{GifSetBuilder.RecorderOutputDirName}/{spec.JobId}.gif";
      var gifPath = GifSetBuilder.GetRecorderGifAbsolutePath(engineDir, gifRelativePath);
      Directory.CreateDirectory(Path.GetDirectoryName(gifPath)!);
      SyntheticGif.WriteFile(gifPath, 640, 480);
      WriteJobResult(
        engineDir,
        spec,
        new RecordingJobResult
        {
          JobId = spec.JobId,
          Status = RecordingJobStatus.Ok,
          AbsoluteIndex = card.AbsoluteIndex,
          CardName = card.Name,
          TaskName = spec.JobId,
          GifPath = gifRelativePath,
          Frames = 200,
          Interval = spec.Interval ?? 3,
          MaxFrame = spec.MaxFrame,
          Complete = false, // 未录满：一遍过
          Success = true,
          Size = 1024,
        }
      );
    };

  /// <summary>造一张卡表：对话 + 非符 + 两张符卡（序号由绝对下标派生）。</summary>
  /// <returns>卡表。</returns>
  private static List<RecordingCardInfo> CreateCardTable() =>
    new()
    {
      new()
      {
        AbsoluteIndex = 1,
        Name = string.Empty,
        IsCombat = false,
        T3Seconds = 15,
      },
      new()
      {
        AbsoluteIndex = 2,
        Name = string.Empty,
        IsCombat = true,
        T3Seconds = 20,
      },
      new()
      {
        AbsoluteIndex = 3,
        Name = "符卡·一",
        IsSpellCard = true,
        IsCombat = true,
        T3Seconds = 30,
      },
      new()
      {
        AbsoluteIndex = 4,
        Name = "符卡·二",
        IsSpellCard = true,
        IsCombat = true,
        T3Seconds = 40,
      },
    };

  /// <summary>造一轮批量录制的输入。</summary>
  /// <param name="packPath">工程包绝对路径。</param>
  /// <param name="sandboxRoot">沙箱根目录。</param>
  /// <param name="parallelism">并行度。</param>
  /// <returns>本轮输入。</returns>
  private RecordingRequest CreateRequest(string packPath, string sandboxRoot, int parallelism) =>
    new(
      _engineDir,
      packPath,
      _outputDir,
      new RecordingConfig { Parallelism = new(parallelism), SandboxRoot = new(sandboxRoot) }
    );

  /// <summary>列沙箱根下现存的沙箱目录（根不存在时为空）。</summary>
  /// <param name="sandboxRoot">沙箱根目录。</param>
  /// <returns>沙箱目录路径数组。</returns>
  private static string[] SandboxDirs(string sandboxRoot) =>
    Directory.Exists(sandboxRoot) ? Directory.GetDirectories(sandboxRoot) : Array.Empty<string>();

  /// <summary>读引擎目录里最近写出的任务描述（假引擎据此判断本次是枚举还是录制）。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns>任务描述。</returns>
  private static RecordingJobSpec ReadNewestJob(string engineDir)
  {
    // 任务号内含毫秒时间戳，按文件名降序即「最新」
    var jobFile = Directory
      .GetFiles(RecordingJobWriter.GetJobsDir(engineDir), "*.json")
      .OrderByDescending(file => file)
      .First();
    return JsonSerializer.Deserialize<RecordingJobSpec>(File.ReadAllText(jobFile))!;
  }

  /// <summary>按任务描述落一份结果，模拟游戏运行期写出结果文件。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="spec">任务描述。</param>
  /// <param name="result">结果内容。</param>
  private static void WriteJobResult(
    string engineDir,
    RecordingJobSpec spec,
    RecordingJobResult result
  ) =>
    File.WriteAllText(
      RecordingJobWriter.GetResultAbsolutePath(engineDir, spec),
      JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
    );

  /// <summary>等并发时序条件成立（最多等 5 秒），用于「录制中取消」这类需要中途介入的用例。</summary>
  /// <param name="condition">条件。</param>
  private static async Task WaitUntilAsync(Func<bool> condition)
  {
    for (var i = 0; i < 500 && !condition(); i++)
    {
      await Task.Delay(10);
    }
    condition().ShouldBeTrue("等待并发时序超时");
  }
}
