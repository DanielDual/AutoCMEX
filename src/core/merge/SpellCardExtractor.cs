namespace AutoCMEX.Core.Merge;

using System;
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

  /// <summary>
  /// 该卡是否设置 `Performing action`（Sharp 中为 `opening_performance`）。
  /// 为 true 时，Sharp 在符卡练习时会「把练习入口重定向到上一阶段」，故其前序阶段
  /// （同父 `BossDefine` 下的 `Boss.Dialog`/`Boss.MoveTo`/前一张 `BossSpellCard`）
  /// 必须与该卡一起注入，否则练习产物流转时会引用缺失的前序节点。
  /// </summary>
  public bool HasPerformingAction { get; init; }

  /// <summary>
  /// 前置段最前一个节点的索引；无前置段时等于 <see cref="StartIndex"/>。
  /// 仅 <see cref="HasPerformingAction"/> 为 true 时有效。
  /// </summary>
  public int LeadingStartIndex { get; init; }

  /// <summary>
  /// 前置段节点（前序阶段节点及其整棵子树，按文档序）。仅
  /// <see cref="HasPerformingAction"/> 为 true 时非空；否则为空列表。
  /// </summary>
  public IReadOnlyList<LstgesNode> LeadingNodes { get; init; } = new List<LstgesNode>();
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

  /// <summary>Performing action 属性的 attrCap（Sharp 中映射到 opening_performance）。</summary>
  private static readonly string PerformingActionAttr = "Performing action";

  /// <summary>
  /// 前置阶段类型：与符卡同父（BossDefine）下的「cards 序列」成员。Performing action=true 时，
  /// 该卡之前的这些兄弟（及其子树）必须与该卡一起注入。BossInit 不在 cards 序列（是 init 方法），
  /// 属于扫描停止边界，不收集。
  /// </summary>
  private static readonly string[] LeadingStageTypes =
  {
    // 对话（可带子 TaskCreate 等）
    ".Boss.Dialog, ",
    // 出场移动（旧版 .Boss.MoveTo，V2 用 .Boss.BossMoveTo，双候选覆盖）
    ".Boss.MoveTo, ",
    ".Boss.BossMoveTo, ",
    // 前一张符卡
    SpellCardType,
  };

  /// <summary>BossInit（阶段序列首，非 cards 成员）——扫描停止边界之一。</summary>
  private static readonly string BossInitType = ".Boss.BossInit, ";

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
      var perform = ParsePerformingAction(node.GetAttr(PerformingActionAttr));

      // Performing action=true 时收集前置段（同父 BossDefine 下的前序阶段兄弟及其子树）。
      IReadOnlyList<LstgesNode> leadingNodes = new List<LstgesNode>();
      int leadingStart = i;
      if (perform)
      {
        (leadingNodes, leadingStart) = CollectLeadingStages(doc, i);
      }

      result.Add(
        new SpellCardInfo
        {
          StartIndex = i,
          Subtree = subtree,
          Name = name,
          IsNonSpell = IsNonSpellName(name),
          RootLevel = node.Level,
          HasPerformingAction = perform,
          LeadingStartIndex = leadingStart,
          LeadingNodes = leadingNodes,
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

  /// <summary>解析 Performing action 属性值（true 时才携带前序阶段）。</summary>
  private static bool ParsePerformingAction(string? value) =>
    string.Equals(value?.Trim(), "true", System.StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// 收集该卡的前序阶段兄弟（同父 BossDefine、位于卡之前、cards 序列成员）及其整棵子树。
  /// </summary>
  /// <param name="doc">创作者包文档。</param>
  /// <param name="cardIndex">该卡根节点索引。</param>
  /// <returns>
  /// 依次为：(1) 前序段节点（按文档序）；(2) 前序段最前节点索引
  /// （无前序时等于 <paramref name="cardIndex"/>）。
  /// </returns>
  /// <remarks>
  /// 语义实证自 Sharp 源码（<c>DefineSpellCard.cgen</c>）：符卡练习时「入口重定向至
  /// 上一阶段」——scprac 通过 <c>_sc_table</c> 中以 <c>#_temporary_class.cards</c> 记录
  /// 的该卡下标 <c>seq</c> 定位上一阶段为 <c>cards[seq-1]</c>，即**紧邻的前一个阶段**
  /// （其整棵子树），而非「直到 BossInit 的所有前序阶段」。
  /// 因此这里从该卡向前扫「同 level 兄弟」，命中第一个 cards 阶段类型即收集并停止，
  /// 不再继续回溯。跨越非 stages 的同级兄弟（如 Comment）——它们不占 cards 下标，
  /// 不属阶段序列；BossInit 是 <c>:init</c> 方法、非 cards 成员，为阶段序列起点，亦停止。
  /// </remarks>
  private static (List<LstgesNode>, int) CollectLeadingStages(LstgesDocument doc, int cardIndex)
  {
    var nodes = doc.Nodes;
    int cardLevel = nodes[cardIndex].Level;

    int scan = cardIndex - 1;
    while (scan >= 0)
    {
      var n = nodes[scan];
      if (n.Level < cardLevel)
        break; // 回到父层级（BossDefine 或更浅）——阶段序列边界，停止
      if (n.Level != cardLevel)
      {
        scan--; // 更深（前一个兄弟的子内部），跳过
        continue;
      }
      // 同 level 兄弟
      if (n.Type == BossInitType)
        break; // 阶段序列起点，停止
      if (IsLeadingStageType(n.Type))
      {
        // 命中紧邻的前一个阶段（cards 序列成员），收集其整棵子树并停止。
        var st = doc.GetSubtree(scan);
        return (st, scan);
      }
      // 非 stages 的同级兄弟（Comment 等）：不占 cards 下标，跨越继续向前扫
      scan--;
    }

    return (new List<LstgesNode>(), cardIndex);
  }

  /// <summary>判断类型是否属于 cards 序列的「前序阶段」成员。</summary>
  private static bool IsLeadingStageType(string? type) =>
    type != null && Array.IndexOf(LeadingStageTypes, type) >= 0;
}
