namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 丢包猜测仓储接口：管理丢包记录的增删查
/// </summary>
public interface IDroppedGuessRepository
{
  /// <summary>
  /// 丢包记录集合（Sync 管理，变更自动通知）
  /// </summary>
  AutoList<DroppedGuess> DroppedGuesses { get; }

  /// <summary>
  /// 添加一条丢包记录
  /// </summary>
  void Add(DroppedGuess dropped);

  /// <summary>
  /// 获取所有丢包记录（只读）
  /// </summary>
  IReadOnlyList<DroppedGuess> GetAll();

  /// <summary>
  /// 查找指定 ID 的丢包记录
  /// </summary>
  DroppedGuess? FindById(string id);

  /// <summary>
  /// 移除指定 ID 的丢包记录
  /// </summary>
  void Remove(string id);

  /// <summary>
  /// 清除所有丢包记录
  /// </summary>
  void Clear();
}
