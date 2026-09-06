namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>一条合并映射：取 package[PackageIndex] 的第 SpellCardIndex 张卡，归 Creator。顺序即注入顺序。</summary>
public readonly record struct MergeMappingEntry(
  int PackageIndex,
  int SpellCardIndex,
  string Creator
);

/// <summary>一份创作者包文档与其（从包名推导出的）创作者名。</summary>
public readonly record struct CreatorPackageDoc(string PackageName, LstgesDocument Doc);

/// <summary>合并选项。</summary>
public sealed class MergeOptions
{
  /// <summary>是否自动为冲突资源重命名（默认 false，保留原名，用户可自改）。</summary>
  public bool AutoRenameResources { get; set; }
}

/// <summary>合并结果。</summary>
public sealed class MergeResult
{
  /// <summary>合并后的文档；失败时为 null。</summary>
  public LstgesDocument? Merged { get; init; }

  /// <summary>收集到的命名冲突（默认保留原名，供 UI 展示）。</summary>
  public List<MergeConflict> Conflicts { get; init; } = new();

  /// <summary>非致命警告（如缺失资源/Object 注入点导致静默丢弃）。</summary>
  public List<string> Warnings { get; init; } = new();

  /// <summary>失败描述；成功时为 null。</summary>
  public string? Error { get; init; }

  /// <summary>是否成功。</summary>
  public bool IsSuccess => Error == null && Merged != null;
}

/// <summary>
/// 把多个创作者包按映射顺序合并进模板：
/// 抽取 BossSpellCard 子树重编号注入符卡注入点；
/// 抽取 Object/Task/Bullet 定义子树注入对象注入点；
/// 抽取顶层资源加载节点注入资源注入点；
/// 重写资源路径（折为纯文件名，冲突可选自动改名）；
/// 收集命名冲突。
/// </summary>
public class Merger
{
  private static readonly string BossDefineType = ".Boss.BossDefine, ";

  private readonly ILog _log;

  public Merger() => _log = AppLogs.GetOrCreate().GetLogger(nameof(Merger));

