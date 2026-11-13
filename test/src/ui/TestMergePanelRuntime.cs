namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Merge;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI;
using AutoCMEX.UI.Merge;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Shouldly;

/// <summary>
/// 运行回归测试：实例化真实 MergePanel.tscn，断言四栏结构存在，
/// 且 <see cref="DataManager"/> 数据模型变化由 AutoList/AutoValue 绑定自动驱动对应表 UI 更新。
/// </summary>
public class TestMergePanelRuntime : TestClass
{
  private Node _host = default!;
  private DataManager _dm = default!;

  /// <summary>导出夹具临时目录（<see cref="Cleanup"/> 统一清理；未使用夹具时为空串）。</summary>
  private string _fixtureDir = string.Empty;

  public TestMergePanelRuntime(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();
    _dm?.Dispose();
    if (!string.IsNullOrEmpty(_fixtureDir) && Directory.Exists(_fixtureDir))
    {
      try
      {
        Directory.Delete(_fixtureDir, true);
      }
      catch
      { /* 清理失败不影响断言 */
      }
    }
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

  private static CreatorPackage Package(string packageName, string creatorName, string card) =>
    new()
    {
      PackageName = packageName,
      CreatorName = new AutoValue<string>(creatorName),
      SpellCards = new() { card },
    };

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
    panel.ImportPackageDialog.ShouldNotBeNull();
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
    panel.MappingModeOption.ShouldNotBeNull();

    panel.IncludeLstgesToggle.ShouldNotBeNull();
    panel.ObfuscateLuaToggle.ShouldNotBeNull();
    panel.OutputDirEdit.ShouldNotBeNull();
    panel.ConflictList.ShouldNotBeNull();
    panel.AutoRenameConflictsToggle.ShouldNotBeNull();
    panel.ExportFullPackageBtn.ShouldNotBeNull();
    panel.ExportMappingBtn.ShouldNotBeNull();
  }

  /// <summary>
  /// 分割条布局记忆：预置 <c>merge.root</c> 记录后实例化真实场景，根分割条采用记忆 offset；
  /// 拖拽信号把新 offset 写回模型（至少覆盖一个 key，其余 key 由单元测试
  /// <see cref="SplitLayoutLinkTest"/> 覆盖）。
  /// </summary>
  [Test]
  public void RealScene_AppliesRememberedSplitOffsetAndWritesBackOnDrag()
  {
    _dm = CreateDataManager();
    _dm.UiLayout.Splits.Add(
      new SplitLayoutRecord { Key = new(UiLayoutKeys.MergeRoot), Offset = new(420) }
    );

    var panel = InstantiatePanel();

    var rootSplit = (HSplitContainer)panel.FindChild("RootSplit", owned: false);
    rootSplit.ShouldNotBeNull();

    var adaptedRoot = GodotInterfaces.Adapt<ISplitContainer>(rootSplit);
    adaptedRoot.SplitOffset.ShouldBe(420);

    // 拖拽信号里的 offset 是引擎给出的新位置：接线器据此更新内存模型
    rootSplit.EmitSignal(SplitContainer.SignalName.Dragged, 500);

    _dm.UiLayout.Splits.Single(record => record.Key.Value == UiLayoutKeys.MergeRoot)
      .Offset.Value.ShouldBe(500);
  }

  /// <summary>
  /// 装配契约：导入创作者包的文件选择框是 <c>MergePanel.tscn</c> 的场景节点，
  /// 模式/过滤按场景声明；节点被释放后再次点击导入由 IsInstanceValid 双检兜住。
  /// </summary>
  [Test]
  public void RealScene_HasSceneImportPackageDialog_AndGuardsFreedNode()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();

    panel.ImportPackageDialog.ShouldNotBeNull("导入对话框应是场景节点");

