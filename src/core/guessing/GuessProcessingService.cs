namespace AutoCMEX.Core.Guessing;

using System;
using System.Threading.Tasks;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// 统一编排手动与托管猜测处理流程
/// </summary>
public class GuessProcessingService : IGuessProcessingService
{
  private readonly DataManager _dataManager;
  private readonly AiServiceFactory _aiServiceFactory;
  private readonly IGuessResponseHandler _responseHandler;
  private readonly ILog _log;

  public GuessProcessingService(
    DataManager dataManager,
    AiServiceFactory aiServiceFactory,
    IGuessResponseHandler responseHandler
  )
    : this(
      dataManager,
      aiServiceFactory,
      responseHandler,
      AppLogs.GetOrCreate().GetLogger(nameof(GuessProcessingService))
    ) { }

  public GuessProcessingService(
    DataManager dataManager,
    AiServiceFactory aiServiceFactory,
    IGuessResponseHandler responseHandler,
    ILog log
  )
  {
    _dataManager = dataManager;
    _aiServiceFactory = aiServiceFactory;
    _responseHandler = responseHandler;
    _log = log;
  }

  /// <inheritdoc/>
  public Boss? ResolveCurrentBoss()
  {
    if (_dataManager.Bosses.Count == 0)
      return null;

    var selectedIndex = _dataManager.Settings.SelectedBossIndex;
    if (selectedIndex < 0 || selectedIndex >= _dataManager.Bosses.Count)
    {
      selectedIndex = 0;
      _dataManager.Settings.SelectedBossIndex = 0;
    }

    return _dataManager.Bosses[selectedIndex];
  }

  /// <inheritdoc/>
  public Task<GuessProcessingResult> ProcessManualAsync(string rawText, Boss? currentBoss) =>
    ProcessAsync(rawText, currentBoss, filterMode: "strict", treatFailureAsNotGuess: false);

  /// <inheritdoc/>
  public Task<GuessProcessingResult> ProcessManagedAsync(string rawText)
  {
    var currentBoss = ResolveCurrentBoss();
    return ProcessAsync(
      rawText,
      currentBoss,
      filterMode: _dataManager.Settings.MessageFilterMode ?? "strict",
      treatFailureAsNotGuess: true
    );
  }

  private async Task<GuessProcessingResult> ProcessAsync(
    string rawText,
    Boss? currentBoss,
    string filterMode,
    bool treatFailureAsNotGuess
  )
  {
    var input = rawText?.Trim() ?? string.Empty;
    if (string.IsNullOrEmpty(input))
    {
      return treatFailureAsNotGuess
        ? GuessProcessingResult.NotGuess("输入为空。")
        : GuessProcessingResult.Error("请输入猜测文本");
    }

    if (currentBoss == null)
    {
      return treatFailureAsNotGuess
        ? GuessProcessingResult.Error("当前未选择 Boss")
        : GuessProcessingResult.Error("当前未选择 Boss");
    }

    var skipStrict = string.Equals(filterMode, "ai", StringComparison.OrdinalIgnoreCase);
    var enableAi = !string.Equals(filterMode, "strict", StringComparison.OrdinalIgnoreCase);

    if (!skipStrict)
    {
      var directResult = RunPipeline(input, currentBoss);
      if (directResult.IsGuess)
        return directResult;

      if (!enableAi)
        return directResult;
    }

    if (!HasAvailableAiModel(out var aiError))
      return GuessProcessingResult.NotGuess(aiError);

    try
    {
      var fuzzifier = new AiFuzzifier(
        _aiServiceFactory,
        _dataManager.Aliases,
        _dataManager.Bosses,
        currentBoss,
        _log
      );
      var fuzzified = await fuzzifier.FuzzifyAsync(input);

      if (AiFuzzifier.IsNotAGuessResult(fuzzified) || string.IsNullOrWhiteSpace(fuzzified))
      {
        return GuessProcessingResult.NotGuess("AI 判定该输入不像猜测文本。");
      }

      var fuzzifiedResult = RunPipeline(fuzzified, currentBoss);
      if (fuzzifiedResult.IsGuess)
        return GuessProcessingResult.Success(
          fuzzified,
          fuzzifiedResult.ReplyText,
          fuzzifiedResult.Details
        );

      return GuessProcessingResult.NotGuess(
        string.IsNullOrEmpty(fuzzifiedResult.FailureReason)
          ? "AI 结果无法解析为有效猜测。"
          : fuzzifiedResult.FailureReason
      );
    }
    catch (Exception ex)
    {
      _log.Err(
        $"GuessProcessingService.ProcessAsync AI fallback failed: {ex.GetType().Name}: {ex.Message}"
      );
      return treatFailureAsNotGuess
        ? GuessProcessingResult.NotGuess(ex.Message)
        : GuessProcessingResult.Error(ex.Message);
    }
  }

  private GuessProcessingResult RunPipeline(string text, Boss currentBoss)
  {
    var pipeline = new GuessPipeline(_responseHandler, _dataManager.Aliases, _log);
    var pipelineResult = pipeline.Process(text, currentBoss);
    if (!pipelineResult.IsSuccess)
      return GuessProcessingResult.Error(pipelineResult.ErrorMessage);

    return GuessProcessingResult.Success(text, pipelineResult.Response, pipelineResult.Details);
  }

  private bool HasAvailableAiModel(out string error)
  {
    try
    {
      _ = _aiServiceFactory.GetActiveModelConfig();
      error = string.Empty;
      return true;
    }
    catch (InvalidOperationException ex)
    {
      error = ex.Message;
      return false;
    }
  }
}
