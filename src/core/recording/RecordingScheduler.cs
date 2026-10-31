namespace AutoCMEX.Core.Recording;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chickensoft.Log;

/// <summary>并行调度结果。</summary>
/// <param name="WorkersStarted">真正跑起来的 worker 数（准备失败的不计）。</param>
/// <param name="WorkerErrors">worker 准备失败与处理项异常的原因（供报告与日志）。</param>
public sealed record RecordingScheduleResult(
  int WorkersStarted,
  IReadOnlyList<string> WorkerErrors
);

/// <summary>
/// 把一组待办项分给 N 个 worker 并行处理：每个 worker 先准备自己的资源（如沙箱），
/// 再按序号升序「领取」待办项，直到取完。
/// </summary>
/// <remarks>
/// <para>
/// <b>领取式而非预分配</b>：某项耗时差异很大（短卡 30 秒、长卡数分钟），预分配会让慢卡拖住整轮，
/// 领取式天然削峰。
/// </para>
/// <para>
/// <b>先准备再领取</b>：调用方按 <c>min(并行度, 项数)</c> 决定 worker 数，故每个 worker 至少有一项可做；
/// 先建沙箱再领卡，就不会出现「沙箱建失败 → 那张卡被认领后没人录」的空洞。
/// </para>
/// <para>
/// <b>失败隔离</b>：worker 准备失败或处理项抛异常都只记进 <see cref="RecordingScheduleResult.WorkerErrors"/>
/// 并让该 worker 退出，其余 worker 照常把待办项做完。
/// </para>
/// </remarks>
public sealed class RecordingScheduler
{
  private readonly ILog _log;

  /// <summary>构造调度器。</summary>
  /// <param name="log">日志。</param>
  public RecordingScheduler(ILog log) => _log = log;

  /// <summary>跑一轮并行调度。</summary>
  /// <typeparam name="TItem">待办项类型。</typeparam>
  /// <param name="items">待办项（按希望的处理顺序排列）。</param>
  /// <param name="parallelism">并行度（被项数压住）。</param>
  /// <param name="prepareWorker">worker 准备：返回 <c>null</c> 表示成功，否则为失败原因。</param>
  /// <param name="processItem">处理一项；异常由调度器兜住并记录。</param>
  /// <param name="cancellationToken">取消令牌（取消后不再领取新项）。</param>
  /// <returns>调度结果。</returns>
  public async Task<RecordingScheduleResult> RunAsync<TItem>(
    IReadOnlyList<TItem> items,
    int parallelism,
    Func<int, CancellationToken, Task<string?>> prepareWorker,
    Func<int, TItem, CancellationToken, Task> processItem,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(items);
    ArgumentNullException.ThrowIfNull(prepareWorker);
    ArgumentNullException.ThrowIfNull(processItem);

    var workerCount = Math.Min(Math.Max(parallelism, 1), items.Count);
    if (workerCount == 0)
    {
      return new RecordingScheduleResult(0, Array.Empty<string>());
    }

    var next = -1;
    var started = 0;
    var errors = new ConcurrentQueue<string>();
    var workers = new List<Task>(workerCount);

    for (var index = 0; index < workerCount; index++)
    {
      var workerIndex = index + 1;
      workers.Add(
        Task.Run(
          async () =>
          {
            if (cancellationToken.IsCancellationRequested)
            {
              return; // 预取消：连准备都不做（省掉 N 份沙箱复制）
            }

            string? prepareError;
            try
            {
              prepareError = await prepareWorker(workerIndex, cancellationToken)
                .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
              prepareError = ex.Message;
            }

            if (prepareError != null)
            {
              errors.Enqueue($"worker {workerIndex} 准备失败：{prepareError}");
              _log.Warn($"worker {workerIndex} 准备失败：{prepareError}");
              return;
            }
            Interlocked.Increment(ref started);

            while (!cancellationToken.IsCancellationRequested)
            {
              var itemIndex = Interlocked.Increment(ref next);
              if (itemIndex >= items.Count)
              {
                return;
              }
              try
              {
                await processItem(workerIndex, items[itemIndex], cancellationToken)
                  .ConfigureAwait(false);
              }
              catch (Exception ex)
              {
                errors.Enqueue($"worker {workerIndex} 处理第 {itemIndex + 1} 项异常：{ex.Message}");
                _log.Err($"worker {workerIndex} 处理第 {itemIndex + 1} 项异常：{ex}");
              }
            }
          },
          CancellationToken.None
        )
      );
    }

    await Task.WhenAll(workers).ConfigureAwait(false);
    return new RecordingScheduleResult(started, errors.ToList());
  }
}
