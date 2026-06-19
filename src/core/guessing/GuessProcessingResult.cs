namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;

/// <summary>
/// 猜测处理结果状态
/// </summary>
public enum GuessProcessingStatus
{
  Success,
  NotGuess,
  Error,
}

/// <summary>
/// 统一猜测处理结果
/// </summary>
public class GuessProcessingResult
{
  public GuessProcessingStatus Status { get; }

  public string NormalizedGuess { get; }

  public string ReplyText { get; }

  public string FailureReason { get; }

  public List<string> Details { get; }

  public bool IsGuess => Status == GuessProcessingStatus.Success;

  public bool ShouldReply =>
    Status == GuessProcessingStatus.Success && !string.IsNullOrEmpty(ReplyText);

  private GuessProcessingResult(
    GuessProcessingStatus status,
    string normalizedGuess,
    string replyText,
    string failureReason,
    List<string> details
  )
  {
    Status = status;
    NormalizedGuess = normalizedGuess;
    ReplyText = replyText;
    FailureReason = failureReason;
    Details = details;
  }

  public static GuessProcessingResult Success(
    string normalizedGuess,
    string replyText,
    List<string> details
  ) => new(GuessProcessingStatus.Success, normalizedGuess, replyText, string.Empty, details);

  public static GuessProcessingResult NotGuess(string reason) =>
    new(GuessProcessingStatus.NotGuess, string.Empty, string.Empty, reason, new List<string>());

  public static GuessProcessingResult Error(string reason) =>
    new(GuessProcessingStatus.Error, string.Empty, string.Empty, reason, new List<string>());
}
