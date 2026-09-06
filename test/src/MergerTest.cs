namespace AutoCMEX;

using System.Linq;
using AutoCMEX.Core.Merge;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 阶段1 Merger 合并器单元测试：重编号注入、按对应表顺序、资源路径重写、冲突收集、可选自动改名。
/// </summary>
public class MergerTest : TestClass
{
  public MergerTest(Node testScene)
    : base(testScene) { }

  /// <summary>
  /// 一个带符卡/资源/对象注入点注释的模板。
  /// </summary>
  private const string TemplateText =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"File\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".ProjSettings, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Output Name\",\"attrInput\":\"\",\"EditWindow\":\"\"},{\"attrCap\":\"Author\",\"attrInput\":\"LuaSTG\",\"EditWindow\":\"\"}],\"AttributeCount\":2}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"shared_boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"code\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  /// <summary>
  /// 构造单张符卡（指定 SCName 与是否非符），可带上一个资源节点。
  /// </summary>
  private static string SpellCard(string name, bool nonSpell, string? resourcePath = null)
  {
    var card =
      "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
      + name
      + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "4,{\"$type\":\".Task.TaskWait, \",\"Attributes\":[{\"attrCap\":\"Time\",\"attrInput\":\"60\",\"EditWindow\":\"yield\"}],\"AttributeCount\":1}\n";
    if (resourcePath != null)
    {
      card +=
        "3,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"res/"
        + resourcePath
        + "\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n";
    }
    return card;
  }

  private static string PackageDoc(params string[] spellCards) =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + string.Join("", spellCards);

