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
  /// 获取丢包列表（只读）
  /// </summary>
  IReadOnlyList<DroppedGuess> GetDroppedGuesses();

  /// <summary>
  /// 重试指定丢包猜测
  /// </summary>
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
