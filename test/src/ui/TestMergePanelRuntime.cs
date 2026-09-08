namespace AutoCMEX;

using System;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Merge;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Shouldly;

/// <summary>
/// 阶段3 运行回归测试：实例化真实 MergePanel.tscn，断言四栏结构存在，
/// 且 <see cref="DataManager"/> 数据模型变化由 AutoList/AutoValue 绑定自动驱动对应表 UI 更新。
/// </summary>
public class TestMergePanelRuntime : TestClass
{
  private Node _host = default!;
  private DataManager _dm = default!;

  public TestMergePanelRuntime(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();
    _dm?.Dispose();
  }

  private DataManager CreateDataManager()
  {
    var dm = new DataManager(
      System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"AutoCMEX_MergeTest_{Guid.NewGuid():N}"
      ),
      new AesEncryptor("test-key")
    );
    dm.LoadAll();
    return dm;
  }

  private MergePanel InstantiatePanel()
  {
    _host = new Node();
    TestScene.AddChild(_host);

    var panel = GD.Load<PackedScene>("res://src/ui/merge/MergePanel.tscn")
      .Instantiate<MergePanel>();
    panel.FakeDependency<DataManager>(_dm);
    _host.AddChild(panel);
    return panel;
  }

  [Test]
  public void RealScene_Instantiates_FourPanesAndNodes()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();

    // 四栏结构容器（RootSplit 下左/右 VSplit，再各分上下两 pane）
    panel.FindChild("RootSplit", owned: false).ShouldNotBeNull();
    panel.FindChild("LeftSplit", owned: false).ShouldNotBeNull();
    panel.FindChild("RightSplit", owned: false).ShouldNotBeNull();

    // AutoConnect 已解析全部 [Node]（事件与列表绑定依赖的节点）
    panel.CreatorTitle.ShouldNotBeNull();
    panel.PackageList.ShouldNotBeNull();
    panel.SpellCardList.ShouldNotBeNull();
    panel.ResourceList.ShouldNotBeNull();
    panel.ObjectList.ShouldNotBeNull();
    panel.ImportPackageBtn.ShouldNotBeNull();
    panel.RemovePackageBtn.ShouldNotBeNull();

    panel.TemplatePathEdit.ShouldNotBeNull();
    panel.SharpPathEdit.ShouldNotBeNull();
    panel.PluginDllEdit.ShouldNotBeNull();
    panel.InjectionStatusLabel.ShouldNotBeNull();
    panel.ImportTemplateBtn.ShouldNotBeNull();

    panel.MappingList.ShouldNotBeNull();
    panel.MoveUpBtn.ShouldNotBeNull();
    panel.MoveDownBtn.ShouldNotBeNull();
    panel.ShuffleBtn.ShouldNotBeNull();
    panel.GroupOption.ShouldNotBeNull();

    panel.IncludeLstgesToggle.ShouldNotBeNull();
    panel.ObfuscateLuaToggle.ShouldNotBeNull();
    panel.OutputDirEdit.ShouldNotBeNull();
    panel.ConflictList.ShouldNotBeNull();
    panel.AutoRenameConflictsToggle.ShouldNotBeNull();
    panel.ExportFullPackageBtn.ShouldNotBeNull();
    panel.ExportMappingBtn.ShouldNotBeNull();
  }

  [Test]
  public async Task InventoryLists_Update_FromSelectedPackageIndexBinding()
  {
    _dm = CreateDataManager();

    // 预置一个带清单缓存的创作者包（导入时由 MergeImporter 抽取填充）。
    _dm.CreatorPackages.Add(
      new CreatorPackage
      {
        PackageName = "SamplePkg_A",
        CreatorName = new AutoValue<string>("Alice"),
        SpellCards = new() { "（非符）", "结界「真名的境界」" },
        Resources = new() { "LoadImage: res/boss.png" },
        Objects = new() { "BossDefine: pkg_enm1" },
      }
    );

    var panel = InstantiatePanel();

    // 事件/外部只写模型（指示24）：选中包索引写入模型，绑定驱动三栏刷新。
    _dm.MergeConfig.SelectedPackageIndex.Value = 0;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.SpellCardList.ShouldNotBeNull();
    panel.SpellCardList.ItemCount.ShouldBe(2);
    panel.SpellCardList.GetItemText(0).ShouldBe("（非符）");
    panel.SpellCardList.GetItemText(1).ShouldBe("结界「真名的境界」");

    panel.ResourceList.ShouldNotBeNull();
    panel.ResourceList.ItemCount.ShouldBe(1);
    panel.ResourceList.GetItemText(0).ShouldBe("LoadImage: res/boss.png");

    panel.ObjectList.ShouldNotBeNull();
    panel.ObjectList.ItemCount.ShouldBe(1);
    panel.ObjectList.GetItemText(0).ShouldBe("BossDefine: pkg_enm1");

    // 取消选中（如删包后置回 -1）→ 三栏清空。
    _dm.MergeConfig.SelectedPackageIndex.Value = -1;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.SpellCardList.ItemCount.ShouldBe(0);
    panel.ResourceList.ItemCount.ShouldBe(0);
    panel.ObjectList.ItemCount.ShouldBe(0);
  }

  [Test]
  public async Task RemovePackage_ClearsInventoryAndMappingEntries()
  {
    _dm = CreateDataManager();

    // 预置一个包及其对应表条目（含该包与另一无关包，验证只删本包条目）。
    _dm.CreatorPackages.Add(
      new CreatorPackage
      {
        PackageName = "SamplePkg_A",
        CreatorName = new AutoValue<string>("Alice"),
        SpellCards = new() { "结界「真名的境界」" },
      }
    );
    _dm.CreatorPackages.Add(
      new CreatorPackage { PackageName = "SamplePkg_B", CreatorName = new AutoValue<string>("Bob") }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "结界「真名的境界」",
        Creator = new AutoValue<string>("Alice"),
        PackageName = "SamplePkg_A",
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = string.Empty,
        Creator = new AutoValue<string>("Bob"),
        PackageName = "SamplePkg_B",
      }
    );

    var panel = InstantiatePanel();

    // 选中第一个包（显示序列索引 0）→ 三栏填充。
    _dm.MergeConfig.SelectedPackageIndex.Value = 0;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    panel.SpellCardList.ShouldNotBeNull();
    panel.SpellCardList.ItemCount.ShouldBe(1);

    // 触发删除（OnReady 已把 Pressed 挂到 OnRemovePackage）。通过真实按钮节点的引擎信号触发。
    var button = panel.GetNode<Godot.Button>("%RemovePackageBtn");
    button.EmitSignal(Godot.Button.SignalName.Pressed);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // P1-1：对应表应移除该包条目、保留无关包条目。
    _dm.MergeConfig.Mapping.Count.ShouldBe(1);
    _dm.MergeConfig.Mapping[0].PackageName.ShouldBe("SamplePkg_B");

    // 三栏清空（SelectedPackageIndex 置回 -1 驱动）。
    panel.SpellCardList.ItemCount.ShouldBe(0);
    panel.ResourceList.ShouldNotBeNull();
    panel.ResourceList.ItemCount.ShouldBe(0);
    panel.ObjectList.ShouldNotBeNull();
    panel.ObjectList.ItemCount.ShouldBe(0);
  }

  [Test]
  public async Task MappingList_Updates_FromAutoListBinding()
  {
    _dm = CreateDataManager();

    // 预置映射：1 符卡 + 1 非符
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "神技「xx」",
        Creator = new AutoValue<string>("Alice"),
        IsNonSpell = new AutoValue<bool>(false),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = string.Empty,
        Creator = new AutoValue<string>("Bob"),
        IsNonSpell = new AutoValue<bool>(true),
      }
    );

    var panel = InstantiatePanel();

    // 绑定在 OnResolved 建立，数据已写入模型 → 首次渲染应显示 2 行
    panel.MappingList.ShouldNotBeNull();
    panel.MappingList.ItemCount.ShouldBe(2);

    // 事件处理器只写数据模型（指示24），AutoList 绑定驱动 UI 自动重建
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "弹幕「yy」",
        Creator = new AutoValue<string>("Charlie"),
        IsNonSpell = new AutoValue<bool>(false),
      }
    );
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.MappingList.ItemCount.ShouldBe(3);
  }
}
