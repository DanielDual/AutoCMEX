namespace AutoCMEX.Core.Logging;

using System;
using System.Diagnostics;
using Chickensoft.Log;

/// <summary>
/// 空日志实现：所有输出方法均为 no-op。
/// </summary>
/// <remarks>
/// <para>
/// 核心层不再依赖启动层的静态日志定位：需要日志的类保留显式 <see cref="ILog"/> 构造入口
/// 供生产/测试注入；不便注入时用本类的 <see cref="Instance"/> 兜底，避免核心层反向依赖启动层。
/// </para>
/// <para>
/// 本类无状态，<see cref="Instance"/> 可安全复用；<see cref="Name"/> 只用于标识，不会输出到任何地方。
/// </para>
/// </remarks>
public sealed class NullLog : ILog
{
  /// <summary>
  /// 共享的空日志实例（无状态，可安全复用）。
  /// </summary>
  public static NullLog Instance { get; } = new();

  private NullLog() { }

  /// <inheritdoc/>
  public string Name => nameof(NullLog);

  /// <inheritdoc/>
  public ILogFormatter Formatter { get; set; } = new NullLogFormatter();

  /// <inheritdoc/>
  public void AddWriter(ILogWriter writer)
  {
    // 空实现：不接收任何写入器
  }

  /// <inheritdoc/>
  public void RemoveWriter(ILogWriter writer)
  {
    // 空实现：本类从不接收写入器
  }

  /// <inheritdoc/>
  public void Print(string message)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Print(StackTrace stackTrace)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Print(StackTrace stackTrace, string message)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Print(Exception e)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Print(Exception e, string message)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Warn(string message)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Err(string message)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Err(Exception e)
  {
    // 空实现：不输出
  }

  /// <inheritdoc/>
  public void Err(Exception e, string message)
  {
    // 空实现：不输出
  }

  /// <summary>
  /// 占位格式化器：原样返回输入，保证若有代码误读 <see cref="Formatter"/> 也不会抛异常。
  /// </summary>
  private sealed class NullLogFormatter : ILogFormatter
  {
    /// <inheritdoc/>
    public string FormatMessage(string logName, string message) => message;

    /// <inheritdoc/>
    public string FormatMessage(string logName, StackTrace stackTrace) => stackTrace.ToString();

    /// <inheritdoc/>
    public string FormatWarning(string logName, string message) => message;

    /// <inheritdoc/>
    public string FormatError(string logName, string message) => message;
  }
}
