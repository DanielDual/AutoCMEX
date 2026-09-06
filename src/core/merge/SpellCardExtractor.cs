namespace AutoCMEX.Core.Merge;

using System.Collections.Generic;

/// <summary>
/// 抽取自创作者包的一张符卡/非符信息。
/// </summary>
public sealed class SpellCardInfo
{
  /// <summary>节点在文档中的起始索引。</summary>
  public int StartIndex { get; init; }

  /// <summary>整棵子树的节点（含 BossSpellCard 根及 SCStart/SCFinish/任务节点）。</summary>
  public IReadOnlyList<LstgesNode> Subtree { get; init; } = new List<LstgesNode>();

  /// <summary>符卡名（SCName）。非符为空、[] 或 null。</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>是否非符（SCName 空 或 []）。</summary>
  public bool IsNonSpell { get; init; }

  /// <summary>根节点层级（原始文件中的层级，供后续重编号）。</summary>
  public int RootLevel { get; init; }
}

/// <summary>
/// 抽取创作者包中全部 BossSpellCard 子树，并按 SCName 区分符卡/非符。
/// 禁用的（IsBanned）符卡不抽取。
/// </summary>
public static class SpellCardExtractor
{
  /// <summary>非符占位名（SCName 为空或 "[]" 视为非符）。</summary>
  public static readonly string[] NonSpellNames = { string.Empty, "[]" };

  private static readonly string SpellCardType = ".Boss.BossSpellCard, ";

  /// <summary>
  /// 抽取文档中全部有效的 BossSpellCard 子树。
  /// </summary>
  /// <param name="doc">创作者包文档。</param>
  /// <returns>符卡信息列表（按文件顺序）。</returns>
  public static List<SpellCardInfo> Extract(LstgesDocument doc)
  {
    var result = new List<SpellCardInfo>();
    var nodes = doc.Nodes;

    for (int i = 0; i < nodes.Count; i++)
    {
      var node = nodes[i];
      if (node.Type != SpellCardType)
        continue;

      // 跳过被禁用的符卡（IsBanned）
      if (node.IsBanned)
        continue;

      var subtree = doc.GetSubtree(i);
      var name = node.GetAttrAt(0) ?? string.Empty; // SCName 在第一个属性

      result.Add(
        new SpellCardInfo
        {
          StartIndex = i,
          Subtree = subtree,
          Name = name,
          IsNonSpell = IsNonSpellName(name),
          RootLevel = node.Level,
        }
      );
    }

    return result;
  }

  /// <summary>判断 SCName 是否表示非符（空 或 []）。</summary>
  public static bool IsNonSpellName(string name)
  {
    foreach (var placeholder in NonSpellNames)
    {
      if (name == placeholder)
        return true;
    }
    return false;
  }
}
