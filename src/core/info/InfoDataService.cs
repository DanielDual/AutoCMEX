namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// 符卡猜测情况表的一行（发布与展示共用同一口径）。
/// </summary>
public sealed class GuessingTableRow
{
  /// <summary>符卡编号（= 符卡在 Boss 内的下标 + 1，与猜测流程一致）。</summary>
  public int Index { get; init; }

  /// <summary>
  /// 第一栏展示的创作者名；<b>未猜出时为空串</b>（发布遮罩，防剧透）。
  /// </summary>
  /// <remarks>
  /// 数据层每张符卡的 <see cref="SpellCard.Creator"/> 都有值，空串是渲染遮罩的结果，
  /// 不是数据缺失。
  /// </remarks>
  public string CreatorDisplay { get; init; } = string.Empty;

  /// <summary>符卡名。</summary>
  public string SpellCardName { get; init; } = string.Empty;

  /// <summary>是否已被猜出；为 true 时该行整行染绿。</summary>
  public bool IsGuessedOut { get; init; }
}

/// <summary>
/// 创作者剩余未被猜测符卡数表的一行。
/// </summary>
public sealed class CreatorRemainingRow
{
  /// <summary>创作者名（取自 <see cref="SpellCard.Creator"/>）。</summary>
  public string Creator { get; init; } = string.Empty;

  /// <summary>该创作者名下的符卡总数。</summary>
  public int Total { get; init; }

  /// <summary>该创作者名下已被猜出的符卡数。</summary>
  public int GuessedOutCount { get; init; }

  /// <summary>剩余未被猜测符卡数（= <see cref="Total"/> − <see cref="GuessedOutCount"/>）。</summary>
  public int Remaining { get; init; }

  /// <summary>剩余为 0 时为 true；该行染绿的唯一判据。</summary>
  public bool IsFullyGuessedOut => Remaining == 0;
}

/// <summary>
/// 信息板块两张表的同一时刻快照（都取自当前 Boss）。
/// </summary>
public sealed class InfoTableSnapshot
{
  /// <summary>当前是否存在可用 Boss（false 时两张表都没有数据，UI 应给出提示）。</summary>
  public bool HasBoss { get; init; }

  /// <summary>当前 Boss 名称（出图标题与界面提示用）。</summary>
  public string BossName { get; init; } = string.Empty;

  /// <summary>当前 Boss 的符卡总数。</summary>
  public int TotalCards { get; init; }

  /// <summary>符卡猜测情况表的行（按 Boss 内符卡顺序，含全部卡）。</summary>
  public IReadOnlyList<GuessingTableRow> GuessingRows { get; init; } =
    Array.Empty<GuessingTableRow>();

  /// <summary>创作者剩余未被猜测符卡数表的行（按创作者首次出现顺序）。</summary>
  public IReadOnlyList<CreatorRemainingRow> CreatorRows { get; init; } =
    Array.Empty<CreatorRemainingRow>();
}

/// <summary>
/// 信息板块数据服务：从猜测模块持有的数据推导两张表。
/// </summary>
/// <remarks>
/// <para>
/// 数据来源唯一且与猜测模块同源：<see cref="DataManager.Bosses"/> → 当前
/// <see cref="Boss"/> → <see cref="Boss.SpellCards"/>，创作者取自
/// <see cref="SpellCard.Creator"/>。不读整合板块的对应表/工程包名，也不依赖别名表。
/// </para>
/// <para>
/// 本类只做纯逻辑推导（O(N)，一次遍历完成分组），不含任何 Godot 渲染代码，可直接单测。
/// </para>
/// </remarks>
public class InfoDataService
{
  private readonly DataManager _dataManager;
  private readonly ILog _log;

  /// <summary>
  /// 使用默认日志器创建服务。
  /// </summary>
  /// <param name="dataManager">数据管理器（提供 Bosses 与当前 Boss 下标）。</param>
  public InfoDataService(DataManager dataManager)
    : this(dataManager, AppLogs.GetOrCreate().GetLogger(nameof(InfoDataService))) { }

  /// <summary>
  /// 使用指定日志器创建服务。
  /// </summary>
  /// <param name="dataManager">数据管理器（提供 Bosses 与当前 Boss 下标）。</param>
  /// <param name="log">日志器。</param>
  public InfoDataService(DataManager dataManager, ILog log)
  {
    _dataManager = dataManager;
    _log = log;
  }

