namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Merge;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 阶段1 合并引擎单元测试：Lstges 解析、注入点/符卡/资源/Object 检测。
/// </summary>
public class MergeEngineTest : TestClass
{
  private const string DataDir = "test/src/merge/data";

  public MergeEngineTest(Node testScene)
    : base(testScene) { }

  /// <summary>
  /// 一个自建的最小模板（含三个注入点注释），用于注入点检测断言。
  /// </summary>
  private const string TemplateText =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"File\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Code\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  /// <summary>
  /// 一个自建的最小创作者包（含 BossDefine + 三个 BossSpellCard：真名/空名/[]），用于符卡口径断言。
  /// </summary>
  private const string PackageText =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"File\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm1\",\"EditWindow\":\"\"},{\"attrCap\":\"Displayed name\",\"attrInput\":\"Alice's Boss\",\"EditWindow\":\"\"}],\"AttributeCount\":2}\n"
    + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"结界「真名的境界」\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n"
    + "4,{\"$type\":\".Task.TaskWait, \",\"Attributes\":[{\"attrCap\":\"Time\",\"attrInput\":\"60\",\"EditWindow\":\"yield\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n"
    + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"[]\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[]}\n";

  private string DataPath(string file) => System.IO.Path.Combine(DataDir, file);

  // ==================== LstgesParser Tests ====================

  [Test]
  public void Parser_ValidText_ParsesNodesWithLevelAndType()
  {
    var doc = LstgesParser.ParseDocument(PackageText, out var error);
    error.ShouldBeNull();
    doc.ShouldNotBeNull();
    doc!.Count.ShouldBe(9);
    doc.Nodes[0].Level.ShouldBe(0);
    doc.Nodes[0].Type.ShouldBe(".RootFolder, LuaSTGEditorSharp");
    doc.Nodes[1].Type.ShouldBe(".Boss.BossDefine, ");
  }

  [Test]
  public void Parser_RoundTrip_ReparsesEqual()
  {
    var doc = LstgesParser.ParseDocument(PackageText, out var error);
    doc.ShouldNotBeNull();

    var serialized = doc!.Serialize();
    var doc2 = LstgesParser.ParseDocument(serialized, out var error2);
    error2.ShouldBeNull();
    doc2!.Count.ShouldBe(doc.Count);
    doc2.Nodes[3].Type.ShouldBe(".Boss.BossSCStart, ");
  }

  [Test]
  public void Parser_RealSampleFirstProject_Parses()
  {
    var doc = LstgesParser.LoadFile(DataPath("sample_project.lstges"), out var error);
    error.ShouldBeNull();
    doc.ShouldNotBeNull();
    doc!.Count.ShouldBeGreaterThan(100);
    doc.Nodes[0].Type.ShouldBe(".RootFolder, LuaSTGEditorSharp");
  }

  [Test]
  public void Parser_Empty_ReturnsError()
  {
    var doc = LstgesParser.ParseDocument("", out var error);
    doc.ShouldBeNull();
    error.ShouldNotBeNull();
  }

  [Test]
  public void Parser_MissingComma_ReturnsError()
  {
    var doc = LstgesParser.ParseDocument("just-a-line-without-comma\n", out var error);
    doc.ShouldBeNull();
    error.ShouldNotBeNull();
    error.ShouldContain("缺少层级逗号");
  }

  [Test]
  public void Parser_InvalidLevel_ReturnsError()
  {
    var doc = LstgesParser.ParseDocument("x,{\"$type\":\"T\"}\n", out var error);
    doc.ShouldBeNull();
    error.ShouldNotBeNull();
    error.ShouldContain("层级不是数字");
  }

  [Test]
  public void Parser_InvalidJson_ReturnsError()
  {
    var doc = LstgesParser.ParseDocument("1,{invalid json}\n", out var error);
    doc.ShouldBeNull();
    error.ShouldNotBeNull();
    error.ShouldContain("JSON 解析失败");
  }

  // ==================== InjectionPointDetector Tests ====================

  [Test]
  public void InjectionDetector_DetectsThreeMarkers()
  {
    var doc = LstgesParser.ParseDocument(TemplateText, out var error);
    doc.ShouldNotBeNull();

    var points = new InjectionPointDetector().Detect(doc!);
    points.Markers.Count.ShouldBe(3);
    points.Find(InjectionPointKind.SpellCards).ShouldNotBeNull();
    points.Find(InjectionPointKind.Resources).ShouldNotBeNull();
    points.Find(InjectionPointKind.Objects).ShouldNotBeNull();
  }

