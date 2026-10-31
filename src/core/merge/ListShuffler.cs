namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;

/// <summary>
/// Fisher-Yates 就地洗牌（均匀分布），供对应表重排的三种模式共用。
/// 规则模式（交替 / 按创作者分组）先洗散同类元素池，再按规则填位，
/// 从而做到「形态由规则决定、内容每次随机」。
/// </summary>
internal static class ListShuffler
{
  /// <summary>就地洗牌；<paramref name="rng"/> 由调用方注入（测试传固定种子以便复现）。</summary>
  public static void Shuffle<T>(IList<T> items, Random rng)
  {
    for (var i = items.Count - 1; i > 0; i--)
    {
      var j = rng.Next(i + 1);
      (items[i], items[j]) = (items[j], items[i]);
    }
  }
}