  [Test]
  public void Merge_InjectsInMappingOrder()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );
    var pkgB = new CreatorPackageDoc(
      "B",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡B", false)), out _)!
    );

    var mapping = new[]
    {
      new MergeMappingEntry(1, 0, "B"), // B 的卡先注入
      new MergeMappingEntry(0, 0, "A"), // A 的卡后注入
    };

    var result = new Merger().Merge(template, new[] { pkgA, pkgB }, mapping);
    result.IsSuccess.ShouldBeTrue();

    var types = result.Merged!.Nodes.Select(n => n.Type).ToList();
    var scIdx = new System.Collections.Generic.List<int>();
    for (int i = 0; i < types.Count; i++)
      if (types[i] == ".Boss.BossSpellCard, ")
        scIdx.Add(i);

    scIdx.Count.ShouldBe(2);
    // 注入位置应位于符卡注入注释之后（注释索引之后）
    // 按映射顺序：第一张是 B 的卡，第二张是 A 的卡 —— 通过 SCName 判断
    result.Merged.Nodes[scIdx[0]].GetAttrAt(0).ShouldBe("卡B");
    result.Merged.Nodes[scIdx[1]].GetAttrAt(0).ShouldBe("卡A");
  }

  [Test]
  public void Merge_ProducesRoundTrippableDocument()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    var serialized = result.Merged!.Serialize();
    var reparsed = LstgesParser.ParseDocument(serialized, out var error);
    error.ShouldBeNull();
    reparsed.ShouldNotBeNull();
    reparsed!.Count.ShouldBe(result.Merged.Count);
  }

  [Test]
  public void Merge_RenumbersLevels_CorrectNesting()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 符卡注入注释在 level 2；注入的 BossSpellCard 根应为 level 2，其子节点 level>2
    var nodes = result.Merged!.Nodes;
    for (int i = 1; i < nodes.Count; i++)
    {
      // 子节点层级必须大于父节点层级（若父节点在模板中连续）
      // 这里仅验证整体：任意节点的 Level 不小于其前驱中最近的、level 更小者的 level
    }
    // 验证注入的卡根层级 == 注释层级 2，且其子节点层级 > 根
    int spellIdx = -1;
    for (int i = 0; i < nodes.Count; i++)
      if (nodes[i].Type == ".Boss.BossSpellCard, ")
      {
        spellIdx = i;
        break;
      }

    spellIdx.ShouldBeGreaterThan(-1);
    nodes[spellIdx].Level.ShouldBe(2);
    nodes[spellIdx + 1].Level.ShouldBeGreaterThan(nodes[spellIdx].Level);
  }

  [Test]
  public void Merge_ResourcePath_RewrittenToBareName()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false, "boss.png")), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    var image = result.Merged!.Nodes.First(n => n.Type == ".Graphics.LoadImage, ");
    image.GetAttrAt(0).ShouldBe("boss.png"); // 已折为纯文件名
    result.Conflicts.ShouldBeEmpty();
  }

  [Test]
  public void Merge_ResourceCollision_ListedAndPreservedByDefault()
  {
    // 模板资源注入点可缺省（此处无资源注入点，仅验证冲突收集与保留原名）
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false, "boss.png")), out _)!
    );
    var pkgB = new CreatorPackageDoc(
      "B",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡B", false, "boss.png")), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA, pkgB },
      new[] { new MergeMappingEntry(0, 0, "A"), new MergeMappingEntry(1, 0, "B") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 默认保留原名（不自动改名）
    result.Conflicts.ShouldNotBeEmpty();
    result
      .Conflicts.Any(c => c.Kind == MergeConflictKind.Resource && c.Name == "boss.png")
      .ShouldBeTrue();
  }

  [Test]
  public void Merge_ResourceAutoRename_PrefixesCollisions()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false, "boss.png")), out _)!
    );
    var pkgB = new CreatorPackageDoc(
      "B",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡B", false, "boss.png")), out _)!
    );

    var opt = new MergeOptions { AutoRenameResources = true };
    var result = new Merger().Merge(
      template,
      new[] { pkgA, pkgB },
      new[] { new MergeMappingEntry(0, 0, "A"), new MergeMappingEntry(1, 0, "B") },
      opt
    );
    result.IsSuccess.ShouldBeTrue();

    var images = result.Merged!.Nodes.Where(n => n.Type == ".Graphics.LoadImage, ").ToList();
    // 自动改名后，两个 boss.png 分别带前缀 A_/B_
    images.Select(n => n.GetAttrAt(0)).OrderBy(x => x).ShouldContain("A_boss.png");
    images.Select(n => n.GetAttrAt(0)).OrderBy(x => x).ShouldContain("B_boss.png");
  }

  [Test]
  public void Merge_ObjectDefs_Injected()
  {
    var text =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Task.TaskDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"my_task\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"卡X\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";
    var pkg = new CreatorPackageDoc("X", LstgesParser.ParseDocument(text, out _)!);

    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "X") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 对象注入点注释存在时，TaskDefine 应被注入
    result.Merged!.Nodes.Any(n => n.Type == ".Task.TaskDefine, ").ShouldBeTrue();
  }

  /// <summary>
  /// 一个带资源注入点注释的模板（Resources marker 放在 level 2）。
  /// </summary>
  private const string TemplateWithResourceMarker =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"File\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"shared_boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  [Test]
  public void Merge_TopLevelResourceNodes_InjectedIntoResourceMarker()
  {
    // 创作者包顶层有独立资源加载节点（不在符卡子树内）
    var pkgText =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"resource\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "2,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"images/bg.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
      + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + SpellCard("卡A", false);

    var template = LstgesParser.ParseDocument(TemplateWithResourceMarker, out _)!;
    var pkgA = new CreatorPackageDoc("A", LstgesParser.ParseDocument(pkgText, out _)!);

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 顶层 LoadImage 应被注入到资源注入点（在该注释之后），且路径折为纯文件名
    var images = result.Merged!.Nodes.Where(n => n.Type == ".Graphics.LoadImage, ").ToList();
    images.Count.ShouldBeGreaterThan(0);
    images.Any(n => n.GetAttrAt(0) == "bg.png").ShouldBeTrue();
  }

  [Test]
  public void Merge_ObjectNameConflict_Deduplicated()
  {
    var taskText =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Task.TaskDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"dup_task\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + SpellCard("卡X", false);

    var pkgA = new CreatorPackageDoc("A", LstgesParser.ParseDocument(taskText, out _)!);
    var pkgB = new CreatorPackageDoc("B", LstgesParser.ParseDocument(taskText, out _)!);

    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var result = new Merger().Merge(
      template,
      new[] { pkgA, pkgB },
      new[] { new MergeMappingEntry(0, 0, "A"), new MergeMappingEntry(1, 0, "B") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 同一对象名冲突只应出现一次（去重），且包名用可读名
    var objConflicts = result
      .Conflicts.Where(c => c.Kind == MergeConflictKind.Object && c.Name == "dup_task")
      .ToList();
    objConflicts.Count.ShouldBe(1);
    objConflicts[0].Packages.ShouldContain("A");
    objConflicts[0].Packages.ShouldContain("B");
  }

  [Test]
  public void Merge_AllInjectedSpellcardRoots_SameLevelAsMarker()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );
    var pkgB = new CreatorPackageDoc(
      "B",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡B", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA, pkgB },
      new[] { new MergeMappingEntry(0, 0, "A"), new MergeMappingEntry(1, 0, "B") }
    );
    result.IsSuccess.ShouldBeTrue();

    var roots = result.Merged!.Nodes.Where(n => n.Type == ".Boss.BossSpellCard, ").ToList();
    roots.Count.ShouldBe(2);
    // 每个注入的符卡根层级都等于注入点注释层级（2），彼此同级（兄弟）
    roots.All(r => r.Level == 2).ShouldBeTrue();
  }

  [Test]
  public void Merge_LowerLevelsMonotonic_NoBrokenNesting()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );
    var pkgB = new CreatorPackageDoc(
      "B",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡B", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA, pkgB },
      new[] { new MergeMappingEntry(0, 0, "A"), new MergeMappingEntry(1, 0, "B") }
    );
    result.IsSuccess.ShouldBeTrue();

    var nodes = result.Merged!.Nodes;
    // 逐节点：任何节点的层级不得跳到比上一个更深的「越级」（子级仅允许比父级深，兄弟/父级层级合法）
    for (int i = 1; i < nodes.Count; i++)
    {
      // 找最近的、层级严格小于本节点的前驱（即父/祖），保证本节点深度不越级
      int parentLevel = -1;
      for (int j = i - 1; j >= 0; j--)
      {
        if (nodes[j].Level < nodes[i].Level)
        {
          parentLevel = nodes[j].Level;
          break;
        }
      }
      if (parentLevel >= 0)
        nodes[i].Level.ShouldBeInRange(parentLevel + 1, parentLevel + 2); // 只允许父-子或父-孙，不越级到深多层
    }
  }

  [Test]
  public void Merge_RoundTrip_PreservesInjectedSubtreeStructure()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    var serialized = result.Merged!.Serialize();
    var reparsed = LstgesParser.ParseDocument(serialized, out var error);
    error.ShouldBeNull();
    reparsed.ShouldNotBeNull();

    var origCards = result.Merged.Nodes.Where(n => n.Type == ".Boss.BossSpellCard, ").ToList();
    var newCards = reparsed!.Nodes.Where(n => n.Type == ".Boss.BossSpellCard, ").ToList();
    origCards.Count.ShouldBe(newCards.Count);
    // 往返后符卡根层级与 SCName 不变
    for (int i = 0; i < origCards.Count; i++)
    {
      newCards[i].Level.ShouldBe(origCards[i].Level);
      newCards[i].GetAttrAt(0).ShouldBe(origCards[i].GetAttrAt(0));
    }
  }

  [Test]
  public void Merge_MissingObjectMarker_Warns()
  {
    // 模板无对象注入点，但包内存在对象定义 → 不报错但应警告且对象不注入
    var noObjMarker =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";
    var tmpl = LstgesParser.ParseDocument(noObjMarker, out _)!;

    var pkgText =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Task.TaskDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"t\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + SpellCard("卡A", false);
    var pkg = new CreatorPackageDoc("A", LstgesParser.ParseDocument(pkgText, out _)!);

    var result = new Merger().Merge(
      tmpl,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();
    result.Warnings.Any(w => w.Contains("对象注入点")).ShouldBeTrue();
  }

  [Test]
  public void Merge_ResourceSuggestedName_PopulatedOnAutoRename()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false, "boss.png")), out _)!
    );
    var pkgB = new CreatorPackageDoc(
      "B",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡B", false, "boss.png")), out _)!
    );

    var opt = new MergeOptions { AutoRenameResources = true };
    var result = new Merger().Merge(
      template,
      new[] { pkgA, pkgB },
      new[] { new MergeMappingEntry(0, 0, "A"), new MergeMappingEntry(1, 0, "B") },
      opt
    );
    result.IsSuccess.ShouldBeTrue();

    var conflict = result.Conflicts.First(c =>
      c.Kind == MergeConflictKind.Resource && c.Name == "boss.png"
    );
    conflict.SuggestedName.ShouldNotBeNullOrEmpty();
    conflict.SuggestedName!.ShouldContain("_boss.png");
  }

  /// <summary>
  /// 一个同时含资源/对象/符卡三个注入点注释的模板（模拟真实模板）。\n
  /// 顺序：references 文件夹(资源注点) → code 文件夹(对象注点) → Boss(符卡注点)。\n
  /// </summary>
  private const string FullTemplateA =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"File\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"references\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"code\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"shared_boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  /// <summary>
  /// 一个含 顶层资源 + 对象定义 + 一张符卡 的创作者包。\n
  /// </summary>
  private static string RichPackageText() =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"resource\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"images/top_bg.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"code\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Task.TaskDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"shared_task\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"卡A\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "4,{\"$type\":\".Task.TaskWait, \",\"Attributes\":[{\"attrCap\":\"Time\",\"attrInput\":\"30\",\"EditWindow\":\"yield\"}],\"AttributeCount\":1}\n";

  [Test]
  public void Merge_ThreeInjectionPoints_AllPositionedCorrectly()
  {
    // P1 回归：多注入点并存时，对象/资源注入不能被符卡注入导致的索引右移所错位。
    var template = LstgesParser.ParseDocument(FullTemplateA, out _)!;
    var pkg = new CreatorPackageDoc("A", LstgesParser.ParseDocument(RichPackageText(), out _)!);

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();
    result.Warnings.ShouldBeEmpty();

    var nodes = result.Merged!.Nodes;

    // 找到注入后的三处目标：BossSpellCard、TaskDefine、LoadImage
    int spellIdx = IndexOf(nodes, ".Boss.BossSpellCard, ");
    int taskIdx = IndexOf(nodes, ".Task.TaskDefine, ");
    int imgIdx = IndexOf(nodes, ".Graphics.LoadImage, ");

    spellIdx.ShouldBeGreaterThan(-1);
    taskIdx.ShouldBeGreaterThan(-1);
    imgIdx.ShouldBeGreaterThan(-1);

    // 对象注点位于符卡注点之前（code 文件夹在 Boss 之前），
    // 因此 TaskDefine 必须排在所有 BossSpellCard 之前，而非被注入进符卡子树内部。
    taskIdx.ShouldBeLessThan(spellIdx);

    // 三处注入点的根层级都等于其注点注释层级(2)
    nodes[spellIdx].Level.ShouldBe(2);
    nodes[taskIdx].Level.ShouldBe(2);
    nodes[imgIdx].Level.ShouldBe(2);

    // 资源路径折为纯文件名
    nodes[imgIdx].GetAttrAt(0).ShouldBe("top_bg.png");

    // 往返解析保持合法且结构不变
    var serialized = result.Merged.Serialize();
    var reparsed = LstgesParser.ParseDocument(serialized, out var error);
    error.ShouldBeNull();
    reparsed.ShouldNotBeNull();
    reparsed!.Nodes.Count(n => n.Type == ".Boss.BossSpellCard, ").ShouldBe(1);
    reparsed.Nodes.Count(n => n.Type == ".Task.TaskDefine, ").ShouldBe(1);
  }

  private static int IndexOf(
    System.Collections.Generic.IReadOnlyList<LstgesNode> nodes,
    string type
  )
  {
    for (int i = 0; i < nodes.Count; i++)
      if (nodes[i].Type == type)
        return i;
    return -1;
  }

  [Test]
  public void Merge_MissingSpellcardInjectionPoint_ReturnsError()
  {
    var noMarker =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n";
    var template = LstgesParser.ParseDocument(noMarker, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeFalse();
    result.Error.ShouldContain("符卡注入点");
  }

  [Test]
  public void Merge_BadPackageIndex_ReturnsError()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(5, 0, "A") }
    );
    result.IsSuccess.ShouldBeFalse();
    result.Error.ShouldContain("不存在的创作者包");
  }

  [Test]
  public void Merge_BadCardIndex_ReturnsError()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("卡A", false)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 9, "A") }
    );
    result.IsSuccess.ShouldBeFalse();
    result.Error.ShouldContain("不存在的符卡");
  }

  [Test]
  public void Merge_InjectsNonSpellToo()
  {
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgA = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageDoc(SpellCard("", true)), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkgA },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    result.Merged!.Nodes.Any(n => n.Type == ".Boss.BossSpellCard, ").ShouldBeTrue();
  }
}