  [Test]
  public void InjectionDetector_NoMarker_ReturnsEmpty()
  {
    var doc = LstgesParser.ParseDocument(PackageText, out _);
    doc.ShouldNotBeNull();

    var points = new InjectionPointDetector().Detect(doc!);
    points.Markers.ShouldBeEmpty();
    points.Find(InjectionPointKind.SpellCards).ShouldBeNull();
  }

  [Test]
  public void InjectionDetector_BannedMarker_Skipped()
  {
    var text =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1,\"IsBanned\":true}\n";
    var doc = LstgesParser.ParseDocument(text, out _);
    doc.ShouldNotBeNull();

    var points = new InjectionPointDetector().Detect(doc!);
    points.Find(InjectionPointKind.SpellCards).ShouldBeNull();
  }

  // ==================== SpellCardExtractor Tests ====================

  [Test]
  public void SpellExtractor_ClassifiesSpellcardAndNonSpells()
  {
    var doc = LstgesParser.ParseDocument(PackageText, out _);
    doc.ShouldNotBeNull();

    var cards = SpellCardExtractor.Extract(doc!);
    cards.Count.ShouldBe(3);
    cards[0].IsNonSpell.ShouldBeFalse();
    cards[0].Name.ShouldBe("结界「真名的境界」");
    cards[0].Subtree.Count.ShouldBe(3); // BossSpellCard + SCStart + TaskWait
    cards[1].IsNonSpell.ShouldBeTrue();
    cards[1].Subtree.Count.ShouldBe(2);
    cards[2].IsNonSpell.ShouldBeTrue();
    cards[2].Name.ShouldBe("[]");
  }

  [Test]
  public void SpellExtractor_RealSampleSamplePkg_ExtractsTwoCards()
  {
    var doc = LstgesParser.LoadFile(DataPath("sample_rich_package.lstges"), out _);
    doc.ShouldNotBeNull();

    var cards = SpellCardExtractor.Extract(doc!);
    cards.Count.ShouldBe(2);
    // 第一张为空名（非符，Performing action=false），第二张为真名「Spellcard 1」（Performing action=true）。
    cards.Any(c => c.IsNonSpell).ShouldBeTrue();
    cards.Any(c => !c.IsNonSpell && c.Name.Contains("Spellcard")).ShouldBeTrue();
    // 真名卡（sample_rich_package L315）Performing action=true，应收集前序 Dialog（L313）为 LeadingNodes。
    var perfCard = cards.First(c => !c.IsNonSpell && c.Name.Contains("Spellcard"));
    perfCard.HasPerformingAction.ShouldBeTrue();
    perfCard.LeadingNodes.Count.ShouldBeGreaterThan(0);
    perfCard.LeadingNodes.Any(n => n.Type == ".Boss.Dialog, ").ShouldBeTrue();
  }

  // ==================== ResourceDetector Tests ====================

  [Test]
  public void ResourceDetector_LoadImage_Detected()
  {
    var text =
      "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
      + "1,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"res/boss.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n";
    var doc = LstgesParser.ParseDocument(text, out _);
    doc.ShouldNotBeNull();

    var resources = ResourceDetector.Detect(doc!);
    resources.Count.ShouldBe(1);
    resources[0].Type.ShouldBe("LoadImage");
    resources[0].Path.ShouldBe("res/boss.png");
  }

  [Test]
  public void ResourceDetector_RealSampleSamplePkg_DetectsResources()
  {
    var doc = LstgesParser.LoadFile(DataPath("sample_rich_package.lstges"), out _);
    doc.ShouldNotBeNull();

    var resources = ResourceDetector.Detect(doc!);
    resources.Count.ShouldBeGreaterThan(0);
  }

  // ==================== ObjectDetector Tests ====================

  [Test]
  public void ObjectDetector_BossDefine_Detected()
  {
    var doc = LstgesParser.ParseDocument(PackageText, out _);
    doc.ShouldNotBeNull();

    var objects = ObjectDetector.Detect(doc!);
    objects.Count.ShouldBe(1);
    objects[0].Type.ShouldBe("BossDefine");
    objects[0].Name.ShouldBe("pkg_enm1");
  }

  [Test]
  public void ObjectDetector_RealSampleSamplePkg_DetectsBossDefine()
  {
    var doc = LstgesParser.LoadFile(DataPath("sample_rich_package.lstges"), out _);
    doc.ShouldNotBeNull();

    var objects = ObjectDetector.Detect(doc!);
    objects.Any(o => o.Type == "BossDefine" && o.Name == "test_enm1").ShouldBeTrue();
  }

