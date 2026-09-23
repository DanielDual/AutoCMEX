namespace AutoCMEX;

using System.Linq;
using AutoCMEX.Core.Merge;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 第二版 Merger（整文件夹搬运）单元测试：顶层文件夹整棵注入对象注入点、
/// 「模板同名=不整合」（Name 例外强制整合）、符卡行为与第一版一致、父链合法+往返可解析。
/// </summary>
public class TopFolderCarryMergerTest : TestClass
{
  public TopFolderCarryMergerTest(Node testScene)
    : base(testScene) { }

  private const string BaseTemplate =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"File\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"references\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"code\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"shared_boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossInit, \",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  /// <summary>含一个顶层文件夹 + 一张符卡的创作者包（文件夹名可指定）。</summary>
  private static string PackageDoc(string folderName, string taskName, string cardName) =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + folderName
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Task.TaskDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + taskName
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + cardName
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[],\"AttributeCount\":0}\n";

  private static TopFolderCarryMergerResult MergeSingle(string templateText, string pkgText)
  {
    var merger = new TopFolderCarryMerger();
    var pkg = new CreatorPackageDoc("A", LstgesParser.ParseDocument(pkgText, out _)!);
    var template = LstgesParser.ParseDocument(templateText, out _)!;
    var result = merger.Merge(template, new[] { pkg }, new[] { new MergeMappingEntry(0, 0, "A") });
    return new TopFolderCarryMergerResult(result, result.Merged!.Nodes.ToList());
  }

  private readonly record struct TopFolderCarryMergerResult(
    MergeResult Result,
    System.Collections.Generic.List<LstgesNode> Nodes
  );

  [Test]
  public void Merge_InjectsWholeFolderUnderObjectInjectionPoint()
  {
    // 模板无同名 Custom 顶层文件夹 → 整棵注入对象注入点：Folder 与注入点同级、子树在下一层。
    var r = MergeSingle(BaseTemplate, PackageDoc("Custom", "custom_task", "卡A"));
    r.Result.IsSuccess.ShouldBeTrue();

    var folderIdx = r.Nodes.FindIndex(n =>
      n.Type == ".General.Folder, LuaSTGEditorSharp" && n.GetAttr("Name") == "Custom"
    );
    folderIdx.ShouldBeGreaterThan(-1);
    r.Nodes[folderIdx].Level.ShouldBe(2); // 对象注入点注释层级（与注入点同级）

    var taskIdx = r.Nodes.FindIndex(n =>
      n.Type == ".Task.TaskDefine, " && n.GetAttrAt(0) == "custom_task"
    );
    taskIdx.ShouldBeGreaterThan(-1);
    r.Nodes[taskIdx].Level.ShouldBe(3); // 文件夹内容在下一层
    taskIdx.ShouldBeGreaterThan(folderIdx);
    LstgesHierarchy.FindFirstInvalidLevel(r.Nodes).ShouldBe(-1);
  }

  [Test]
  public void Merge_TemplateSameNameFolder_NotCarried()
  {
    // 模板已有同名顶级 Resources 文件夹 → 创作包含的 Resources 不整合（保持模板持有）。
    var templateText =
      BaseTemplate
      + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";
    var r = MergeSingle(templateText, PackageDoc("Resources", "res_task", "卡A"));
    r.Result.IsSuccess.ShouldBeTrue();

    // 产物中顶层 Resources 文件夹只有模板的 1 个（创作者同名未注入）
    var resCount = r.Nodes.Count(n =>
      n.Type == ".General.Folder, LuaSTGEditorSharp" && n.GetAttr("Name") == "Resources"
    );
    resCount.ShouldBe(1);
    // 符卡仍注入（符卡行为不变）
    r.Nodes.Any(n => n.Type == ".Boss.BossSpellCard, ").ShouldBeTrue();
    LstgesHierarchy.FindFirstInvalidLevel(r.Nodes).ShouldBe(-1);
  }

  [Test]
  public void Merge_AlwaysCarryFolderNamedName_EvenIfTemplateHasIt()
  {
    // 例外：模板已有同名 Name 顶层文件夹，创作包含的 Name 也强制整合。
    var templateText =
      BaseTemplate
      + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Name\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";
    var r = MergeSingle(templateText, PackageDoc("Name", "name_task", "卡A"));
    r.Result.IsSuccess.ShouldBeTrue();

    // 模板 1 个 + 创作包注入 1 个 = 2 个 Name 顶层文件夹
    var nameCount = r.Nodes.Count(n =>
      n.Type == ".General.Folder, LuaSTGEditorSharp" && n.GetAttr("Name") == "Name"
    );
    nameCount.ShouldBe(2);
    // 注入的 Name（对象注入点下）含有创作包的任务
    r.Nodes.Any(n => n.Type == ".Task.TaskDefine, " && n.GetAttrAt(0) == "name_task")
      .ShouldBeTrue();
    LstgesHierarchy.FindFirstInvalidLevel(r.Nodes).ShouldBe(-1);
  }

  [Test]
  public void Merge_RoundTrips()
  {
    var r = MergeSingle(BaseTemplate, PackageDoc("Custom", "custom_task", "卡A"));
    r.Result.IsSuccess.ShouldBeTrue();
    var reparsed = LstgesParser.ParseDocument(r.Result.Merged!.Serialize(), out var err);
    err.ShouldBeNull();
    reparsed.ShouldNotBeNull();
    reparsed.Nodes.Count.ShouldBe(r.Result.Merged.Count);
  }
}
