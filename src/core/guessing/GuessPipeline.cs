namespace AutoCMEX.Core.Guessing;

using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Models;

/// <summary>
/// 猜测处理管道：统一入口，处理手动输入和群聊抓取的猜测文本
/// </summary>
public class GuessPipeline
{
    private readonly IGuessResponseHandler _responseHandler;
    private readonly List<CreatorAlias> _aliasTable;

    public GuessPipeline(IGuessResponseHandler responseHandler, List<CreatorAlias> aliasTable)
    {
        _responseHandler = responseHandler;
        _aliasTable = aliasTable;
    }

    /// <summary>
    /// 处理猜测文本
    /// </summary>
    /// <param name="text">猜测文本（严格格式）</param>
    /// <param name="currentBoss">当前 Boss</param>
    /// <returns>处理结果</returns>
    public PipelineResult Process(string text, Boss currentBoss)
    {
        // 1. 别名转换
        text = ConvertAliases(text);

        // 2. 格式校验与解析
        var parseResult = GuessParser.Parse(text, currentBoss.SpellCards.Count);
        if (!parseResult.IsSuccess)
            return PipelineResult.Error(parseResult.ErrorMessage);

        // 3. 匹配对错
        var details = new List<string>();
        int totalCards = 0;
        int correctCount = 0;

        foreach (var (index, creator) in parseResult.Pairs)
        {
            var spellCard = currentBoss.SpellCards[index - 1];

            // 已揭晓符卡跳过
            if (spellCard.IsRevealed)
            {
                details.Add($"符卡 {index}（{spellCard.Name}）已揭晓，创作者为 {spellCard.Creator}");
                continue;
            }

            totalCards++;

            if (string.IsNullOrEmpty(spellCard.Creator))
            {
                // 创作者答案未知，无法判断对错
                details.Add($"符卡 {index}（{spellCard.Name}）：猜测创作者为 {creator}，待揭晓后验证");
            }
            else
            {
                var isCorrect = string.Equals(spellCard.Creator, creator, System.StringComparison.OrdinalIgnoreCase);
                if (isCorrect)
                {
                    correctCount++;
                    details.Add($"符卡 {index}（{spellCard.Name}）：猜对，创作者为 {spellCard.Creator}");
                }
                else
                {
                    details.Add($"符卡 {index}（{spellCard.Name}）：猜错，猜测 {creator}，实际为 {spellCard.Creator}");
                }
            }
        }

        // 4. 生成回应
        var response = _responseHandler.Handle(totalCards, correctCount, details);

        return PipelineResult.Success(response, details);
    }

    /// <summary>
    /// 将猜测文本中的别名转换为主名
    /// </summary>
    private string ConvertAliases(string text)
    {
        if (_aliasTable.Count == 0)
            return text;

        var parts = text.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var match = System.Text.RegularExpressions.Regex.Match(part, @"^(\d+)(\S+)$");
            if (!match.Success)
                continue;

            var index = match.Groups[1].Value;
            var creator = match.Groups[2].Value;

            foreach (var alias in _aliasTable)
            {
                if (alias.Aliases.Any(a => string.Equals(a, creator, System.StringComparison.OrdinalIgnoreCase)))
                {
                    creator = alias.MainName;
                    break;
                }
            }

            parts[i] = index + creator;
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

    public static PipelineResult Success(string response, List<string> details)
        => new(true, response, string.Empty, details);

    public static PipelineResult Error(string message)
        => new(false, string.Empty, message, new());
}
