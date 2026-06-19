namespace AutoCMEX.Core.Guessing;

using System.Threading.Tasks;
using AutoCMEX.Models;

/// <summary>
/// 统一猜测处理服务接口
/// </summary>
public interface IGuessProcessingService
{
  /// <summary>
  /// 获取当前共享的 Boss 选择
  /// </summary>
  Boss? ResolveCurrentBoss();

  /// <summary>
  /// 处理托管猜测消息
  /// </summary>
  Task<GuessProcessingResult> ProcessManagedAsync(string rawText);

  /// <summary>
  /// 处理手动输入的猜测消息
  /// </summary>
  Task<GuessProcessingResult> ProcessManualAsync(string rawText, Boss? currentBoss);
}
