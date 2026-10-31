namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AutoCMEX.Core.Logging;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// 猜测处理管道：统一入口，处理手动输入和群聊抓取的猜测文本
/// </summary>
public partial class GuessPipeline
{
  private readonly IGuessResponseHandler _responseHandler;
  private readonly IReadOnlyList<CreatorAlias> _aliasTable;
  private readonly ILog _log;

  public GuessPipeline(
    IGuessResponseHandler responseHandler,
    IReadOnlyList<CreatorAlias> aliasTable
  )
    : this(responseHandler, aliasTable, AppLogs.GetOrCreate().GetLogger(nameof(GuessPipeline))) { }

  public GuessPipeline(
    IGuessResponseHandler responseHandler,
    IReadOnlyList<CreatorAlias> aliasTable,
    ILog log
  )
  {
    _responseHandler = responseHandler;
    _aliasTable = aliasTable;
    _log = log;
  }

  /// <summary>
  /// 处理猜测文本
  /// </summary>
  /// <param name="text">猜测文本（严格格式）</param>
  /// <param name="currentBoss">当前 Boss</param>
  /// <returns>处理结果</returns>
  public PipelineResult Process(string text, Boss currentBoss)
  {
    _log.Print(
      $"GuessPipeline.Process: text_len={text?.Length ?? 0}, boss={currentBoss?.Name ?? "(null)"}"
    );
    var totalSw = Stopwatch.StartNew();

    // 1. 别名转换
    var aliasSw = Stopwatch.StartNew();
    text = ConvertAliases(text ?? string.Empty);
    aliasSw.Stop();
    _log.Print($"GuessPipeline: alias conversion {aliasSw.ElapsedMilliseconds}ms");

    // 2. 格式校验与解析
    var parseSw = Stopwatch.StartNew();
    var parseResult = GuessParser.Parse(text, currentBoss?.SpellCards?.Count ?? 0);
    parseSw.Stop();
    _log.Print(
      $"GuessPipeline: parse {parseSw.ElapsedMilliseconds}ms, success={parseResult.IsSuccess}"
    );
    if (!parseResult.IsSuccess)
    {
      _log.Warn($"GuessPipeline: parse error: {parseResult.ErrorMessage}");
      totalSw.Stop();
      _log.Print($"GuessPipeline.Process failed after {totalSw.ElapsedMilliseconds}ms.");
      return PipelineResult.Error(parseResult.ErrorMessage);
    }

    // 3. 匹配对错（先过滤重复猜测）
    var details = new List<string>();
    int totalCards = 0;
    int correctCount = 0;
    int guessedOutCount = 0;
    var guessedOutNames = new List<string>();
    var seenPairs = new HashSet<(int, string)>();

    if (currentBoss == null)
    {
      totalSw.Stop();
      _log.Warn("GuessPipeline.Process: currentBoss is null, aborting.");
      return PipelineResult.Error("当前未选择 Boss");
    }
    foreach (var (index, creator) in parseResult.Pairs)
    {
      var spellCard = currentBoss.SpellCards[index - 1];

      // 过滤重复猜测
      if (!seenPairs.Add((index, creator)))
      {
        details.Add($"符卡 {index}（{spellCard.Name.Value}）：重复猜测 {creator}，已跳过");
        continue;
      }

      // 已揭晓符卡跳过
      if (spellCard.IsRevealed.Value)
      {
        details.Add(
          $"符卡 {index}（{spellCard.Name.Value}）已揭晓，创作者为 {spellCard.Creator.Value}"
        );
        continue;
      }

      // 已被猜出的符卡跳过
      if (spellCard.IsGuessedOut.Value)
      {
        guessedOutCount++;
        guessedOutNames.Add($"{index}");
        details.Add($"符卡 {index}（{spellCard.Name.Value}）已被猜出，跳过");
        continue;
      }

      totalCards++;

      if (string.IsNullOrEmpty(spellCard.Creator.Value))
      {
        // 创作者答案未知，无法判断对错
        details.Add(
          $"符卡 {index}（{spellCard.Name.Value}）：猜测创作者为 {creator}，待揭晓后验证"
        );
      }
      else
      {
        var isCorrect = string.Equals(
          spellCard.Creator.Value,
          creator,
          System.StringComparison.OrdinalIgnoreCase
        );
        if (isCorrect)
        {
          correctCount++;
          details.Add(
            $"符卡 {index}（{spellCard.Name.Value}）：猜对，创作者为 {spellCard.Creator.Value}"
          );
        }
        else
        {
          details.Add(
            $"符卡 {index}（{spellCard.Name.Value}）：猜错，猜测 {creator}，实际为 {spellCard.Creator.Value}"
          );
        }
      }
    }

    // 如果本次猜测全部正确且猜测数量合法（≥2），将涉及的符卡标记为已猜出
    if (totalCards >= 2 && correctCount == totalCards)
    {
      foreach (var (index, _) in parseResult.Pairs)
      {
        var spellCard = currentBoss.SpellCards[index - 1];
        if (!spellCard.IsRevealed.Value && !spellCard.IsGuessedOut.Value)
        {
          spellCard.IsGuessedOut.Value = true;
        }
      }
    }

    // 4. 生成回应
    var response = _responseHandler.Handle(
      totalCards,
      correctCount,
      details,
      guessedOutCount,
      guessedOutNames
    );

    totalSw.Stop();
    _log.Print(
      $"GuessPipeline.Process done in {totalSw.ElapsedMilliseconds}ms: "
        + $"{correctCount}/{totalCards} correct"
    );
    return PipelineResult.Success(response, details);
  }

