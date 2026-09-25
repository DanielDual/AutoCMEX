namespace AutoCMEX.Core.Recording;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Models;

/// <summary>批量录制（一轮跑完）这一半的实现，另一半见 <c>RecordingOrchestrator.cs</c>。</summary>
public sealed partial class RecordingOrchestrator
{
  /// <summary>报告文件名（落在输出目录下）。</summary>
  public const string ReportFileName = "recording_report.json";

  /// <summary>
  /// 一轮批量录制：清扫残留沙箱 → 前置检查 → 枚举 → 并行逐卡录制 → 串行归集 → 写报告 → 删沙箱。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 真实引擎目录全程只读：工程包只复制进沙箱，任务/结果/产物都落在沙箱，归集时只往输出目录写。
  /// </para>
  /// <para>
  /// worker 数 = <c>min(并行度, 战斗卡数)</c>，再按临时卷剩余空间压一次（只降不报错，见
  /// <see cref="RecordingRunResult.Warning"/>）；枚举用的那个沙箱直接复用给 worker 1，省一份复制。
  /// </para>
  /// <para>
  /// 归集固定串行执行：输出目录与报告是全局共享资源，且报告必须按战斗序号升序，与完成顺序无关。
  /// </para>
  /// <para>
  /// 取消：停止领卡，已起的进程由各自的 <c>KillTree</c> 杀掉；随后照常归集已产出、删沙箱、写报告
  /// （<see cref="RecordingReport.Cancelled"/> 为 <c>true</c>，未录的卡记「未录制」）。
  /// </para>
  /// </remarks>
  /// <param name="request">本轮输入（引擎、工程包、输出目录、配置）。</param>
  /// <param name="progress">进度回调（可为 <c>null</c>）。</param>
  /// <param name="cancellationToken">取消令牌。</param>
  /// <returns>本轮结果；前置失败时 <see cref="RecordingRunResult.Error"/> 给出可展示的原因。</returns>
  /// <exception cref="ArgumentNullException"><paramref name="request"/> 或其 <c>Config</c> 为 <c>null</c>。</exception>
  public async Task<RecordingRunResult> RunAsync(
    RecordingRequest request,
    IProgress<RecordingProgress>? progress = null,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Config);

    var config = request.Config;
    var report = new RecordingReport
    {
      GeneratedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
      Parallelism = RecordingConfig.ClampParallelism(config.Parallelism.Value),
    };
    var hub = new RecordingProgressHub(progress);
    var sandboxes = new List<RecordingSandbox>();
    var warnings = new List<string>();

    RecordingRunResult Failure(string error, string? engineLogTail = null)
    {
      _log.Err($"RecordingOrchestrator: 本轮中止 —— {error}");
      return new RecordingRunResult(error, report, request.OutputDir, null, engineLogTail);
    }

    // 阶段 0：引擎、可执行文件、包名、磁盘空间
    if (!EngineLocator.TryValidate(request.EngineDir, out var engineReason))
    {
      return Failure($"引擎目录不可用：{engineReason}");
    }
    var exePath = EngineLocator.FindEngineExe(request.EngineDir);
    if (exePath == null)
    {
      return Failure($"引擎目录里找不到 {EngineLocator.ExePattern}");
    }
    var engineExeFileName = Path.GetFileName(exePath);
    var modPackName = Path.GetFileNameWithoutExtension(request.ModPackZipPath);
    if (string.IsNullOrWhiteSpace(modPackName))
    {
      return Failure($"工程包名无法识别：{request.ModPackZipPath}");
    }
    report.ModPackName = modPackName;

    var sandboxRoot = RecordingSandbox.ResolveRootDir(config.SandboxRoot.Value);
    var swept = RecordingSandbox.SweepLeftovers(sandboxRoot, RecordingSandbox.LeftoverMaxAge, _log);
    if (swept > 0)
    {
      _log.Print($"RecordingOrchestrator: 清扫上次残留沙箱 {swept} 个");
    }

