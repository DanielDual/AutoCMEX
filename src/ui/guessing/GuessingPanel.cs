namespace AutoCMEX.UI.Guessing;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 猜测板块脚本 - 协调符卡表、别名表、猜测处理和丢包子节点
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class GuessingPanel : Control
{
  #region AutoConnect Nodes

  [Node("%GuessInput")]
  public ITextEdit GuessInput { get; set; } = default!;

  [Node("%FuzzifyBtn")]
  public IButton FuzzifyBtn { get; set; } = default!;

  [Node("%ProcessBtn")]
  public IButton ProcessBtn { get; set; } = default!;

  [Node("%ResponseDisplay")]
  public IRichTextLabel ResponseDisplay { get; set; } = default!;

  #endregion

  #region Dropped UI Nodes

  [Node("%DroppedList")]
  public IItemList DroppedList { get; set; } = default!;

  [Node("%RetryDroppedBtn")]
  public IButton RetryDroppedBtn { get; set; } = default!;

  [Node("%ClearDroppedBtn")]
  public IButton ClearDroppedBtn { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  [Dependency]
  public AiServiceFactory AiServiceFactory => this.DependOn<AiServiceFactory>();

  [Dependency]
  public IGuessProcessingService GuessProcessingService => this.DependOn<IGuessProcessingService>();

  #endregion

  private DataManager? _dm;
  private IGuessProcessingService? _guessProcessingService;
  private AutoValue<string?>.Binding? _activeAiModelIdBinding;
  private AutoList<AiModelConfig>.Binding? _aiModelsBinding;
  private AutoList<DroppedGuess>.Binding? _droppedGuessesBinding;
  private bool _isRetrying;
  private ILog _log = AppLogs.GetOrCreate().GetLogger(nameof(GuessingPanel));

  /// <summary>
  /// 测试用：获取 OnClearDropped 委托
  /// </summary>
  public Action GetOnClearDropped() => OnClearDropped;

  /// <summary>
  /// 测试用：获取 OnRetryAllDropped 委托
  /// </summary>
  public Action GetOnRetryAllDropped() => OnRetryAllDropped;

  public override void _Notification(int what) => this.Notify(what);

  public override void _ExitTree()
  {
    _activeAiModelIdBinding?.Dispose();
    _aiModelsBinding?.Dispose();
    _droppedGuessesBinding?.Dispose();
  }

  public void OnReady()
  {
    ProcessBtn.Pressed += OnProcessGuess;
    FuzzifyBtn.Pressed += OnFuzzify;

    // 丢包重试 UI 信号连接
    RetryDroppedBtn.Pressed += OnRetryAllDropped;
    ClearDroppedBtn.Pressed += OnClearDropped;
  }

  public void OnResolved()
  {
    _dm = DataManager;
    _guessProcessingService = GuessProcessingService;

    if (_dm != null)
    {
      // 数据加载统一由 MainWindow/DataManager 负责，面板不重复 LoadAll
      // UI 刷新由 Sync 绑定驱动，无需手动调用
      _activeAiModelIdBinding = _dm
        .Settings.ActiveAiModelId.Bind()
        .OnValue(_ => UpdateFuzzifyButtonState());
      _aiModelsBinding = _dm.Settings.AiModels.Bind().OnModify(() => UpdateFuzzifyButtonState());
      UpdateFuzzifyButtonState();
    }

    if (_guessProcessingService != null)
    {
      _droppedGuessesBinding = _guessProcessingService
        .DroppedGuesses.Bind()
        .OnModify(() => CallDeferred(nameof(RefreshDroppedUI)));
      RefreshDroppedUI();
    }
  }

  private void UpdateFuzzifyButtonState()
  {
    // Node references may not be resolved yet if called from _Notification
    // before AutoInject has run.
    if (FuzzifyBtn == null)
      return;

    var hasAi = false;
    if (_dm != null)
    {
      var activeId = _dm.Settings.ActiveAiModelId.Value;
      if (!string.IsNullOrEmpty(activeId))
      {
        var activeModel = _dm.Settings.AiModels.FirstOrDefault(m => m.Id.Value == activeId);
        hasAi = activeModel != null && AiServiceFactory.IsModelValid(activeModel);
      }
    }
    FuzzifyBtn.Disabled = !hasAi;
    FuzzifyBtn.TooltipText = hasAi ? "使用 AI 模糊化" : "请先在设置中选择一个有效的 AI 模型";
  }

  // ==================== 猜测处理 ====================

  private async void OnProcessGuess()
  {
    var currentBoss = _guessProcessingService?.ResolveCurrentBoss();
    if (currentBoss == null)
    {
      _log.Warn("OnProcessGuess: no current boss selected.");
      ResponseDisplay.Text = "[color=red]请先选择 Boss[/color]";
      return;
    }
    var text = GuessInput.Text.Trim();
    if (string.IsNullOrEmpty(text))
    {
      _log.Warn("OnProcessGuess: empty input.");
      ResponseDisplay.Text = "[color=red]请输入猜测文本[/color]";
      return;
    }
    _log.Print(
      $"OnProcessGuess: processing guess (len={text.Length}) for boss '{currentBoss.Name}'."
    );

    var result = await _guessProcessingService!.ProcessAsync(text);
    if (!result.IsGuess)
    {
      _log.Warn($"OnProcessGuess: processing returned failure: {result.FailureReason}");
      ResponseDisplay.Text = $"[color=red]{result.FailureReason}[/color]";
      return;
    }
    var display = "";
    if (!string.IsNullOrEmpty(result.ReplyText))
      display += $"[b]{result.ReplyText}[/b]\n\n";
    foreach (var d in result.Details)
      display += $"{d}\n";
    ResponseDisplay.Text = display;
  }

  private async void OnFuzzify()
  {
    var currentBoss = _guessProcessingService?.ResolveCurrentBoss();
    if (currentBoss == null)
    {
      _log.Warn("OnFuzzify: no current boss selected.");
      ResponseDisplay.Text = "[color=red]请先选择 Boss[/color]";
      return;
    }
    var text = GuessInput.Text.Trim();
    if (string.IsNullOrEmpty(text))
    {
      _log.Warn("OnFuzzify: empty input.");
      ResponseDisplay.Text = "[color=red]请输入猜测文本[/color]";
      return;
    }
    if (_dm == null)
    {
      _log.Warn("OnFuzzify: DataManager not available.");
      ResponseDisplay.Text = "[color=yellow]数据管理器未就绪[/color]";
      return;
    }

    AiModelConfig mc;
    try
    {
      mc = AiServiceFactory.GetActiveModelConfig();
    }
    catch (InvalidOperationException ex)
    {
      _log.Warn($"OnFuzzify: no valid active model - {ex.Message}");
      ResponseDisplay.Text = $"[color=yellow]{ex.Message}[/color]";
      return;
    }

    _log.Print(
      $"OnFuzzify: start, model={mc.ModelId.Value}, format={mc.ApiFormat.Value}, input_len={text.Length}"
    );
    FuzzifyBtn.Disabled = true;
    FuzzifyBtn.Text = "模糊化中...";
    ResponseDisplay.Text = "[color=gray]正在调用 AI...[/color]";
    try
    {
      var fuzzifier = new AiFuzzifier(AiServiceFactory, _dm.Aliases, currentBoss);
      var result = await fuzzifier.FuzzifyAsync(text);
      if (AiFuzzifier.IsNotAGuessResult(result))
      {
        _log.Print("OnFuzzify: AI judged current input as not a guess.");
        ResponseDisplay.Text = "[color=yellow]AI 判定该输入不像猜测文本[/color]";
        return;
      }
      _log.Print($"OnFuzzify: succeeded, output_len={result?.Length ?? 0}");
      GuessInput.Text = result;
      ResponseDisplay.Text = $"[color=green]完成[/color]\n\n{result}";
    }
    catch (Exception ex)
    {
      _log.Err($"OnFuzzify failed: {ex.GetType().Name}: {ex.Message}");
      ResponseDisplay.Text = $"[color=red]失败: {ex.Message}[/color]";
    }
    finally
    {
      FuzzifyBtn.Disabled = false;
      FuzzifyBtn.Text = "模糊化";
    }
  }

  // ==================== 丢包重试 ====================

  public void RefreshDroppedUI()
  {
    // Node references may not be resolved yet if called from _Notification
    // before AutoInject has run.
    if (DroppedList == null || RetryDroppedBtn == null || ClearDroppedBtn == null)
      return;

    var service = _guessProcessingService;
    if (service == null)
      return;

    DroppedList.Clear();
    var dropped = service.GetDroppedGuesses();
    if (dropped.Count == 0)
    {
      RetryDroppedBtn.Disabled = true;
      ClearDroppedBtn.Disabled = true;
      return;
    }

    foreach (var d in dropped)
    {
      DroppedList.AddItem($"[{d.Id}] {d.RawText}");
    }

    RetryDroppedBtn.Disabled = _isRetrying;
    ClearDroppedBtn.Disabled = false;
  }

  private async void OnRetryAllDropped()
  {
    var service = _guessProcessingService;
    if (service == null)
      return;

    var dropped = service.GetDroppedGuesses();
    if (dropped.Count == 0)
      return;

    _log.Print($"OnRetryAllDropped: retrying {dropped.Count} dropped guesses.");
    _isRetrying = true;
    RetryDroppedBtn.Disabled = true;
    RetryDroppedBtn.Text = "重试中...";

    var successCount = 0;
    var failCount = 0;

    // 并行重试所有丢包
    var tasks = dropped.Select(async d =>
    {
      var result = await service.RetryDroppedGuessAsync(d.Id);
      if (result.IsGuess)
        Interlocked.Increment(ref successCount);
      else
        Interlocked.Increment(ref failCount);
    });

    try
    {
      await Task.WhenAll(tasks);
    }
    finally
    {
      _isRetrying = false;
      RetryDroppedBtn.Text = "重试全部丢包";
      RefreshDroppedUI();
    }

    _log.Print($"OnRetryAllDropped: done, success={successCount}, fail={failCount}");
  }

  private void OnClearDropped()
  {
    var service = _guessProcessingService;
    if (service == null)
      return;

    var count = service.GetDroppedGuesses().Count;
    service.ClearDroppedGuesses();
    _log.Print($"OnClearDropped: cleared {count} dropped guesses.");
  }
}
