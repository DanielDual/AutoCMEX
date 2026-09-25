namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Models;
using AutoCMEX.UI.Info;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 录制过程对话框单测：录制委托被换成假实现，故不启任何真实进程。
/// 覆盖「起录即锁关闭/结束解锁」「进度经延时刷新落到控件」「取消与关闭请求都转为取消」
/// 「委托抛异常兜底成结果」四条链路。
/// </summary>
public class TestRecordingRunPanel : TestClass
{
  private Node? _host;

  public TestRecordingRunPanel(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    if (_host is not null && !_host.IsQueuedForDeletion())
    {
      _host.QueueFree();
    }

    _host = null;
  }

  [Test]
  public void Constructor_NullRunner_Throws() =>
    Should.Throw<ArgumentNullException>(() => new RecordingRunPanel(null!));

  [Test]
  public async Task Start_ShowsOutputDirAndLocksCloseUntilFinished()
  {
    var starts = 0;
    var gate = new TaskCompletionSource<RecordingRunResult>();
    var panel = Mount(
      (request, _, _) =>
      {
        starts++;
        return gate.Task;
      }
    );

    panel.Start(CreateRequest("C:/out/gifset"));
    await SettleAsync();

    panel.IsRunning.ShouldBeTrue();
    panel.StageLabel.Text.ShouldContain("C:/out/gifset");
    panel.CountLabel.Text.ShouldContain("总数 —");
    panel.SummaryLabel.Text.ShouldBeEmpty();
    // 运行期间不能靠「关闭」把对话框收掉，否则取消入口就没了
    panel.GetOkButton().Disabled.ShouldBeTrue();
    panel.CancelButton.Visible.ShouldBeTrue();
    panel.CancelButton.Disabled.ShouldBeFalse();

    // 已在跑时再起一轮应被忽略：同一引擎目录不能并发
    panel.Start(CreateRequest("C:/out/gifset"));
    panel.IsRunning.ShouldBeTrue();
    starts.ShouldBe(1);

    gate.SetResult(new RecordingRunResult(null, new RecordingReport(), "C:/out/gifset"));
    await WaitUntilAsync(() => !panel.IsRunning);
    await SettleAsync();

    panel.GetOkButton().Disabled.ShouldBeFalse();
    panel.CancelButton.Visible.ShouldBeFalse();
    panel.StageLabel.Text.ShouldContain("已结束");
  }

  [Test]
  public void Render_ShowsStageCountsAndWorkers()
  {
    var panel = Mount((_, _, _) => new TaskCompletionSource<RecordingRunResult>().Task);

    panel.Render(
      new RecordingProgress(
        "录制中",
        3,
        1,
        1,
        new List<RecordingWorkerProgress> { new(2, 3, "符卡·二", 2), new(1, 2, "符卡·一", 1) }
      )
    );

    panel.StageLabel.Text.ShouldBe("阶段：录制中");
    panel.CountLabel.Text.ShouldBe("总数 3　已完成 1　失败 1");
    // 按 worker 序号排序展示；首次尝试不写「第 N 次」（避免每行都挂噪声）
    panel.WorkersLabel.Text.ShouldContain("worker 1：2. 符卡·一");
    panel.WorkersLabel.Text.ShouldNotContain("worker 1：2. 符卡·一（第 1 次尝试）");
    panel.WorkersLabel.Text.ShouldContain("worker 2：3. 符卡·二（第 2 次尝试）");
    panel
      .WorkersLabel.Text.IndexOf("worker 1", StringComparison.Ordinal)
      .ShouldBeLessThan(panel.WorkersLabel.Text.IndexOf("worker 2", StringComparison.Ordinal));

    panel.Render(new RecordingProgress("归集", 3, 3, 0, Array.Empty<RecordingWorkerProgress>()));

    // 无人在录时必须显式写「（无）」，否则看起来像卡住
    panel.WorkersLabel.Text.ShouldBe("正在录制：（无）");
  }

  [Test]
  public async Task Progress_ReportedByRunner_EndsUpOnLabels()
  {
    var panel = Mount(
      (request, progress, _) =>
      {
        progress?.Report(
          new RecordingProgress(
            "录制中",
            2,
            1,
            0,
            new List<RecordingWorkerProgress> { new(1, 1, "普通攻击 1", 1) }
          )
        );

        return Task.FromResult(
          new RecordingRunResult(null, new RecordingReport(), request.OutputDir)
        );
      }
    );

    panel.Start(CreateRequest("C:/out/gifset"));

    await WaitUntilAsync(() => !panel.IsRunning);
    await SettleAsync();

    // 进度经 CallDeferred 回主线程刷新（若丢了这一环，标签会停在起录时的占位文案）
    panel.CountLabel.Text.ShouldBe("总数 2　已完成 1　失败 0");
    panel.WorkersLabel.Text.ShouldContain("worker 1：1. 普通攻击 1");
  }

