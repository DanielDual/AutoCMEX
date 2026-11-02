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
  /// <param name="totalCards">本次参与判定的符卡总数（已猜出的符卡不计入）</param>
  /// <param name="correctCount">猜对的符卡数</param>
  /// <param name="details">每条符卡的匹配详情（含已猜出标注）</param>
  /// <param name="guessedOutCount">已被猜出而跳过的符卡数</param>
  /// <param name="guessedOutNames">已被猜出而跳过的符卡下标列表（如 "1"、"3"），由回应实现以「、」连接后追加进回应文本</param>
  /// <returns>回应文本</returns>
  string Handle(
    int totalCards,
    int correctCount,
    List<string> details,
    int guessedOutCount,
    List<string> guessedOutNames
  );
}
