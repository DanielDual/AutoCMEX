namespace AutoCMEX.Core.Recording;

using System;
using System.Collections.Generic;
using System.IO;
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
/// 单张卡的录制结果。
/// </summary>
/// <param name="Error">失败原因；成功为 <c>null</c>。</param>
/// <param name="Card">目标卡（含序号与清单名）。</param>
/// <param name="GifFileName">集内文件名（如 <c>3.gif</c>）；失败时为空串。</param>
/// <param name="Width">产物宽（像素）；失败时为 0。</param>
/// <param name="Height">产物高（像素）；失败时为 0。</param>
/// <param name="Frames">最终采用产物的帧数；失败时为 0。</param>
/// <param name="Interval">最终采用产物的抽帧间隔（GIF 帧率 = 60 / 该值）；失败时为 0。</param>
/// <param name="Complete">该卡是否完整录完，口径见 <see cref="RecordingOrchestrator.RecordCardAsync"/>。</param>
/// <param name="Attempts">区间尝试次数（1 = 首次即录完；2 = 首次截断后重录）。</param>
/// <param name="Runs">实际启动引擎进程的次数（含失败重试）。</param>
/// <param name="Cancelled">是否因取消而中止。</param>
/// <param name="Process">最后一次进程结果，用于取 <c>engine.log</c> 尾部等诊断信息。</param>
public sealed record RecordingCardOutcome(
  string? Error,
  RecordingCardOption Card,
  string GifFileName,
  int Width,
  int Height,
  int Frames,
  int Interval,
  bool Complete,
  int Attempts,
  int Runs,
  bool Cancelled,
  GameProcessOutcome? Process
)
{
  /// <summary>是否录制成功（产物已按序号落到输出目录）。</summary>
  public bool Succeeded => Error == null;

  /// <summary>引擎日志尾部的拷贝，失败时用于展示原因。</summary>
  public string? EngineLogTail => Process?.EngineLogTail;

  /// <summary>造一个失败结果。</summary>
  /// <param name="error">失败原因。</param>
  /// <param name="card">目标卡。</param>
  /// <param name="attempts">区间尝试次数。</param>
  /// <param name="runs">进程启动次数。</param>
  /// <param name="process">最后一次进程结果。</param>
  /// <param name="cancelled">是否因取消而中止。</param>
  /// <returns>失败结果。</returns>
  public static RecordingCardOutcome Failed(
    string error,
    RecordingCardOption card,
    int attempts = 0,
    int runs = 0,
    GameProcessOutcome? process = null,
    bool cancelled = false
  ) => new(error, card, string.Empty, 0, 0, 0, 0, false, attempts, runs, cancelled, process);
}

