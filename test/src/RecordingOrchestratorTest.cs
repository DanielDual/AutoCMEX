namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
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
/// 录制编排器单测（P1：枚举最小闭环）：引擎目录校验、任务写出 → 进程 → 结果校验的串接，
/// 以及卡表序号与清单名的派生。
/// </summary>
/// <remarks>
/// 一律注入假进程，不真启游戏（真启需要引擎与 ffmpeg，且同一引擎目录不可并发）。
/// </remarks>
public class RecordingOrchestratorTest : TestClass
{
  private const string ModPackName = "CMEX22_Qerfcxz";

  private string _root = string.Empty;
  private string _engineDir = string.Empty;
  private string _gameDir = string.Empty;
  private string _outputDir = string.Empty;
  private FakeEngineProcess _fake = new();
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
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
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
            "boss_class": "cmex22_enm1",
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
    outcome.BossClass.ShouldBe("cmex22_enm1");
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
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

    outcome.Succeeded.ShouldBeTrue();
    outcome.Error.ShouldBeNull();
    outcome.GifFileName.ShouldBe("2.gif");
    outcome.Width.ShouldBe(640);
    outcome.Height.ShouldBe(480);
    outcome.Frames.ShouldBe(200);
    outcome.Interval.ShouldBe(3);
    outcome.Complete.ShouldBeTrue();
    outcome.Attempts.ShouldBe(1);
    outcome.Runs.ShouldBe(1);
    File.Exists(Path.Combine(_outputDir, "2.gif")).ShouldBeTrue();

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
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

    run.ShouldBe(2);
    outcome.Succeeded.ShouldBeTrue();
    outcome.Attempts.ShouldBe(2);
    outcome.Runs.ShouldBe(2);
    outcome.Interval.ShouldBe(5);
    outcome.Frames.ShouldBe(210);
    // 方案 §6.3.4：凡触发重录的卡一律记 complete=false
    outcome.Complete.ShouldBeFalse();
    // 集里只留第二次的产物，首次的截断件不进集
    var gifs = Directory.GetFiles(_outputDir);
    gifs.Length.ShouldBe(1);
    GifSetBuilder.ReadGifSize(gifs[0]).ShouldBe((800, 600));
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
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

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
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("result_missing");
    outcome.Attempts.ShouldBe(1);
    outcome.Runs.ShouldBe(2);
    File.Exists(Path.Combine(_outputDir, "2.gif")).ShouldBeFalse();
  }

  [Test]
  public async Task RecordCardAsync_ProductUnusable_RetriesAndFails()
  {
    _fake.OnStart = () => WriteRecordResult(frames: 0, success: false);

    var outcome = await CreateOrchestrator()
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

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
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

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
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

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
      .RecordCardAsync(_engineDir, ModPackName, CreateCard(), _outputDir, new RecordingConfig());

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("首次截断后重录失败");
    outcome.Attempts.ShouldBe(2);
    // 首次 1 次 + 第二次失败重试 1 次 + 重试前各 1 次
    outcome.Runs.ShouldBe(3);
    File.Exists(Path.Combine(_outputDir, "2.gif")).ShouldBeFalse();
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
        _outputDir,
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
      .RecordCardAsync(_engineDir, ModPackName, dialogue, _outputDir, new RecordingConfig());

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
        _outputDir,
        new RecordingConfig()
      );

    outcome.Succeeded.ShouldBeFalse();
    outcome.Error.ShouldNotBeNull();
    outcome.Error!.ShouldContain("引擎目录不可用");
    _fake.StartInfo.ShouldBeNull();
  }
}
