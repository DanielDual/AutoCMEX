namespace AutoCMEX.Core.Guessing;

/// <summary>
/// 丢包重试的结局
/// </summary>
public enum DroppedRetryStatus
{
  /// <summary>已把回复推给群聊，记录随之移除。</summary>
  Replied,

  /// <summary>结果已算出，但记录没有来源请求标识（如本地手输产生的丢包），无人可回；记录已移除。</summary>
  NoReplyTarget,

  /// <summary>结果已算出，但按回应策略本来就不需要回帖；记录已移除。</summary>
  NoReplyNeeded,

  /// <summary>重放判定为非猜测，记录保留待用户处置。</summary>
  NotGuess,

  /// <summary>重放失败（AI 仍不可用等），记录保留。</summary>
  Failed,

  /// <summary>链路没在跑、没有对端或推送失败，结果没能送达，记录保留。</summary>
  LinkInactive,
}

/// <summary>
/// 单条丢包重试的结果：结局 + 面向用户的一句话 + 记录是否已被移除
/// </summary>
public class DroppedRetryOutcome
{
  /// <summary>丢包记录 Id。</summary>
  public string DroppedId { get; }

  /// <summary>该记录的原始猜测文本。</summary>
  public string RawText { get; }

  /// <summary>结局。</summary>
  public DroppedRetryStatus Status { get; }

  /// <summary>面向用户的结局说明。</summary>
  public string Message { get; }

  /// <summary>记录是否已从丢包列表移除。</summary>
  public bool Removed { get; }

  /// <summary>结果是否算出来了（已回帖、无源可回或本就不需要回帖）。</summary>
  public bool Succeeded =>
    Status
      is DroppedRetryStatus.Replied
        or DroppedRetryStatus.NoReplyTarget
        or DroppedRetryStatus.NoReplyNeeded;

  /// <summary>
  /// 创建一条重试结局
  /// </summary>
  /// <param name="droppedId">丢包记录 Id。</param>
  /// <param name="rawText">原始猜测文本。</param>
  /// <param name="status">结局。</param>
  /// <param name="message">面向用户的结局说明。</param>
  /// <param name="removed">记录是否已被移除。</param>
  public DroppedRetryOutcome(
    string droppedId,
    string rawText,
    DroppedRetryStatus status,
    string message,
    bool removed
  )
  {
    DroppedId = droppedId;
    RawText = rawText;
    Status = status;
    Message = message;
    Removed = removed;
  }

  /// <summary>按丢包记录创建结局（Id 与文本取自记录）。</summary>
  /// <param name="dropped">丢包记录。</param>
  /// <param name="status">结局。</param>
  /// <param name="message">面向用户的结局说明。</param>
  /// <param name="removed">记录是否已被移除。</param>
  public DroppedRetryOutcome(
    DroppedGuess dropped,
    DroppedRetryStatus status,
    string message,
    bool removed
  )
    : this(dropped.Id, dropped.RawText, status, message, removed) { }
}
