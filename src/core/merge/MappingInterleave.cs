namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Models;

/// <summary>
/// 对应表「交替」打乱：把非符（N）与符卡（S）重排为交替插花形态。
/// <para>
/// <strong>形态由规则决定</strong>：设 a=非符数、b=符卡数，k=min(a,b)、q=max(a,b)/k、r=max(a,b)%k，
/// 则第 i 对（i 从 1 到 k）的多者块长 <c>w[i] = q + (i &gt; k-r ? 1 : 0)</c>
/// （非降、余数摊到尾块、且 Σw = max(a,b) 恰好用尽多者）。
/// 每对 = 非符块 + 符卡块，首块恒为非符块：a&gt;=b 时「非符 w[i] 个 + 符卡 1 个」，否则「非符 1 个 + 符卡 w[i] 个」。
/// a==b 时退化为 1+1 交替；缺一类时形态退化为「单块」（整列随机）。
/// </para>
/// <para>
/// <strong>内容每次随机</strong>：填位前先把非符池、符卡池各自 Fisher-Yates 洗牌，
/// 故每次执行同一对应表得到的排列不同（同类元素可互换，等价于把两类元素随机分配到固定的形态槽位）；
/// 缺一类时退化为整列洗牌（此时交替规则无约束，与「随机」模式等价）。
/// </para>
/// </summary>
public static class MappingInterleave
{
  /// <summary>
  /// 按交替插花规则重排给定对应表（不改动入参，返回新列表）。
  /// </summary>
  /// <param name="mapping">待重排的对应表。</param>
  /// <param name="rng">
  /// 随机源；传 <c>null</c> 用 <see cref="Random.Shared"/>（每次执行结果不同）。
  /// 测试传固定种子以获得可复现结果。
  /// </param>
  public static List<SpellCardMappingEntry> Reinterleave(
    IEnumerable<SpellCardMappingEntry> mapping,
    Random? rng = null
  )
  {
    var source = mapping.ToList();
    var result = new List<SpellCardMappingEntry>(source.Count);

    // 无交替价值：保持原序。
    if (source.Count <= 1)
    {
      result.AddRange(source);
      return result;
    }

    var nonSpells = source.Where(e => e.IsNonSpell.Value).ToList();
    var spells = source.Where(e => !e.IsNonSpell.Value).ToList();
    var a = nonSpells.Count;
    var b = spells.Count;

    if (a == 0 || b == 0)
    {
      // 只有一类：交替形态退化为「单块」（该块无长度约束），等价于整列随机；
      // 这样「每次按下结果不同」对所有输入都成立。
      var only = a == 0 ? spells : nonSpells;
      ListShuffler.Shuffle(only, rng ?? Random.Shared);
      result.AddRange(only);
      return result;
    }

    // 形态由规则决定、内容随机：先各自洗牌，再按块长填位。
    var random = rng ?? Random.Shared;
    ListShuffler.Shuffle(nonSpells, random);
    ListShuffler.Shuffle(spells, random);

    var k = Math.Min(a, b);
    var q = Math.Max(a, b) / k;
    var r = Math.Max(a, b) % k;
    var nonSpellIndex = 0;
    var spellIndex = 0;

    for (var i = 1; i <= k; i++)
    {
      var blockLength = q + (i > k - r ? 1 : 0);
      if (a >= b)
      {
        for (var t = 0; t < blockLength; t++)
          result.Add(nonSpells[nonSpellIndex++]);
        result.Add(spells[spellIndex++]);
      }
      else
      {
        result.Add(nonSpells[nonSpellIndex++]);
        for (var t = 0; t < blockLength; t++)
          result.Add(spells[spellIndex++]);
      }
    }

    return result;
  }
}
