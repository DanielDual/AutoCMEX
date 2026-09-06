namespace AutoCMEX.Core.Merge;

using System.Collections.Generic;

/// <summary>
/// 一个检测到的资源引用：类型 + 路径（可含 | 分隔多路径）。
/// </summary>
public sealed class ResourceInfo
{
  /// <summary>节点在文档中的索引。</summary>
  public int NodeIndex { get; init; }

  /// <summary>节点类型（相对短名，如 Graphics.LoadImage）。</summary>
  public string Type { get; init; } = string.Empty;

  /// <summary>资源路径（attrInput，可含 | 分隔的多个路径）。</summary>
  public string Path { get; init; } = string.Empty;

  /// <summary>按 | 拆分的路径列表。</summary>
  public IReadOnlyList<string> Paths =>
    Path.Split('|', System.StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// 检测创作者包中使用到的资源节点，抽取其路径引用。
/// </summary>
public static class ResourceDetector
{
  /// <summary>被视为资源引用的节点类型集合（与 WeekendMerger 一致）。</summary>
  public static readonly HashSet<string> ResourceTypes = new()
  {
    ".General.AddFile, LuaSTGEditorSharp",
    ".General.Patch, LuaSTGEditorSharp",
    ".Audio.LoadBGM, ",
    ".Audio.LoadSE, ",
    ".Boss.SetBossWalkImageSystem, ",
    ".Graphics.LoadAnimation, ",
    ".Graphics.LoadFX, ",
    ".Graphics.LoadImage, ",
    ".Graphics.LoadImageGroup, ",
    ".Graphics.LoadParticle, ",
  };

  /// <summary>相对短名映射（用于 UI 展示）。</summary>
  public static readonly Dictionary<string, string> TypeShortNames = new()
  {
    [".General.AddFile, LuaSTGEditorSharp"] = "AddFile",
    [".General.Patch, LuaSTGEditorSharp"] = "Patch",
    [".Audio.LoadBGM, "] = "LoadBGM",
    [".Audio.LoadSE, "] = "LoadSE",
    [".Boss.SetBossWalkImageSystem, "] = "SetBossWalkImageSystem",
    [".Graphics.LoadAnimation, "] = "LoadAnimation",
    [".Graphics.LoadFX, "] = "LoadFX",
    [".Graphics.LoadImage, "] = "LoadImage",
    [".Graphics.LoadImageGroup, "] = "LoadImageGroup",
    [".Graphics.LoadParticle, "] = "LoadParticle",
  };

  /// <summary>
  /// 检测文档中的资源引用节点。路径取自第一个属性的 attrInput（可含 | 分隔）。
  /// </summary>
  /// <param name="doc">创作者包文档。</param>
  public static List<ResourceInfo> Detect(LstgesDocument doc)
  {
    var result = new List<ResourceInfo>();
    var nodes = doc.Nodes;

    for (int i = 0; i < nodes.Count; i++)
    {
      var node = nodes[i];
      if (node.IsBanned)
        continue;
      var type = node.Type;
      if (type == null || !ResourceTypes.Contains(type))
        continue;

      var path = node.GetAttrAt(0) ?? string.Empty;
      if (string.IsNullOrWhiteSpace(path))
        continue;

      result.Add(
        new ResourceInfo
        {
          NodeIndex = i,
          Type = TypeShortNames.TryGetValue(type, out var shortName) ? shortName : type,
          Path = path.Trim(),
        }
      );
    }

    return result;
  }
}