  /// <summary>
  /// 执行合并。失败时返回带 <see cref="MergeResult.Error"/> 的结果。
  /// </summary>
  public MergeResult Merge(
    LstgesDocument template,
    IReadOnlyList<CreatorPackageDoc> packages,
    IReadOnlyList<MergeMappingEntry> mapping,
    MergeOptions? options = null
  )
  {
    var opt = options ?? new MergeOptions();
    var doc = template.Clone();
    var conflicts = new List<MergeConflict>();
    var warnings = new List<string>();

    var injection = new InjectionPointDetector().Detect(doc);
    var spellMarker = injection.Find(InjectionPointKind.SpellCards);
    if (spellMarker == null)
    {
      _log.Warn("Merger: template has no spellcard injection point.");
      return new MergeResult { Error = "模板缺少符卡注入点（未找到约定注释）" };
    }

    // ---- 1. 按映射顺序收集要注入的符卡子树（按包缓存抽取结果） ----
    var cardCache = new Dictionary<int, List<SpellCardInfo>>();
    var spellSubtrees = new List<SubtreeRef>();
    for (int i = 0; i < mapping.Count; i++)
    {
      var entry = mapping[i];
      if (entry.PackageIndex < 0 || entry.PackageIndex >= packages.Count)
        return new MergeResult { Error = $"映射第 {i + 1} 条引用了不存在的创作者包" };

      var pkg = packages[entry.PackageIndex];
      if (!cardCache.TryGetValue(entry.PackageIndex, out var cards))
      {
        cards = SpellCardExtractor.Extract(pkg.Doc);
        cardCache[entry.PackageIndex] = cards;
      }

      if (entry.SpellCardIndex < 0 || entry.SpellCardIndex >= cards.Count)
      {
        return new MergeResult
        {
          Error = $"映射第 {i + 1} 条引用了创作者包 \"{pkg.PackageName}\" 中不存在的符卡",
        };
      }

      var card = cards[entry.SpellCardIndex];
      spellSubtrees.Add(
        new SubtreeRef
        {
          Pkg = entry.PackageIndex,
          RootLevel = card.RootLevel,
          StartIndex = card.StartIndex,
          Nodes = card.Subtree.ToList(),
        }
      );
    }

    // ---- 2. 对象定义收集（用于注入对象注入点，不依赖模板索引） ----
    var objectSubtrees = CollectObjectSubtrees(packages);

    // ---- 3. 顶层资源节点收集（未被任何注入子树覆盖的资源加载节点） ----
    var topResources = CollectTopLevelResources(packages, spellSubtrees, objectSubtrees);

    // ---- 4. 资源路径解析（冲突检测 + 可选自动改名） ----
    var renameMap = ResolveResourceRenames(
      packages,
      spellSubtrees,
      objectSubtrees,
      topResources,
      opt,
      conflicts
    );

    // ---- 5. 注入符卡子树（重编号，按映射顺序） ----
    var spellSeg = BuildInjectedSegments(spellMarker.Value, spellSubtrees, renameMap);
    InjectSegments(doc, EndOfMarker(doc, spellMarker.Value), spellSeg);

    // 注入会改变 doc.Nodes 索引，故每个注入点必须在对应注入步骤前重新检测（取当前索引）。
    // ---- 6. 注入对象定义子树 ----
    var objectMarker = new InjectionPointDetector().Detect(doc).Find(InjectionPointKind.Objects);
    if (objectSubtrees.Count > 0 && objectMarker == null)
    {
      const string msg = "模板缺少对象注入点（未找到约定注释），对象定义未注入";
      _log.Warn($"Merger: {msg}.");
      warnings.Add(msg);
    }
    else if (objectMarker != null)
    {
      var objSeg = BuildInjectedSegments(objectMarker.Value, objectSubtrees, renameMap);
      InjectSegments(doc, EndOfMarker(doc, objectMarker.Value), objSeg);
    }

    // ---- 7. 注入顶层资源节点（对象注入后再次重新检测） ----
    var resourceMarker = new InjectionPointDetector()
      .Detect(doc)
      .Find(InjectionPointKind.Resources);
    if (topResources.Count > 0 && resourceMarker == null)
    {
      const string msg = "模板缺少资源注入点（未找到约定注释），顶层资源节点未注入";
      _log.Warn($"Merger: {msg}.");
      warnings.Add(msg);
    }
    else if (resourceMarker != null)
    {
      var resSeg = BuildInjectedSegments(resourceMarker.Value, topResources, renameMap);
      InjectSegments(doc, EndOfMarker(doc, resourceMarker.Value), resSeg);
    }

    // ---- 8. 对象名冲突 ----
    CollectObjectNameConflicts(packages, objectSubtrees, conflicts);

    _log.Print(
      $"Merger: merged {spellSubtrees.Count} spellcards, {objectSubtrees.Count} object defs, "
        + $"{topResources.Count} top-level resources, {conflicts.Count} conflicts."
    );
    return new MergeResult
    {
      Merged = doc,
      Conflicts = conflicts,
      Warnings = warnings,
    };
  }

  private sealed class SubtreeRef
  {
    public int Pkg;
    public int RootLevel;
    public int StartIndex;
    public List<LstgesNode> Nodes = new();
  }

  private static List<SubtreeRef> CollectObjectSubtrees(IReadOnlyList<CreatorPackageDoc> packages)
  {
    var result = new List<SubtreeRef>();
    for (int p = 0; p < packages.Count; p++)
    {
      var nodes = packages[p].Doc.Nodes;
      for (int i = 0; i < nodes.Count; i++)
      {
        var node = nodes[i];
        if (node.IsBanned)
          continue;
        var type = node.Type;
        if (type == null || !ObjectDetector.ObjectTypes.Contains(type))
          continue;
        if (type == BossDefineType)
          continue; // BossDefine 由模板共享

        var subtree = packages[p].Doc.GetSubtree(i);
        result.Add(
          new SubtreeRef
          {
            Pkg = p,
            RootLevel = node.Level,
            StartIndex = i,
            Nodes = subtree,
          }
        );
      }
    }
    return result;
  }

