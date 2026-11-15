namespace AutoCMEX.Core.Help.Markdown;

using System;

/// <summary>
/// 生成期诊断：一条「文件:行 + 原因」，供生成器打印与测试断言。
/// </summary>
/// <param name="FilePath">出错的源文件（相对 <c>docs/help</c> 的路径或绝对路径）。</param>
/// <param name="Line">出错行号（1 起）；0 表示问题与具体行无关。</param>
/// <param name="Reason">失败原因（面向内容作者，不出现实现细节）。</param>
public sealed record HelpDiagnostic(string FilePath, int Line, string Reason)
{
  /// <summary>渲染成 <c>文件:行: 原因</c> 的单行文本。</summary>
  /// <returns>便于直接打印或断言的单行文本。</returns>
  public override string ToString() =>
    Line > 0 ? $"{FilePath}:{Line}: {Reason}" : $"{FilePath}: {Reason}";
}

/// <summary>
/// 帮助内容格式错误：携带行号，由页面集合装配器捕获并转换成 <see cref="HelpDiagnostic"/>。
/// </summary>
public sealed class HelpFormatException : Exception
{
  /// <summary>以「行号 + 原因」构造异常。</summary>
  /// <param name="line">出错行号（1 起）。</param>
  /// <param name="message">失败原因。</param>
  public HelpFormatException(int line, string message)
    : base(message)
  {
    Line = line;
  }

  /// <summary>出错行号（1 起）。</summary>
  public int Line { get; }
}
