namespace AutoCMEX.Core.Merge;

using System.Collections.Generic;
using System.Text.Json.Nodes;

/// <summary>
/// LuaSTG Editor Sharp 工程文件（<c>.lstges</c>/<c>.lstgproj</c>）中的单个节点行。
/// 文件为每行 <c>{level},{JSON}</c> 格式，此处保留原始 JSON 节点以支持无损往返。
/// </summary>
public sealed class LstgesNode
{
  /// <summary>节点层级。</summary>
  public int Level { get; set; }

  /// <summary>节点的 JSON 内容（对象）。</summary>
  public JsonNode? Line { get; set; }

  /// <summary>节点类型（<c>$type</c>）。非对象节点为空。</summary>
  public string? Type => Line is JsonObject obj ? obj["$type"]?.GetValue<string>() : null;

  /// <summary>是否被禁用（<c>IsBanned</c>）。</summary>
  public bool IsBanned => (Line as JsonObject)?["IsBanned"]?.GetValue<bool>() == true;

  /// <summary>获取节点对象（仅当内容为对象时）。</summary>
  public JsonObject? AsObject => Line as JsonObject;

  /// <summary>节点的属性列表（<c>Attributes</c> 数组元素）。</summary>
  public List<JsonObject> Attributes
  {
    get
    {
      var list = new List<JsonObject>();
      if (Line is not JsonObject obj)
        return list;
      if (obj["Attributes"] is not JsonArray arr)
        return list;
      foreach (var item in arr)
      {
        if (item is JsonObject ao)
          list.Add(ao);
      }
      return list;
    }
  }

  /// <summary>
  /// 按属性名（<c>attrCap</c>）取第一个的属性值（<c>attrInput</c>）。
  /// </summary>
  /// <param name="cap">属性名。</param>
  /// <returns>属性值；未找到返回 <c>null</c>。</returns>
  public string? GetAttr(string cap)
  {
    foreach (var attr in Attributes)
    {
      if (attr["attrCap"]?.GetValue<string>() == cap)
        return attr["attrInput"]?.GetValue<string>();
    }
    return null;
  }

  /// <summary>
  /// 按索引取属性值（<c>Attributes[index]["attrInput"]</c>）。
  /// </summary>
  /// <param name="index">属性索引。</param>
  /// <returns>属性值；越界或无值为 <c>null</c>。</returns>
  public string? GetAttrAt(int index)
  {
    var attrs = Attributes;
    if (index < 0 || index >= attrs.Count)
      return null;
    return attrs[index]["attrInput"]?.GetValue<string>();
  }

  /// <summary>
  /// 序列化为文件行（<c>{level},{JSON}</c>）。
  /// </summary>
  public string ToLine() => $"{Level},{Line?.ToJsonString()}";

  /// <inheritdoc/>
  public override string ToString() => ToLine();
}
