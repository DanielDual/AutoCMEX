namespace AutoCMEX.Core.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chickensoft.Log;

/// <summary>
/// 按数量轮转的文件日志写入器。
/// </summary>
/// <remarks>
/// <para>写入当前文件 <c>app.log</c>；每次 <see cref="RotateIfNeeded"/> 检查目录，
/// 若文件数量超过 <see cref="LogConfig.MaxFileCount"/>，将最旧的文件删除，
/// 并将 <c>app.log</c> 重命名为 <c>app.1.log</c>，<c>app.1.log</c> 重命名为 <c>app.2.log</c>，依此类推。</para>
/// <para>线程安全；自动对敏感字段（API Key、密码、Token 等）做脱敏。</para>
/// </remarks>
public sealed partial class RotatingFileWriter : ILogWriter
{
  private readonly LogConfig _config;
  private readonly object _lock = new();

  // 匹配形如 key=xxx / key: xxx / "key":"xxx" 的敏感键值
  [GeneratedRegex(
    @"(?i)(""?(api[_-]?key|apikey|password|passwd|secret|token|access[_-]?token|authorization)""?\s*["":=]\s*""?)([^""\s,;}]+)(""?)"
  )]
  private static partial Regex SensitiveKvRegex();

  [GeneratedRegex(@"(?i)(Bearer\s+)([A-Za-z0-9\-_\.=]+)")]
  private static partial Regex BearerRegex();

  /// <summary>
  /// 创建轮转文件写入器。
  /// </summary>
  public RotatingFileWriter(LogConfig config)
  {
    _config = config ?? throw new ArgumentNullException(nameof(config));
    EnsureDirectoryExists();
  }

  /// <inheritdoc/>
  public void WriteMessage(string message) => WriteInternal(message);

  /// <inheritdoc/>
  public void WriteWarning(string message) => WriteInternal(message);

  /// <inheritdoc/>
  public void WriteError(string message) => WriteInternal(message);

  /// <summary>
  /// 检查并执行轮转（删除超过 <see cref="LogConfig.MaxFileCount"/> 的旧文件）。
  /// </summary>
  public void RotateIfNeeded()
  {
    lock (_lock)
    {
      try
      {
        EnsureDirectoryExists();
        var files = GetLogFiles();
        if (files.Count < _config.MaxFileCount)
          return;

        // files 已按索引升序。files[0] 是 app.log，files[1] 是 app.1.log，...
        // 当总数 >= MaxFileCount，需要先删除最旧的（files 末项），再逐个前移。
        var toRemove = files.Count - _config.MaxFileCount + 1;
        for (int i = 0; i < toRemove; i++)
        {
          var last = files[files.Count - 1 - i];
          TryDelete(last.FullName);
        }

        // 将剩余的 app.N.log 重新编号 (N -> N + toRemove)
        var remaining = GetLogFiles();
        // 从大到小处理避免覆盖
        for (int i = remaining.Count - 1; i >= 0; i--)
        {
          var f = remaining[i];
          var newIndex = i + toRemove;
          var newPath = Path.Combine(
            _config.LogDirectory,
            $"{Path.GetFileNameWithoutExtension(_config.FileName)}.{newIndex}.log"
          );
          try
          {
            if (i == 0)
            {
              // app.log 重命名为 app.{toRemove}.log
              if (File.Exists(newPath))
                TryDelete(newPath);
              File.Move(f.FullName, newPath);
            }
            else if (!File.Exists(newPath))
            {
              File.Move(f.FullName, newPath);
            }
            else
            {
              // 目标已存在：先删除目标再重命名（极端情况）
              TryDelete(newPath);
              File.Move(f.FullName, newPath);
            }
          }
          catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
          {
            Godot.GD.PrintErr(
              $"[RotatingFileWriter] Failed to rotate {f.FullName} -> {newPath}: {ex.Message}"
            );
          }
        }
      }
      catch (Exception ex)
      {
        Godot.GD.PrintErr($"[RotatingFileWriter] Rotate failed: {ex.Message}");
      }
    }
  }

  private void WriteInternal(string formattedMessage)
  {
    if (string.IsNullOrEmpty(formattedMessage))
      return;
    var sanitized = Sanitize(formattedMessage);
    lock (_lock)
    {
      try
      {
        EnsureDirectoryExists();
        File.AppendAllText(_config.CurrentFilePath, sanitized + Environment.NewLine, Encoding.UTF8);
      }
      catch (Exception ex)
      {
        // 写入失败不应中断应用；输出到 Godot 控制台便于排错
        Godot.GD.PrintErr($"[RotatingFileWriter] Write failed: {ex.Message}");
      }
    }
  }

  private void EnsureDirectoryExists()
  {
    if (!Directory.Exists(_config.LogDirectory))
      Directory.CreateDirectory(_config.LogDirectory);
  }

  private static void TryDelete(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Godot.GD.PrintErr($"[RotatingFileWriter] Failed to delete {path}: {ex.Message}");
    }
  }

  private List<FileInfo> GetLogFiles()
  {
    var baseName = Path.GetFileNameWithoutExtension(_config.FileName);
    var ext = Path.GetExtension(_config.FileName);
    if (string.IsNullOrEmpty(ext))
      ext = ".log";

    var list = new List<FileInfo>();
    if (Directory.Exists(_config.LogDirectory))
    {
      foreach (var f in Directory.GetFiles(_config.LogDirectory, $"{baseName}*{ext}"))
      {
        var name = Path.GetFileName(f);
        if (name == _config.FileName)
        {
          list.Add(new FileInfo(f));
        }
        else if (
          name.StartsWith($"{baseName}.", StringComparison.OrdinalIgnoreCase)
          && TryParseIndex(name, baseName, ext, out _)
        )
        {
          list.Add(new FileInfo(f));
        }
      }
    }
    list.Sort(
      (a, b) => ParseIndex(a.Name, baseName, ext).CompareTo(ParseIndex(b.Name, baseName, ext))
    );
    return list;
  }

  private static int ParseIndex(string fileName, string baseName, string ext)
  {
    if (fileName == $"{baseName}{ext}")
      return 0;
    if (TryParseIndex(fileName, baseName, ext, out var idx))
      return idx;
    return int.MaxValue;
  }

  private static bool TryParseIndex(string fileName, string baseName, string ext, out int index)
  {
    index = 0;
    var prefix = $"{baseName}.";
    if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
      return false;
    if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
      return false;
    var middle = fileName[prefix.Length..^ext.Length];
    return int.TryParse(middle, out index);
  }

  private static string Sanitize(string message)
  {
    if (string.IsNullOrEmpty(message))
      return message;
    // 1. 替换 Bearer xxx
    var s = BearerRegex().Replace(message, m => m.Groups[1].Value + "[REDACTED]");
    // 2. 替换 key=value、key: value、"key":"value"
    s = SensitiveKvRegex().Replace(s, m => m.Groups[1].Value + "[REDACTED]" + m.Groups[4].Value);
    return s;
  }
}
