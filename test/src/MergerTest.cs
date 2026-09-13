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
  public void Merge_ResourcePath_PreservedInSourceDir()
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
    // 资源保留「源目录中的相对位置」（boss.png 在符卡子树里，路径为 res/boss.png），不折成裸名
    image.GetAttrAt(0).ShouldBe("res/boss.png");
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
    // 自动改名后，两个 boss.png 分别带前缀 A_/B_；目录层次（res/）保持不变
    images.Select(n => n.GetAttrAt(0)).OrderBy(x => x).ShouldContain("res/A_boss.png");
    images.Select(n => n.GetAttrAt(0)).OrderBy(x => x).ShouldContain("res/B_boss.png");
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

    // 顶层 LoadImage 应被注入到资源注入点（在该注释之后），且保留源目录相对路径 images/bg.png
    var images = result.Merged!.Nodes.Where(n => n.Type == ".Graphics.LoadImage, ").ToList();
    images.Count.ShouldBeGreaterThan(0);
    images.Any(n => n.GetAttrAt(0) == "images/bg.png").ShouldBeTrue();
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

    // 资源路径保留「源目录中的相对路径」（不折为裸名）
    nodes[imgIdx].GetAttrAt(0).ShouldBe("images/top_bg.png");

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

  /// <summary>
  /// 一个「自带归档空间 + 资源注入点」的模板（模板拥有 resource/ 归档空间，资源注点在 level 2）。
  /// </summary>
  private const string TemplateWithArchiveSpace =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"resource/\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"shared_boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  /// <summary>
  /// 一个含「顶层资源（归类到给定归档空间）+ 一张符卡」的创作者包。
  /// </summary>
  private static string PackageWithTopResource(string archiveName, string resourcePath) =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + archiveName
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\""
    + resourcePath
    + "\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + SpellCard("卡A", false);

  [Test]
  public void Merge_TopLevelResource_ExcludedWhenArchiveHitsTemplateSet()
  {
    // 包顶层资源落在模板已有的归档空间 resource/ 下（模板自带、创作者不改动）→ 不检测/不导入。
    var template = LstgesParser.ParseDocument(TemplateWithArchiveSpace, out _)!;
    var pkg = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageWithTopResource("resource/", "boss/my.png"), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 该资源被排除：合并工程不应出现其 LoadImage，且模板的归档空间不动。
    var images = result.Merged!.Nodes.Where(n => n.Type == ".Graphics.LoadImage, ").ToList();
    images.Any(i => i.GetAttrAt(0) == "boss/my.png").ShouldBeFalse();
  }

  [Test]
  public void Merge_TopLevelResource_ExcludedByConfigList()
  {
    // 归档空间不在模板，但命中配置排除清单 → 同样排除。
    var template = LstgesParser.ParseDocument(TemplateWithArchiveSpace, out _)!;
    var pkg = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageWithTopResource("common/", "theme/bg.png"), out _)!
    );

    var opt = new MergeOptions { ExcludedArchiveSpaces = new[] { "common/" } };
    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") },
      opt
    );
    result.IsSuccess.ShouldBeTrue();

    var images = result.Merged!.Nodes.Where(n => n.Type == ".Graphics.LoadImage, ").ToList();
    images.Any(i => i.GetAttrAt(0) == "theme/bg.png").ShouldBeFalse();
    // 排除时归档空间也不搬迁
    result
      .Merged!.Nodes.Any(n =>
        n.Type == ".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp"
        && n.GetAttr("Name") == "common/"
      )
      .ShouldBeFalse();
  }

  [Test]
  public void Merge_TopLevelResource_CarriesArchiveSpaceVerbatim_WhenNotExcluded()
  {
    // 归档空间不是模板已有、也不在配置清单 → 资源连同其归档空间基准节点原封不动搬迁并注入。
    var template = LstgesParser.ParseDocument(TemplateWithArchiveSpace, out _)!;
    var pkg = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(PackageWithTopResource("custom/", "boss/my.png"), out _)!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 归档空间节点被原样注入（attrInput="custom/" 一字不差），且资源被注入（保留源目录相对路径 boss/my.png）。
    var archiveNode = result.Merged!.Nodes.FirstOrDefault(n =>
      n.Type == ".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp"
      && n.GetAttr("Name") == "custom/"
    );
    archiveNode.ShouldNotBeNull();
    // 归档空间节点落在「资源注入点」同级（marker 于 level2），而非被抬到其上层（否则基准错乱）
    archiveNode.Level.ShouldBe(2);
    var images = result.Merged!.Nodes.Where(n => n.Type == ".Graphics.LoadImage, ").ToList();
    var injectedImage = images.First(i => i.GetAttrAt(0) == "boss/my.png");
    injectedImage.Level.ShouldBeGreaterThan(archiveNode.Level);

    // 往返解析合法，结构保持
    var reparsed = LstgesParser.ParseDocument(result.Merged!.Serialize(), out var error);
    error.ShouldBeNull();
    reparsed.ShouldNotBeNull();
  }

  [Test]
  public void Merge_PatchScript_TreatedAsTopLevelResource_InjectedWithArchive()
  {
    // Patch 的作用是「导入脚本」（attrInput=脚本相对路径），必须与图片/音频资源同等对待：
    // 未排除时连同其归档空间原样搬迁并注入资源注入点，脚本相对路径保留（不打成纯文件名），
    // Sharp 打包才能把脚本收进压缩包（否则导出包缺脚本，运行时无法加载）。
    var template = LstgesParser.ParseDocument(TemplateWithArchiveSpace, out _)!;
    var pkg = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(
        PackageWithPatch("custom/", "resource\\\\bg\\\\samp_bg.lua"),
        out _
      )!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    // Patch 被注入，脚本相对路径一字不差保留。
    var patch = result.Merged!.Nodes.FirstOrDefault(n =>
      n.Type == ".General.Patch, LuaSTGEditorSharp"
    );
    patch.ShouldNotBeNull();
    patch!.GetAttrAt(0).ShouldBe("resource\\bg\\samp_bg.lua");

    // 归档空间落资源注入点同级(marker level2)，Patch 在其下（保持源包内相对层级）。
    var archive = result.Merged!.Nodes.FirstOrDefault(n =>
      n.Type == ".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp"
    );
    archive.ShouldNotBeNull();
    archive!.Level.ShouldBe(2);
    patch.Level.ShouldBeGreaterThan(archive.Level);

    // 往返解析合法
    var reparsed = LstgesParser.ParseDocument(result.Merged.Serialize(), out var error);
    error.ShouldBeNull();
    reparsed.ShouldNotBeNull();
  }

  private static string PackageWithPatch(string archiveName, string scriptPath) =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + archiveName
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".General.Patch, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\""
    + scriptPath
    + "\",\"EditWindow\":\"luaFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + SpellCard("卡A", false);

  private static string PackageWithPatchSibling(string archiveName, string scriptPath) =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + archiveName
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Patch, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\""
    + scriptPath
    + "\",\"EditWindow\":\"luaFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + SpellCard("卡A", false);

  [Test]
  public void Merge_PatchSiblingOfArchive_StillInjectedWithArchive()
  {
    // 真机形态：脚本 Patch 与 sample_exp\ 归档「同级、且在归档之后」。ArchiveSpace 是流式作用域
    // （其后所有节点无论层级都归属它，直到下一个 ArchiveSpace），故脚本必须连同归档注入、脚本相对路径保留；
    // 绝不能因「层级配对」漏掉脚本（否则导出包缺脚本，运行时无法加载）。
    var template = LstgesParser.ParseDocument(TemplateWithArchiveSpace, out _)!;
    var pkg = new CreatorPackageDoc(
      "A",
      LstgesParser.ParseDocument(
        PackageWithPatchSibling("sample_exp/", "sample_exp\\\\sample_scripts.lua"),
        out _
      )!
    );

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    var patch = result.Merged!.Nodes.FirstOrDefault(n =>
      n.Type == ".General.Patch, LuaSTGEditorSharp"
    );
    patch.ShouldNotBeNull();
    patch!.GetAttrAt(0).ShouldBe("sample_exp\\sample_scripts.lua"); // 脚本路径保留
    var archive = result.Merged!.Nodes.FirstOrDefault(n =>
      n.Type == ".Advanced.ArchiveSpaceIndicator, LuaSTGEditorSharp"
    );
    archive.ShouldNotBeNull();
    archive!.Level.ShouldBe(2); // 归档落资源注入点同级
    // 流式：脚本在归档之后，注入时归档节点应排在脚本之前（脚本随归档一并注入）
    var mergedNodes = result.Merged.Nodes.ToList();
    mergedNodes.IndexOf(archive!).ShouldBeLessThan(mergedNodes.IndexOf(patch!));

    var reparsed = LstgesParser.ParseDocument(result.Merged.Serialize(), out var error);
    error.ShouldBeNull();
    reparsed.ShouldNotBeNull();
  }

  [Test]
  public void Merge_StandaloneDefinitions_InjectedIntoObjectMarker()
  {
    // 独立定义/代码节点（敌人类型/弯折激光/Boss背景/渲染目标/函数/自定义节点，未命中任何被注入子树、
    // 非 BossDefine、非 Stage）应由统一采集器打上「定义」标签注入对象注入点，确保合并产物
    // 不缺运行时代码（类/函数/背景），避免运行时缺席报错。
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgText =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Enemy.EnemyDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"my_enemy\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "1,{\"$type\":\".Laser.BentLaserDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"my_blaser\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "1,{\"$type\":\".Boss.BossBGDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"my_scbg\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "1,{\"$type\":\".Render.RenderTarget, \",\"Attributes\":[{\"attrCap\":\"Operation\",\"attrInput\":\"Push\",\"EditWindow\":\"renderOp\"},{\"attrCap\":\"Name\",\"attrInput\":\"\\\"\\\"\"}],\"AttributeCount\":2}\n"
      + "1,{\"$type\":\".Render.CreateRenderTarget, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"my_target\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "1,{\"$type\":\".Data.Function, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"my_util\",\"EditWindow\":\"\"},{\"attrCap\":\"Parameter List\",\"attrInput\":\"\",\"EditWindow\":\"\"},{\"attrCap\":\"Localized\",\"attrInput\":\"false\",\"EditWindow\":\"bool\"}],\"AttributeCount\":3}\n"
      + "1,{\"$type\":\".Advanced.UnidentifiedNode, LuaSTGEditorSharp\",\"Attributes\":[{\"$type\":\".DependencyAttrItem, \",\"attrCap\":\"Type\",\"attrInput\":\"my_custom\",\"EditWindow\":\"userDefinedNodeDefinition\"},{\"attrCap\":\"tag\",\"attrInput\":\"normal\"}],\"AttributeCount\":2}\n"
      + SpellCard("卡A", false);
    var pkg = new CreatorPackageDoc("A", LstgesParser.ParseDocument(pkgText, out _)!);

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    var nodes = result.Merged!.Nodes;
    nodes.Any(n => n.Type == ".Enemy.EnemyDefine, ").ShouldBeTrue();
    nodes.Any(n => n.Type == ".Laser.BentLaserDefine, ").ShouldBeTrue();
    nodes.Any(n => n.Type == ".Boss.BossBGDefine, ").ShouldBeTrue();
    nodes.Any(n => n.Type == ".Render.RenderTarget, ").ShouldBeTrue();
    nodes.Any(n => n.Type == ".Render.CreateRenderTarget, ").ShouldBeTrue();
    nodes.Any(n => n.Type == ".Data.Function, ").ShouldBeTrue();
    nodes.Any(n => n.Type == ".Advanced.UnidentifiedNode, LuaSTGEditorSharp").ShouldBeTrue();

    // 定义注入在对象注入点上（模板中该注释位于 level 3，其子树根重编号到注入点层级）。
    var enemy = nodes.First(n => n.Type == ".Enemy.EnemyDefine, ");
    enemy.Level.ShouldBe(3);
    var func = nodes.First(n => n.Type == ".Data.Function, ");
    func.Level.ShouldBe(3);
  }

  [Test]
  public void Merge_BossDefineChildren_NotStandaloneInjectedWhenCovered()
  {
    // 回归：BossDefine 及其子树（BossInit/Dialog/BossSpellCard 等）由模板共享，绝不作为独立定义注入，
    // 其内部的字面代码/资源随符卡子树覆盖去重逻辑一并移植（不双份）。
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgText =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + SpellCard("卡A", false);
    var pkg = new CreatorPackageDoc("A", LstgesParser.ParseDocument(pkgText, out _)!);

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 模板已有一个 shared_boss；包内新 BossDefine 不应被当作定义再注入对象注入点（仅符卡注点注入符卡）。
    var bossDefs = result.Merged!.Nodes.Where(n => n.Type == ".Boss.BossDefine, ").ToList();
    bossDefs.Count.ShouldBe(1); // 只有一个（模板的），包内 BossDefine 不重复注入
  }

  [Test]
  public void Merge_NestedDefinitionInsideInjectedSubtree_NotReInjected()
  {
    // 覆盖去重核心：位于已注入子树内部的嵌套定义（如对象子树内的 TaskDefine）不单独注入对象注入点，
    // 只随所属被注入子树携带移植一次，避免双份注入（否则产物出现两个同名/重复的任务定义）。
    var template = LstgesParser.ParseDocument(TemplateText, out _)!;
    var pkgText =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Object.ObjectDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"nested_obj\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + "2,{\"$type\":\".Task.TaskDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"nested_task\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
      + SpellCard("卡A", false);
    var pkg = new CreatorPackageDoc("A", LstgesParser.ParseDocument(pkgText, out _)!);

    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();

    // 嵌套 TaskDefine 只出现一次（随 ObjectDefine 子树带出），不作独立定义重复注入。
    var tasks = result.Merged!.Nodes.Where(n => n.Type == ".Task.TaskDefine, ").ToList();
    tasks.Count.ShouldBe(1);
  }

  [Test]
  public void HierarchyFindFirstInvalid_ValidSequence_ReturnsMinusOne()
  {
    // 父链完整、每行回落不超过现有父链深度的序列 → 无越界行。
    var text =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n";
    var doc = LstgesParser.ParseDocument(text, out _)!;

    LstgesHierarchy.FindFirstInvalidLevel(doc.Nodes).ShouldBe(-1);
  }

  [Test]
  public void HierarchyFindFirstInvalid_OverflowFallback_ReturnsIndex()
  {
    // 产物 NRE 的触发模式：深层节点(6)被直接挂在 root 之下（父链仅 2 层），随后立刻回落 level3，
    // 上溯步数(4) 超过父链深度(1) → Sharp 会把 prev 越过 root 变 null，AddChild 抛 NRE。
    var text =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "6,{\"$type\":\".Object.Del, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "3,{\"$type\":\".Object.ObjectDefine, \",\"Attributes\":[],\"AttributeCount\":0}\n";
    var doc = LstgesParser.ParseDocument(text, out _)!;

    LstgesHierarchy.FindFirstInvalidLevel(doc.Nodes).ShouldBe(2);
  }

  [Test]
  public void Hierarchy_ReturnToOverflowAfterJump_IsDetectedAndNormalized()
  {
    // 审查反例：深层节点(4)经「跳层到达」后再浅层回落(2)，父链回溯越界——Sharp 会 NRE。
    // 修正前 Find 漏报 → 修正后应检测到，且 Normalize 后无越界、幂等。
    var text =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "4,{\"$type\":\".Object.ObjectInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "3,{\"$type\":\".Object.ObjectInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "2,{\"$type\":\".Object.Del, \",\"Attributes\":[],\"AttributeCount\":0}\n";
    var doc = LstgesParser.ParseDocument(text, out _)!;

    LstgesHierarchy.FindFirstInvalidLevel(doc.Nodes).ShouldBe(4);
    LstgesHierarchy.NormalizeParentChain(doc.Nodes).ShouldBeTrue();
    LstgesHierarchy.FindFirstInvalidLevel(doc.Nodes).ShouldBe(-1);
    LstgesHierarchy.NormalizeParentChain(doc.Nodes).ShouldBeFalse(); // 幂等
  }

  [Test]
  public void Merge_RichPackage_ProducesParentChainValidAndRoundTrips()
  {
    // 富包（顶层资源+对象+符卡）合并：父链对齐保证无越界行（Sharp 可安全重建），且往返可解析。
    var template = LstgesParser.ParseDocument(FullTemplateA, out _)!;
    var pkg = new CreatorPackageDoc("A", LstgesParser.ParseDocument(RichPackageText(), out _)!);
    var result = new Merger().Merge(
      template,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 0, "A") }
    );
    result.IsSuccess.ShouldBeTrue();
    LstgesHierarchy.FindFirstInvalidLevel(result.Merged!.Nodes).ShouldBe(-1);

    var reparsed = LstgesParser.ParseDocument(result.Merged.Serialize(), out var error);
    error.ShouldBeNull();
    reparsed.ShouldNotBeNull();
  }
}
