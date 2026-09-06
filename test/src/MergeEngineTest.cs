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
    var doc = LstgesParser.LoadFile(DataPath("first_luastg_project.lstges"), out var error);
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
  public void SpellExtractor_RealSampleCMEX22_ExtractsTwoCards()
  {
    var doc = LstgesParser.LoadFile(DataPath("CMEX22_LY39qd.lstges"), out _);
    doc.ShouldNotBeNull();

    var cards = SpellCardExtractor.Extract(doc!);
    cards.Count.ShouldBe(2);
    // 第一张为空名（非符），第二张为真名「萃符「鬼之黑洞」」
    cards.Any(c => c.IsNonSpell).ShouldBeTrue();
    cards.Any(c => !c.IsNonSpell && c.Name.Contains("黑洞")).ShouldBeTrue();
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
  public void ResourceDetector_RealSampleCMEX22_DetectsResources()
  {
    var doc = LstgesParser.LoadFile(DataPath("CMEX22_LY39qd.lstges"), out _);
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
  public void ObjectDetector_RealSampleCMEX22_DetectsBossDefine()
  {
    var doc = LstgesParser.LoadFile(DataPath("CMEX22_LY39qd.lstges"), out _);
    doc.ShouldNotBeNull();

    var objects = ObjectDetector.Detect(doc!);
    objects.Any(o => o.Type == "BossDefine" && o.Name == "cmex22_enm1").ShouldBeTrue();
  }
}
