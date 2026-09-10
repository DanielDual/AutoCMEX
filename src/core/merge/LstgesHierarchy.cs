namespace AutoCMEX.Core.Merge;

using System.Collections.Generic;

/// <summary>
/// 逐行模拟 LuaSTGEditorSharp 的 <c>DocumentData.CreateNodeFromFileAsync</c> 父链重建逻辑，
/// 用于校验合并产物的层级是否合法。
/// Sharp 每读完一行用 <c>levelgrad = 当前level - 前level</c> 决定上溯；当 <c>levelgrad &le; 0</c> 时沿
/// 已建立父链向 root 回溯，若回溯步数超过父链深度，<c>prev</c> 会越过 root 变为 <c>null</c>，
/// 下一行 <c>prev.AddChild</c> 即抛 <c>NullReferenceException</c>（即用户真机所见 :328 崩溃）。
/// 层级回落本身合法（<c>.lstgproj</c> 允许跳层），真正的非法条件是「回落步数 &gt; 现有父链深度」。
/// </summary>
public static class LstgesHierarchy
{
  /// <summary>
  /// 返回「首个父链回溯越界」的行索引；-1 表示整份文档可被 Sharp 安全重建。
  /// </summary>
  public static int FindFirstInvalidLevel(IReadOnlyList<LstgesNode> nodes)
  {
    // path 保存从 root 到当前 prev 的实际父链（每层一个代表节点层级）。
    // 可安全上溯的步数上限 = path.Count - 1（root 无父，再多一步就越界成 null）。
    var path = new List<int>();
    for (int i = 0; i < nodes.Count; i++)
    {
      int level = nodes[i].Level;
      if (i == 0)
      {
        path.Clear();
        path.Add(level);
        continue;
      }

      int prev = path[path.Count - 1];
      int grad = level - prev;
      if (grad <= 0)
      {
        int upSteps = -grad + 1;
        if (upSteps > path.Count - 1)
          return i;
        // 回落到倒数第 upSteps 层的祖先：保留 [0 .. path.Count - upSteps - 1]
        int keep = path.Count - upSteps;
        path.RemoveRange(keep, path.Count - keep);
        // 当前节点成为新的 prev，压回 path 末尾（与 Sharp 一致：prev 始终指向刚 AddChild 的节点）。
        path.Add(level);
      }
      else
      {
        path.Add(level);
      }
    }
    return -1;
  }

  /// <summary>
  /// 就地修正层级使「父链回溯永不越界」（父链对齐）。越界节点被抬到「现有父链可容纳的最浅合法层」：
  /// 使回溯步数恰好不超过当前父链深度（prev 回落停在 root 之下仍合法），保证 Sharp 逐行重建不产生\null prev。
  /// 返回是否发生了至少一次修正。
  /// </summary>
  public static bool NormalizeParentChain(IReadOnlyList<LstgesNode> nodes)
  {
    var path = new List<int>();
    bool changed = false;
    for (int i = 0; i < nodes.Count; i++)
    {
      int level = nodes[i].Level;
      if (i == 0)
      {
        path.Clear();
        path.Add(level);
        continue;
      }

      int prev = path[path.Count - 1];
      int grad = level - prev;
      if (grad <= 0)
      {
        int upSteps = -grad + 1;
        int maxAllowed = path.Count - 1;
        if (upSteps > maxAllowed)
        {
          // 抬高层级使新回溯步数 = maxAllowed（恰好停在 root 之下，不越界）。
          level = prev - maxAllowed + 1;
          if (level < 0)
            level = 0;
          nodes[i].Level = level;
          changed = true;
          grad = level - prev;
          upSteps = -grad + 1;
        }
        int keep = path.Count - upSteps;
        path.RemoveRange(keep, path.Count - keep);
        // 当前节点成为新的 prev，压回 path 末尾（与 Sharp 语义一致）。
        path.Add(level);
      }
      else
      {
        path.Add(level);
      }
    }
    return changed;
  }
}
