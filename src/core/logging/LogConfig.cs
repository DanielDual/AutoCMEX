namespace AutoCMEX.Core.Logging;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 日志系统配置。所有路径默认位于系统临时目录，避免污染项目目录。
/// </summary>
public class LogConfig
{
  /// <summary>默认日志目录名（位于系统临时目录之下）。</summary>
  public const string DefaultLogSubdir = "AutoCMEX/logs";

  /// <summary>默认当前日志文件名。</summary>
  public const string DefaultLogFileName = "app.log";

  /// <summary>默认最大保留日志文件数（用户可配置）。</summary>
  public const int DefaultMaxFileCount = 30;

  /// <summary>默认内存缓冲区条数。</summary>
  public const int DefaultInMemoryBufferSize = 1000;

  /// <summary>最低记录日志级别。低于此级别的日志不输出。</summary>
  public LogLevel MinLevel { get; set; } = LogLevel.Info;

  /// <summary>日志根目录（绝对路径）。默认为 <c>%TEMP%/AutoCMEX/logs</c>。</summary>
  public string LogDirectory { get; set; } = DefaultLogDirectory();

  /// <summary>当前日志文件名（不含路径）。</summary>
  public string FileName { get; set; } = DefaultLogFileName;

  /// <summary>最大保留日志文件数量（含当前文件），超过将自动轮转删除最旧文件。</summary>
  public int MaxFileCount { get; set; } = DefaultMaxFileCount;

  /// <summary>内存缓冲区条目上限，超出后丢弃最旧条目。</summary>
  public int InMemoryBufferSize { get; set; } = DefaultInMemoryBufferSize;

  /// <summary>当前日志文件的完整路径（计算属性）。</summary>
  public string CurrentFilePath => Path.Combine(LogDirectory, FileName);

  /// <summary>
  /// 返回系统临时目录下的默认日志目录。
  /// </summary>
  public static string DefaultLogDirectory() => Path.Combine(Path.GetTempPath(), DefaultLogSubdir);
}
