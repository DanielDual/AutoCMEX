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

  /// <summary>
  /// 从 Chickensoft.Log 格式化字符串创建日志条目。
  /// 解析格式 "{Prefix} (ModuleName): Message"。
  /// </summary>
  /// <param name="defaultLevel">默认日志级别。</param>
  /// <param name="formatted">格式化日志字符串。</param>
  /// <returns>解析后的日志条目。</returns>
  public static LogEntry FromFormattedString(LogLevel defaultLevel, string? formatted)
  {
    var s = formatted ?? string.Empty;
    var entry = new LogEntry { Level = defaultLevel, Message = s };

    var colonIdx = s.IndexOf(':');
    if (colonIdx <= 0)
    {
      entry.Module = string.Empty;
      return entry;
    }

    var head = s[..colonIdx];
    var msg = s[(colonIdx + 1)..].TrimStart();

    var openParen = head.IndexOf('(');
    var closeParen = head.IndexOf(')');
    if (openParen > 0 && closeParen > openParen)
    {
      var levelStr = head[..openParen].Trim();
      var module = head[(openParen + 1)..closeParen];
      entry.Module = module;
      entry.Level = levelStr.ToLowerInvariant() switch
      {
        "warn" or "warning" => LogLevel.Warn,
        "error" or "err" => LogLevel.Error,
        _ => defaultLevel,
      };
      entry.Message = msg;
    }

    return entry;
  }
}
