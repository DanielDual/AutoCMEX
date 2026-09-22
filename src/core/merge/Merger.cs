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

  /// <summary>
  /// 额外排除的归档空间清单（用户配置兜底）。源包资源的最内层归属归档空间命中
  /// 「模板已有归档空间 ∪ 本清单」时，该资源不检测、不导入、不搬迁。
  /// </summary>
  public IReadOnlyCollection<string>? ExcludedArchiveSpaces { get; set; }
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

  /// <summary>通用代码块节点类型（承载任意 Lua 代码，可含被依赖的全局函数/类定义）。</summary>
  private const string GeneralCodeType = ".General.Code, LuaSTGEditorSharp";

  private const string ArchiveSpaceIndicatorType =
    ".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp";

  /// <summary>归档空间名规整（统一斜杠、去掉末尾 `/`），用于命中比较。</summary>
  private static string NormalizeArchive(string value)
  {
    var s = value.Trim().Replace('\\', '/').TrimEnd('/');
    return s;
  }

  /// <summary>枚举文档中全部归档空间名（attrInput，规整后）。</summary>
  private static HashSet<string> CollectArchiveSpaces(LstgesDocument doc)
  {
    var set = new HashSet<string>();
    foreach (var node in doc.Nodes)
    {
      if (node.Type != ArchiveSpaceIndicatorType)
        continue;
      var name = node.GetAttr("Name");
      if (!string.IsNullOrEmpty(name))
        set.Add(NormalizeArchive(name));
    }
    return set;
  }

  /// <summary>
  /// 返回给定节点（索引）在源文档中归属的归档空间节点；无则为 null。
  /// ArchiveSpace 是「流式作用域」：从该节点起、其后所有节点（无论层级）归属它，直到下一个
  /// ArchiveSpace。因此节点归属 = 其位置之前最近的一个 ArchiveSpace（不限层级），
  /// 而非「更高层级的祖先」（真机脚本 Patch 往往与归档同级且在归档之后，层级匹配会漏掉）。
  /// 注意前提：注入偏移以 archive.Level 为基准（RootLevel=archive.Level），故按流式归属的节点
  /// 层级须 ≥ 其归档层级（真机脚本与归档同级即满足）；若更浅节点被流式吸纳后注入层级高于归档，
  /// 会脱离该归档并触发父链对齐告警——采集时应保证目标节点不早于（不浅于）其归档层级。
  /// </summary>
  private static LstgesNode? InnermostArchiveSpace(IReadOnlyList<LstgesNode> nodes, int index)
  {
    for (int i = index - 1; i >= 0; i--)
    {
      if (nodes[i].Type == ArchiveSpaceIndicatorType)
        return nodes[i];
    }
    return null;
  }

  /// <summary>
  /// 判断 <paramref name="index"/> 节点是否「嵌套在某定义型节点子树内」。沿完整父链回溯到根，
  /// 只要祖先链上任一节点为定义型（ObjectDetector.ObjectTypes，排除本代码块自身与模板共享的
  /// BossDefine），即视为定义内部代码——必须随所属定义整棵移植，绝不能被当作顶层全局代码独立剥离。
  /// 关键：不能只查「最近一层」祖先，否则嵌在多层的代码块（如 bent laser 的
  ///  Repeat→TaskNode→BentLaserInit→BentLaserDefine 链）会因最近父是非定义的 Repeat 而被漏判。
  /// </summary>
  private static bool IsNestedInsideDefinition(IReadOnlyList<LstgesNode> nodes, int index)
  {
    int level = nodes[index].Level;
    int floor = index; // 从当前节点向前扫，只回看层级 < 当前 level 的祖先
    for (int i = index - 1; i >= 0; i--)
    {
      int ancestorLevel = nodes[i].Level;
      if (ancestorLevel >= level)
        continue; // 同层或更深，不是祖先（跳过兄弟/后代）
      // 命中祖先（层级更浅）。检查是否定义型。
      string? t = nodes[i].Type;
      bool isDefinitionAncestor =
        t != null
        && t != GeneralCodeType
        && t != BossDefineType
        && ObjectDetector.ObjectTypes.Contains(t);
      if (isDefinitionAncestor)
        return true;
      // 该祖先非定义型。为继续回溯「它的祖先」，把基准 level 压到这个祖先层级，往前再找更深一层的祖先。
      level = ancestorLevel;
    }
    return false;
  }

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

    // 排除集合 = 模板已有归档空间 ∪ 用户配置清单（兜底）。命中者不检测/不导入/不搬迁。
    var excludedSpaces = CollectArchiveSpaces(template);
    if (opt.ExcludedArchiveSpaces != null)
    {
      foreach (var s in opt.ExcludedArchiveSpaces)
      {
        var norm = NormalizeArchive(s);
        if (norm.Length > 0)
          excludedSpaces.Add(norm);
      }
    }

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

      // Performing action=true 时，把前序阶段兄弟（及子树）并入 Nodes 一起注入，保证
      // Sharp 符卡练习「入口重定向到上一阶段」引用的前序节点都在产物内。
      var nodes = new List<LstgesNode>();
      nodes.AddRange(card.LeadingNodes); // 前置段（文档序），无则空
      nodes.AddRange(card.Subtree);

      spellSubtrees.Add(
        new SubtreeRef
        {
          Pkg = entry.PackageIndex,
          RootLevel = card.RootLevel,
          StartIndex = card.LeadingStartIndex, // 前移到前置段最前节点（无则 = 卡）
          Nodes = nodes,
        }
      );
    }

    // ---- 2. 统一采集可移植节点（定义/代码类 + 资源类），按 IsResource 分到两个注入点 ----
    // 取代原第 2 步（定义采集）与第 3 步（顶层资源采集）。定义/代码类注入对象注入点；
    // 资源类（带归档流式基准）注入资源注入点，另行物理随迁。
    // 两语义差异仅在于资源类需处理文件导入链路（路径改写 + 物理复制），采集/注入逻辑一致。
    var transplants = CollectTransplantables(packages, spellSubtrees, excludedSpaces);
    var objectSubtrees = transplants.Definitions;
    var topResources = transplants.Resources;

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

    // ---- 9. 父链对齐（父链合法性修正） ----
    // 逐行模拟 LuaSTGEditorSharp 的 CreateNodeFromFileAsync 重建。注入段的层级由 offset 平移产生，
    // 可能在某行「回落上溯步数超过现有父链深度」，Sharp 会把 prev 越过 root 变 null，AddChild 抛 NRE。
    // 这里就地修正：把越界节点层级抬到父链可容纳的合法值，保证产物可被 Sharp 与编辑器安全打开。
    // 若发生了修正则记入 warning（说明合并段层级有微调，父链已对齐）。
    if (LstgesHierarchy.NormalizeParentChain(doc.Nodes))
    {
      const string msg =
        "合并产物父链已自动对齐（部分注入段层级被微调以确保 LuaSTGEditorSharp 可安全打开）";
      _log.Warn($"Merger: {msg}.");
      warnings.Add(msg);
    }

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

    /// <summary>
    /// 资源型节点注入时是否强制与「归属 ArchiveSpace」同级、紧随其后（不落入归档子树）。
    /// ArchiveSpaceIndicator 是 [LeafNode]，编辑器不允许其拥有子树成员；资源归位同级才能保证
    /// AddFile/Patch 等按流式 archiveSpace 命中正确归档。
    /// </summary>
    public bool ForceSiblingOfArchive;
  }

  /// <summary>统一采集结果：定义/代码类子树（注入对象注入点）与资源类子树（注入资源注入点）。</summary>
  private sealed record Transplantables(List<SubtreeRef> Definitions, List<SubtreeRef> Resources);

  /// <summary>
  /// 统一采集创作者包中需要移植的可移植节点，按类型打到资源/定义两个注入点：
  /// <list type="bullet">
  /// <item><b>定义/代码类</b>（<see cref="ObjectDetector.ObjectTypes"/>，BossDefine 由模板共享除外）：对象/任务/
  /// 子弹/激光/弯折激光/敌人/Boss背景/渲染/函数/自定义节点等承载运行时代码的节点，注入对象注入点。</item>
  /// <item><b>资源类</b>（<see cref="ResourceDetector.ResourceTypes"/>）：加载外部文件、需另行物理随迁，
  /// 注入资源注入点并带最内层归属归档空间作为相对路径基准。</item>
  /// </list>
  /// 两类共用同一覆盖规则（与旧实现一致）：已被已注入子树（符卡/定义）覆盖过的节点不重复采集，
  /// 其嵌套定义/资源随所属被注入子树一起移植，避免双份。Stage 不入本集合（关卡由模板统一提供）。
  /// </summary>
  private static Transplantables CollectTransplantables(
    IReadOnlyList<CreatorPackageDoc> packages,
    IReadOnlyList<SubtreeRef> spellSubtrees,
    IReadOnlySet<string> excludedSpaces
  )
  {
    var definitions = new List<SubtreeRef>();
    var resources = new List<SubtreeRef>();

    // 每个包被已注入子树覆盖的索引区间（初始来自符卡子树；后续追加已采集定义子树）。
    // 注：符卡段（含 Performing action 携带的前序段）用 [StartIndex, StartIndex+Nodes.Count)
    // 近似为连续区间。前序段是「跨越非 stages 同级兄弟」收集的非连续段，因此该近似会把被跳过的
    // 同级兄弟一并包进 covered；这些兄弟通常不在 ObjectTypes/ResourceTypes（无独立采集影响），
    // 仅当 BossDefine 下、卡前夹带资源/定义型非 stages 兄弟（罕见）时才可能被误覆盖漏采——属低危近似。
    var covered = new List<Tuple<int, int>>[packages.Count];
    for (int p = 0; p < packages.Count; p++)
      covered[p] = new();
    foreach (var s in spellSubtrees)
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
        if (type == null)
          continue;

        bool isDefinition = ObjectDetector.ObjectTypes.Contains(type);
        bool isResource = ResourceDetector.ResourceTypes.Contains(type);
        if (!isDefinition && !isResource)
          continue;

        if (IsCovered(i, covered[p]))
          continue; // 已在已注入子树覆盖内（嵌套定义/资源随子树移植），不重复采集

        if (isDefinition)
        {
          if (type == BossDefineType)
            continue; // BossDefine 由模板共享
          if (type == GeneralCodeType)
          {
            // 代码块必须「真正位于顶层/自有文件夹」才作为全局代码剥离注入；若嵌套在某定义型节点
            // 子树内（最近祖先是定义，如 `.Laser.BentLaserDefine` 的 init 代码块），则应由所属定义
            // 整棵携带，绝不独立剥离——否则会被提级到对象注入点、脱离 `self`/局部作用域而并列报错。
            if (IsNestedInsideDefinition(nodes, i))
              continue;
            // 代码块按「位置归属」近似：若其最内层归属归档命中排除集（模板已有该归档）→ 模板已持有，不搬。
            var codeArchive = InnermostArchiveSpace(nodes, i);
            if (codeArchive != null)
            {
              var codeArchiveName = codeArchive.GetAttr("Name");
              var codeNorm =
                codeArchiveName == null ? string.Empty : NormalizeArchive(codeArchiveName);
              if (excludedSpaces.Contains(codeNorm))
                continue;
            }
          }
          var subtree = packages[p].Doc.GetSubtree(i);
          covered[p].Add(Tuple.Create(i, subtree.Count)); // 供后续嵌套定义/资源跳过重复采集
          definitions.Add(
            new SubtreeRef
            {
              Pkg = p,
              RootLevel = node.Level,
              StartIndex = i,
              Nodes = subtree,
            }
          );
          continue;
        }

        // 资源类：排除过滤——最内层归属归档空间命中排除集 → 不检测/不导入（模板已有，创作者不改动）。
        var archive = InnermostArchiveSpace(nodes, i);
        if (archive != null)
        {
          var archiveName = archive.GetAttr("Name");
          var norm = archiveName == null ? string.Empty : NormalizeArchive(archiveName);
          if (excludedSpaces.Contains(norm))
            continue;
        }

        // 归档空间（若携带）与资源节点一并注入，且二者强制落在注入点同级、紧随其后（非归档子树）。
        // 原因：ArchiveSpaceIndicator 是 [LeafNode]，编辑器不允许其子树有任何节点；若资源落入归档子树，
        // Source 流的归档值（CompileProcess.archiveSpace）会随文档序被隔断而错位，导致 AddFile/Patch 等
        // 打错/漏打归档。故资源一律作为「其归属归档的同级后继」注入（ForceSiblingOfArchive 见 BuildInjectedSegments）。
        var nodesToInject =
          archive == null ? new List<LstgesNode> { node } : new List<LstgesNode> { archive, node };
        resources.Add(
          new SubtreeRef
          {
            Pkg = p,
            // 子树根为归档空间节点（若携带）；配合 ForceSiblingOfArchive 在 BuildInjectedSegments 中
            // 让归档空间与资源节点都落在注入点同级，而不是资源落归档子树。
            RootLevel = archive == null ? node.Level : archive.Level,
            StartIndex = i,
            Nodes = nodesToInject,
            ForceSiblingOfArchive = true,
          }
        );
      }
    }

    return new Transplantables(definitions, resources);
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
        // 资源同归档：归档空间与资源节点都落在注入点同级、按 Nodes 列表序紧随（非归档子树）——\
        // 保证 ArchiveSpaceIndicator(LeafNode) 下没有任何子树成员。
        int level = s.ForceSiblingOfArchive ? targetLevel : node.Level + offset;
        var clone = new LstgesNode { Level = level, Line = node.Line?.DeepClone() };
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
          renameMap.TryGetValue((pkg, p), out var replaced)
            ? ReplaceResourceFileName(p, replaced)
            : p
        )
    );
  }

  /// <summary>
  /// 替换资源路径中的文件名（保留其目录层次，即「源目录中的位置」不丢失）。
  /// 目录层次由资源节点保留；物理复制与 Sharp 按此相对路径定位源/落点。
  /// </summary>
  private static string ReplaceResourceFileName(string original, string newFileName)
  {
    int sep = original.LastIndexOfAny(new[] { '/', '\\' });
    if (sep < 0)
      return newFileName;
    return original.Substring(0, sep + 1) + newFileName;
  }

  private static void InjectSegments(LstgesDocument doc, int insertAt, List<LstgesNode> segments)
  {
    if (segments.Count == 0)
      return;
    doc.InsertRange(insertAt, segments);
  }
}
