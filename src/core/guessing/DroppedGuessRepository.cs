namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;
using System.Linq;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 丢包猜测仓储实现：基于 AutoList 的丢包记录管理，变更由 Sync 自动通知。
/// 所有集合操作由 lock 保护，保证跨线程（WebSocket ThreadPool、并行重试）安全。
/// </summary>
public class DroppedGuessRepository : IDroppedGuessRepository
{
  private readonly AutoList<DroppedGuess> _droppedGuesses = new();
  private readonly object _lock = new();

  /// <inheritdoc/>
  public AutoList<DroppedGuess> DroppedGuesses => _droppedGuesses;

  /// <inheritdoc/>
  public void Add(DroppedGuess dropped)
  {
    lock (_lock)
    {
      _droppedGuesses.Add(dropped);
    }
  }

  /// <inheritdoc/>
  public IReadOnlyList<DroppedGuess> GetAll()
  {
    lock (_lock)
    {
      return _droppedGuesses.ToList();
    }
  }

  /// <inheritdoc/>
  public DroppedGuess? FindById(string id)
  {
    lock (_lock)
    {
      return _droppedGuesses.FirstOrDefault(d => d.Id == id);
    }
  }

  /// <inheritdoc/>
  public void Remove(string id)
  {
    lock (_lock)
    {
      var toRemove = _droppedGuesses.Where(d => d.Id == id).ToList();
      foreach (var dropped in toRemove)
        _droppedGuesses.Remove(dropped);
    }
  }

  /// <inheritdoc/>
  public void Clear()
  {
    lock (_lock)
    {
      _droppedGuesses.Clear();
    }
  }
}
