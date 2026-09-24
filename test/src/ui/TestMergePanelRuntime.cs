namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Linq;
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
    panel.MappingModeOption.ShouldNotBeNull();

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
    _dm.MergeConfig.ShuffleMode.Value = MappingShuffleMode.Interleave;
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
    _dm.MergeConfig.TemplatePath.Value.ShouldBe("C:/tmp/template/main.lstgproj");
    _dm.MergeConfig.SharpEditorPath.Value.ShouldBe("C:/Program Files/LuaSTG");
    _dm.MergeConfig.PluginDll.Value.ShouldBe("LuaSTGPlusLib.dll");
    _dm.MergeConfig.OutputDir.Value.ShouldBe("C:/out/mod");
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

    // 事件只写模型（指示 24）：模拟用户在下拉选定「交替」→ ItemSelected 驱动 SyncMappingModeToModel 写回。
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
}
