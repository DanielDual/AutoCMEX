namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using AutoCMEX.Models;

/// <summary>
/// 对应表「按创作者分组」重排：组间顺序取各创作者在表中<strong>首次出现</strong>的先后（形态/分组确定），
/// 组内元素每次<strong>随机洗牌</strong>。创作者名为空串时视为同一组。
/// </summary>
public static class MappingGroupByCreator
{
  /// <summary>
  /// 按创作者聚集重排给定对应表（不改动入参，返回新列表）。
  /// </summary>
  /// <param name="mapping">待重排的对应表。</param>
  /// <param name="rng">
  /// 随机源；传 <c>null</c> 用 <see cref="Random.Shared"/>（每次执行组内顺序不同）。
  /// 测试传固定种子以获得可复现结果。
  /// </param>
  public static List<SpellCardMappingEntry> RegroupByCreator(
    IEnumerable<SpellCardMappingEntry> mapping,
    Random? rng = null
  )
  {
    var order = new List<string>();
    var groups = new Dictionary<string, List<SpellCardMappingEntry>>();

    foreach (var entry in mapping)
    {
      var key = entry.Creator.Value ?? string.Empty;
      if (!groups.TryGetValue(key, out var group))
      {
        group = new List<SpellCardMappingEntry>();
        groups[key] = group;
        order.Add(key);
      }
      group.Add(entry);
    }

    // 分组确定、组内随机：每组各自洗牌后再按序拼接。
    var random = rng ?? Random.Shared;
    var result = new List<SpellCardMappingEntry>(order.Count);
    foreach (var key in order)
    {
      var group = groups[key];
      ListShuffler.Shuffle(group, random);
      result.AddRange(group);
    }
    return result;
  }
}