  /// <summary>
  /// 收集未被任何已注入子树覆盖的顶层资源加载节点。
  /// </summary>
  private static List<SubtreeRef> CollectTopLevelResources(
    IReadOnlyList<CreatorPackageDoc> packages,
    IReadOnlyList<SubtreeRef> spellSubtrees,
    IReadOnlyList<SubtreeRef> objectSubtrees
  )
  {
    var result = new List<SubtreeRef>();

    // 每个包被注入子树覆盖的索引区间
    var covered = new List<Tuple<int, int>>[packages.Count];
    for (int p = 0; p < packages.Count; p++)
      covered[p] = new();

    foreach (var s in spellSubtrees)
      covered[s.Pkg].Add(Tuple.Create(s.StartIndex, s.Nodes.Count));
    foreach (var s in objectSubtrees)
      covered[s.Pkg].Add(Tuple.Create(s.StartIndex, s.Nodes.Count));

    for (int p = 0; p < packages.Count; p++)
    {
      var nodes = packages[p].Doc.Nodes;
      for (int i = 0; i < nodes.Count; i++)
      {
        var node = nodes[i];
        if (node.IsBanned)
          continue;
        var type = node.Type;
        if (type == null || !ResourceDetector.ResourceTypes.Contains(type))
          continue;
        if (IsCovered(i, covered[p]))
          continue;

        result.Add(
          new SubtreeRef
          {
            Pkg = p,
            RootLevel = node.Level,
            StartIndex = i,
            Nodes = new List<LstgesNode> { node },
          }
        );
      }
    }

    return result;
  }

  private static bool IsCovered(int index, List<Tuple<int, int>> ranges)
  {
    foreach (var r in ranges)
    {
      if (index >= r.Item1 && index < r.Item1 + r.Item2)
        return true;
    }
    return false;
  }

  private sealed record ResourceRef(int Pkg, string BareName, string Original, string PackageName);

  private static Dictionary<(int Pkg, string Path), string> ResolveResourceRenames(
    IReadOnlyList<CreatorPackageDoc> packages,
    IReadOnlyList<SubtreeRef> spellSubtrees,
    IReadOnlyList<SubtreeRef> objectSubtrees,
    IReadOnlyList<SubtreeRef> topResources,
    MergeOptions opt,
    List<MergeConflict> conflicts
  )
  {
    var map = new Dictionary<(int, string), string>();

    var refs = new List<ResourceRef>();
    foreach (var s in spellSubtrees)
      refs.AddRange(CollectResourceRefs(s, packages));
    foreach (var s in objectSubtrees)
      refs.AddRange(CollectResourceRefs(s, packages));
    foreach (var s in topResources)
      refs.AddRange(CollectResourceRefs(s, packages));

    var byName = refs.GroupBy(r => r.BareName).ToList();
    foreach (var group in byName)
    {
      var distinctPkgs = group.Select(r => r.Pkg).Distinct().OrderBy(x => x).ToList();
      bool collides = distinctPkgs.Count > 1;

      if (collides)
      {
        var firstPkgName = packages[distinctPkgs[0]].PackageName;
        conflicts.Add(
          new MergeConflict
          {
            Kind = MergeConflictKind.Resource,
            Name = group.Key,
            Packages = string.Join(", ", distinctPkgs.Select(p => packages[p].PackageName)),
            SuggestedName = $"{firstPkgName}_{group.Key}",
            Description = $"资源文件名 \"{group.Key}\" 在多个创作者包中重名",
          }
        );
      }

      foreach (var r in group)
      {
        map[(r.Pkg, r.Original)] =
          collides && opt.AutoRenameResources
            ? $"{packages[r.Pkg].PackageName}_{group.Key}"
            : group.Key;
      }
    }

    return map;
  }

