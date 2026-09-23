namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// 第二版 Merger：整文件夹搬运（<see cref="MergeAlgorithm.TopFolderCarry"/>）。
/// 与提取式 Merger（<see cref="Merger"/>）的本质区别：不以「资源/Obj 节点」为单位打散重放注入点，
/// 而是以「包根（File）下最靠近根目录的顶层文件夹」为单位，把该文件夹连同整棵子树原样注入到
/// 对象注入点下；**资源注入点在整文件夹模式下弃用**（可存在但不使用）。
/// 整合判定：文件夹名与模板已有同名顶层文件夹 → 不整合（模板持有）；**例外：名为 "Name" 的文件夹
/// 强制整合**（即使模板已有同名）。符卡处理（映射 → 重编号注入符卡注入点）与提取式完全一致；
/// 其余（路径重写保留相对位置、可选资源自动改名、对象文件夹名冲突收集、父链对齐）亦一致。
/// 第一版 Merger 原样保留，本类独立实现、不依赖其内部私有成员。
/// </summary>
public sealed class TopFolderCarryMerger
{
  /// <summary>整文件夹搬运认的容器文件夹节点类型。</summary>
  private const string FolderType = ".General.Folder, LuaSTGEditorSharp";

  /// <summary>模板共享的 BossDefine 类型（符卡抽取时用于定位 Boss 子树）。</summary>
  private static readonly string BossDefineType = ".Boss.BossDefine, ";

  /// <summary>整文件夹判定的强制整合例外名。</summary>
  private const string AlwaysCarryFolderName = "Name";

  private readonly ILog _log;

