namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Models;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 统一猜测处理服务接口
/// </summary>
public interface IGuessProcessingService
{
  /// <summary>
  /// 丢包列表（Sync 管理，变更自动通知）
  /// </summary>
  AutoList<DroppedGuess> DroppedGuesses { get; }

  /// <summary>
  /// 获取当前共享的 Boss 选择
  /// </summary>
  Boss? ResolveCurrentBoss();

  /// <summary>
  /// 处理猜测文本，内部解析当前 Boss 并从设置读取过滤模式。
  /// 调用方自行判断 <see cref="GuessProcessingResult.IsGuess"/> 和 <see cref="GuessProcessingResult.Status"/> 来处理结果。
  /// </summary>
  Task<GuessProcessingResult> ProcessAsync(string rawText);

  /// <summary>
  /// 处理来自 WebSocket 的猜测文本，并把来源上下文写进丢包记录
  /// </summary>
  /// <param name="rawText">原始猜测文本。</param>
  /// <param name="requestId">来源请求标识（WebSocket 消息 Id），用于后续重试时回帖到原消息。</param>
  /// <param name="sender">来源发送者。</param>
  Task<GuessProcessingResult> ProcessAsync(string rawText, string requestId, string sender);

  /// <summary>
  /// 获取丢包列表（只读）
  /// </summary>
  IReadOnlyList<DroppedGuess> GetDroppedGuesses();

  /// <summary>
  /// 按 Id 查找丢包记录
  /// </summary>
  DroppedGuess? FindDroppedGuess(string droppedId);

  /// <summary>
  /// 重放指定丢包猜测：按记录里保存的筛选模式重跑，**不改动丢包列表**
  /// </summary>
  /// <remarks>
  /// 记录的删除由调用方决定（见 <see cref="IDroppedGuessRetryService"/>）：只有结果真正送达群聊
  /// 或确定无法回帖时才该移除，否则用户会白丢一条记录。
  /// </remarks>
  Task<GuessProcessingResult> RetryDroppedGuessAsync(string droppedId);

  /// <summary>
  /// 移除指定丢包记录
  /// </summary>
  void RemoveDroppedGuess(string droppedId);

  /// <summary>
  /// 清除所有丢包记录
  /// </summary>
  void ClearDroppedGuesses();
}
