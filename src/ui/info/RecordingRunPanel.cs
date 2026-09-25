namespace AutoCMEX.UI.Info;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Models;
using Godot;

/// <summary>
/// 跑一轮录制的委托。
/// </summary>
/// <param name="request">本轮输入（引擎、工程包、输出目录、配置）。</param>
/// <param name="progress">进度回调（可为 <c>null</c>）。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>本轮结果。</returns>
public delegate Task<RecordingRunResult> RecordingRunner(
  RecordingRequest request,
  IProgress<RecordingProgress>? progress,
  CancellationToken cancellationToken
);

/// <summary>
/// 录制过程对话框：阶段、完成计数、各 worker 正在录的卡、取消按钮与结束汇总。
/// </summary>
/// <remarks>
/// <para>
/// 只负责展示与生命周期，不碰服务层：录成之后的落库（导入为 GIF 集并选中）由调用方在
/// <see cref="Finished"/> 里完成。这样单测能直接替换 <see cref="RecordingRunner"/> 驱动本对话框，
/// 不必真启游戏进程。
/// </para>
/// <para>
/// 关闭窗口在运行中意为「取消录制」（产物与报告照常保留，报告标 <c>cancelled</c>），
/// 因为窗口一关就再没有别的地方能中止本轮。
/// </para>
/// </remarks>
public sealed partial class RecordingRunPanel : AcceptDialog
{
  /// <summary>取消按钮的动作名（<see cref="AcceptDialog.CustomAction"/> 的参数）。</summary>
  public const string CancelAction = "cancel_recording";

  private readonly RecordingRunner _runner;
  private CancellationTokenSource? _cts;
  private Button? _cancelButton;
  private RecordingProgress? _pendingProgress;
  private RecordingRunResult? _pendingResult;
  private bool _renderQueued;

  /// <summary>构建对话框（内容全用 C# 搭建，不改任何场景文件）。</summary>
  /// <param name="runner">跑一轮录制的委托（默认实现见调用方，单测可替换）。</param>
  /// <exception cref="ArgumentNullException"><paramref name="runner"/> 为 <c>null</c>。</exception>
  public RecordingRunPanel(RecordingRunner runner)
  {
    _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    BuildUi();
  }

  /// <summary>一轮录制结束（成功、取消或失败都触发）。</summary>
  public event Action<RecordingRunResult>? Finished;

  /// <summary>本轮是否还在跑。</summary>
  public bool IsRunning { get; private set; }

  /// <summary>阶段与输出目录。</summary>
  public Label StageLabel { get; private set; } = null!;

  /// <summary>完成计数。</summary>
  public Label CountLabel { get; private set; } = null!;

  /// <summary>各 worker 正在录的卡。</summary>
  public Label WorkersLabel { get; private set; } = null!;

  /// <summary>结束汇总（由调用方在导入之后回填）。</summary>
  public Label SummaryLabel { get; private set; } = null!;

  /// <summary>取消按钮（运行中可点，结束后隐藏）。</summary>
  public Button CancelButton => _cancelButton!;

  /// <summary>
  /// 起录一轮：弹出本对话框并开始上报进度。
  /// </summary>
  /// <param name="request">本轮输入。</param>
  /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 <c>null</c>。</exception>
  public void Start(RecordingRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (IsRunning)
    {
      return;
    }

    IsRunning = true;
    _cts = new CancellationTokenSource();
    StageLabel.Text = $"阶段：准备中　输出目录：{request.OutputDir}";
    CountLabel.Text = "总数 —　已完成 —　失败 —";
    WorkersLabel.Text = "正在录制：（等待引擎启动）";
    SummaryLabel.Text = string.Empty;
    GetOkButton().Disabled = true;
    CancelButton.Disabled = false;
    CancelButton.Visible = true;

    PopupCentered(new Vector2I(760, 460));

    // 进度可能从后台线程上报：只记快照并排一次延时刷新，回主线程再碰控件
    _ = RunAsync(request, new Progress<RecordingProgress>(OnProgress), _cts.Token);
  }

  /// <summary>请求取消本轮（已结束则忽略）。</summary>
  public void Cancel()
  {
    if (!IsRunning)
    {
      return;
    }

    CancelButton.Disabled = true;
    StageLabel.Text = "阶段：正在取消（等待当前卡的进程结束）…";
    _cts?.Cancel();
  }

  /// <summary>回填结束汇总（由调用方在自动导入之后调用）。</summary>
  /// <param name="summary">汇总文本。</param>
  public void AppendSummary(string summary) => SummaryLabel.Text = summary;

  /// <summary>进度到达：只记下最新快照、排一次延时刷新（多 worker 并发时不会把控件刷爆）。</summary>
  /// <param name="progress">进度快照。</param>
  private void OnProgress(RecordingProgress progress)
  {
    _pendingProgress = progress;
    if (_renderQueued)
    {
      return;
    }

    _renderQueued = true;
    CallDeferred(nameof(RenderPending));
  }

