namespace AutoCMEX.Core.Recording;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// 枚举阶段产出的一张卡：插件原始字段 + CMEX 侧派生的序号与清单名。
/// </summary>
/// <param name="AbsoluteIndex">在插件 <c>cards</c> 里的绝对下标（1 基，含对话阶段）。</param>
/// <param name="Name">插件给出的名字（符卡名；非符与对话阶段为空串）。</param>
/// <param name="IsSpellCard">是否符卡。</param>
/// <param name="IsCombat">是否战斗阶段（符卡或非符）。</param>
/// <param name="T3Seconds">该卡最长时长（秒），用于估算录制耗时。</param>
/// <param name="CombatOrdinal">在全部战斗阶段里的序号（1 基），非战斗阶段为 0。</param>
/// <param name="NonSpellOrdinal">在非符序列里的序号（1 基），非非符为 0。</param>
/// <param name="EntryName">清单里的名字：符卡名 / 「普通攻击 N」/ 空串（非战斗阶段），见 <see cref="GifNaming"/>。</param>
public sealed record RecordingCardOption(
  int AbsoluteIndex,
  string Name,
  bool IsSpellCard,
  bool IsCombat,
  double T3Seconds,
  int CombatOrdinal,
  int NonSpellOrdinal,
  string EntryName
);

/// <summary>
/// 枚举阶段的结果。
/// </summary>
/// <param name="Error">失败原因；成功为 <c>null</c>。</param>
/// <param name="BossName">定位到的 Boss 名（成功时非空）。</param>
/// <param name="BossClass">定位到的 Boss 类名（成功时非空）。</param>
/// <param name="Cards">补全序号后的卡表（按绝对下标升序）；失败时为空表。</param>
/// <param name="Process">底层进程结果，用于取诊断信息（<c>engine.log</c> 尾部、退出码等）。</param>
public sealed record RecordingEnumerateOutcome(
  string? Error,
  string? BossName,
  string? BossClass,
  IReadOnlyList<RecordingCardOption> Cards,
  GameProcessOutcome? Process
)
{
  /// <summary>是否枚举成功。</summary>
  public bool Succeeded => Error == null;

  /// <summary>引擎日志尾部的拷贝，失败时用于展示原因。</summary>
  public string? EngineLogTail => Process?.EngineLogTail;

  /// <summary>造一个失败结果。</summary>
  /// <param name="error">失败原因。</param>
  /// <param name="process">底层进程结果。</param>
  /// <returns>失败结果。</returns>
  public static RecordingEnumerateOutcome Failed(
    string error,
    GameProcessOutcome? process = null
  ) => new(error, null, null, Array.Empty<RecordingCardOption>(), process);
}

/// <summary>
/// 录制编排：引擎定位 → 枚举卡表 → 逐卡录制 → 归集产物。
/// </summary>
/// <remarks>
/// <para>
/// 当前实现**枚举阶段**（P1「插件 + 枚举最小闭环」）：调用方拿到带序号的卡表即可驱动
/// UI 与后续录制；逐卡录制（P2）与重试/取消/报告（P3）在同一入口下续接，
/// 调用方式不变。
/// </para>
/// <para>
/// 串行约束：同一 <paramref name="engineDir"/> 不得并发调用——录制器临时目录在启动时被
/// 清空，产物名又是秒级时间戳，并发会互相破坏。
/// </para>
/// </remarks>
public sealed class RecordingOrchestrator
{
  /// <summary>枚举阶段默认超时：冷启动 + 载包 + 定位 Boss，实测远低于该值。</summary>
  public static readonly TimeSpan EnumerateTimeout = TimeSpan.FromSeconds(60);

  private readonly ILog _log;
  private readonly GameProcessRunner _runner;

  /// <summary>构造编排器。</summary>
  /// <param name="log">日志。</param>
  /// <param name="runner">进程运行器；传 <c>null</c> 时用默认实现（单测注入假实现以免真启游戏）。</param>
  /// <exception cref="ArgumentNullException"><paramref name="log"/> 为 <c>null</c>。</exception>
  public RecordingOrchestrator(ILog log, GameProcessRunner? runner = null)
  {
    _log = log ?? throw new ArgumentNullException(nameof(log));
    _runner = runner ?? new GameProcessRunner(log);
  }

