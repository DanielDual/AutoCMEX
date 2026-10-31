namespace AutoCMEX.Core.Guessing;

using System;

/// <summary>
/// 丢包猜测记录：AI 重试全部失败后暂存，支持用户手动重试
/// </summary>
/// <remarks>
/// 记录里同时保存「回复原消息」与「按原口径重放」所需的上下文：重试要按丢包当时的
/// <see cref="FilterMode"/> 重放，成功后要按 <see cref="RequestId"/> 回帖到原消息。
/// </remarks>
public class DroppedGuess
{
  public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
  public string RawText { get; }
  public DateTime Timestamp { get; }
  public string LastError { get; }

  /// <summary>
  /// 来源请求标识（WebSocket 消息 Id）：插件据此把回复引用到原消息上。
  /// 本地手输等无来源场景为空串，此时重试只能算出结果、无法回帖。
  /// </summary>
  public string RequestId { get; }

  /// <summary>来源发送者（插件上报的 sender）；本地手输为空串。</summary>
  public string Sender { get; }

  /// <summary>丢包时的消息筛选模式（strict / ai / strict_then_ai）；为空表示按当前设置重放。</summary>
  public string FilterMode { get; }

  /// <summary>
  /// 创建丢包记录
  /// </summary>
  /// <param name="rawText">原始猜测文本。</param>
  /// <param name="lastError">最近一次失败原因。</param>
  /// <param name="requestId">来源请求标识；无来源时留空。</param>
  /// <param name="sender">来源发送者；无来源时留空。</param>
  /// <param name="filterMode">丢包时的筛选模式；留空表示按当前设置重放。</param>
  public DroppedGuess(
    string rawText,
    string lastError,
    string requestId = "",
    string sender = "",
    string filterMode = ""
  )
  {
    RawText = rawText;
    Timestamp = DateTime.Now;
    LastError = lastError;
    RequestId = requestId ?? string.Empty;
    Sender = sender ?? string.Empty;
    FilterMode = filterMode ?? string.Empty;
  }
}