  /// <summary>把最新进度快照刷到控件上。</summary>
  /// <remarks>
  /// 进度可能从后台线程上报，故一律经 <c>CallDeferred</c> 回主线程再触碰控件；
  /// 公开以便按名调用，不属于对外契约的一部分。
  /// </remarks>
  public void RenderPending()
  {
    _renderQueued = false;

    if (_pendingProgress is not null)
    {
      Render(_pendingProgress);
    }
  }

  /// <summary>按进度快照刷新三个标签。</summary>
  /// <param name="progress">进度快照。</param>
  public void Render(RecordingProgress progress)
  {
    ArgumentNullException.ThrowIfNull(progress);

    StageLabel.Text = $"阶段：{progress.Stage}";
    CountLabel.Text = $"总数 {progress.Total}　已完成 {progress.Completed}　失败 {progress.Failed}";
    WorkersLabel.Text = RenderWorkers(progress.Running);
  }

  /// <summary>跑一轮并收尾（异常兜底成结果，不让异常冒进 Godot 主循环）。</summary>
  /// <param name="request">本轮输入。</param>
  /// <param name="progress">进度回调。</param>
  /// <param name="cancellationToken">取消令牌。</param>
  /// <returns>无。</returns>
  private async Task RunAsync(
    RecordingRequest request,
    IProgress<RecordingProgress> progress,
    CancellationToken cancellationToken
  )
  {
    RecordingRunResult result;
    try
    {
      result = await _runner(request, progress, cancellationToken);
    }
    catch (Exception ex)
    {
      result = new RecordingRunResult(
        $"录制异常终止：{ex.Message}",
        new RecordingReport(),
        request.OutputDir
      );
    }

    IsRunning = false;
    _cts?.Dispose();
    _cts = null;
    _pendingResult = result;

    if (!GodotObject.IsInstanceValid(this))
    {
      return;
    }

    // 收尾同样可能落在后台线程，而 Finished 的订阅方会碰控件与数据层
    CallDeferred(nameof(FinishPending));
  }

  /// <summary>回主线程收尾：复位按钮并抛出 <see cref="Finished"/>。</summary>
  /// <remarks>公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约的一部分。</remarks>
  public void FinishPending()
  {
    if (!GodotObject.IsInstanceValid(this))
    {
      return;
    }

    var result = _pendingResult;
    _pendingResult = null;

    StageLabel.Text = "阶段：已结束";
    GetOkButton().Disabled = false;
    CancelButton.Visible = false;
    WorkersLabel.Text = "正在录制：—";

    if (result is not null)
    {
      Finished?.Invoke(result);
    }
  }

  /// <summary>把各 worker 的当前卡拼成多行文本。</summary>
  /// <param name="running">正在录的卡。</param>
  /// <returns>展示文本（无人在录时给出明确占位，避免看起来像卡住）。</returns>
  private static string RenderWorkers(IReadOnlyList<RecordingWorkerProgress> running)
  {
    if (running is null || running.Count == 0)
    {
      return "正在录制：（无）";
    }

    var lines = running
      .OrderBy(slot => slot.WorkerIndex)
      .Select(slot =>
        $"　worker {slot.WorkerIndex}：{slot.CombatOrdinal}. {slot.EntryName}"
        + (slot.Attempt > 1 ? $"（第 {slot.Attempt} 次尝试）" : string.Empty)
      );
    return "正在录制：\n" + string.Join("\n", lines);
  }

  /// <summary>搭建对话框内容（单个容器承载全部控件：AcceptDialog 只给内容区一个矩形）。</summary>
  private void BuildUi()
  {
    Title = "符卡 GIF 录制";
    OkButtonText = "关闭";
    Exclusive = true;
    Unresizable = false;
    CloseRequested += OnCloseRequested;

    var body = new VBoxContainer { CustomMinimumSize = new Vector2(680, 300) };
    body.AddThemeConstantOverride("separation", 8);

    StageLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    CountLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    WorkersLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    SummaryLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };

    body.AddChild(StageLabel);
    body.AddChild(CountLabel);
    body.AddChild(WorkersLabel);
    body.AddChild(new HSeparator());
    body.AddChild(SummaryLabel);
    AddChild(body);

    _cancelButton = AddButton("取消录制", true, CancelAction);
    // 未起录时没有任何可取消的东西，先藏起来（起录时再点亮）
    _cancelButton.Visible = false;
    _cancelButton.Disabled = true;
    CustomAction += OnCustomAction;
  }

  /// <summary>处理自定义按钮动作。</summary>
  /// <param name="action">动作名。</param>
  private void OnCustomAction(StringName action)
  {
    if (action == CancelAction)
    {
      Cancel();
    }
  }

  /// <summary>关闭请求：运行中转为取消，免得窗口一关就再没有中止入口。</summary>
  private void OnCloseRequested()
  {
    if (IsRunning)
    {
      Cancel();
    }
  }
}
