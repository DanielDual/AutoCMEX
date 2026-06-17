namespace AutoCMEX.Core.Logging;

/// <summary>
/// 日志级别。与 <c>Chickensoft.Log</c> 的 <c>ILog</c> 三个方法一一对应。
/// </summary>
public enum LogLevel
{
  /// <summary>一般信息，对应 <c>ILog.Print</c>。</summary>
  Info = 0,

  /// <summary>警告，对应 <c>ILog.Warn</c>。</summary>
  Warn = 1,

  /// <summary>错误，对应 <c>ILog.Err</c>。</summary>
  Error = 2,
}