    var footprint = RecordingSandbox.EstimateFootprint(
      request.EngineDir,
      request.ModPackZipPath,
      engineExeFileName
    );
    if (!RecordingSandbox.HasEnoughFreeSpace(sandboxRoot, footprint, out var freeBytes))
    {
      return Failure(
        $"临时目录剩余空间不足：单个沙箱约需 {ToMegabytes(footprint)} MB，"
          + $"可用 {ToMegabytes(freeBytes)} MB（{sandboxRoot}）"
      );
    }

    try
    {
      // 阶段 1：枚举（用 worker 1 的沙箱，稍后复用）
      hub.SetStage(RecordingStage.Enumerate);
      RecordingSandbox enumSandbox;
      try
      {
        enumSandbox = await RecordingSandbox.CreateAsync(
          request.EngineDir,
          request.ModPackZipPath,
          engineExeFileName,
          sandboxRoot,
          1,
          _log,
          cancellationToken
        );
      }
      catch (RecordingSandboxException ex)
      {
        return Failure(ex.Message);
      }
      sandboxes.Add(enumSandbox);

      var enumeration = await EnumerateAsync(enumSandbox.EngineDir, modPackName, cancellationToken);
      if (!enumeration.Succeeded)
      {
        return Failure($"枚举卡表失败：{enumeration.Error}", enumeration.EngineLogTail);
      }
      report.BossName = enumeration.BossName;
      report.BossClass = enumeration.BossClass;

      var combatCards = new List<RecordingCardOption>();
      foreach (var card in enumeration.Cards)
      {
        if (card.IsCombat)
        {
          combatCards.Add(card);
        }
      }
      report.TotalCombat = combatCards.Count;
      if (combatCards.Count == 0)
      {
        return Failure("卡表里没有可录制的战斗阶段");
      }
      hub.SetTotal(combatCards.Count);

      // worker 数：并行度先被卡数压一次，再被临时卷剩余空间压一次（只降，不中途报错）
      var workerCount = Math.Min(report.Parallelism, combatCards.Count);
      var affordable = AffordableWorkers(freeBytes, footprint);
      if (affordable < workerCount)
      {
        warnings.Add(
          $"可用空间 {ToMegabytes(freeBytes)} MB 只够 {affordable} 个沙箱"
            + $"（每个约 {ToMegabytes(footprint)} MB），并行度由 {workerCount} 降为 {affordable}"
        );
        _log.Warn($"RecordingOrchestrator: {warnings[^1]}");
        workerCount = affordable;
      }

      // 阶段 2：并行逐卡录制
      hub.SetStage(RecordingStage.Record);
      var (outcomes, schedule) = await RunWorkersAsync(
        request,
        config,
        modPackName,
        engineExeFileName,
        sandboxRoot,
        combatCards,
        workerCount,
        sandboxes,
        hub,
        cancellationToken
      );
      foreach (var workerError in schedule.WorkerErrors)
      {
        warnings.Add(workerError);
      }
      report.WorkersStarted = schedule.WorkersStarted;

      // 阶段 3：串行归集、计数、写报告
      hub.SetStage(RecordingStage.Collect);
      Collect(report, combatCards, outcomes, request.OutputDir);
      report.Cancelled = cancellationToken.IsCancellationRequested;
      foreach (var cardReport in report.Cards)
      {
        if (cardReport.Status == RecordingCardStatus.Ok)
        {
          report.Succeeded++;
        }
        else if (cardReport.Status == RecordingCardStatus.Failed)
        {
          report.Failed++;
        }
      }

      warnings.AddRange(WriteReport(report, request.OutputDir));
      _log.Print(
        $"RecordingOrchestrator: 本轮结束 —— 成功 {report.Succeeded}，失败 {report.Failed}，"
          + $"未录制 {report.TotalCombat - report.Succeeded - report.Failed}，"
          + $"worker {report.WorkersStarted}/{report.Parallelism}"
      );

      return new RecordingRunResult(
        null,
        report,
        request.OutputDir,
        warnings.Count == 0 ? null : string.Join("；", warnings)
      );
    }
    finally
    {
      lock (sandboxes)
      {
        foreach (var sandbox in sandboxes)
        {
          sandbox.Delete();
        }
      }
    }
  }

  /// <summary>起 worker、建沙箱、并行逐卡录制；回传逐卡结果与调度统计。</summary>
  /// <param name="request">本轮输入。</param>
  /// <param name="config">录制配置。</param>
  /// <param name="modPackName">工程包名。</param>
  /// <param name="engineExeFileName">引擎可执行文件名（各沙箱都复制这一个）。</param>
  /// <param name="sandboxRoot">沙箱根目录。</param>
  /// <param name="combatCards">战斗卡表（升序）。</param>
  /// <param name="workerCount">实际生效的 worker 数。</param>
  /// <param name="sandboxes">已建沙箱（含枚举沙箱），本方法会追加。</param>
  /// <param name="hub">进度聚合器。</param>
  /// <param name="cancellationToken">取消令牌。</param>
  /// <returns>逐卡结果（键为战斗序号）与调度统计。</returns>
  private async Task<(
    ConcurrentDictionary<int, RecordingCardOutcome> Outcomes,
    RecordingScheduleResult Schedule
  )> RunWorkersAsync(
    RecordingRequest request,
    RecordingConfig config,
    string modPackName,
    string engineExeFileName,
    string sandboxRoot,
    IReadOnlyList<RecordingCardOption> combatCards,
    int workerCount,
    List<RecordingSandbox> sandboxes,
    RecordingProgressHub hub,
    CancellationToken cancellationToken
  )
  {
    var outcomes = new ConcurrentDictionary<int, RecordingCardOutcome>();

    RecordingSandbox? GetSandbox(int workerIndex)
    {
      lock (sandboxes)
      {
        return sandboxes.FirstOrDefault(sandbox => sandbox.WorkerIndex == workerIndex);
      }
    }

    var schedule = await new RecordingScheduler(_log).RunAsync(
      combatCards,
      workerCount,
      (workerIndex, cancellationToken_) =>
        PrepareWorkerAsync(
          request,
          engineExeFileName,
          sandboxRoot,
          workerIndex,
          sandboxes,
          cancellationToken_
        ),
      async (workerIndex, card, cancellationToken_) =>
      {
        var sandbox =
          GetSandbox(workerIndex)
          ?? throw new InvalidOperationException($"worker {workerIndex} 的沙箱不存在");

        var outcome = await RecordCardAsync(
          sandbox.EngineDir,
          modPackName,
          card,
          config,
          cancellationToken_,
          null,
          attempt => hub.CardAttempt(workerIndex, card, attempt)
        );
        outcomes[card.CombatOrdinal] = outcome;
        hub.CardFinished(workerIndex, outcome);
      },
      cancellationToken
    );

    return (outcomes, schedule);
  }

  /// <summary>worker 准备：worker 1 复用枚举沙箱，其余各建一份。</summary>
  /// <param name="request">本轮输入。</param>
  /// <param name="engineExeFileName">引擎可执行文件名。</param>
  /// <param name="sandboxRoot">沙箱根目录。</param>
  /// <param name="workerIndex">worker 序号（1 基）。</param>
  /// <param name="sandboxes">已建沙箱，本方法会追加。</param>
  /// <param name="cancellationToken">取消令牌。</param>
  /// <returns><c>null</c> 表示成功，否则为失败原因。</returns>
  private async Task<string?> PrepareWorkerAsync(
    RecordingRequest request,
    string engineExeFileName,
    string sandboxRoot,
    int workerIndex,
    List<RecordingSandbox> sandboxes,
    CancellationToken cancellationToken
  )
  {
    if (workerIndex == 1)
    {
      return null;
    }

    try
    {
      var sandbox = await RecordingSandbox.CreateAsync(
        request.EngineDir,
        request.ModPackZipPath,
        engineExeFileName,
        sandboxRoot,
        workerIndex,
        _log,
        cancellationToken
      );
      lock (sandboxes)
      {
        sandboxes.Add(sandbox);
      }
      return null;
    }
    catch (RecordingSandboxException ex)
    {
      return ex.Message;
    }
  }

  /// <summary>
  /// 把各 worker 的产物按战斗序号归集进输出目录，并组装逐卡报告（串行执行）。
  /// </summary>
  /// <remarks>
  /// 顺序以卡表为准而非完成顺序，故报告天然按序号升序；没拿到结果的卡记「未录制」而不是「失败」——
  /// 用户据此能区分「试过没成」与「压根没轮到」。
  /// </remarks>
  /// <param name="report">待填充的报告。</param>
  /// <param name="combatCards">战斗卡表（按绝对下标升序）。</param>
  /// <param name="outcomes">逐卡结果（键为战斗序号）。</param>
  /// <param name="outputDir">GIF 集输出目录。</param>
  private void Collect(
    RecordingReport report,
    IReadOnlyList<RecordingCardOption> combatCards,
    IReadOnlyDictionary<int, RecordingCardOutcome> outcomes,
    string outputDir
  )
  {
    foreach (var card in combatCards)
    {
      var entry = new RecordingCardReport
      {
        CombatOrdinal = card.CombatOrdinal,
        AbsoluteIndex = card.AbsoluteIndex,
        EntryName = card.EntryName,
      };
      report.Cards.Add(entry);

      if (!outcomes.TryGetValue(card.CombatOrdinal, out var outcome))
      {
        entry.Status = RecordingCardStatus.Unrecorded;
        entry.Error = "未开始录制（已取消，或承载它的 worker 未启动）";
        continue;
      }

      entry.Attempts = outcome.Attempts;
      entry.Runs = outcome.Runs;
      entry.Frames = outcome.Frames;
      entry.Complete = outcome.Complete;
      entry.Fps = outcome.Interval > 0 ? 60.0 / outcome.Interval : 0;
      entry.DurationSeconds = outcome.Frames * outcome.Interval / 60.0;

      if (outcome.Cancelled)
      {
        entry.Status = RecordingCardStatus.Unrecorded;
        entry.Error = outcome.Error ?? "录制已取消";
        continue;
      }

      if (!outcome.Succeeded)
      {
        entry.Status = RecordingCardStatus.Failed;
        entry.Error = outcome.Error;
        entry.EngineLogTail = outcome.EngineLogTail;
        continue;
      }

      try
      {
        var target = GifSetBuilder.PlaceCardGif(
          outcome.RecorderGifAbsolutePath,
          outputDir,
          card.CombatOrdinal
        );
        var (width, height) = GifSetBuilder.ReadGifSize(target);
        entry.FileName = GifSetBuilder.EntryFileName(card.CombatOrdinal);
        entry.Width = width;
        entry.Height = height;
        entry.Status = RecordingCardStatus.Ok;
      }
      catch (Exception ex)
        when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
      {
        entry.Status = RecordingCardStatus.Failed;
        entry.Error = $"归集产物失败：{ex.Message}";
      }
    }
  }

  /// <summary>把报告写进输出目录（失败只作为提示回传，不中断本轮）。</summary>
  /// <param name="report">本轮报告。</param>
  /// <param name="outputDir">GIF 集输出目录。</param>
  /// <returns>写盘失败时的提示；成功为空集合。</returns>
  private static IEnumerable<string> WriteReport(RecordingReport report, string outputDir)
  {
    try
    {
      Directory.CreateDirectory(outputDir);
      File.WriteAllText(Path.Combine(outputDir, ReportFileName), report.ToJson());
      return Array.Empty<string>();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return new[] { $"写报告失败：{ex.Message}" };
    }
  }

  /// <summary>按剩余空间算最多能建几个沙箱（留 10% 余量，至少 1 个）。</summary>
  /// <param name="freeBytes">可用字节数；未知为 -1。</param>
  /// <param name="footprintBytes">单个沙箱字节数。</param>
  /// <returns>可承受的 worker 数；无法探测时返回 <see cref="int.MaxValue"/>（即不压并行度）。</returns>
  private static int AffordableWorkers(long freeBytes, long footprintBytes) =>
    freeBytes <= 0 || footprintBytes <= 0
      ? int.MaxValue
      : (int)(freeBytes * 9 / 10 / footprintBytes);

  /// <summary>字节数转 MB（向上取整，便于日志展示）。</summary>
  /// <param name="bytes">字节数；负数原样返回。</param>
  /// <returns>MB 数。</returns>
  private static long ToMegabytes(long bytes) => bytes < 0 ? bytes : (bytes + 1048575) / 1048576;

  /// <summary>
  /// 进度快照的组装器：多 worker 并发更新，内部加锁后产出不可变快照。
  /// </summary>
  private sealed class RecordingProgressHub
  {
    private readonly IProgress<RecordingProgress>? _progress;
    private readonly object _gate = new();
    private readonly Dictionary<int, RecordingWorkerProgress> _running = new();
    private string _stage = RecordingStage.Enumerate;
    private int _total;
    private int _completed;
    private int _failed;

    /// <summary>构造聚合器。</summary>
    /// <param name="progress">外部进度回调；可为 <c>null</c>（只记账不上报）。</param>
    public RecordingProgressHub(IProgress<RecordingProgress>? progress) => _progress = progress;

    /// <summary>切换阶段并上报。</summary>
    /// <param name="stage">阶段，取 <see cref="RecordingStage"/> 之一。</param>
    public void SetStage(string stage)
    {
      lock (_gate)
      {
        _stage = stage;
      }
      Report();
    }

    /// <summary>设定战斗卡总数并上报。</summary>
    /// <param name="total">战斗卡总数。</param>
    public void SetTotal(int total)
    {
      lock (_gate)
      {
        _total = total;
      }
      Report();
    }

    /// <summary>某 worker 开始一次尝试：登记为「正在录」并上报。</summary>
    /// <param name="workerIndex">worker 序号。</param>
    /// <param name="card">目标卡。</param>
    /// <param name="attempt">尝试序号。</param>
    public void CardAttempt(int workerIndex, RecordingCardOption card, int attempt)
    {
      lock (_gate)
      {
        _running[workerIndex] = new RecordingWorkerProgress(
          workerIndex,
          card.CombatOrdinal,
          card.EntryName,
          attempt
        );
      }
      Report();
    }

    /// <summary>某 worker 录完一张卡：移出「正在录」、累计成败并上报。</summary>
    /// <param name="workerIndex">worker 序号。</param>
    /// <param name="outcome">该卡结果。</param>
    public void CardFinished(int workerIndex, RecordingCardOutcome outcome)
    {
      lock (_gate)
      {
        _running.Remove(workerIndex);
        if (outcome.Succeeded)
        {
          _completed++;
        }
        else if (!outcome.Cancelled)
        {
          _failed++;
        }
      }
      Report();
    }

    /// <summary>产出并派发一份快照。</summary>
    private void Report()
    {
      if (_progress == null)
      {
        return;
      }

      RecordingProgress snapshot;
      lock (_gate)
      {
        snapshot = new RecordingProgress(
          _stage,
          _total,
          _completed,
          _failed,
          _running.Values.OrderBy(progress => progress.WorkerIndex).ToList()
        );
      }
      _progress.Report(snapshot);
    }
  }
}