  // PairRegex 已统一到 GuessParser.PairRegex，避免重复定义

  /// <summary>
  /// 将猜测文本中的别名转换为主名
  /// </summary>
  /// <param name="text">猜测文本，形如「下标+创作者」并以空格分隔。</param>
  /// <returns>替换后的文本；未命中别名表的部分原样保留。</returns>
  /// <remarks>
  /// 判定规则：<b>主名优先</b>——猜测里的名字若本身是某行主名，则一律不作替换；
  /// 否则按「先声明者胜」取第一条把它列为别名的行。别名表可被用户编辑，因此既可能出现
  /// 「甲行的别名恰是乙行的主名」（旧的「命中即替换」会把真实主名改写掉，造成判错），
  /// 也可能出现同一别名被多行声明，两种情形都必须有确定且可预期的归宿。
  /// </remarks>
  private string ConvertAliases(string text)
  {
    if (_aliasTable.Count == 0)
      return text;

    var mainNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    foreach (var alias in _aliasTable)
      mainNames.Add(alias.MainName);

    var aliasToMain = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    foreach (var alias in _aliasTable)
    {
      foreach (var name in alias.Aliases)
      {
        if (string.IsNullOrWhiteSpace(name) || mainNames.Contains(name))
          continue;
        aliasToMain.TryAdd(name, alias.MainName);
      }
    }

    var parts = text.Split(' ');
    for (int i = 0; i < parts.Length; i++)
    {
      var match = GuessParser.PairRegex().Match(parts[i]);
      if (!match.Success)
        continue;

      if (aliasToMain.TryGetValue(match.Groups[2].Value, out var mainName))
        parts[i] = match.Groups[1].Value + mainName;
    }

    return string.Join(' ', parts);
  }
}

/// <summary>
/// 管道处理结果
/// </summary>
public class PipelineResult
{
  public bool IsSuccess { get; }
  public string Response { get; } = string.Empty;
  public string ErrorMessage { get; } = string.Empty;
  public List<string> Details { get; } = new();

  private PipelineResult(bool success, string response, string error, List<string> details)
  {
    IsSuccess = success;
    Response = response;
    ErrorMessage = error;
    Details = details;
  }

  public static PipelineResult Success(string response, List<string> details) =>
    new(true, response, string.Empty, details);

  public static PipelineResult Error(string message) => new(false, string.Empty, message, new());
}
