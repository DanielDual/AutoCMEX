namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;

/// <summary>
/// 默认猜测回应策略实现
/// </summary>
public class GuessResponseHandler : IGuessResponseHandler
{
  /// <inheritdoc/>
  public string Handle(
    int totalCards,
    int correctCount,
    List<string> details,
    int guessedOutCount,
    List<string> guessedOutNames
  )
  {
    string baseResponse;

    if (totalCards >= 3)
      baseResponse = $"{correctCount}/{totalCards}";
    else if (totalCards == 2)
      baseResponse = correctCount == 2 ? "✔️" : "❌️";
    else if (totalCards == 0 && guessedOutCount > 0)
      baseResponse = "所有猜测的符卡均已被猜出";
    else
      baseResponse = "必须猜两个以上";

    if (guessedOutCount > 0 && totalCards > 0)
    {
      var names = string.Join("、", guessedOutNames);
      baseResponse += $"（{names} 已被猜出，已跳过）";
    }

    return baseResponse;
  }
}