  [Test]
  public async Task Cancel_SignalsTokenAndFinishesAsCancelled()
  {
    RecordingRunResult? finished = null;
    var panel = Mount(
      async (request, _, cancellationToken) =>
      {
        try
        {
          await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
          // 编排层的取消语义：进程被杀、已归集产物保留、报告标 cancelled
        }

        return new RecordingRunResult(
          null,
          new RecordingReport { Cancelled = true },
          request.OutputDir
        );
      }
    );
    panel.Finished += result => finished = result;

    panel.Start(CreateRequest("C:/out/gifset"));
    await SettleAsync();
    panel.Cancel();

    panel.CancelButton.Disabled.ShouldBeTrue();
    panel.StageLabel.Text.ShouldContain("正在取消");
    // 再来一次（例如连点）不得重复取消
    panel.Cancel();

    await WaitUntilAsync(() => finished is not null);
    finished!.Report.Cancelled.ShouldBeTrue();
    panel.IsRunning.ShouldBeFalse();
  }

  [Test]
  public async Task CloseRequested_WhileRunning_Cancels()
  {
    // 走真实的引擎信号：关闭请求在运行中必须转为取消，否则就没有中止入口了
    var requests = new List<RecordingRequest>();
    var panel = Mount(
      async (request, _, cancellationToken) =>
      {
        requests.Add(request);
        try
        {
          await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) { }

        return new RecordingRunResult(
          null,
          new RecordingReport { Cancelled = true },
          request.OutputDir
        );
      }
    );

    panel.Start(CreateRequest("C:/out/gifset"));
    await SettleAsync();

    panel.EmitSignal(Window.SignalName.CloseRequested);

    // 等收尾真正落到控件上：IsRunning 先翻假，延时收尾要再等一帧
    await WaitUntilAsync(() => panel.StageLabel.Text.Contains("已结束"));
    panel.IsRunning.ShouldBeFalse();
    requests.Count.ShouldBe(1);
  }

  [Test]
  public void Cancel_NotRunning_IsIgnored()
  {
    var panel = Mount((_, _, _) => new TaskCompletionSource<RecordingRunResult>().Task);

    panel.Cancel();

    panel.IsRunning.ShouldBeFalse();
    panel.CancelButton.Visible.ShouldBeFalse();
  }

  [Test]
  public async Task RunnerThrows_ReportsExceptionAndStillUnlocksDialog()
  {
    RecordingRunResult? finished = null;
    var panel = Mount((_, _, _) => throw new InvalidOperationException("引擎目录被占用"));
    panel.Finished += result => finished = result;

    panel.Start(CreateRequest("C:/out/gifset"));

    await WaitUntilAsync(() => finished is not null);
    await SettleAsync();

    // 异常不让它冒进 Godot 主循环，但要作为可读结果交给调用方
    finished!.Error.ShouldNotBeNull();
    finished.Error!.ShouldContain("引擎目录被占用");
    finished.Succeeded.ShouldBeFalse();
    panel.GetOkButton().Disabled.ShouldBeFalse();
    panel.CancelButton.Visible.ShouldBeFalse();
  }

  [Test]
  public async Task AppendSummary_ShowsTextAfterFinish()
  {
    RecordingRunResult? finished = null;
    var panel = Mount(
      (request, _, _) =>
        Task.FromResult(new RecordingRunResult(null, new RecordingReport(), request.OutputDir))
    );
    panel.Finished += result => finished = result;

    panel.Start(CreateRequest("C:/out/gifset"));
    await WaitUntilAsync(() => finished is not null);

    panel.AppendSummary("本轮录制：成功 3　失败 0");

    panel.SummaryLabel.Text.ShouldBe("本轮录制：成功 3　失败 0");
  }

  /// <summary>挂一个用假录制委托驱动的对话框（必须进场景树，延时刷新才有主循环可跑）。</summary>
  /// <param name="runner">假录制委托。</param>
  /// <returns>对话框。</returns>
  private RecordingRunPanel Mount(RecordingRunner runner)
  {
    _host = new Node();
    TestScene.AddChild(_host);

    var panel = new RecordingRunPanel(runner);
    _host.AddChild(panel);
    return panel;
  }

  /// <summary>造一轮输入（对话框本身不碰磁盘，路径只需可读）。</summary>
  /// <param name="outputDir">输出目录。</param>
  /// <returns>本轮输入。</returns>
  private static RecordingRequest CreateRequest(string outputDir) =>
    new(
      Path.Combine(Path.GetTempPath(), "engine"),
      Path.Combine(Path.GetTempPath(), "pack.zip"),
      outputDir,
      new RecordingConfig()
    );

  /// <summary>空转一帧。</summary>
  private async Task SettleAsync() =>
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

  /// <summary>逐帧等到条件成立（超时即断言失败，避免假死变成挂起）。</summary>
  /// <param name="condition">条件。</param>
  private async Task WaitUntilAsync(Func<bool> condition)
  {
    for (var frame = 0; frame < 3000 && !condition(); frame++)
    {
      await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    condition().ShouldBeTrue("等待超时：对话框未在预期帧数内结束");
  }
}
