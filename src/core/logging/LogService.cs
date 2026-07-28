namespace AutoCMEX.Core.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using Chickensoft.Log;
using Chickensoft.Log.Godot;

/// <summary>
/// 应用日志服务。基于 <c>Chickensoft.Log</c> 统一管理 <c>ILog</c>、写入器与轮转。
/// </summary>
/// <remarks>
/// <para>默认写入器：<c>GDWriter</c>（Godot 控制台）、<c>RotatingFileWriter</c>（按数量轮转的本地文件）、
/// <c>InMemoryLogWriter</c>（供 UI 面板实时显示）。</para>
/// <para>线程安全：<c>GetLogger</c> 会对模块名加锁以保证同一模块返回同一 <c>ILog</c>。</para>
/// </remarks>
public sealed class LogService : ILogService
{
  private readonly Dictionary<string, ILog> _loggers = new(StringComparer.Ordinal);
  private readonly object _loggersLock = new();
  private readonly LogConfig _config;
  private readonly RotatingFileWriter _fileWriter;
  private readonly InMemoryLogWriter _inMemoryWriter;
  private readonly GDWriter? _gdWriter;
  private bool _disposed;

  /// <inheritdoc/>
  public LogConfig Config => _config;

  /// <inheritdoc/>
  public InMemoryLogWriter InMemoryWriter => _inMemoryWriter;

  /// <summary>
  /// 创建日志服务。
  /// </summary>
  /// <param name="config">日志配置；为空时使用默认配置（位于系统临时目录）。</param>
  /// <param name="includeGodotConsole">是否同时输出到 Godot 控制台（默认 true）。</param>
  public LogService(LogConfig? config = null, bool includeGodotConsole = true)
  {
    _config = config ?? new LogConfig();
    EnsureDirectoryExists();

    _fileWriter = new RotatingFileWriter(_config);
    _inMemoryWriter = new InMemoryLogWriter(_config.InMemoryBufferSize)
    {
      MinLevel = _config.MinLevel,
    };

    _gdWriter = includeGodotConsole ? new GDWriter() : null;
  }

  /// <inheritdoc/>
  public ILog GetLogger(string moduleName)
  {
    if (string.IsNullOrWhiteSpace(moduleName))
      moduleName = "Unknown";

    lock (_loggersLock)
    {
      if (_loggers.TryGetValue(moduleName, out var existing))
        return existing;

      var writers = new List<ILogWriter> { _fileWriter, _inMemoryWriter };
      if (_gdWriter != null)
        writers.Add(_gdWriter);

      var log = new Log(moduleName, writers.ToArray())
      {
        Formatter = new LogFormatter
        {
          MessagePrefix = "Info",
          WarningPrefix = "Warn",
          ErrorPrefix = "Error",
        },
      };
      _loggers[moduleName] = log;
      return log;
    }
  }

  /// <summary>
  /// 检查并执行日志文件轮转。建议在写入一定条数后或定时调用。
  /// </summary>
  public void RotateIfNeeded() => _fileWriter.RotateIfNeeded();

  /// <inheritdoc/>
  public void Shutdown()
  {
    if (_disposed)
      return;
    _disposed = true;
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;
    Shutdown();
    GC.SuppressFinalize(this);
  }

  private void EnsureDirectoryExists()
  {
    try
    {
      if (!Directory.Exists(_config.LogDirectory))
        Directory.CreateDirectory(_config.LogDirectory);
    }
    catch (Exception ex)
    {
      Godot.GD.PrintErr($"[LogService] Failed to create log dir: {ex.Message}");
    }
  }
}
