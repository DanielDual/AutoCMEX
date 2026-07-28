namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 丢包猜测仓储实现：线程安全的丢包记录管理
/// </summary>
public class DroppedGuessRepository : IDroppedGuessRepository
{
  private readonly List<DroppedGuess> _droppedGuesses = new();
  private readonly object _lock = new();

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
      _droppedGuesses.RemoveAll(d => d.Id == id);
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
