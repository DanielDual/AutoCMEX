namespace AutoCMEX.Core.Guessing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 统一编排手动与托管猜测处理流程
/// </summary>
public class GuessProcessingService : IGuessProcessingService
{
  private readonly DataManager _dataManager;
  private readonly AiServiceFactory _aiServiceFactory;
  private readonly IGuessResponseHandler _responseHandler;
  private readonly IDroppedGuessRepository _droppedGuessRepository;
  private readonly ILog _log;

  public GuessProcessingService(
    DataManager dataManager,
    AiServiceFactory aiServiceFactory,
    IGuessResponseHandler responseHandler,
    IDroppedGuessRepository droppedGuessRepository
  )
    : this(
      dataManager,
      aiServiceFactory,
      responseHandler,
      droppedGuessRepository,
      AppLogs.GetOrCreate().GetLogger(nameof(GuessProcessingService))
    ) { }

  public GuessProcessingService(
    DataManager dataManager,
    AiServiceFactory aiServiceFactory,
    IGuessResponseHandler responseHandler,
    IDroppedGuessRepository droppedGuessRepository,
    ILog log
  )
  {
    _dataManager = dataManager;
    _aiServiceFactory = aiServiceFactory;
    _responseHandler = responseHandler;
    _droppedGuessRepository = droppedGuessRepository;
    _log = log;
  }

  /// <inheritdoc/>
  public Boss? ResolveCurrentBoss()
  {
    if (_dataManager.Bosses.Count == 0)
      return null;

    var selectedIndex = _dataManager.Settings.SelectedBossIndex.Value;
    if (selectedIndex < 0 || selectedIndex >= _dataManager.Bosses.Count)
    {
      selectedIndex = 0;
      _dataManager.Settings.SelectedBossIndex.Value = 0;
    }

    return _dataManager.Bosses[selectedIndex];
  }

  /// <inheritdoc/>
  public Task<GuessProcessingResult> ProcessAsync(string rawText) =>
    ProcessInternalAsync(
      rawText,
      ResolveCurrentBoss(),
      filterMode: _dataManager.Settings.MessageFilterMode.Value ?? "strict",
      treatFailureAsNotGuess: true,
      requestId: string.Empty,
      sender: string.Empty,
      recordDroppedOnFailure: true
    );

  /// <inheritdoc/>
  public Task<GuessProcessingResult> ProcessAsync(
    string rawText,
    string requestId,
    string sender
  ) =>
    ProcessInternalAsync(
      rawText,
      ResolveCurrentBoss(),
      filterMode: _dataManager.Settings.MessageFilterMode.Value ?? "strict",
      treatFailureAsNotGuess: true,
      requestId: requestId ?? string.Empty,
      sender: sender ?? string.Empty,
      recordDroppedOnFailure: true
    );

  /// <summary>
  /// 猜测处理主干：严格管道、AI 兜底与丢包落库都在这里
  /// </summary>
  /// <param name="rawText">原始猜测文本。</param>
  /// <param name="currentBoss">当前 Boss。</param>
  /// <param name="filterMode">消息筛选模式。</param>
  /// <param name="treatFailureAsNotGuess">失败时是否按「非猜测」返回。</param>
  /// <param name="requestId">来源请求标识，写进丢包记录供重试回帖用。</param>
  /// <param name="sender">来源发送者，写进丢包记录。</param>
  /// <param name="recordDroppedOnFailure">
  /// AI 兜底失败时是否新落一条丢包记录。重放时为 false：否则会删掉旧记录又换一个 Id 落新记录，
  /// 用户看到的「丢包被重新解析了一遍」正是这么来的。
  /// </param>
  private async Task<GuessProcessingResult> ProcessInternalAsync(
    string rawText,
    Boss? currentBoss,
    string filterMode,
    bool treatFailureAsNotGuess,
    string requestId,
    string sender,
    bool recordDroppedOnFailure
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
      var fuzzifier = new AiFuzzifier(_aiServiceFactory, _dataManager.Aliases, currentBoss, _log);
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

      if (recordDroppedOnFailure)
      {
        var dropped = new DroppedGuess(input, ex.Message, requestId, sender, filterMode);
        _droppedGuessRepository.Add(dropped);
        _log.Print(
          $"GuessProcessingService: added to dropped list (total={_droppedGuessRepository.GetAll().Count}), id={dropped.Id}"
        );
      }
      else
      {
        _log.Print(
          "GuessProcessingService: replay failed again; the existing dropped record is kept."
        );
      }

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

    _dataManager.TriggerAutoSave();
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

  /// <inheritdoc/>
  public AutoList<DroppedGuess> DroppedGuesses => _droppedGuessRepository.DroppedGuesses;

  /// <inheritdoc/>
  public IReadOnlyList<DroppedGuess> GetDroppedGuesses() => _droppedGuessRepository.GetAll();

  /// <inheritdoc/>
  public DroppedGuess? FindDroppedGuess(string droppedId) =>
    _droppedGuessRepository.FindById(droppedId);

  /// <inheritdoc/>
  public async Task<GuessProcessingResult> RetryDroppedGuessAsync(string droppedId)
  {
    var dropped = _droppedGuessRepository.FindById(droppedId);

    if (dropped == null)
      return GuessProcessingResult.Error($"丢包记录 {droppedId} 不存在。");

    _log.Print($"GuessProcessingService: retrying dropped guess {droppedId}: {dropped.RawText}");

    // 三处刻意与首次处理不同：按丢包当时的口径重放、不再新落记录、也不在这里删记录
    //（删记录由 DroppedGuessRetryService 在确认能回帖或确定无源可回之后统一裁决）。
    return await ProcessInternalAsync(
      dropped.RawText,
      ResolveCurrentBoss(),
      filterMode: string.IsNullOrEmpty(dropped.FilterMode)
        ? _dataManager.Settings.MessageFilterMode.Value ?? "strict"
        : dropped.FilterMode,
      treatFailureAsNotGuess: true,
      requestId: dropped.RequestId,
      sender: dropped.Sender,
      recordDroppedOnFailure: false
    );
  }

  /// <inheritdoc/>
  public void RemoveDroppedGuess(string droppedId)
  {
    _droppedGuessRepository.Remove(droppedId);
    _log.Print($"GuessProcessingService: removed dropped guess {droppedId}.");
  }

  /// <inheritdoc/>
  public void ClearDroppedGuesses()
  {
    _droppedGuessRepository.Clear();
    _log.Print("GuessProcessingService: cleared all dropped guesses.");
  }
}
