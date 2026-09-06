namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;

/// <summary>注入点类型。</summary>
public enum InjectionPointKind
{
  /// <summary>符卡注入点。</summary>
  SpellCards,

  /// <summary>资源注入点。</summary>
  Resources,

  /// <summary>Object 注入点。</summary>
  Objects,
}

/// <summary>
/// 一个注入点标记：对应模板中某条约定 <c>General.Comment</c> 注释。
/// </summary>
public readonly record struct InjectionMarker(InjectionPointKind Kind, int NodeIndex, int Level);

/// <summary>
/// 检测结果：各类注入点标记集合。
/// </summary>
public sealed class InjectionPoints
{
  public List<InjectionMarker> Markers { get; } = new();

  public void Add(InjectionMarker marker) => Markers.Add(marker);

  /// <summary>
  /// 取某一类的第一个注入点标记；未找到返回 <c>null</c>。
  /// </summary>
  public InjectionMarker? Find(InjectionPointKind kind)
  {
    foreach (var m in Markers)
    {
      if (m.Kind == kind)
        return m;
    }
    return null;
  }
}

/// <summary>
/// 在工程模板中扫描约定注释，检测符卡/资源/Object 注入点。
/// 约定注释为 <c>.General.Comment</c>，其 Comment 属性包含特定前缀/关键字。
/// </summary>
public class InjectionPointDetector
{
  private readonly string _spellcardMarker;
  private readonly string _resourceMarker;
  private readonly string _objectMarker;

  private static readonly string SpellType = ".General.Comment, LuaSTGEditorSharp";
  private static readonly string DefaultSpellMarker = "Insert spellcards here";
  private static readonly string DefaultResourceMarker = "Insert resources here";
  private static readonly string DefaultObjectMarker = "Insert objects here";

  public InjectionPointDetector(
    string? spellcardMarker = null,
    string? resourceMarker = null,
    string? objectMarker = null
  )
  {
    _spellcardMarker = spellcardMarker ?? DefaultSpellMarker;
    _resourceMarker = resourceMarker ?? DefaultResourceMarker;
    _objectMarker = objectMarker ?? DefaultObjectMarker;
  }

  /// <summary>
  /// 在文档中检测注入点。
  /// </summary>
  /// <param name="doc">模板文档。</param>
  public InjectionPoints Detect(LstgesDocument doc)
  {
    var result = new InjectionPoints();
    var nodes = doc.Nodes;
    for (int i = 0; i < nodes.Count; i++)
    {
      var node = nodes[i];
      if (node.Type != SpellType)
        continue;
      if (node.IsBanned)
        continue;

      var comment = node.GetAttr("Comment");
      if (string.IsNullOrWhiteSpace(comment))
        continue;

      if (ContainsMarker(comment, _spellcardMarker))
        result.Add(new InjectionMarker(InjectionPointKind.SpellCards, i, node.Level));
      else if (ContainsMarker(comment, _resourceMarker))
        result.Add(new InjectionMarker(InjectionPointKind.Resources, i, node.Level));
      else if (ContainsMarker(comment, _objectMarker))
        result.Add(new InjectionMarker(InjectionPointKind.Objects, i, node.Level));
    }

    return result;
  }

  private static bool ContainsMarker(string comment, string marker) =>
    comment.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
