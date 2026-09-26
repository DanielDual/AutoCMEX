namespace AutoCMEX.Core.Guessing;

using AutoCMEX.Core.WebSocket;

/// <summary>
/// 猜测回复消息的唯一构造点
/// </summary>
/// <remarks>
/// 「消息到达」与「丢包重试」两条链路共用本工厂，回复口径（是否回、回什么）只在这里定义一次，
/// 避免两条链各写一份而逐渐不一致。
/// </remarks>
public static class GuessReplyFactory
{
  /// <summary>
  /// 构造一条 <c>guess_result</c> 事件
  /// </summary>
  /// <param name="requestId">原请求的消息 Id（插件据此引用原消息回帖）。</param>
  /// <param name="result">猜测处理结果。</param>
  /// <returns>该回帖时返回事件消息；无需回帖或缺少原请求标识时返回 null。</returns>
  public static WebSocketMessage? CreateResultEvent(string requestId, GuessProcessingResult result)
  {
    if (!result.ShouldReply || string.IsNullOrEmpty(requestId))
      return null;

    return WebSocketMessage.CreateEvent(
      "guess_result",
      new
      {
        requestId,
        replyText = result.ReplyText,
        normalizedGuess = result.NormalizedGuess,
      }
    );
  }
}
