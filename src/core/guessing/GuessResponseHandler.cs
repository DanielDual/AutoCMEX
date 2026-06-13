namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;

/// <summary>
/// 默认猜测回应策略实现
/// </summary>
public class GuessResponseHandler : IGuessResponseHandler
{
  /// <inheritdoc/>
  public string Handle(int totalCards, int correctCount, List<string> details)
  {
    if (totalCards >= 3)
      return $"猜对 {correctCount}/{totalCards} 张";

    if (totalCards == 2)
      return correctCount == 2 ? "对" : "错";

    // totalCards <= 1: 不回应
    return string.Empty;
  }
}
