namespace AutoCMEX;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Recording;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 并行调度器单测：分工顺序、worker 数自适应、失败隔离与取消传播。
/// </summary>
/// <remarks>
/// 全部用内存假任务，不碰文件系统；真机并行见临时探针。
/// </remarks>
public class RecordingSchedulerTest : TestClass
{
  public RecordingSchedulerTest(Node testScene)
    : base(testScene) { }

  private static RecordingScheduler CreateScheduler() => new(new Mock<ILog>().Object);

  [Test]
  public async Task RunAsync_NoItems_StartsNoWorker()
  {
    var schedule = await CreateScheduler()
      .RunAsync(
        Array.Empty<int>(),
        16,
        (_, _) => Task.FromResult<string?>(null),
        (_, _, _) => Task.CompletedTask
      );

    schedule.WorkersStarted.ShouldBe(0);
    schedule.WorkerErrors.ShouldBeEmpty();
  }

  [Test]
  public async Task RunAsync_DistributesEveryItemExactlyOnce()
  {
    var items = Enumerable.Range(0, 7).ToList();
    var seen = new ConcurrentBag<int>();

    var schedule = await CreateScheduler()
      .RunAsync(
        items,
        3,
        (_, _) => Task.FromResult<string?>(null),
        (_, item, _) =>
        {
          seen.Add(item);
          return Task.CompletedTask;
        }
      );

    schedule.WorkersStarted.ShouldBe(3);
    seen.OrderBy(item => item).ShouldBe(items);
  }

  [Test]
  public async Task RunAsync_WorkerCountIsCappedByItemCount()
  {
    var started = 0;

    var schedule = await CreateScheduler()
      .RunAsync(
        new[] { 1, 2 },
        16,
        (_, _) =>
        {
          Interlocked.Increment(ref started);
          return Task.FromResult<string?>(null);
        },
        (_, _, _) => Task.CompletedTask
      );

    started.ShouldBe(2);
    schedule.WorkersStarted.ShouldBe(2);
  }

  [Test]
  public async Task RunAsync_PrepareFailure_OnlyKillsThatWorker()
  {
    var done = new ConcurrentBag<int>();

    var schedule = await CreateScheduler()
      .RunAsync(
        new[] { 1, 2, 3, 4 },
        2,
        (workerIndex, _) => Task.FromResult<string?>(workerIndex == 2 ? "沙箱建不起来" : null),
        (_, item, _) =>
        {
          done.Add(item);
          return Task.CompletedTask;
        }
      );

    schedule.WorkersStarted.ShouldBe(1);
    done.OrderBy(item => item).ShouldBe(new[] { 1, 2, 3, 4 }); // 剩下的活由存活 worker 做完
    schedule.WorkerErrors.Count.ShouldBe(1);
    schedule.WorkerErrors[0].ShouldContain("worker 2");
    schedule.WorkerErrors[0].ShouldContain("沙箱建不起来");
  }

  [Test]
  public async Task RunAsync_ItemThrows_RecordsAndKeepsGoing()
  {
    var done = new ConcurrentBag<int>();

    var schedule = await CreateScheduler()
      .RunAsync(
        new[] { 1, 2, 3 },
        1,
        (_, _) => Task.FromResult<string?>(null),
        (_, item, _) =>
        {
          done.Add(item);
          return item == 2
            ? Task.FromException(new InvalidOperationException("炸了"))
            : Task.CompletedTask;
        }
      );

    done.OrderBy(item => item).ShouldBe(new[] { 1, 2, 3 });
    schedule.WorkerErrors.Count.ShouldBe(1);
    schedule.WorkerErrors[0].ShouldContain("炸了");
  }

  [Test]
  public async Task RunAsync_PreCancelled_ClaimsNothing()
  {
    var done = new List<int>();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var schedule = await CreateScheduler()
      .RunAsync(
        new[] { 1, 2, 3 },
        2,
        (_, _) => Task.FromResult<string?>(null),
        (_, item, _) =>
        {
          lock (done)
          {
            done.Add(item);
          }
          return Task.CompletedTask;
        },
        cts.Token
      );

    done.ShouldBeEmpty();
    schedule.WorkersStarted.ShouldBe(0);
  }
}
