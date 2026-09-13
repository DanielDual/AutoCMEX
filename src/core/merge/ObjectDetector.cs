namespace AutoCMEX.Core.Merge;

using System.Collections.Generic;

/// <summary>
/// 一个检测到的 Object/定义节点（Object/Task/Bullet/Boss/Laser 定义等）。
/// </summary>
public sealed class ObjectInfo
{
  /// <summary>节点在文档中的索引。</summary>
  public int NodeIndex { get; init; }

  /// <summary>对象/定义名称（第一个"Name"属性的值，找不到则类型短名）。</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>节点类型（相对短名，如 ObjectDefine）。</summary>
  public string Type { get; init; } = string.Empty;
}

/// <summary>
/// 检测创作者包中可自定义的 Object/定义节点。
/// </summary>
public static class ObjectDetector
{
  /// <summary>
  /// 被视为「可移植定义/代码节点」的类型集合：类定义（Object/Bullet/Laser/Enemy）、任务定义、
  /// Boss 背景定义、渲染节点、函数定义与自定义节点等承载运行时代码的节点。
  /// 注意：① 不含 <c>.Stage.*</c>（关卡由模板统一提供，不移植）；② <c>.Boss.BossDefine</c> 由模板共享，
  /// 在 Merger 中被排除注入；③ 资源型节点（<see cref="ResourceDetector"/>）走文件导入注入点，不属于本集合。
  /// </summary>
  public static readonly HashSet<string> ObjectTypes = new()
  {
    ".Object.ObjectDefine, ",
    ".Task.TaskDefine, ",
    ".Bullet.BulletDefine, ",
    ".Boss.BossDefine, ",
    ".Laser.LaserDefine, ",
    ".Enemy.EnemyDefine, ",
    ".Laser.BentLaserDefine, ",
    ".Boss.BossBGDefine, ",
    ".Render.RenderTarget, ",
    ".Render.CreateRenderTarget, ",
    ".Render.OnRender, ",
    ".Render.Render4V, ",
    ".Data.Function, ",
    ".Advanced.UnidentifiedNode, LuaSTGEditorSharp",
  };

  private static readonly Dictionary<string, string> TypeShortNames = new()
  {
    [".Object.ObjectDefine, "] = "ObjectDefine",
    [".Task.TaskDefine, "] = "TaskDefine",
    [".Bullet.BulletDefine, "] = "BulletDefine",
    [".Boss.BossDefine, "] = "BossDefine",
    [".Laser.LaserDefine, "] = "LaserDefine",
    [".Enemy.EnemyDefine, "] = "EnemyDefine",
    [".Laser.BentLaserDefine, "] = "BentLaserDefine",
    [".Boss.BossBGDefine, "] = "BossBGDefine",
    [".Render.RenderTarget, "] = "RenderTarget",
    [".Render.CreateRenderTarget, "] = "CreateRenderTarget",
    [".Render.OnRender, "] = "OnRender",
    [".Render.Render4V, "] = "Render4V",
    [".Data.Function, "] = "Function",
    [".Advanced.UnidentifiedNode, LuaSTGEditorSharp"] = "UnidentifiedNode",
  };

  private static readonly string NameAttr = "Name";

  /// <summary>
  /// 检测文档中的自定义定义节点。
  /// </summary>
  /// <param name="doc">创作者包文档。</param>
  public static List<ObjectInfo> Detect(LstgesDocument doc)
  {
    var result = new List<ObjectInfo>();
    var nodes = doc.Nodes;

    for (int i = 0; i < nodes.Count; i++)
    {
      var node = nodes[i];
      if (node.IsBanned)
        continue;
      var type = node.Type;
      if (type == null || !ObjectTypes.Contains(type))
        continue;

      var name = node.GetAttr(NameAttr) ?? string.Empty;
      if (string.IsNullOrWhiteSpace(name))
        name = TypeShortNames.TryGetValue(type, out var shortName) ? shortName : type;

      result.Add(
        new ObjectInfo
        {
          NodeIndex = i,
          Name = name,
          Type = TypeShortNames.TryGetValue(type, out var display) ? display : type,
        }
      );
    }

    return result;
  }
}
