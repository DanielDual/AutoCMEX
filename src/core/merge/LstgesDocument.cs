namespace AutoCMEX.Core.Merge;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// 一份 .lstges/.lstgproj 文档的节点集合，附带子树/注入/序列化等操作辅助。
/// 节点顺序即文件行顺序，供合并器做重编号与注入。
/// </summary>
public sealed class LstgesDocument
{
  private readonly List<LstgesNode> _nodes;

  /// <summary>全部节点（按文件行顺序）。</summary>
  public IReadOnlyList<LstgesNode> Nodes => _nodes;

  public LstgesDocument(IEnumerable<LstgesNode> nodes) => _nodes = nodes.ToList();

  /// <summary>节点总数。</summary>
  public int Count => _nodes.Count;

  /// <summary>按类型谓词查找所有节点。</summary>
  public IEnumerable<LstgesNode> FindAll(System.Predicate<LstgesNode> predicate)
  {
    foreach (var node in _nodes)
    {
      if (predicate(node))
        yield return node;
    }
  }

  /// <summary>
  /// 返回从 <paramref name="startIndex"/> 起始的整棵子树（含根），
  /// 直到下一个层级不大于根节点的兄弟/父节点结束。
  /// </summary>
  public List<LstgesNode> GetSubtree(int startIndex)
  {
    if (startIndex < 0 || startIndex >= _nodes.Count)
      return new();
    int rootLevel = _nodes[startIndex].Level;
    int end = startIndex + 1;
    while (end < _nodes.Count && _nodes[end].Level > rootLevel)
      end++;
    return _nodes.GetRange(startIndex, end - startIndex);
  }

  /// <summary>子树根节点索引（即自身索引）。</summary>
  public int GetRootIndex(int nodeIndex) => nodeIndex;

  /// <summary>
  /// 在指定位置插入一组节点（按给定根层级重新编号后的节点）。
  /// </summary>
  public void InsertRange(int index, IEnumerable<LstgesNode> nodes)
  {
    var list = nodes.ToList();
    if (list.Count == 0)
      return;
    _nodes.InsertRange(index, list);
  }

  /// <summary>从文档移除 [start, end) 的连续节点。</summary>
  public void RemoveRange(int start, int count)
  {
    if (start < 0 || start >= _nodes.Count || count <= 0)
      return;
    int end = System.Math.Min(start + count, _nodes.Count);
    _nodes.RemoveRange(start, end - start);
  }

  /// <summary>克隆当前文档（浅拷贝节点引用，用于生成副本）。</summary>
  public LstgesDocument Clone() => new(_nodes);

  /// <summary>序列化为工程文件文本。</summary>
  public string Serialize()
  {
    var sb = new StringBuilder();
    foreach (var node in _nodes)
    {
      sb.Append(node.ToLine());
      sb.Append('\n');
    }
    return sb.ToString();
  }
}
