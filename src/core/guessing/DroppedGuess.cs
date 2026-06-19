namespace AutoCMEX.Core.Guessing;

using System;

/// <summary>
/// 丢包猜测记录：AI 重试全部失败后暂存，支持用户手动重试
/// </summary>
public class DroppedGuess
{
  public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
  public string RawText { get; }
  public DateTime Timestamp { get; }
  public string LastError { get; }

  public DroppedGuess(string rawText, string lastError)
  {
    RawText = rawText;
    Timestamp = DateTime.Now;
    LastError = lastError;
  }
}