  public TopFolderCarryMerger() =>
    _log = AppLogs.GetOrCreate().GetLogger(nameof(TopFolderCarryMerger));

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
      _log.Warn("TopFolderCarryMerger: template has no spellcard injection point.");
      return new MergeResult { Error = "模板缺少符卡注入点（未找到约定注释）" };
    }

    // ---- 1. 符卡注入（与提取式 Merger 完全一致）----
    var spellSubtrees = CollectSpellSubtrees(packages, mapping, opt, out var error);
    if (error != null)
      return new MergeResult { Error = error };

    // ---- 2. 顶层文件夹采集（整合/不整合判定）----
    var templateTopFolders = CollectTemplateTopFolders(template);
    var carrySubtrees = CollectCarryFolders(packages, templateTopFolders);

    // ---- 3. 资源引用收集与可选自动改名（与提取式一致）----
    var renameMap = ResolveResourceRenames(packages, spellSubtrees, carrySubtrees, opt, conflicts);

    // ---- 4. 注入符卡子树 ----
    var spellSeg = BuildFlat(spellMarker.Value, spellSubtrees, renameMap);
    InjectSegments(doc, EndOfMarker(doc, spellMarker.Value), spellSeg);

    // ---- 5. 注入整棵文件夹到对象注入点 ----
    // 资源注入点在整文件夹模式下弃用：不再按类型重放到资源注入点，资源随所属文件夹整体搬运。
    var objectMarker = new InjectionPointDetector().Detect(doc).Find(InjectionPointKind.Objects);
    if (carrySubtrees.Count > 0 && objectMarker == null)
    {
      const string msg = "模板缺少对象注入点（未找到约定注释），整文件夹未注入";
      _log.Warn($"TopFolderCarryMerger: {msg}.");
      warnings.Add(msg);
    }
    else if (objectMarker != null)
    {
      var objSeg = BuildFlat(objectMarker.Value, carrySubtrees, renameMap);
      InjectSegments(doc, EndOfMarker(doc, objectMarker.Value), objSeg);
    }

    // ---- 6. 整文件夹名冲突（跨包同名）----
    CollectFolderNameConflicts(packages, carrySubtrees, conflicts);

    // ---- 7. 父链对齐（与提取式一致）----
    if (LstgesHierarchy.NormalizeParentChain(doc.Nodes))
    {
      const string msg =
        "合并产物父链已自动对齐（部分注入段层级被微调以确保 LuaSTGEditorSharp 可安全打开）";
      _log.Warn($"TopFolderCarryMerger: {msg}.");
      warnings.Add(msg);
    }

    _log.Print(
      $"TopFolderCarryMerger: merged {spellSubtrees.Count} spellcards, "
        + $"{carrySubtrees.Count} top folders, {conflicts.Count} conflicts."
    );
    return new MergeResult
    {
      Merged = doc,
      Conflicts = conflicts,
      Warnings = warnings,
    };
  }

  private readonly struct SubtreeRef
  {
    public readonly int Pkg;
    public readonly int RootLevel;
    public readonly List<LstgesNode> Nodes;

    public SubtreeRef(int pkg, int rootLevel, List<LstgesNode> nodes)
    {
      Pkg = pkg;
      RootLevel = rootLevel;
      Nodes = nodes;
    }
  }

  /// <summary>按映射顺序收集符卡子树（复用 SpellCardExtractor，与提取式一致）。</summary>
  private List<SubtreeRef> CollectSpellSubtrees(
    IReadOnlyList<CreatorPackageDoc> packages,
    IReadOnlyList<MergeMappingEntry> mapping,
    MergeOptions opt,
    out string? error
  )
  {
    error = null;
    var result = new List<SubtreeRef>();
    var cardCache = new Dictionary<int, List<SpellCardInfo>>();
    for (int i = 0; i < mapping.Count; i++)
    {
      var entry = mapping[i];
      if (entry.PackageIndex < 0 || entry.PackageIndex >= packages.Count)
      {
        error = $"映射第 {i + 1} 条引用了不存在的创作者包";
        return result;
      }
      var pkg = packages[entry.PackageIndex];
      if (!cardCache.TryGetValue(entry.PackageIndex, out var cards))
      {
        cards = SpellCardExtractor.Extract(pkg.Doc, opt.ForcePerformAction);
        cardCache[entry.PackageIndex] = cards;
      }
      if (entry.SpellCardIndex < 0 || entry.SpellCardIndex >= cards.Count)
      {
        error = $"映射第 {i + 1} 条引用了创作者包 \"{pkg.PackageName}\" 中不存在的符卡";
        return result;
      }
      var card = cards[entry.SpellCardIndex];
      var nodes = new List<LstgesNode>();
      nodes.AddRange(card.LeadingNodes);
      nodes.AddRange(card.Subtree);
      result.Add(new SubtreeRef(entry.PackageIndex, card.RootLevel, nodes));
    }
    return result;
  }

  /// <summary>枚举模板中顶层层级（Level 1）的文件夹名；用于「模板已有同名文件夹=不整合」判定。</summary>
  private static HashSet<string> CollectTemplateTopFolders(LstgesDocument template)
  {
    var set = new HashSet<string>();
    foreach (var node in template.Nodes)
    {
      if (node.Level != 1)
        continue;
      if (node.IsBanned)
        continue;
      if (node.Type != FolderType)
        continue;
      var name = (node.GetAttr("Name") ?? string.Empty).Trim();
      if (name.Length > 0)
        set.Add(name);
    }
    return set;
  }

  /// <summary>
  /// 采集各包「整棵搬运」的顶层文件夹：
  /// 判定整合 = 文件夹名 == "Name"（强制例外）或模板无同名顶层文件夹；否则不整合。
  /// </summary>
  private static List<SubtreeRef> CollectCarryFolders(
    IReadOnlyList<CreatorPackageDoc> packages,
    IReadOnlySet<string> templateTopFolders
  )
  {
    var result = new List<SubtreeRef>();
    for (int p = 0; p < packages.Count; p++)
    {
      var nodes = packages[p].Doc.Nodes;
      for (int i = 0; i < nodes.Count; i++)
      {
        var node = nodes[i];
        if (node.Level != 1)
          continue;
        if (node.IsBanned)
          continue;
        if (node.Type != FolderType)
          continue;
        var name = (node.GetAttr("Name") ?? string.Empty).Trim();
        bool carry =
          name.Equals(AlwaysCarryFolderName, StringComparison.OrdinalIgnoreCase)
          || !templateTopFolders.Contains(name);
        if (!carry)
          continue; // 模板已有同名且非 Name → 不整合（模板持有）
        var subtree = packages[p].Doc.GetSubtree(i);
        result.Add(new SubtreeRef(p, node.Level, subtree));
      }
    }
    return result;
  }

  /// <summary>把子树按基准层级平铺并深克隆、重写资源路径；生成可注入节点段。</summary>
  private static List<LstgesNode> BuildFlat(
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

  private readonly record struct ResourceRef(int Pkg, string BareName, string Original);

  /// <summary>收集全部资源引用（含符卡子树与整文件夹子树），跨包按文件名判定冲突并可选自动改名。</summary>
  private Dictionary<(int Pkg, string Path), string> ResolveResourceRenames(
    IReadOnlyList<CreatorPackageDoc> packages,
    IReadOnlyList<SubtreeRef> spellSubtrees,
    IReadOnlyList<SubtreeRef> carrySubtrees,
    MergeOptions opt,
    List<MergeConflict> conflicts
  )
  {
    var map = new Dictionary<(int, string), string>();
    var refs = new List<ResourceRef>();
    foreach (var s in spellSubtrees)
      refs.AddRange(CollectResourceRefs(s, packages));
    foreach (var s in carrySubtrees)
      refs.AddRange(CollectResourceRefs(s, packages));

    foreach (var group in refs.GroupBy(r => r.BareName))
    {
      var distinctPkgs = group.Select(r => r.Pkg).Distinct().OrderBy(x => x).ToList();
      bool collides = distinctPkgs.Count > 1;
      if (collides)
      {
        conflicts.Add(
          new MergeConflict
          {
            Kind = MergeConflictKind.Resource,
            Name = group.Key,
            Packages = string.Join(", ", distinctPkgs.Select(p => packages[p].PackageName)),
            SuggestedName = $"{packages[distinctPkgs[0]].PackageName}_{group.Key}",
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
        var pg = part.Trim();
        if (string.IsNullOrWhiteSpace(pg))
          continue;
        result.Add(new ResourceRef(s.Pkg, Path.GetFileName(pg), pg));
      }
    }
    return result;
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
          renameMap.TryGetValue((pkg, p), out var replaced) ? ReplaceFileName(p, replaced) : p
        )
    );
  }

  /// <summary>替换资源路径中的文件名（保留目录层次）。</summary>
  private static string ReplaceFileName(string original, string newFileName)
  {
    int sep = original.LastIndexOfAny(new[] { '/', '\\' });
    return sep < 0 ? newFileName : original.Substring(0, sep + 1) + newFileName;
  }

  /// <summary>收集整文件夹名跨包冲突（同名整文件夹来自多个包）。</summary>
  private static void CollectFolderNameConflicts(
    IReadOnlyList<CreatorPackageDoc> packages,
    List<SubtreeRef> carrySubtrees,
    List<MergeConflict> conflicts
  )
  {
    var byName = new Dictionary<string, HashSet<string>>();
    foreach (var s in carrySubtrees)
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
          Description = $"顶层文件夹 \"{name}\" 在多个创作者包中重名（整文件夹搬运冲突）",
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

  private static void InjectSegments(LstgesDocument doc, int insertAt, List<LstgesNode> segments)
  {
    if (segments.Count == 0)
      return;
    doc.InsertRange(insertAt, segments);
  }
}
