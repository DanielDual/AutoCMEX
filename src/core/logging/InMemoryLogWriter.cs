namespace AutoCMEX.Core.Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Chickensoft.Log;

/// <summary>
/// 内存日志写入器：缓存最近的日志条目并提供 <see cref="OnNewLogEntry"/> 事件，
/// 供 UI 面板实时显示。
/// </summary>
/// <remarks>
/// 本类为 <c>Chickensoft.Log</c> 兼容的 <see cref="ILogWriter"/> 实现。
/// </remarks>
public class InMemoryLogWriter : ILogWriter
{
  private readonly ConcurrentQueue<LogEntry> _entries = new();
  private readonly int _maxBufferSize;
  private LogLevel _minLevel = LogLevel.Info;

  /// <summary>当新日志条目写入时触发。</summary>
  public event Action<LogEntry>? OnNewLogEntry;

  /// <summary>
  /// 创建内存日志写入器。
  /// </summary>
  /// <param name="maxBufferSize">内存缓冲区最大条目数。</param>
  public InMemoryLogWriter(int maxBufferSize = LogConfig.DefaultInMemoryBufferSize)
  {
    _maxBufferSize = Math.Max(1, maxBufferSize);
  }

  /// <summary>最低记录级别（可动态调整）。</summary>
  public LogLevel MinLevel
  {
    get => _minLevel;
    set => _minLevel = value;
  }

  /// <inheritdoc/>
  public void WriteMessage(string message) =>
    AddEntry(LogEntry.FromFormattedString(LogLevel.Info, message));

  /// <inheritdoc/>
  public void WriteWarning(string message) =>
    AddEntry(LogEntry.FromFormattedString(LogLevel.Warn, message));

  /// <inheritdoc/>
  public void WriteError(string message) =>
    AddEntry(LogEntry.FromFormattedString(LogLevel.Error, message));

  /// <summary>
  /// 直接写入一个 <see cref="LogEntry"/>（供测试或外部服务使用）。
  /// </summary>
  public void AddEntry(LogEntry entry)
  {
    if (entry == null)
      return;
    if (entry.Level < _minLevel)
      return;

    _entries.Enqueue(entry);
    while (_entries.Count > _maxBufferSize && _entries.TryDequeue(out _))
    {
      // 丢弃最旧条目
    }
    OnNewLogEntry?.Invoke(entry);
  }

  /// <summary>
  /// 取得当前所有缓存条目（按时间正序）。
  /// </summary>
  public IReadOnlyList<LogEntry> GetEntries() => _entries.ToArray();

  /// <summary>
  /// 取得最近 N 条日志（按时间正序）。
  /// </summary>
  public IEnumerable<LogEntry> GetRecentEntries(int count)
  {
    if (count <= 0)
      return Array.Empty<LogEntry>();
    return _entries.ToArray().TakeLast(count);
  }

  /// <summary>
  /// 取得指定级别及以上的日志。
  /// </summary>
  public IEnumerable<LogEntry> GetEntriesByLevel(LogLevel minLevel) =>
    _entries.ToArray().Where(e => e.Level >= minLevel);

  /// <summary>清空缓冲区。</summary>
  public void Clear() => _entries.Clear();
}