    // 注入的是接口适配器：取底层 Godot 对象核对模式/过滤与旧实现一致
    var node = (FileDialog)panel.ImportPackageDialog.GetTargetObj<GodotObject>();
    node.ShouldNotBeNull();
    node.GetParent().ShouldBe(panel, "对话框应是 MergePanel.tscn 的直接子节点");
    node.FileMode.ShouldBe(FileDialog.FileModeEnum.OpenFile);
    node.Access.ShouldBe(FileDialog.AccessEnum.Filesystem);
    node.UseNativeDialog.ShouldBeFalse();
    node.Filters.Length.ShouldBe(1);
    node.Filters[0].ShouldBe("*.zip ; 创作者包");

    // 双检：场景节点被释放后再点导入不得对失效节点弹窗，也不得抛异常（面板仍在树内、仍可用）
    node.Free();
    panel.GetNode<Button>("%ImportPackageBtn").EmitSignal(BaseButton.SignalName.Pressed);

    panel.IsInsideTree().ShouldBeTrue();
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
        SpellCards = new() { "（非符）", "结界「样本境界」" },
        Resources = new() { "LoadImage: res/boss.png" },
        Objects = new() { "BossDefine: pkg_enm1" },
      }
    );

    var panel = InstantiatePanel();

    // 事件/外部只写模型：选中包索引写入模型，绑定驱动三栏刷新。
    _dm.MergeConfig.SelectedPackageIndex.Value = 0;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.SpellCardList.ShouldNotBeNull();
    panel.SpellCardList.ItemCount.ShouldBe(2);
    panel.SpellCardList.GetItemText(0).ShouldBe("（非符）");
    panel.SpellCardList.GetItemText(1).ShouldBe("结界「样本境界」");

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
        SpellCards = new() { "结界「样本境界」" },
      }
    );
    _dm.CreatorPackages.Add(
      new CreatorPackage { PackageName = "SamplePkg_B", CreatorName = new AutoValue<string>("Bob") }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "结界「样本境界」",
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

    // 对应表应移除该包条目、保留无关包条目。
    _dm.MergeConfig.Mapping.Count.ShouldBe(1);
    _dm.MergeConfig.Mapping[0].PackageName.ShouldBe("SamplePkg_B");

    // 三栏清空（SelectedPackageIndex 置回 -1 驱动）。
    panel.SpellCardList.ItemCount.ShouldBe(0);
    panel.ResourceList.ShouldNotBeNull();
    panel.ResourceList.ItemCount.ShouldBe(0);
    panel.ObjectList.ShouldNotBeNull();
    panel.ObjectList.ItemCount.ShouldBe(0);

    // 包列表经 IsDeleted 元素级绑定重建：被删包被过滤掉（不再可选中/导出）。
    panel.PackageList.ItemCount.ShouldBe(1);
    panel.PackageList.GetItemText(0).ShouldBe("Bob（SamplePkg_B）");
  }

  /// <summary>
  /// 回归：删包只改包内 <see cref="CreatorPackage.IsDeleted"/>（元素级 AutoValue），列表本身没有增删；
  /// 包列表必须由每包的 IsDeleted 绑定驱动重建，不能依赖删包处理器里的手工刷新。
  /// </summary>
  [Test]
  public async Task PackageList_Updates_FromIsDeletedElementBinding()
  {
    _dm = CreateDataManager();
    _dm.CreatorPackages.Add(
      new CreatorPackage
      {
        PackageName = "SamplePkg_A",
        CreatorName = new AutoValue<string>("Alice"),
        SpellCards = new() { "结界「样本境界」" },
      }
    );
    _dm.CreatorPackages.Add(
      new CreatorPackage
      {
        PackageName = "SamplePkg_B",
        CreatorName = new AutoValue<string>("Bob"),
        SpellCards = new() { "B 符卡" },
      }
    );

    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.PackageList.ItemCount.ShouldBe(2);

    // 只写模型（不按按钮、不调用面板方法）：元素级绑定排队重建包列表。
    _dm.CreatorPackages[0].IsDeleted.Value = true;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.PackageList.ItemCount.ShouldBe(1);
    panel.PackageList.GetItemText(0).ShouldBe("Bob（SamplePkg_B）");

    // 被删包不在列表中 → 显示序号 0 落到 SamplePkg_B（已删包不可选中，也不会被用于导出）。
    _dm.MergeConfig.SelectedPackageIndex.Value = 0;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    panel.SpellCardList.ItemCount.ShouldBe(1);
    panel.SpellCardList.GetItemText(0).ShouldBe("B 符卡");

    // 绑定随列表增删重挂：新导入的包也要受 IsDeleted 绑定驱动。
    var imported = new CreatorPackage
    {
      PackageName = "SamplePkg_C",
      CreatorName = new AutoValue<string>("Carol"),
    };
    _dm.CreatorPackages.Add(imported);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    panel.PackageList.ItemCount.ShouldBe(2);

    imported.IsDeleted.Value = true;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    panel.PackageList.ItemCount.ShouldBe(1);
  }

  /// <summary>
  /// 回归：SelectedPackageIndex 是「过滤后可见列表」的显示序号；删掉显示序号比当前选中项靠前的
  /// 非选中包后，沿用旧序号会静默选到别的包。必须按包身份重定位（找不到则 -1）。
  /// </summary>
  [Test]
  public async Task RemoveEarlierNonSelectedPackage_KeepsSelectionOnSamePackage()
  {
    _dm = CreateDataManager();
    _dm.CreatorPackages.Add(Package("SamplePkg_A", "Alice", "A 符卡"));
    _dm.CreatorPackages.Add(Package("SamplePkg_B", "Bob", "B 符卡"));
    _dm.CreatorPackages.Add(Package("SamplePkg_C", "Carol", "C 符卡"));
    _dm.MergeConfig.SelectedPackageIndex.Value = 2;

    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.PackageList.ItemCount.ShouldBe(3);
    panel.SpellCardList.ItemCount.ShouldBe(1);
    panel.SpellCardList.GetItemText(0).ShouldBe("C 符卡");

    // 删除显示序号 0 的非选中包 A：过滤后列表变 [B, C]，选中项必须仍是 C（新序号 1）
    _dm.CreatorPackages[0].IsDeleted.Value = true;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    _dm.MergeConfig.SelectedPackageIndex.Value.ShouldBe(1);
    panel.PackageList.ItemCount.ShouldBe(2);
    panel.SpellCardList.GetItemText(0).ShouldBe("C 符卡", "选中项必须仍是原包 C，不能落到 B");

    // 再删除当前选中的包 C：选中项清空为 -1（不落到剩余的 B）
    _dm.CreatorPackages[2].IsDeleted.Value = true;
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    _dm.MergeConfig.SelectedPackageIndex.Value.ShouldBe(-1);
    panel.PackageList.ItemCount.ShouldBe(1);
    panel.SpellCardList.ItemCount.ShouldBe(0);
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

    // 事件处理器只写数据模型，AutoList 绑定驱动 UI 自动重建
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

    // 事件只写模型：真实 LineEdit 输入触发 TextChanged → 路径配置写回模型 + 触发保存。
    // 场景内这些编辑框均设 unique_name_in_owner；此处递归按名取真实 LineEdit 节点（与 [Node] 解析到同一节点）。
    // 用 EmitSignal(TextChanged) 显式触发处理器以模拟用户输入，保证测试确定性（与程序化赋值是否触发放一起无关）。
    var templateEdit =
      panel.FindChild("TemplatePathEdit", owned: false, recursive: true) as Godot.LineEdit;
    templateEdit.ShouldNotBeNull();
    templateEdit.Text = "tmp/template/main.lstgproj";
    templateEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "tmp/template/main.lstgproj");
    var sharpEdit =
      panel.FindChild("SharpPathEdit", owned: false, recursive: true) as Godot.LineEdit;
    sharpEdit.ShouldNotBeNull();
    sharpEdit.Text = "LuaSTG";
    sharpEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "LuaSTG");
    var pluginEdit =
      panel.FindChild("PluginDllEdit", owned: false, recursive: true) as Godot.LineEdit;
    pluginEdit.ShouldNotBeNull();
    pluginEdit.Text = "LuaSTGPlusLib.dll";
    pluginEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "LuaSTGPlusLib.dll");
    var outputEdit =
      panel.FindChild("OutputDirEdit", owned: false, recursive: true) as Godot.LineEdit;
    outputEdit.ShouldNotBeNull();
    outputEdit.Text = "out/mod";
    outputEdit.EmitSignal(Godot.LineEdit.SignalName.TextChanged, "out/mod");
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    _dm.MergeConfig.TemplatePath.Value.ShouldBe("tmp/template/main.lstgproj");
    _dm.MergeConfig.SharpEditorPath.Value.ShouldBe("LuaSTG");
    _dm.MergeConfig.PluginDll.Value.ShouldBe("LuaSTGPlusLib.dll");
    _dm.MergeConfig.OutputDir.Value.ShouldBe("out/mod");
  }

  [Test]
  public async Task LoadConfig_Backfills_AllPathEdits()
  {
    _dm = CreateDataManager();

    // 预置持久化配置：重启后应回显到 4 个路径/目录编辑框（LoadConfigToControls）。
    _dm.MergeConfig.TemplatePath.Value = "tmp/template/main.lstgproj";
    _dm.MergeConfig.SharpEditorPath.Value = "LuaSTG";
    _dm.MergeConfig.PluginDll.Value = "LuaSTGPlusLib.dll";
    _dm.MergeConfig.OutputDir.Value = "out/mod";

    var panel = InstantiatePanel();

    // 等待 OnResolved 完成（回填在 Ready 通知建立），避免偶发时序读到空串。
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.TemplatePathEdit.Text.ShouldBe("tmp/template/main.lstgproj");
    panel.SharpPathEdit.Text.ShouldBe("LuaSTG");
    panel.PluginDllEdit.Text.ShouldBe("LuaSTGPlusLib.dll");
    panel.OutputDirEdit.Text.ShouldBe("out/mod");
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
    _dm.MergeConfig.ShuffleMode.Value = MappingShuffleMode.Interleave;
    _dm.MergeConfig.Algorithm.Value = MergeAlgorithm.TopFolderCarry;
    _dm.MergeConfig.TemplatePath.Value = "tmp/template/main.lstgproj";
    _dm.MergeConfig.SharpEditorPath.Value = "LuaSTG";
    _dm.MergeConfig.PluginDll.Value = "LuaSTGPlusLib.dll";
    _dm.MergeConfig.OutputDir.Value = "out/mod";

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
    panel.MappingModeOption.Selected.ShouldBe((int)MappingShuffleMode.Interleave);
    panel.MergeAlgorithmOption.Selected.ShouldBe((int)MergeAlgorithm.TopFolderCarry);

    // 反向（本用例的核心回归）：回填不得污染模型。
    // 修复前：回填的程序化赋值触发 Toggled → PersistConfig 全量写回，
    // 把尚未回填的控件默认值写进模型并落盘，导致重载后配置丢失。
    // 注：OptionButton.Select() 本身不发 ItemSelected，属同一类「回填即触发」风险的守门用例。
    _dm.MergeConfig.IncludeLstges.Value.ShouldBeTrue();
    _dm.MergeConfig.ObfuscateLua.Value.ShouldBeTrue();
    _dm.MergeConfig.AutoRenameConflicts.Value.ShouldBeTrue();
    _dm.MergeConfig.ForcePerformAction.Value.ShouldBeTrue();
    _dm.MergeConfig.GroupByCreatorFolders.Value.ShouldBeTrue();
    _dm.MergeConfig.ShuffleMode.Value.ShouldBe(MappingShuffleMode.Interleave);
    _dm.MergeConfig.Algorithm.Value.ShouldBe(MergeAlgorithm.TopFolderCarry);
    _dm.MergeConfig.TemplatePath.Value.ShouldBe("tmp/template/main.lstgproj");
    _dm.MergeConfig.SharpEditorPath.Value.ShouldBe("LuaSTG");
    _dm.MergeConfig.PluginDll.Value.ShouldBe("LuaSTGPlusLib.dll");
    _dm.MergeConfig.OutputDir.Value.ShouldBe("out/mod");
  }

  [Test]
  public async Task MappingModeOption_Selection_WritesBackToModel()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();

    // 等待 OnResolved 完成（回填在 OnResolved 执行；OnReady 仅填充下拉选项）。
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 初始：模型默认 Random → 下拉选中第 0 项。
    _dm.MergeConfig.ShuffleMode.Value.ShouldBe(MappingShuffleMode.Random);
    panel.MappingModeOption.Selected.ShouldBe((int)MappingShuffleMode.Random);

    // 事件只写模型：模拟用户在下拉选定「交替」→ ItemSelected 驱动 SyncMappingModeToModel 写回。
    var node =
      panel.FindChild("MappingModeOption", owned: false, recursive: true) as Godot.OptionButton;
    node.ShouldNotBeNull();
    node.Selected = (int)MappingShuffleMode.Interleave;
    node.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, (int)MappingShuffleMode.Interleave);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    _dm.MergeConfig.ShuffleMode.Value.ShouldBe(MappingShuffleMode.Interleave);

    // 改选「按创作者分组」同样写回（状态可逆、不残留）。
    node.Selected = (int)MappingShuffleMode.GroupByCreator;
    node.EmitSignal(
      Godot.OptionButton.SignalName.ItemSelected,
      (int)MappingShuffleMode.GroupByCreator
    );
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    _dm.MergeConfig.ShuffleMode.Value.ShouldBe(MappingShuffleMode.GroupByCreator);
  }

  /// <summary>对应表的类型序列（形态），如 "N S N N S"。</summary>
  private static string MappingShape(IEnumerable<SpellCardMappingEntry> entries) =>
    string.Join(" ", entries.Select(e => e.IsNonSpell.Value ? "N" : "S"));

  /// <summary>对应表的名称序列（内容）。</summary>
  private static List<string> MappingNames(IEnumerable<SpellCardMappingEntry> entries) =>
    entries.Select(e => e.Name).ToList();

  [Test]
  public async Task ShuffleMapping_Interleave_FollowsRuleShape()
  {
    _dm = CreateDataManager();
    _dm.MergeConfig.ShuffleMode.Value = MappingShuffleMode.Interleave;

    // 3 非符 + 2 符卡 → 形态由规则决定：块长 [1,2] ⇒ N S N N S（元素身份随机，故只断言形态）。
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "n1",
        IsNonSpell = new(true),
        Creator = new("Alice"),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "n2",
        IsNonSpell = new(true),
        Creator = new("Alice"),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "n3",
        IsNonSpell = new(true),
        Creator = new("Alice"),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "s1",
        IsNonSpell = new(false),
        Creator = new("Bob"),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "s2",
        IsNonSpell = new(false),
        Creator = new("Bob"),
      }
    );

    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.GetNode<Godot.Button>("%ShuffleBtn").EmitSignal(Godot.Button.SignalName.Pressed);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    _dm.MergeConfig.Mapping.Count.ShouldBe(5);
    MappingShape(_dm.MergeConfig.Mapping).ShouldBe("N S N N S");
    MappingNames(_dm.MergeConfig.Mapping)
      .ShouldBe(new List<string> { "n1", "n2", "n3", "s1", "s2" }, ignoreOrder: true);
  }

  /// <summary>
  /// 回归：用户实测「按很多次都是一个结果」。交替模式也必须每次给出不同排列
  /// （形态恒定、内容随机），否则等于没打乱。
  /// </summary>
  [Test]
  public async Task ShuffleMapping_Interleave_RepeatedPresses_ProduceDifferentOrders()
  {
    _dm = CreateDataManager();
    _dm.MergeConfig.ShuffleMode.Value = MappingShuffleMode.Interleave;

    for (var i = 1; i <= 5; i++)
      _dm.MergeConfig.Mapping.Add(
        new SpellCardMappingEntry
        {
          Name = $"n{i}",
          IsNonSpell = new(true),
          Creator = new("Alice"),
        }
      );
    for (var i = 1; i <= 5; i++)
      _dm.MergeConfig.Mapping.Add(
        new SpellCardMappingEntry
        {
          Name = $"s{i}",
          IsNonSpell = new(false),
          Creator = new("Bob"),
        }
      );

    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    var shuffleBtn = panel.GetNode<Godot.Button>("%ShuffleBtn");
    var orders = new List<string>();
    for (var press = 0; press < 10; press++)
    {
      shuffleBtn.EmitSignal(Godot.Button.SignalName.Pressed);
      await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

      // 5×5 ⇒ 块长恒为 1，形态必须是严格交替；内容逐次随机。
      MappingShape(_dm.MergeConfig.Mapping).ShouldBe("N S N S N S N S N S");
      MappingNames(_dm.MergeConfig.Mapping)
        .ShouldBe(
          new List<string> { "n1", "n2", "n3", "n4", "n5", "s1", "s2", "s3", "s4", "s5" },
          ignoreOrder: true
        );
      orders.Add(string.Join(" ", MappingNames(_dm.MergeConfig.Mapping)));
    }

    orders.Distinct().Count().ShouldBeGreaterThan(1);
  }

  /// <summary>「随机」模式（默认）仍为整体洗牌，且保持旧的完全随机行为不回归。</summary>
  [Test]
  public async Task ShuffleMapping_Random_ShufflesWholeList()
  {
    _dm = CreateDataManager();
    _dm.MergeConfig.ShuffleMode.Value = MappingShuffleMode.Random;

    for (var i = 1; i <= 6; i++)
      _dm.MergeConfig.Mapping.Add(
        new SpellCardMappingEntry
        {
          Name = $"e{i}",
          IsNonSpell = new(i % 2 == 1),
          Creator = new("Alice"),
        }
      );

    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    var shuffleBtn = panel.GetNode<Godot.Button>("%ShuffleBtn");
    var orders = new List<string>();
    for (var press = 0; press < 10; press++)
    {
      shuffleBtn.EmitSignal(Godot.Button.SignalName.Pressed);
      await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

      // 随机模式不受规则约束：只要求内容守恒。
      MappingNames(_dm.MergeConfig.Mapping)
        .ShouldBe(new List<string> { "e1", "e2", "e3", "e4", "e5", "e6" }, ignoreOrder: true);
      orders.Add(string.Join(" ", MappingNames(_dm.MergeConfig.Mapping)));
    }

    orders.Distinct().Count().ShouldBeGreaterThan(1);
  }

  [Test]
  public async Task ShuffleMapping_GroupByCreator_ReordersModel()
  {
    _dm = CreateDataManager();
    _dm.MergeConfig.ShuffleMode.Value = MappingShuffleMode.GroupByCreator;

    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "a1",
        IsNonSpell = new(false),
        Creator = new("Alice"),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "b1",
        IsNonSpell = new(false),
        Creator = new("Bob"),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "a2",
        IsNonSpell = new(false),
        Creator = new("Alice"),
      }
    );
    _dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "b2",
        IsNonSpell = new(false),
        Creator = new("Bob"),
      }
    );

    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.GetNode<Godot.Button>("%ShuffleBtn").EmitSignal(Godot.Button.SignalName.Pressed);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 组间按首次出现（Alice → Bob）、组内随机，故只断言分组归属与内容守恒。
    _dm.MergeConfig.Mapping.Count.ShouldBe(4);
    _dm.MergeConfig.Mapping.Select(e => e.Creator.Value)
      .ShouldBe(new List<string> { "Alice", "Alice", "Bob", "Bob" });
    MappingNames(_dm.MergeConfig.Mapping)
      .ShouldBe(new List<string> { "a1", "a2", "b1", "b2" }, ignoreOrder: true);
  }

  // ==================== Phase 1：导出警告在冲突列表可见 ====================

  /// <summary>双包同名资源导出夹具用的最小模板（含符卡/资源/对象三个注入点注释）。</summary>
  private const string AutoRenameExportTemplate =
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"boss\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert spellcards here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert resources here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".General.Comment, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Comment\",\"attrInput\":\"Insert objects here\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n";

  /// <summary>含一个文件夹与一张符卡、并引用 <c>res/shared.png</c> 的创作者包工程文本。</summary>
  private static string AutoRenamePackageText(string card) =>
    "0,{\"$type\":\".RootFolder, LuaSTGEditorSharp\",\"Attributes\":[],\"AttributeCount\":0}\n"
    + "1,{\"$type\":\".General.Folder, LuaSTGEditorSharp\",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"Resources\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Graphics.LoadImage, \",\"Attributes\":[{\"attrCap\":\"Path\",\"attrInput\":\"res/shared.png\",\"EditWindow\":\"plainFile\"}],\"AttributeCount\":1}\n"
    + "1,{\"$type\":\".Boss.BossDefine, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\"pkg_enm\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "2,{\"$type\":\".Boss.BossSpellCard, \",\"Attributes\":[{\"attrCap\":\"Name\",\"attrInput\":\""
    + card
    + "\",\"EditWindow\":\"\"}],\"AttributeCount\":1}\n"
    + "3,{\"$type\":\".Boss.BossSCStart, \",\"Attributes\":[],\"AttributeCount\":0}\n";

  /// <summary>
  /// 构造「双包同名资源 + 其中一个包缺物理图片」的真实导出夹具（写入 <see cref="_fixtureDir"/>，
  /// 由 <see cref="Cleanup"/> 清理），并预置模板/输出目录/开关；返回模板路径。
  /// </summary>
  private string CreateAutoRenameMissingImageFixture()
  {
    _fixtureDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_MergePanelExport_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_fixtureDir);

    var templatePath = Path.Combine(_fixtureDir, "template.lstgproj");
    File.WriteAllText(templatePath, AutoRenameExportTemplate);

    _dm = CreateDataManager();
    foreach (
      var (packageName, card, includeImage) in new[]
      {
        ("pkg_alice", "卡A", true),
        ("pkg_bob", "卡B", false),
      }
    )
    {
      var zipPath = CreateSharedResourcePackage(packageName, card, includeImage);
      var import = MergeImporter.ImportZip(zipPath);
      import.IsSuccess.ShouldBeTrue();
      _dm.CreatorPackages.Add(import.Package!);
      foreach (var importCard in import.Cards)
        _dm.MergeConfig.Mapping.Add(importCard);
    }

    _dm.MergeConfig.TemplatePath.Value = templatePath;
    _dm.MergeConfig.OutputDir.Value = Path.Combine(_fixtureDir, "out");
    _dm.MergeConfig.OutputName.Value = "mod";
    _dm.MergeConfig.IncludeLstges.Value = true;
    _dm.MergeConfig.AutoRenameConflicts.Value = true;
    return templatePath;
  }

  /// <summary>
  /// 写一个含 <c>root.lstges</c>（引用 <c>res/shared.png</c>）的创作者包 zip；
  /// <paramref name="includeImage"/> 为 false 时故意不放物理图片。
  /// </summary>
  private string CreateSharedResourcePackage(string packageName, string card, bool includeImage)
  {
    var srcDir = Path.Combine(_fixtureDir, "src_" + packageName);
    Directory.CreateDirectory(srcDir);
    var lstgesPath = Path.Combine(srcDir, "root.lstges");
    File.WriteAllText(lstgesPath, AutoRenamePackageText(card));

    var zipPath = Path.Combine(_fixtureDir, packageName + ".zip");
    using var fs = File.Create(zipPath);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
    zip.CreateEntryFromFile(lstgesPath, "root.lstges");
    if (includeImage)
    {
      var imgPath = Path.Combine(srcDir, "shared.png");
      File.WriteAllText(imgPath, "PNG-" + packageName);
      zip.CreateEntryFromFile(imgPath, "res/shared.png");
    }
    return zipPath;
  }

  [Test]
  public async Task RealScene_ExportFullPackage_ShowsMissingRenameWarningRow()
  {
    CreateAutoRenameMissingImageFixture();
    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 驱动真实「导出完整包」按钮（未配置 Sharp → 跳过 zip 打包，不阻塞合并/警告展示）
    panel
      .GetNode<Godot.Button>("%ExportFullPackageBtn")
      .EmitSignal(Godot.Button.SignalName.Pressed);

    var rows = Enumerable
      .Range(0, panel.ConflictList.ItemCount)
      .Select(i => panel.ConflictList.GetItemText(i))
      .ToList();

    // 正向：缺失资源警告以「[警告]」前缀出现在冲突列表，且带包名便于定位
    rows.ShouldContain(r => r.StartsWith("[警告]"), string.Join(" | ", rows));
    rows.ShouldContain(
      r => r.Contains("自动改名资源缺失") && r.Contains("pkg_bob"),
      string.Join(" | ", rows)
    );
    // 警告不阻塞导出：走完全流程并提示对应表已导出
    rows.ShouldContain(r => r.StartsWith("[完成]"));
  }

  // ==================== Phase 1：算法联动置灰 + 实验性标签 ====================

  [Test]
  public async Task RealScene_SwitchAlgorithm_TogglesGroupByCreatorAvailability()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 默认提取式：可用、无 hover 说明
    _dm.MergeConfig.Algorithm.Value.ShouldBe(MergeAlgorithm.Extractive);
    panel.GroupByCreatorFoldersToggle.Disabled.ShouldBeFalse();
    panel.GroupByCreatorFoldersToggle.TooltipText.ShouldBeNullOrEmpty();

    var node =
      panel.FindChild("MergeAlgorithmOption", owned: false, recursive: true) as Godot.OptionButton;
    node.ShouldNotBeNull();

    // 用户切到按顶层文件夹整搬 → 置灰 + hover 说明；模型写回
    node!.Selected = (int)MergeAlgorithm.TopFolderCarry;
    node.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, (int)MergeAlgorithm.TopFolderCarry);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    _dm.MergeConfig.Algorithm.Value.ShouldBe(MergeAlgorithm.TopFolderCarry);
    panel.GroupByCreatorFoldersToggle.Disabled.ShouldBeTrue();
    panel.GroupByCreatorFoldersToggle.TooltipText.ShouldNotBeNullOrEmpty();

    // 切回提取式 → 恢复可用并清空说明（状态可逆、不残留）
    node.Selected = (int)MergeAlgorithm.Extractive;
    node.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, (int)MergeAlgorithm.Extractive);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    _dm.MergeConfig.Algorithm.Value.ShouldBe(MergeAlgorithm.Extractive);
    panel.GroupByCreatorFoldersToggle.Disabled.ShouldBeFalse();
    panel.GroupByCreatorFoldersToggle.TooltipText.ShouldBeNullOrEmpty();
  }

  [Test]
  public async Task LoadConfig_Backfill_TopFolderCarry_DisablesGroupByCreator()
  {
    _dm = CreateDataManager();
    _dm.MergeConfig.Algorithm.Value = MergeAlgorithm.TopFolderCarry;
    _dm.MergeConfig.GroupByCreatorFolders.Value = true;

    var panel = InstantiatePanel();
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    // 配置回填后状态一致：算法回显、开关值保留但置灰 + 说明
    panel.MergeAlgorithmOption.Selected.ShouldBe((int)MergeAlgorithm.TopFolderCarry);
    panel.GroupByCreatorFoldersToggle.ButtonPressed.ShouldBeTrue();
    panel.GroupByCreatorFoldersToggle.Disabled.ShouldBeTrue();
    panel.GroupByCreatorFoldersToggle.TooltipText.ShouldNotBeNullOrEmpty();

    // 回填只同步控件状态，不写模型（不清掉用户已保存的开关值）
    _dm.MergeConfig.Algorithm.Value.ShouldBe(MergeAlgorithm.TopFolderCarry);
    _dm.MergeConfig.GroupByCreatorFolders.Value.ShouldBeTrue();
  }

  [Test]
  public void RealScene_AutoRenameToggle_LabelMarksExperimental()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();

    // 场景文本含「实验性」，向用户明示自动改名的边界（只覆盖跨包同名资源文件）
    panel.AutoRenameConflictsToggle.Text.ShouldContain("实验性");
  }

  [Test]
  public void RealScene_ObfuscateLuaToggle_LabelMarksExperimental()
  {
    _dm = CreateDataManager();
    var panel = InstantiatePanel();

    // 混淆同样标「实验性」：Prometheus 对少数 Lua 写法不兼容，失败时导出 fail-closed
    panel.ObfuscateLuaToggle.Text.ShouldContain("实验性");
  }
}
