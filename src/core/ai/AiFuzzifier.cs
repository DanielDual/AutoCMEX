namespace AutoCMEX.Core.Ai;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// AI 模糊化处理：将非严格格式猜测文本转为严格格式，别名转主名
/// </summary>
public class AiFuzzifier
{
  private readonly IAiService _aiService;
  private readonly List<CreatorAlias> _aliasTable;
  private readonly List<Boss> _bosses;
  private readonly Boss _currentBoss;
  private readonly ILog _log;

  public AiFuzzifier(
    IAiService aiService,
    List<CreatorAlias> aliasTable,
    List<Boss> bosses,
    Boss currentBoss
  )
    : this(
      aiService,
      aliasTable,
      bosses,
      currentBoss,
      AppLogs.GetOrCreate().GetLogger(nameof(AiFuzzifier))
    ) { }

  public AiFuzzifier(
    IAiService aiService,
    List<CreatorAlias> aliasTable,
    List<Boss> bosses,
    Boss currentBoss,
    ILog log
  )
  {
    _aiService = aiService;
    _aliasTable = aliasTable;
    _bosses = bosses;
    _currentBoss = currentBoss;
    _log = log;
  }

  /// <summary>
  /// 执行模糊化处理
  /// </summary>
  /// <param name="rawText">原始猜测文本</param>
  /// <returns>严格格式文本</returns>
  public async Task<string> FuzzifyAsync(string rawText)
  {
    _log.Print($"AiFuzzifier.FuzzifyAsync: input_len={rawText?.Length ?? 0}");
    var sw = Stopwatch.StartNew();
    try
    {
      var systemPrompt = BuildSystemPrompt();
      var result = await _aiService.ChatAsync(systemPrompt, rawText);
      sw.Stop();
      _log.Print(
        $"AiFuzzifier.FuzzifyAsync completed in {sw.ElapsedMilliseconds}ms, output_len={result.Length}"
      );
      return result.Trim();
    }
    catch (Exception ex)
    {
      sw.Stop();
      _log.Err(
        $"AiFuzzifier.FuzzifyAsync failed after {sw.ElapsedMilliseconds}ms: "
          + $"{ex.GetType().Name}: {ex.Message}"
      );
      throw;
    }
  }

  /// <summary>
  /// 构建系统提示词
  /// </summary>
  private string BuildSystemPrompt()
  {
    var sb = new StringBuilder();
    sb.AppendLine("你是一个猜测文本格式化助手。你的任务是将用户的猜测文本转换为严格格式。");
    sb.AppendLine();
    sb.AppendLine("严格格式规则：");
    sb.AppendLine("- 格式为：<符卡下标><创作者> <符卡下标><创作者> ...");
    sb.AppendLine("- 符卡下标是数字，创作者是字符串，两者紧邻无空格");
    sb.AppendLine("- 每个符卡下标—创作者对之间用空格分隔");
    sb.AppendLine("- 示例：1Alice 2Bob 3Charlie");
    sb.AppendLine();

    // 别名表
    if (_aliasTable.Count > 0)
    {
      sb.AppendLine("创作者别名表（请将别名转换为主名）：");
      foreach (var alias in _aliasTable)
      {
        var aliases = string.Join(", ", alias.Aliases);
        sb.Append(CultureInfo.InvariantCulture, $"- {alias.MainName}（别名：{aliases}）");
        sb.AppendLine();
      }
      sb.AppendLine();
    }

    // 当前 Boss 符卡列表
    sb.Append(CultureInfo.InvariantCulture, $"当前 Boss：{_currentBoss.Name}");
    sb.AppendLine();
    sb.AppendLine("符卡列表：");
    for (int i = 0; i < _currentBoss.SpellCards.Count; i++)
    {
      var card = _currentBoss.SpellCards[i];
      var creator = string.IsNullOrEmpty(card.Creator) ? "未揭晓" : card.Creator;
      sb.Append(CultureInfo.InvariantCulture, $"  {i + 1}. {card.Name}（创作者：{creator}）");
      sb.AppendLine();
    }
    sb.AppendLine();
    sb.AppendLine("请仅输出转换后的严格格式文本，不要输出任何其他内容。");

    return sb.ToString();
  }
}
