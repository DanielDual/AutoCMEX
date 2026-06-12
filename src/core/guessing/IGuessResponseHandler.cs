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
    /// <param name="totalCards">猜测的未揭晓符卡总数</param>
    /// <param name="correctCount">猜对的符卡数</param>
    /// <param name="details">每条符卡的匹配详情（含已揭晓标注）</param>
    /// <returns>回应文本，空字符串表示不回应</returns>
    string Handle(int totalCards, int correctCount, List<string> details);
}
