namespace AutoCMEX.Core.Recording;

using System.Collections.Generic;
using AutoCMEX.Models;

/// <summary>
/// GIF 集的序号与命名派生规则（纯函数，无副作用）。
/// </summary>
/// <remarks>
/// <para>
/// 规则来自需求裁定，与游戏侧 <c>cards</c> 数组下标**不是**一回事：
/// </para>
/// <list type="number">
/// <item>序号从 1 起，**只数战斗阶段**（符卡与非符），对话/入场移动等阶段不计数。</item>
/// <item>非符统一命名「普通攻击 N」，N 是**非符自己**的序号（只数非符，从 1 起），
/// 与它在「符卡 + 非符」合并序列里的总序号无关。</item>
/// <item>符卡保留游戏里的符卡名。</item>
/// </list>
/// <para>
/// 故「绝对下标 → 序号」必须两套计数并行，本类只提供这两套计数与命名，不碰磁盘。
/// </para>
/// </remarks>
public static class GifNaming
{
  /// <summary>非符名的前缀（实际名字为「前缀 + 空格 + 非符序号」）。</summary>
  public const string NonSpellNamePrefix = "普通攻击";

  /// <summary>「非战斗阶段」的哨兵返回值：对话等阶段没有序号。</summary>
  public const int NoOrdinal = 0;

  /// <summary>
  /// 求某张卡在**全部战斗阶段**里的序号（1 基）。
  /// </summary>
  /// <param name="cards">枚举得到的完整卡表（含对话阶段，按绝对下标升序）。</param>
  /// <param name="absoluteIndex">目标卡的绝对下标。</param>
  /// <returns>序号；目标卡不存在或不是战斗阶段时返回 <see cref="NoOrdinal"/>。</returns>
  public static int CombatOrdinalOf(IReadOnlyList<RecordingCardInfo>? cards, int absoluteIndex)
  {
    var ordinal = 0;
    foreach (var card in EnumerateUntil(cards, absoluteIndex))
    {
      if (card.IsCombat)
      {
        ordinal++;
      }
    }

    var target = Find(cards, absoluteIndex);
    return target is { IsCombat: true } ? ordinal : NoOrdinal;
  }

  /// <summary>
  /// 求某张非符在**非符序列**里的序号（1 基）。
  /// </summary>
  /// <param name="cards">枚举得到的完整卡表（含对话阶段，按绝对下标升序）。</param>
  /// <param name="absoluteIndex">目标卡的绝对下标。</param>
  /// <returns>序号；目标卡不存在、是符卡或不是战斗阶段时返回 <see cref="NoOrdinal"/>。</returns>
  public static int NonSpellOrdinalOf(IReadOnlyList<RecordingCardInfo>? cards, int absoluteIndex)
  {
    var ordinal = 0;
    foreach (var card in EnumerateUntil(cards, absoluteIndex))
    {
      if (card.IsCombat && !card.IsSpellCard)
      {
        ordinal++;
      }
    }

    var target = Find(cards, absoluteIndex);
    return target is { IsCombat: true, IsSpellCard: false } ? ordinal : NoOrdinal;
  }

  /// <summary>
  /// 生成非符名。
  /// </summary>
  /// <param name="nonSpellOrdinal">非符序号（1 基）。</param>
  /// <returns>形如「普通攻击 1」的名字；序号非法时返回空串。</returns>
  public static string NonSpellName(int nonSpellOrdinal) =>
    nonSpellOrdinal > 0 ? $"{NonSpellNamePrefix} {nonSpellOrdinal}" : string.Empty;

  /// <summary>
  /// 求某张卡在清单里的名字。
  /// </summary>
  /// <param name="card">目标卡。</param>
  /// <param name="nonSpellOrdinal">该卡为非符时的非符序号，见 <see cref="NonSpellOrdinalOf"/>。</param>
  /// <returns>符卡返回游戏内符卡名；非符返回「普通攻击 N」；非战斗阶段返回空串。</returns>
  public static string ResolveEntryName(RecordingCardInfo? card, int nonSpellOrdinal)
  {
    if (card is not { IsCombat: true })
    {
      return string.Empty;
    }

    return card.IsSpellCard ? card.Name : NonSpellName(nonSpellOrdinal);
  }

  /// <summary>按下标升序枚举到目标卡（含目标卡）为止。</summary>
  /// <param name="cards">卡表。</param>
  /// <param name="absoluteIndex">目标卡绝对下标。</param>
  /// <returns>可枚举序列；目标卡不存在时为空序列。</returns>
  private static IEnumerable<RecordingCardInfo> EnumerateUntil(
    IReadOnlyList<RecordingCardInfo>? cards,
    int absoluteIndex
  )
  {
    if (cards == null)
    {
      yield break;
    }

    foreach (var card in cards)
    {
      if (card.AbsoluteIndex <= absoluteIndex)
      {
        yield return card;
      }
    }
  }

  /// <summary>按绝对下标找卡。</summary>
  /// <param name="cards">卡表。</param>
  /// <param name="absoluteIndex">目标绝对下标。</param>
  /// <returns>找到的卡；未找到返回 <c>null</c>。</returns>
  private static RecordingCardInfo? Find(IReadOnlyList<RecordingCardInfo>? cards, int absoluteIndex)
  {
    if (cards == null)
    {
      return null;
    }

    foreach (var card in cards)
    {
      if (card.AbsoluteIndex == absoluteIndex)
      {
        return card;
      }
    }

    return null;
  }
}
