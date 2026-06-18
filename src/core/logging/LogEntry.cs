namespace AutoCMEX.Core.Logging;

using System;
using System.Collections.Generic;

/// <summary>
/// 单条日志记录。供 <c>InMemoryLogWriter</c> 缓存与 UI 面板消费。
/// </summary>
public class LogEntry
{
  /// <summary>UTC 时间戳。</summary>
  public DateTime Timestamp { get; set; }

  /// <summary>日志级别。</summary>
  public LogLevel Level { get; set; }

  /// <summary>来源模块（类名或自定义）。</summary>
  public string Module { get; set; } = string.Empty;

  /// <summary>日志消息。</summary>
  public string Message { get; set; } = string.Empty;

  /// <summary>可选上下文（用于结构化日志）。</summary>
  public Dictionary<string, object?>? Context { get; set; }

  /// <summary>原始异常（可选）。</summary>
  public Exception? Exception { get; set; }

  /// <summary>
  /// 构造一条日志条目。
  /// </summary>
  /// <param name="level">日志级别。</param>
  /// <param name="module">模块名。</param>
  /// <param name="message">消息内容。</param>
  /// <param name="exception">关联异常（可选）。</param>
  public LogEntry(LogLevel level, string module, string message, Exception? exception = null)
  {
    Timestamp = DateTime.UtcNow;
    Level = level;
    Module = module ?? string.Empty;
    Message = message ?? string.Empty;
    Exception = exception;
  }

  /// <summary>无参构造（用于反序列化）。</summary>
  public LogEntry()
  {
    Timestamp = DateTime.UtcNow;
  }
}
