namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;

/// <summary>
/// 猜测回应策略接口
/// </summary>
public interface IGuessResponseHandler
{
  /// <summary>
  /// 根据猜测结果生成回应文本
  /// </summary>
  /// <param name="totalCards">猜测的未揭晓且未猜出符卡总数</param>
  /// <param name="correctCount">猜对的符卡数</param>
  /// <param name="details">每条符卡的匹配详情（含已揭晓、已猜出标注）</param>
  /// <param name="guessedOutCount">已被猜出而跳过的符卡数</param>
  /// <param name="guessedOutNames">已被猜出的符卡描述列表（如 "符卡1（Card1）"）</param>
  /// <returns>回应文本</returns>
  string Handle(
    int totalCards,
    int correctCount,
    List<string> details,
    int guessedOutCount,
    List<string> guessedOutNames
  );
}