  /// <summary>
  /// 解析当前 Boss。
  /// </summary>
  /// <remarks>
  /// 与猜测模块保持同一口径：读 <see cref="AppSettings.SelectedBossIndex"/>，下标越界时
  /// 回退为 0（并写回设置），无 Boss 时返回 null。
  /// </remarks>
  /// <returns>当前 Boss；无数据时为 null。</returns>
  public Boss? ResolveCurrentBoss()
  {
    if (_dataManager.Bosses.Count == 0)
      return null;

    var selectedIndex = _dataManager.Settings.SelectedBossIndex.Value;
    if (selectedIndex < 0 || selectedIndex >= _dataManager.Bosses.Count)
    {
      selectedIndex = 0;
      _dataManager.Settings.SelectedBossIndex.Value = 0;
    }

    return _dataManager.Bosses[selectedIndex];
  }

  /// <summary>
  /// 推导两张表的完整快照。
  /// </summary>
  /// <returns>
  /// 当前 Boss 的快照；无 Boss 时返回 <see cref="InfoTableSnapshot.HasBoss"/> 为 false 的空快照。
  /// </returns>
  public InfoTableSnapshot BuildSnapshot()
  {
    var boss = ResolveCurrentBoss();
    if (boss is null)
    {
      _log.Print("InfoDataService.BuildSnapshot: no current boss, returning empty snapshot.");
      return new InfoTableSnapshot();
    }

    var cards = boss.SpellCards;
    var guessingRows = new List<GuessingTableRow>(cards.Count);
    var creatorOrder = new List<string>();
    var totals = new Dictionary<string, int>(StringComparer.Ordinal);
    var guessedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    var unassignedCards = 0;

    for (var i = 0; i < cards.Count; i++)
    {
      var card = cards[i];
      var isGuessedOut = card.IsGuessedOut.Value;
      var creator = card.Creator.Value ?? string.Empty;

      guessingRows.Add(
        new GuessingTableRow
        {
          Index = i + 1,
          // 未猜出 → 第一栏留空（防剧透遮罩）
          CreatorDisplay = isGuessedOut ? creator : string.Empty,
          SpellCardName = card.Name.Value ?? string.Empty,
          IsGuessedOut = isGuessedOut,
        }
      );

      if (string.IsNullOrWhiteSpace(creator))
      {
        // 没有创作者就无法归属，排除在创作者表之外（记数提示，不静默丢弃）
        unassignedCards++;
        continue;
      }

      if (!totals.TryGetValue(creator, out var total))
      {
        creatorOrder.Add(creator);
        totals[creator] = 0;
        guessedCounts[creator] = 0;
        total = 0;
      }

      totals[creator] = total + 1;
      if (isGuessedOut)
        guessedCounts[creator] = guessedCounts[creator] + 1;
    }

    if (unassignedCards > 0)
    {
      _log.Warn(
        $"InfoDataService.BuildSnapshot: {unassignedCards} card(s) of boss '{boss.Name}' "
          + "have an empty creator and are excluded from the creator table."
      );
    }

    var creatorRows = new List<CreatorRemainingRow>(creatorOrder.Count);
    foreach (var creator in creatorOrder)
    {
      var total = totals[creator];
      var guessedOut = guessedCounts[creator];
      creatorRows.Add(
        new CreatorRemainingRow
        {
          Creator = creator,
          Total = total,
          GuessedOutCount = guessedOut,
          Remaining = total - guessedOut,
        }
      );
    }

    _log.Print(
      $"InfoDataService.BuildSnapshot: boss='{boss.Name}', cards={cards.Count}, "
        + $"creators={creatorRows.Count}, guessedOut={CountGuessedOut(guessingRows)}."
    );

    return new InfoTableSnapshot
    {
      HasBoss = true,
      BossName = boss.Name,
      TotalCards = cards.Count,
      GuessingRows = guessingRows,
      CreatorRows = creatorRows,
    };
  }

  private static int CountGuessedOut(List<GuessingTableRow> rows)
  {
    var count = 0;
    foreach (var row in rows)
    {
      if (row.IsGuessedOut)
        count++;
    }

    return count;
  }
}
