namespace AutoCMEX.Core.Guessing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.WebSocket;
using Chickensoft.Log;

/// <summary>
/// 丢包重试协调器：唯一负责「重放 → 回帖 → 决定记录去留」的执行者
/// </summary>
public interface IDroppedGuessRetryService
{
  /// <summary>
  /// 重试单条丢包记录
  /// </summary>
  /// <param name="droppedId">丢包记录 Id。</param>
  Task<DroppedRetryOutcome> RetryAsync(string droppedId);

  /// <summary>
  /// 重试当前全部丢包记录（按调用时的列表快照并发）
  /// </summary>
  Task<IReadOnlyList<DroppedRetryOutcome>> RetryAllAsync();
}

/// <summary>
/// 丢包重试协调器的默认实现
/// </summary>
/// <remarks>
/// <para>
/// 重试链路的终点必须是群聊而不是 UI：重放算出结果后，这里用 <see cref="GuessReplyFactory"/>
/// 生成与「消息到达」完全相同的 <c>guess_result</c> 事件，经活跃的 WebSocket 端点推送回原消息。
/// </para>
/// <para>
/// 记录的删除也收在这里，且判据是「**真的送出去了**」：<see cref="IWebSocketServer.BroadcastAsync"/>
/// 对单个连接失败只记日志、不抛异常，所以必须看它的送达数；只有送达（<see cref="DroppedRetryStatus.Replied"/>）、
/// 确定无源可回（<see cref="DroppedRetryStatus.NoReplyTarget"/>）或按回应策略本就不需要回帖
/// （<see cref="DroppedRetryStatus.NoReplyNeeded"/>）才移除，其余一律保留，避免用户白丢一条记录。
/// </para>
/// </remarks>
public class DroppedGuessRetryService : IDroppedGuessRetryService
{
  private readonly IGuessProcessingService _guessProcessingService;
  private readonly WebSocketLifecycle _lifecycle;
  private readonly ILog _log;

  /// <summary>
  /// 创建重试协调器
  /// </summary>
  /// <param name="guessProcessingService">猜测处理服务（提供重放与丢包记录读写）。</param>
  /// <param name="lifecycle">WebSocket 生命周期（取当前活跃端点推送回复）。</param>
  public DroppedGuessRetryService(
    IGuessProcessingService guessProcessingService,
    WebSocketLifecycle lifecycle
  )
    : this(
      guessProcessingService,
      lifecycle,
      AppLogs.GetOrCreate().GetLogger(nameof(DroppedGuessRetryService))
    ) { }

  /// <summary>
  /// 创建重试协调器
  /// </summary>
  /// <param name="guessProcessingService">猜测处理服务。</param>
  /// <param name="lifecycle">WebSocket 生命周期。</param>
  /// <param name="log">日志。</param>
  public DroppedGuessRetryService(
    IGuessProcessingService guessProcessingService,
    WebSocketLifecycle lifecycle,
    ILog log
  )
  {
    _guessProcessingService = guessProcessingService;
    _lifecycle = lifecycle;
    _log = log;
  }

