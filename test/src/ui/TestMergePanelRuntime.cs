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

  [Test]
  public async Task ConfigPathEdits_WriteBackToModel_OnTextChanged()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();

    // 等待 OnResolved 完成（事件绑定在 Ready 通知建立），确保 TextChanged 处理器已挂上。
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 事件只写模型（指示24）：真实 LineEdit 输入触发 TextChanged → 路径配置写回模型 + 触发保存。
    // 场景内这些编辑框均设 unique_name_in_owner；此处递归按名取真实 LineEdit 节点（与 [Node] 解析到同一节点）。
    // 用 EmitSignal(TextChanged) 显式触发处理器以模拟用户输入，保证测试确定性（与程序化赋值是否触发放一起无关）。
    var templateEdit =
      panel.FindChild("TemplatePathEdit", owned: false, recursive: true) as Godot.LineEdit;
    templateEdit.ShouldNotBeNull();
    templateEdit.Text = "C:/tmp/template/main.lstgproj";
    templateEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "C:/tmp/template/main.lstgproj");
    var sharpEdit =
      panel.FindChild("SharpPathEdit", owned: false, recursive: true) as Godot.LineEdit;
    sharpEdit.ShouldNotBeNull();
    sharpEdit.Text = "C:/Program Files/LuaSTG";
    sharpEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "C:/Program Files/LuaSTG");
    var pluginEdit =
      panel.FindChild("PluginDllEdit", owned: false, recursive: true) as Godot.LineEdit;
    pluginEdit.ShouldNotBeNull();
    pluginEdit.Text = "LuaSTGPlusLib.dll";
    pluginEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "LuaSTGPlusLib.dll");
    var outputEdit =
      panel.FindChild("OutputDirEdit", owned: false, recursive: true) as Godot.LineEdit;
    outputEdit.ShouldNotBeNull();
    outputEdit.Text = "C:/out/mod";
    outputEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "C:/out/mod");
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    _dm.MergeConfig.TemplatePath.Value.ShouldBe("C:/tmp/template/main.lstgproj");
    _dm.MergeConfig.SharpEditorPath.Value.ShouldBe("C:/Program Files/LuaSTG");
    _dm.MergeConfig.PluginDll.Value.ShouldBe("LuaSTGPlusLib.dll");
    _dm.MergeConfig.OutputDir.Value.ShouldBe("C:/out/mod");
  }

  [Test]
  public async Task LoadConfig_Backfills_AllPathEdits()
  {
    _dm = CreateDataManager();

    // 预置持久化配置：重启后应回显到 4 个路径/目录编辑框（LoadConfigToControls）。
    _dm.MergeConfig.TemplatePath.Value = "C:/tmp/template/main.lstgproj";
    _dm.MergeConfig.SharpEditorPath.Value = "C:/Program Files/LuaSTG";
    _dm.MergeConfig.PluginDll.Value = "LuaSTGPlusLib.dll";
    _dm.MergeConfig.OutputDir.Value = "C:/out/mod";

    var panel = InstantiatePanel();

    // 等待 OnResolved 完成（回填在 Ready 通知建立），避免偶发时序读到空串。
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.TemplatePathEdit.Text.ShouldBe("C:/tmp/template/main.lstgproj");
    panel.SharpPathEdit.Text.ShouldBe("C:/Program Files/LuaSTG");
    panel.PluginDllEdit.Text.ShouldBe("LuaSTGPlusLib.dll");
    panel.OutputDirEdit.Text.ShouldBe("C:/out/mod");
  }

  [Test]
  public async Task LoadConfig_Backfill_DoesNotOverwriteModel()
  {
    _dm = CreateDataManager();

    // 预置全部非默认配置（模拟用户上次选择后重启，磁盘上已是这些值）。
    _dm.MergeConfig.IncludeLstges.Value = true;
    _dm.MergeConfig.ObfuscateLua.Value = true;
    _dm.MergeConfig.AutoRenameConflicts.Value = true;
    _dm.MergeConfig.ForcePerformAction.Value = true;
    _dm.MergeConfig.GroupByCreatorFolders.Value = true;
    _dm.MergeConfig.GroupMappingByCreator.Value = true;
    _dm.MergeConfig.Algorithm.Value = MergeAlgorithm.TopFolderCarry;
    _dm.MergeConfig.TemplatePath.Value = "C:/tmp/template/main.lstgproj";
    _dm.MergeConfig.SharpEditorPath.Value = "C:/Program Files/LuaSTG";
    _dm.MergeConfig.PluginDll.Value = "LuaSTGPlusLib.dll";
    _dm.MergeConfig.OutputDir.Value = "C:/out/mod";

    var panel = InstantiatePanel();

    // 等待 OnResolved 完成回填（回填期间不应存在监听者，故不得触发任何写回）。
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 正向：模型 → 控件 回显正确。
    panel.IncludeLstgesToggle.ButtonPressed.ShouldBeTrue();
    panel.ObfuscateLuaToggle.ButtonPressed.ShouldBeTrue();
    panel.AutoRenameConflictsToggle.ButtonPressed.ShouldBeTrue();
    panel.ForcePerformActionToggle.ButtonPressed.ShouldBeTrue();
    panel.GroupByCreatorFoldersToggle.ButtonPressed.ShouldBeTrue();
    panel.GroupOption.ButtonPressed.ShouldBeTrue();
    panel.MergeAlgorithmOption.Selected.ShouldBe((int)MergeAlgorithm.TopFolderCarry);

    // 反向（本用例的核心回归）：回填不得污染模型。
    // 修复前：回填的程序化赋值触发 Toggled → PersistConfig 全量写回，
    // 把尚未回填的控件默认值写进模型并落盘，导致重载后配置丢失。
    _dm.MergeConfig.IncludeLstges.Value.ShouldBeTrue();
    _dm.MergeConfig.ObfuscateLua.Value.ShouldBeTrue();
    _dm.MergeConfig.AutoRenameConflicts.Value.ShouldBeTrue();
    _dm.MergeConfig.ForcePerformAction.Value.ShouldBeTrue();
    _dm.MergeConfig.GroupByCreatorFolders.Value.ShouldBeTrue();
    _dm.MergeConfig.GroupMappingByCreator.Value.ShouldBeTrue();
    _dm.MergeConfig.Algorithm.Value.ShouldBe(MergeAlgorithm.TopFolderCarry);
    _dm.MergeConfig.TemplatePath.Value.ShouldBe("C:/tmp/template/main.lstgproj");
    _dm.MergeConfig.SharpEditorPath.Value.ShouldBe("C:/Program Files/LuaSTG");
    _dm.MergeConfig.PluginDll.Value.ShouldBe("LuaSTGPlusLib.dll");
    _dm.MergeConfig.OutputDir.Value.ShouldBe("C:/out/mod");
  }

  [Test]
  public async Task MappingGroupOption_Toggle_WritesBackToModel()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();

    // 等待 OnResolved 完成（回填在 OnResolved 执行；OnReady 仅填充下拉选项）。
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 初始：模型默认 false → 控件未勾选。
    _dm.MergeConfig.GroupMappingByCreator.Value.ShouldBeFalse();
    panel.GroupOption.ButtonPressed.ShouldBeFalse();

    // 事件只写模型（指示 24）：CheckBox 程序化设 ButtonPressed 会按引擎语义触发 Toggled，
    // 驱动 SyncMappingGroupingToModel 写回模型（等价于一次真实勾选）。
    var node = panel.FindChild("GroupOption", owned: false, recursive: true) as Godot.CheckBox;
    node.ShouldNotBeNull();
    node.ButtonPressed = true;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    _dm.MergeConfig.GroupMappingByCreator.Value.ShouldBeTrue();

    // 取消勾选同样触发写回（保证状态可逆、不残留）。
    node.ButtonPressed = false;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    _dm.MergeConfig.GroupMappingByCreator.Value.ShouldBeFalse();
  }
}
