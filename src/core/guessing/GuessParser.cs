namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;
using System.Text.RegularExpressions;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// 猜测文本严格格式解析器
/// </summary>
/// <remarks>
/// 日志由调用方注入（<see cref="GuessPipeline"/> 传入自己的日志器），本类不再依赖启动层的静态日志定位；
/// 未注入时用 <see cref="NullLog.Instance"/> 兜底，便于纯单测。
/// </remarks>
public static partial class GuessParser
{
  /// <summary>
  /// 严格格式正则：数字+非空白字符，分隔符为任意空白（<c>\s+</c>）；注意 Parse 后续只按半角空格切分
  /// </summary>
  [GeneratedRegex(@"^(\d+\S+)(\s+\d+\S+)*$", RegexOptions.Compiled)]
  private static partial Regex StrictFormatRegex();

  /// <summary>
  /// 解析单个猜测对的正则
  /// </summary>
  [GeneratedRegex(@"^(\d+)(\S+)$", RegexOptions.Compiled)]
  internal static partial Regex PairRegex();

  /// <summary>
  /// 解析猜测文本为符卡下标—创作者对列表
  /// </summary>
  /// <param name="text">猜测文本</param>
  /// <param name="maxCardIndex">当前 Boss 的最大符卡下标（从 1 开始）</param>
  /// <param name="log">日志；为 <c>null</c> 时使用 <see cref="NullLog.Instance"/>（测试兜底）。</param>
  /// <returns>解析结果</returns>
  public static ParseResult Parse(string text, int maxCardIndex, ILog? log = null)
  {
    var logger = log ?? NullLog.Instance;

    if (string.IsNullOrWhiteSpace(text))
    {
      logger.Warn("GuessParser: input is empty/whitespace.");
      return ParseResult.Error("猜测文本为空");
    }

    text = text.Trim();

    if (!StrictFormatRegex().IsMatch(text))
    {
      logger.Warn($"GuessParser: format mismatch for '{text}'");
      return ParseResult.Error("格式错误：请使用严格格式，如 1Alice 2Bob 3Charlie");
    }

    var pairs = new List<(int Index, string Creator)>();
    var parts = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

    foreach (var part in parts)
    {
      var match = PairRegex().Match(part);
      if (!match.Success)
      {
        logger.Warn($"GuessParser: cannot parse pair '{part}'");
        return ParseResult.Error($"无法解析 '{part}'");
      }

      if (!int.TryParse(match.Groups[1].Value, out var index))
      {
        logger.Warn($"GuessParser: invalid index in '{part}'");
        return ParseResult.Error($"'{part}' 中的下标不是有效数字");
      }

      if (index < 1 || index > maxCardIndex)
      {
        logger.Warn($"GuessParser: index {index} out of range (max={maxCardIndex})");
        return ParseResult.Error($"符卡下标 {index} 越界（当前 Boss 共 {maxCardIndex} 张符卡）");
      }

      var creator = match.Groups[2].Value;
      pairs.Add((index, creator));
    }

    logger.Print($"GuessParser: parsed {pairs.Count} pairs from text.");
    return ParseResult.Success(pairs);
  }
}

/// <summary>
/// 解析结果
/// </summary>
public class ParseResult
{
  public bool IsSuccess { get; }
  public string ErrorMessage { get; } = string.Empty;
  public List<(int Index, string Creator)> Pairs { get; } = new();

  private ParseResult(bool success, string error, List<(int, string)>? pairs)
  {
    IsSuccess = success;
    ErrorMessage = error;
    Pairs = pairs ?? new();
  }

  public static ParseResult Success(List<(int, string)> pairs) => new(true, string.Empty, pairs);

  public static ParseResult Error(string message) => new(false, message, null);
}