  [Test]
  public void Merge_RealRichPackage_PerformingActionTrueCardCarriesLeadingDialog_ValidParentChainAndRoundTrips()
  {
    // 真实数据往返：sample_rich_package 合并到模板，断言 Performing action=true 的卡（L315 "Spellcard 1"）
    // 携带前序 Dialog（L313），注入后 Dialog 位于卡前、父链合法、产物往返可解析。
    var template = LstgesParser.ParseDocument(TemplateText, out var tErr);
    tErr.ShouldBeNull();
    template.ShouldNotBeNull();

    var pkg = new CreatorPackageDoc(
      "A",
      LstgesParser.LoadFile(DataPath("sample_rich_package.lstges"), out var pErr)!
    );
    pErr.ShouldBeNull();

    var result = new Merger().Merge(
      template!,
      new[] { pkg },
      new[] { new MergeMappingEntry(0, 1, "A") } // 选第二张（真名卡 "Spellcard 1"，Performing action=true）
    );
    result.IsSuccess.ShouldBeTrue();

    var nodes = result.Merged!.Nodes.ToList();
    // 注入后：Performing action 卡的卡节点存在，且其前序 Dialog 也注入（位于卡前）
    var cardNodes = nodes.Where(n => n.Type == ".Boss.BossSpellCard, ").ToList();
    cardNodes.Count.ShouldBeGreaterThan(0);
    // 找到真名卡（Performing action=true）的位置
    var perfNode = cardNodes.FirstOrDefault(n => n.GetAttrAt(0)?.Contains("Spellcard") == true);
    perfNode.ShouldNotBeNull();
    // 注入点之后应有前序 Dialog，且位于该卡之前
    var nodesAfterSpellMarker = nodes.ToList();
    var markerIdx = nodesAfterSpellMarker.FindIndex(n =>
      n.Type == ".General.Comment, LuaSTGEditorSharp"
      && n.GetAttr("Comment") == "Insert spellcards here"
    );
    markerIdx.ShouldBeGreaterThan(-1);
    var injected = nodesAfterSpellMarker.Skip(markerIdx + 1).ToList();
    // 前序 Dialog 应存在，且在卡之前
    var dialogInInjected = injected.Any(n => n.Type == ".Boss.Dialog, ");
    dialogInInjected.ShouldBeTrue();
    var perfCardInInjected = injected.First(n =>
      n.Type == ".Boss.BossSpellCard, " && n.GetAttrAt(0)?.Contains("Spellcard") == true
    );
    var perfIdxInInjected = injected.IndexOf(perfCardInInjected);
    // 存在至少一个 Dialog 位于真名卡之前（紧邻的前序阶段）
    injected.Take(perfIdxInInjected).Any(n => n.Type == ".Boss.Dialog, ").ShouldBeTrue();

    // 父链合法 + 往返可解析
    LstgesHierarchy.FindFirstInvalidLevel(result.Merged.Nodes).ShouldBe(-1);
    var reparsed = LstgesParser.ParseDocument(result.Merged.Serialize(), out var rErr);
    rErr.ShouldBeNull();
    reparsed.ShouldNotBeNull();
  }

  [Test]
  public void Merge_RealRichPackage_GroupByCreatorFolders_CreatesFolderAndRoundTrips()
  {
    // 需求2真实数据往返：sample_rich_package 合并到模板，开关开启 + creatorNames。
    // 断言注入产生按创作者命名的 .General.Folder（Name=Alice）、父链合法且往返可解析。
    var template = LstgesParser.ParseDocument(TemplateText, out var tErr);
    tErr.ShouldBeNull();
    template.ShouldNotBeNull();

    var pkg = new CreatorPackageDoc(
      "A",
      LstgesParser.LoadFile(DataPath("sample_rich_package.lstges"), out var pErr)!
    );
    pErr.ShouldBeNull();

    var result = new Merger().Merge(
      template!,
      new[] { pkg },
      System.Array.Empty<MergeMappingEntry>(),
      new MergeOptions { GroupByCreatorFolders = true },
      new[] { "Alice" }
    );
    result.IsSuccess.ShouldBeTrue();

    var nodes = result.Merged!.Nodes.ToList();
    nodes
      .Any(n => n.Type == ".General.Folder, LuaSTGEditorSharp" && n.GetAttr("Name") == "Alice")
      .ShouldBeTrue();
    LstgesHierarchy.FindFirstInvalidLevel(nodes).ShouldBe(-1);

    var reparsed = LstgesParser.ParseDocument(result.Merged.Serialize(), out var rErr);
    rErr.ShouldBeNull();
    reparsed.ShouldNotBeNull();
  }
}