  /// <summary>
  /// 枚举指定引擎目录下当前 Boss 的完整卡表（含对话阶段），并补全 CMEX 侧序号。
  /// </summary>
  /// <param name="engineDir">已选定的引擎根目录（其下须有 <c>game/LuaSTGSub.exe</c>）。</param>
  /// <param name="modPackName">工程包名（引擎 <c>mod/</c> 下的包名）。</param>
  /// <param name="cancellationToken">取消令牌，用于中止用户已放弃的枚举。</param>
  /// <param name="timeout">超时；传 <c>null</c> 用 <see cref="EnumerateTimeout"/>。</param>
  /// <returns>
  /// 枚举结果；失败时 <see cref="RecordingEnumerateOutcome.Error"/> 给出可展示的原因，
  /// 并尽量带上 <see cref="RecordingEnumerateOutcome.EngineLogTail"/>。
  /// </returns>
  public async Task<RecordingEnumerateOutcome> EnumerateAsync(
    string engineDir,
    string modPackName,
    CancellationToken cancellationToken = default,
    TimeSpan? timeout = null
  )
  {
    var budget = timeout ?? EnumerateTimeout;

    // 先自行校验引擎目录，避免拉起一个注定失败、只留含糊日志的进程
    if (!EngineLocator.TryValidate(engineDir, out var reason))
    {
      return RecordingEnumerateOutcome.Failed($"引擎目录不可用：{reason}");
    }

    var spec = RecordingJobWriter.CreateEnumerateJob(RecordingJobWriter.NewJobId("enum"));
    RecordingJobWriter.Write(engineDir, spec);
    _log.Print(
      $"RecordingOrchestrator: 开始枚举 engine={engineDir} mod={modPackName} job={spec.JobId}"
    );

    var process = await _runner.RunAsync(engineDir, modPackName, spec, budget, cancellationToken);

    if (process.StartupError != null)
    {
      return RecordingEnumerateOutcome.Failed(process.StartupError, process);
    }

    if (process.Cancelled)
    {
      return RecordingEnumerateOutcome.Failed("枚举已取消", process);
    }

    if (process.TimedOut)
    {
      return RecordingEnumerateOutcome.Failed($"枚举超时（{budget.TotalSeconds:F0} 秒）", process);
    }

    if (process.Result == null)
    {
      // job_id 不符、status 非 ok、卡表为空等都在运行器判掉，原因直接可展示
      return RecordingEnumerateOutcome.Failed(process.ResultError ?? "result_missing", process);
    }

    var result = process.Result;
    var cards = Derive(result.Cards!);
    _log.Print(
      $"RecordingOrchestrator: 枚举完成 boss={result.BossName}（{result.BossClass}）"
        + $"卡表 {cards.Count} 项，战斗阶段 {CountCombat(cards)} 项"
    );

    return new RecordingEnumerateOutcome(null, result.BossName, result.BossClass, cards, process);
  }

  /// <summary>统计战斗阶段数量（供日志与调用方预估录制时长）。</summary>
  /// <param name="cards">卡表。</param>
  /// <returns>符卡与非符的总数。</returns>
  public static int CountCombat(IReadOnlyList<RecordingCardOption> cards)
  {
    var count = 0;
    foreach (var card in cards)
    {
      if (card.IsCombat)
      {
        count++;
      }
    }

    return count;
  }

  /// <summary>给插件的原始卡表补上序号与清单名（纯计算，序号规则唯一来源是 <see cref="GifNaming"/>）。</summary>
  /// <param name="cards">插件返回的卡表（按绝对下标升序，非空）。</param>
  /// <returns>补全后的卡表。</returns>
  private static IReadOnlyList<RecordingCardOption> Derive(IReadOnlyList<RecordingCardInfo> cards)
  {
    var derived = new List<RecordingCardOption>(cards.Count);
    foreach (var card in cards)
    {
      var combatOrdinal = GifNaming.CombatOrdinalOf(cards, card.AbsoluteIndex);
      var nonSpellOrdinal = GifNaming.NonSpellOrdinalOf(cards, card.AbsoluteIndex);
      derived.Add(
        new RecordingCardOption(
          card.AbsoluteIndex,
          card.Name,
          card.IsSpellCard,
          card.IsCombat,
          card.T3Seconds,
          combatOrdinal,
          nonSpellOrdinal,
          GifNaming.ResolveEntryName(card, nonSpellOrdinal)
        )
      );
    }

    return derived;
  }
}
