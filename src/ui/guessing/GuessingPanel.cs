namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
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
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 猜测板块脚本 - 协调符卡表、别名表、猜测处理和丢包子节点
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class GuessingPanel : Control
{
  #region AutoConnect Nodes

  [Node]
  public TextEdit GuessInput { get; set; } = default!;

  [Node]
  public Button FuzzifyBtn { get; set; } = default!;

  [Node]
  public Button ProcessBtn { get; set; } = default!;

  [Node]
  public RichTextLabel ResponseDisplay { get; set; } = default!;

  #endregion

  #region Dropped UI Nodes

  [Node]
  public ItemList DroppedList { get; set; } = default!;

  [Node]
  public Button RetryDroppedBtn { get; set; } = default!;

  [Node]
  public Button ClearDroppedBtn { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager =>
    this.DependOn<DataManager>(() =>
    {
      // 尝试多个路径确保 fallback 不会抛出
      string[] dirs = { Path.Combine(Path.GetTempPath(), "AutoCMEX_Fallback"), Path.GetTempPath() };
      foreach (var dir in dirs)
      {
        try
        {
          Directory.CreateDirectory(dir);
          return new DataManager(dir, new AesEncryptor(AesEncryptor.GetDefaultKeyPath(dir)));
        }
        catch (Exception ex)
        {
          GD.PrintErr($"[GuessingPanel] Fallback attempt {dir}: {ex.Message}");
        }
      }
      // 最终兜底：使用内存中的临时路径
      var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tmpDir);
      return new DataManager(tmpDir, new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir)));
    });

  [Dependency]
  public AiServiceFactory AiServiceFactory =>
    this.DependOn<AiServiceFactory>(() => new AiServiceFactory(DataManager));

  [Dependency]
  public IGuessProcessingService GuessProcessingService =>
    this.DependOn<IGuessProcessingService>(() =>
      new GuessProcessingService(
        DataManager,
        AiServiceFactory,
        new GuessResponseHandler(),
        new DroppedGuessRepository()
      )
    );

  #endregion

  private DataManager? _dm;
  private IGuessProcessingService? _guessProcessingService;
  private ILog _log = AppLogs.GetOrCreate().GetLogger(nameof(GuessingPanel));

  public override void _Notification(int what)
  {
    if (what == NotificationVisibilityChanged && Visible)
    {
      UpdateFuzzifyButtonState();
      RefreshDroppedUI();
    }
    this.Notify(what);
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
    try
    {
      _dm = DataManager;
    }
    catch (Exception ex)
    {
      GD.PrintErr($"[GuessingPanel] Resolve DataManager: {ex.Message}");
      _dm = null;
    }
    try
    {
      _guessProcessingService = GuessProcessingService;
    }
    catch
    {
      _guessProcessingService = null;
    }

    if (_dm != null)
    {
      _dm.LoadAll();
      _dm.DataChanged += OnDataChanged;
      UpdateFuzzifyButtonState();
    }

    RefreshDroppedUI();
  }

  private void OnDataChanged()
  {
    CallDeferred(nameof(UpdateFuzzifyButtonState));
  }

  /// <summary>
  /// 注入测试数据管理器并刷新 UI。仅供测试使用。
  /// </summary>
  public void InjectTestData(DataManager dm)
  {
    _dm = dm;
    UpdateFuzzifyButtonState();
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
      var activeId = _dm.Settings.ActiveAiModelId;
      if (!string.IsNullOrEmpty(activeId))
      {
        var activeModel = _dm.Settings.AiModels.Find(m => m.Id == activeId);
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
    var service =
      _guessProcessingService
      ?? (
        _dm != null
          ? new GuessProcessingService(
            _dm,
            AiServiceFactory,
            new GuessResponseHandler(),
            new DroppedGuessRepository()
          )
          : GuessProcessingService
      );

    var result = await service.ProcessAsync(text);
    RefreshDroppedUI();
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
      $"OnFuzzify: start, model={mc.ModelId}, format={mc.ApiFormat}, input_len={text.Length}"
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
      RefreshDroppedUI();
    }
  }

  // ==================== 丢包重试 ====================

  private void RefreshDroppedUI()
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

    RetryDroppedBtn.Disabled = false;
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

    await Task.WhenAll(tasks);

    _log.Print($"OnRetryAllDropped: done, success={successCount}, fail={failCount}");
    RetryDroppedBtn.Text = "重试全部丢包";
    RefreshDroppedUI();
  }

  private void OnClearDropped()
  {
    var service = _guessProcessingService;
    if (service == null)
      return;

    var dropped = service.GetDroppedGuesses();
    foreach (var d in dropped)
      service.RemoveDroppedGuess(d.Id);

    _log.Print($"OnClearDropped: cleared {dropped.Count} dropped guesses.");
    RefreshDroppedUI();
  }
}