  /// <inheritdoc/>
  public async Task<DroppedRetryOutcome> RetryAsync(string droppedId)
  {
    var dropped = _guessProcessingService.FindDroppedGuess(droppedId);
    if (dropped is null)
      return new DroppedRetryOutcome(
        droppedId,
        string.Empty,
        DroppedRetryStatus.Failed,
        $"丢包记录 {droppedId} 不存在。",
        removed: false
      );

    var result = await _guessProcessingService.RetryDroppedGuessAsync(droppedId);

    if (result.Status == GuessProcessingStatus.Success)
      return await DeliverAsync(dropped, result);

    var reason = string.IsNullOrEmpty(result.FailureReason)
      ? "无有效猜测结果。"
      : result.FailureReason;
    return result.Status == GuessProcessingStatus.NotGuess
      ? new DroppedRetryOutcome(
        dropped,
        DroppedRetryStatus.NotGuess,
        $"重放未得到有效猜测：{reason}",
        false
      )
      : new DroppedRetryOutcome(dropped, DroppedRetryStatus.Failed, $"重放失败：{reason}", false);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<DroppedRetryOutcome>> RetryAllAsync()
  {
    var pending = _guessProcessingService.GetDroppedGuesses().ToList();
    if (pending.Count == 0)
      return Array.Empty<DroppedRetryOutcome>();

    return await Task.WhenAll(pending.Select(dropped => RetryAsync(dropped.Id)));
  }

  /// <summary>
  /// 把重放结果回帖到原消息，并据此决定记录去留
  /// </summary>
  /// <param name="dropped">丢包记录（含来源请求标识）。</param>
  /// <param name="result">重放结果。</param>
  private async Task<DroppedRetryOutcome> DeliverAsync(
    DroppedGuess dropped,
    GuessProcessingResult result
  )
  {
    // 判据是「记录里有没有来源」而不是「事件是不是 null」：后者在「有来源但按回应策略无需回帖」时
    // 同样为 null，混在一起会把「本来就不需要回帖」误报成「无源可回」。
    if (string.IsNullOrEmpty(dropped.RequestId))
    {
      _guessProcessingService.RemoveDroppedGuess(dropped.Id);
      _log.Print(
        $"DroppedGuessRetryService: dropped guess {dropped.Id} has no reply target; removed after replay."
      );
      return new DroppedRetryOutcome(
        dropped,
        DroppedRetryStatus.NoReplyTarget,
        "无来源可回（本地手输产生的丢包），结果已算出，记录已移除。",
        removed: true
      );
    }

    var replyEvent = GuessReplyFactory.CreateResultEvent(dropped.RequestId, result);
    if (replyEvent is null)
    {
      // 有来源、但重放结果按回应策略不需要回帖（例如只猜中一张符卡）：这件事已经处理完，不必再留着重试
      _guessProcessingService.RemoveDroppedGuess(dropped.Id);
      _log.Print(
        $"DroppedGuessRetryService: dropped guess {dropped.Id} replayed without a reply per the response policy; removed."
      );
      return new DroppedRetryOutcome(
        dropped,
        DroppedRetryStatus.NoReplyNeeded,
        "按回应策略无需回帖，结果已算出，记录已移除。",
        removed: true
      );
    }

    var server = _lifecycle.Current;
    if (server is null || !server.IsRunning || server.ConnectionCount == 0)
    {
      _log.Print(
        $"DroppedGuessRetryService: dropped guess {dropped.Id} replayed but the link is inactive; kept."
      );
      return new DroppedRetryOutcome(
        dropped,
        DroppedRetryStatus.LinkInactive,
        "链路未运行或没有已连接的对端，结果未送达，记录保留。",
        removed: false
      );
    }

    int delivered;
    try
    {
      // 送达数才是判据：BroadcastAsync 内部对单个连接失败只记日志，不会抛出来
      delivered = await server.BroadcastAsync(replyEvent);
    }
    catch (Exception ex)
    {
      _log.Err(
        $"DroppedGuessRetryService: broadcast of dropped guess {dropped.Id} failed: {ex.GetType().Name}: {ex.Message}"
      );
      return new DroppedRetryOutcome(
        dropped,
        DroppedRetryStatus.Failed,
        $"回复推送失败：{ex.Message}",
        removed: false
      );
    }

    if (delivered == 0)
    {
      _log.Err(
        $"DroppedGuessRetryService: dropped guess {dropped.Id} replayed but nothing was delivered; kept."
      );
      return new DroppedRetryOutcome(
        dropped,
        DroppedRetryStatus.LinkInactive,
        "结果没能送达任何对端（链路刚好断开或对端不可达），记录保留。",
        removed: false
      );
    }

    _guessProcessingService.RemoveDroppedGuess(dropped.Id);
    _log.Print(
      $"DroppedGuessRetryService: dropped guess {dropped.Id} replayed and replied (requestId={dropped.RequestId})."
    );
    return new DroppedRetryOutcome(
      dropped,
      DroppedRetryStatus.Replied,
      "已引用原消息回帖，记录已移除。",
      removed: true
    );
  }
}