  private static List<ResourceRef> CollectResourceRefs(
    SubtreeRef s,
    IReadOnlyList<CreatorPackageDoc> packages
  )
  {
    var result = new List<ResourceRef>();
    foreach (var node in s.Nodes)
    {
      if (node.IsBanned)
        continue;
      var type = node.Type;
      if (type == null || !ResourceDetector.ResourceTypes.Contains(type))
        continue;
      var path = node.GetAttrAt(0);
      if (string.IsNullOrWhiteSpace(path))
        continue;

      foreach (var part in path.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries))
      {
        var p = part.Trim();
        if (string.IsNullOrWhiteSpace(p))
          continue;
        result.Add(new ResourceRef(s.Pkg, Path.GetFileName(p), p, packages[s.Pkg].PackageName));
      }
    }
    return result;
  }

  private static void CollectObjectNameConflicts(
    IReadOnlyList<CreatorPackageDoc> packages,
    List<SubtreeRef> objectSubtrees,
    List<MergeConflict> conflicts
  )
  {
    // 名 → 涉及包集合（用可读包名）
    var byName = new Dictionary<string, HashSet<string>>();
    foreach (var s in objectSubtrees)
    {
      var name = s.Nodes[0].GetAttr("Name");
      if (string.IsNullOrWhiteSpace(name))
        continue;
      if (!byName.TryGetValue(name, out var set))
      {
        set = new HashSet<string>();
        byName[name] = set;
      }
      set.Add(packages[s.Pkg].PackageName);
    }

    foreach (var (name, pkgs) in byName)
    {
      if (pkgs.Count <= 1)
        continue;
      conflicts.Add(
        new MergeConflict
        {
          Kind = MergeConflictKind.Object,
          Name = name,
          Packages = string.Join(", ", pkgs.OrderBy(x => x)),
          Description = $"对象/定义 \"{name}\" 在多个创作者包中重名",
        }
      );
    }
  }

  private static int EndOfMarker(LstgesDocument doc, InjectionMarker marker)
  {
    int end = marker.NodeIndex + 1;
    while (end < doc.Count && doc.Nodes[end].Level > marker.Level)
      end++;
    return end;
  }

  /// <summary>
  /// 把子树重编号（相对目标层级）、深度克隆并重写资源路径，生成可注入的节点段。
  /// 深度克隆保证不污染源包文档，同一包可重复合并。
  /// </summary>
  private static List<LstgesNode> BuildInjectedSegments(
    InjectionMarker marker,
    List<SubtreeRef> subtrees,
    Dictionary<(int Pkg, string Path), string> renameMap
  )
  {
    var segments = new List<LstgesNode>();
    int targetLevel = marker.Level;

    foreach (var s in subtrees)
    {
      int offset = targetLevel - s.RootLevel;
      foreach (var node in s.Nodes)
      {
        var clone = new LstgesNode { Level = node.Level + offset, Line = node.Line?.DeepClone() };
        RewriteResourceNode(clone, s.Pkg, renameMap);
        segments.Add(clone);
      }
    }

    return segments;
  }

  private static void RewriteResourceNode(
    LstgesNode node,
    int pkg,
    Dictionary<(int Pkg, string Path), string> renameMap
  )
  {
    var type = node.Type;
    if (type == null || !ResourceDetector.ResourceTypes.Contains(type))
      return;
    if (node.Line is not JsonObject obj)
      return;
    if (obj["Attributes"] is not JsonArray arr || arr.Count == 0)
      return;
    if (arr[0] is not JsonObject first)
      return;

    var attrVal = first["attrInput"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(attrVal))
      return;

    first["attrInput"] = string.Join(
      '|',
      attrVal
        .Split('|', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Trim())
        .Where(p => p.Length > 0)
        .Select(p =>
          renameMap.TryGetValue((pkg, p), out var replaced) ? replaced : Path.GetFileName(p)
        )
    );
  }

  private static void InjectSegments(LstgesDocument doc, int insertAt, List<LstgesNode> segments)
  {
    if (segments.Count == 0)
      return;
    doc.InsertRange(insertAt, segments);
  }
}
