namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Recording;
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
}