/// <summary>
/// 录制编排：引擎定位 → 枚举卡表 → 逐卡录制 → 归集产物。
/// </summary>
/// <remarks>
/// <para>
/// 已实现**枚举阶段**（P1）与**单卡录制闭环**（P2）：后者见 <see cref="RecordCardAsync"/>，
/// 一次调用即可把一张卡录成集内文件 <c>{序号}.gif</c>。逐卡串联、重试策略汇总、取消与
/// 报告（P3）在同一入口下续接，调用方式不变。
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

  /// <summary>录制阶段的固定余量（秒）：启动引擎 + 载包 + 跳到目标卡。</summary>
  public const double CardStartupSeconds = 45;

  /// <summary>
  /// 录制阶段每帧的编码余量（秒）：收尾时录制器同步跑 ffmpeg 编码，实测约 0.08–0.17 s/帧。
  /// </summary>
  public const double EncodeSecondsPerFrame = 0.3;

  /// <summary>单卡失败后的重试次数（方案 §6.3.5：失败自动重试 1 次）。</summary>
  private const int MaxRetriesPerAttempt = 1;

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

    var spec = RecordingJobWriter.WriteEnumerateJob(engineDir, RecordingJobWriter.NewJobId("enum"));
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

  /// <summary>
  /// 单卡一次尝试的进程超时预算：<c>min(t3, maxFrame×interval/60) + 45 秒 + 每帧 0.3 秒</c>
  /// （方案 §6.3.1）。
  /// </summary>
  /// <param name="card">目标卡（<c>t3</c> 用于取「卡自然结束」与「录满帧数」的较小者）。</param>
  /// <param name="interval">抽帧间隔（1..60，越大覆盖时间越长但帧率越低）。</param>
  /// <param name="maxFrame">帧数上限。</param>
  /// <returns>该次尝试的超时预算。</returns>
  public static TimeSpan CardTimeout(RecordingCardOption card, int interval, int maxFrame)
  {
    var recordSeconds = Math.Min(card.T3Seconds, maxFrame * (double)interval / 60.0);

    return TimeSpan.FromSeconds(
      recordSeconds + CardStartupSeconds + (maxFrame * EncodeSecondsPerFrame)
    );
  }

  /// <summary>
  /// 录制一张卡，产出集内文件 <c>{序号}.gif</c>。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 「录两遍」是常态而非补救：<c>interval=3</c>（20 fps）时 350 帧只覆盖 17.5 秒，长卡必然录满被
  /// 截断；故首次被截断即换 <c>interval=5</c>（12 fps）重录一次，并**采用第二次的产物**（方案 §6.3.4）。
  /// </para>
  /// <para>
  /// <see cref="RecordingCardOutcome.Complete"/> 取保守口径：凡触发重录的卡一律记 <c>false</c>，
  /// 实际帧数与帧率照实回传，调用方可据此判断。
  /// </para>
  /// <para>
  /// 失败重试只发生在单次尝试内部（方案 §6.3.5）：遇到超时 / 非零退出 / 结果缺失 / 产物缺失 /
  /// 名字核对不符即自动重试 1 次；该次尝试两次都失败则本卡判失败并返回原因，由调用方决定是否继续下一张。
  /// 第二次尝试彻底失败时**不**回退首次的截断产物——半截 GIF 混进集里比明确失败更难排查。
  /// </para>
  /// </remarks>
  /// <param name="engineDir">已选定的引擎根目录。</param>
  /// <param name="modPackName">工程包名（引擎 <c>mod/</c> 下的包名）。</param>
  /// <param name="card">目标卡（须为战斗阶段，序号已在枚举阶段派生）。</param>
  /// <param name="outputDir">GIF 集输出目录（不存在时创建；同名产物会被覆盖）。</param>
  /// <param name="config">录制配置（帧数上限与两档抽帧间隔）。</param>
  /// <param name="cancellationToken">取消令牌，用于中止用户已放弃的录制。</param>
  /// <param name="timeout">
  /// 单次尝试的超时；传 <c>null</c> 用 <see cref="CardTimeout"/>。该预算按「一次尝试」计，
  /// 不随重试与第二次尝试叠加。
  /// </param>
  /// <returns>单卡录制结果；失败时 <see cref="RecordingCardOutcome.Error"/> 给出可展示的原因。</returns>
  /// <exception cref="ArgumentNullException"><paramref name="card"/> 或 <paramref name="config"/> 为 <c>null</c>。</exception>
  /// <exception cref="ArgumentException"><paramref name="outputDir"/> 为空。</exception>
  public async Task<RecordingCardOutcome> RecordCardAsync(
    string engineDir,
    string modPackName,
    RecordingCardOption card,
    string outputDir,
    RecordingConfig config,
    CancellationToken cancellationToken = default,
    TimeSpan? timeout = null
  )
  {
    ArgumentNullException.ThrowIfNull(card);
    ArgumentNullException.ThrowIfNull(config);
    ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

    if (!card.IsCombat)
    {
      return RecordingCardOutcome.Failed($"第 {card.AbsoluteIndex} 项不是战斗阶段，无需录制", card);
    }

    // 先自行校验引擎目录，避免拉起一个注定失败、只留含糊日志的进程
    if (!EngineLocator.TryValidate(engineDir, out var reason))
    {
      return RecordingCardOutcome.Failed($"引擎目录不可用：{reason}", card);
    }

    var maxFrame = config.MaxFrame.Value;
    if (maxFrame <= 0)
    {
      return RecordingCardOutcome.Failed($"帧数上限非法：{maxFrame}", card);
    }

    var adoptedInterval = config.FirstInterval.Value;
    var first = await RunAttemptAsync(
      engineDir,
      modPackName,
      card,
      adoptedInterval,
      maxFrame,
      timeout,
      cancellationToken
    );

    if (first.Cancelled)
    {
      return RecordingCardOutcome.Failed(
        "录制已取消",
        card,
        1,
        first.Runs,
        first.Process,
        cancelled: true
      );
    }

    if (first.Error != null)
    {
      return RecordingCardOutcome.Failed(first.Error, card, 1, first.Runs, first.Process);
    }

    var adopted = first;
    var attempts = 1;
    var runs = first.Runs;
    var complete = true;

    if (first.Result!.Complete == true)
    {
      adoptedInterval = config.SecondInterval.Value;
      _log.Print(
        $"RecordingOrchestrator: 卡 {card.CombatOrdinal}（{card.EntryName}）录满 {first.Result.Frames} 帧被截断，"
          + $"改用 interval={adoptedInterval} 重录"
      );

      var second = await RunAttemptAsync(
        engineDir,
        modPackName,
        card,
        adoptedInterval,
        maxFrame,
        timeout,
        cancellationToken
      );

      if (second.Cancelled)
      {
        return RecordingCardOutcome.Failed(
          "录制已取消",
          card,
          2,
          first.Runs + second.Runs,
          second.Process,
          cancelled: true
        );
      }

      if (second.Error != null)
      {
        return RecordingCardOutcome.Failed(
          $"首次截断后重录失败：{second.Error}",
          card,
          2,
          first.Runs + second.Runs,
          second.Process
        );
      }

      adopted = second;
      attempts = 2;
      runs += second.Runs;
      complete = false;
    }

    var result = adopted.Result!;
    var interval = result.Interval ?? adoptedInterval;

    try
    {
      var target = GifSetBuilder.PlaceCardGif(
        GifSetBuilder.GetRecorderGifAbsolutePath(engineDir, result.GifPath!),
        outputDir,
        card.CombatOrdinal
      );
      var (width, height) = GifSetBuilder.ReadGifSize(target);

      _log.Print(
        $"RecordingOrchestrator: 卡 {card.CombatOrdinal}（{card.EntryName}）完成 {width}×{height}，"
          + $"{result.Frames} 帧，interval={interval}，尝试 {attempts} 次"
      );

      return new RecordingCardOutcome(
        null,
        card,
        GifSetBuilder.EntryFileName(card.CombatOrdinal),
        width,
        height,
        result.Frames ?? 0,
        interval,
        complete,
        attempts,
        runs,
        false,
        adopted.Process
      );
    }
    catch (Exception ex)
      when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
      return RecordingCardOutcome.Failed(
        $"归集产物失败：{ex.Message}",
        card,
        attempts,
        runs,
        adopted.Process
      );
    }
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

  /// <summary>
  /// 跑一次区间尝试：写录制任务 → 启动引擎 → 校验结果与产物，进程类失败按方案 §6.3.5 重试。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="modPackName">工程包名。</param>
  /// <param name="card">目标卡。</param>
  /// <param name="interval">本次尝试的抽帧间隔。</param>
  /// <param name="maxFrame">帧数上限。</param>
  /// <param name="timeout">单次进程超时；<c>null</c> 时按 <see cref="CardTimeout"/> 计算。</param>
  /// <param name="cancellationToken">取消令牌。</param>
  /// <returns>该次尝试的结论（成败、采用的结果、进程结果与启动次数）。</returns>
  private async Task<CardAttempt> RunAttemptAsync(
    string engineDir,
    string modPackName,
    RecordingCardOption card,
    int interval,
    int maxFrame,
    TimeSpan? timeout,
    CancellationToken cancellationToken
  )
  {
    var budget = timeout ?? CardTimeout(card, interval, maxFrame);
    var runs = 0;
    string? failure = null;
    GameProcessOutcome? last = null;

    for (var attempt = 0; attempt <= MaxRetriesPerAttempt; attempt++)
    {
      runs++;

      // 每次尝试都换新任务号并清掉上一轮结果，免得把旧结果当成这一次的产物
      var spec = RecordingJobWriter.WriteRecordJob(
        engineDir,
        RecordingJobWriter.NewJobId("rec"),
        card.AbsoluteIndex,
        interval,
        maxFrame
      );
      RecordingJobWriter.DeleteResultIfExists(engineDir, spec);
      _log.Print(
        $"RecordingOrchestrator: 录卡 {card.CombatOrdinal}（{card.EntryName}）"
          + $"第 {attempt + 1} 次进程 job={spec.JobId} interval={interval}"
      );

      last = await _runner.RunAsync(engineDir, modPackName, spec, budget, cancellationToken);

      if (last.Cancelled)
      {
        return new CardAttempt(null, null, last, runs, true);
      }

      failure = EvaluateAttempt(engineDir, card, last, budget);
      if (failure == null)
      {
        return new CardAttempt(null, last.Result, last, runs, false);
      }

      _log.Print($"RecordingOrchestrator: 卡 {card.CombatOrdinal} 本次尝试失败：{failure}");
    }

    return new CardAttempt(failure, null, last, runs, false);
  }

  /// <summary>判定一次进程结果是否产出了可用产物（方案 §6.6 的判定点）。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="card">目标卡。</param>
  /// <param name="process">进程结果。</param>
  /// <param name="budget">本次尝试的超时预算（用于措辞）。</param>
  /// <returns>失败原因；一切正常时为 <c>null</c>。</returns>
  private static string? EvaluateAttempt(
    string engineDir,
    RecordingCardOption card,
    GameProcessOutcome process,
    TimeSpan budget
  )
  {
    if (process.StartupError != null)
    {
      return process.StartupError;
    }

    if (process.TimedOut)
    {
      return $"进程超时（{budget.TotalSeconds:F0} 秒）";
    }

    if (process.ExitCode is not null and not 0)
    {
      return $"引擎非零退出：{process.ExitCode}";
    }

    if (process.Result == null)
    {
      // 结果缺失 / job_id 不符 / status 非 ok 都由运行器判掉，原因直接可展示
      return process.ResultError ?? "result_missing";
    }

    var result = process.Result;

    // 产物可用性：录制器自报成功，且留下了任务名与帧数
    if (
      result.Success != true
      || result.Frames is null or <= 0
      || string.IsNullOrWhiteSpace(result.TaskName)
    )
    {
      return $"product_unusable: success={result.Success} frames={result.Frames} task={result.TaskName}";
    }

    // 名字核对：防止跳到邻卡、把别人的 GIF 当成这一张（非符与对话阶段的名字为空串）
    if (!SameCardName(result.CardName, card.Name))
    {
      return $"card_name_mismatch: 期望「{card.Name}」，实际「{result.CardName}」";
    }

    // 产物落盘：录制器可能自报成功却没把文件落下来
    if (
      string.IsNullOrWhiteSpace(result.GifPath)
      || !File.Exists(GifSetBuilder.GetRecorderGifAbsolutePath(engineDir, result.GifPath))
    )
    {
      return $"gif_missing: 录制器产物未落盘（{result.GifPath}）";
    }

    return null;
  }

  /// <summary>比对录制器回报的卡名与目标卡名（两侧去空白后按序比较）。</summary>
  /// <param name="recordedName">结果里的 <c>card_name</c>。</param>
  /// <param name="expectedName">枚举阶段拿到的卡名。</param>
  /// <returns>是否为同一张卡。</returns>
  private static bool SameCardName(string? recordedName, string? expectedName) =>
    string.Equals(
      (recordedName ?? string.Empty).Trim(),
      (expectedName ?? string.Empty).Trim(),
      StringComparison.Ordinal
    );

  /// <summary>一次区间尝试的结论。</summary>
  /// <param name="Error">失败原因；成功为 <c>null</c>。</param>
  /// <param name="Result">成功时的结果文件内容。</param>
  /// <param name="Process">最后一次进程结果。</param>
  /// <param name="Runs">本次尝试启动的进程次数（含重试）。</param>
  /// <param name="Cancelled">是否被取消。</param>
  private sealed record CardAttempt(
    string? Error,
    RecordingJobResult? Result,
    GameProcessOutcome? Process,
    int Runs,
    bool Cancelled
  );
}
