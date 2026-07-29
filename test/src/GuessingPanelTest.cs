namespace AutoCMEX;

using System;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Chickensoft.Introspection;
using Godot;
using Shouldly;

/// <summary>
/// 测试用 DataManager 提供者节点
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class DataManagerProvider : Node, IProvide<DataManager>
{
  public DataManager? DataManagerInstance { get; set; }

  DataManager IProvide<DataManager>.Value() => DataManagerInstance!;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady() => this.Provide();
}

/// <summary>
/// GuessingPanel UI 集成测试 - 验证所有编辑功能
/// 使用自定义 Driver 封装节点操作，避免硬编码路径。
/// </summary>
public class GuessingPanelTest : TestClass
{
  private Fixture _fixture = default!;
  private GuessingPanelDriver _driver = default!;
  private GuessingPanel _panel = default!;

  public GuessingPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _fixture = new Fixture(TestScene.GetTree());

    // 创建 DataManager 并通过 AutoInject 提供给子节点处理器
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_TestSetup_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    var dm = new DataManager(tmpDir, new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir)));
    var provider = new DataManagerProvider { DataManagerInstance = dm };
    TestScene.GetTree().Root.AddChild(provider);

    // 手动加载 GuessingPanel 场景并添加为提供者的子节点
    var scene = GD.Load<PackedScene>("res://src/ui/guessing/GuessingPanel.tscn");
    _panel = scene.Instantiate<GuessingPanel>();
    provider.AddChild(_panel);

    _driver = new GuessingPanelDriver(() => _panel);
  }

  [Cleanup]
  public void Cleanup() => _fixture.Cleanup();

  // ==================== 基础存在性测试 ====================

  [Test]
  public void LoadsSuccessfully() => _panel.ShouldNotBeNull();

  [Test]
  public void BossSelect_Exists() => _driver.SpellCardHandler.BossSelect.ShouldNotBeNull();

  [Test]
  public void SpellCardTree_Exists() => _driver.SpellCardHandler.Tree.ShouldNotBeNull();

  [Test]
  public void AliasTree_Exists() => _driver.AliasHandler.Tree.ShouldNotBeNull();

  [Test]
  public void GuessInput_Exists() => _driver.GuessInput.ShouldNotBeNull();

  [Test]
  public void ResponseDisplay_Exists() => _driver.ResponseDisplay.ShouldNotBeNull();

  // ==================== 符卡表按钮测试 ====================

  [Test]
  public void ImportCardBtn_Exists() => _driver.SpellCardHandler.ImportCardBtn.ShouldNotBeNull();

  [Test]
  public void ExportCardBtn_Exists() => _driver.SpellCardHandler.ExportCardBtn.ShouldNotBeNull();

  [Test]
  public void AddBossBtn_Exists() => _driver.SpellCardHandler.AddBossBtn.ShouldNotBeNull();

  [Test]
  public void AddCardBtn_Exists() => _driver.SpellCardHandler.AddCardBtn.ShouldNotBeNull();

  [Test]
  public void DeleteBtn_Exists() => _driver.SpellCardHandler.DeleteBtn.ShouldNotBeNull();

  // ==================== 别名表按钮测试 ====================

  [Test]
  public void ImportAliasBtn_Exists() => _driver.AliasHandler.ImportAliasBtn.ShouldNotBeNull();

  [Test]
  public void ExportAliasBtn_Exists() => _driver.AliasHandler.ExportAliasBtn.ShouldNotBeNull();

  [Test]
  public void AddAliasBtn_Exists() => _driver.AliasHandler.AddAliasBtn.ShouldNotBeNull();

  [Test]
  public void AddAliasToCreatorBtn_Exists() =>
    _driver.AliasHandler.AddAliasToCreatorBtn.ShouldNotBeNull();

  [Test]
  public void DeleteAliasBtn_Exists() => _driver.AliasHandler.DeleteAliasBtn.ShouldNotBeNull();

  // ==================== 猜测区测试 ====================

  [Test]
  public void FuzzifyBtn_InitiallyDisabled() => _driver.FuzzifyBtn.Disabled.ShouldBeTrue();

  [Test]
  public void ProcessBtn_Exists() => _driver.ProcessBtn.ShouldNotBeNull();

  [Test]
  public void ProcessEmptyInput_ShowsError()
  {
    _driver.ProcessBtn.ClickCenter();
    _driver.ResponseDisplay.Text.ShouldNotBeNullOrEmpty();
  }

  // ==================== 按钮可点击测试 ====================

  [Test]
  public void ExportCardBtn_IsClickable() =>
    _driver.SpellCardHandler.ExportCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ExportAliasBtn_IsClickable() =>
    _driver.AliasHandler.ExportAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddBossBtn_IsClickable() =>
    _driver.SpellCardHandler.AddBossBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddCardBtn_IsClickable() =>
    _driver.SpellCardHandler.AddCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void DeleteBtn_IsClickable() =>
    _driver.SpellCardHandler.DeleteBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddAliasBtn_IsClickable() =>
    _driver.AliasHandler.AddAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddAliasToCreatorBtn_IsClickable() =>
    _driver.AliasHandler.AddAliasToCreatorBtn.Disabled.ShouldBeFalse();

  [Test]
  public void DeleteAliasBtn_IsClickable() =>
    _driver.AliasHandler.DeleteAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ImportCardBtn_IsClickable() =>
    _driver.SpellCardHandler.ImportCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ImportAliasBtn_IsClickable() =>
    _driver.AliasHandler.ImportAliasBtn.Disabled.ShouldBeFalse();

  // ==================== 别名表编辑功能测试 ====================

  [Test]
  public void AddAlias_AddsCreatorToTree()
  {
    // Directly invoke handler method since dependencies may not be
    // resolved yet in test fixture setup.
    _driver.AliasHandler.Root?.GetOnAlias()();

    var root = _driver.AliasHandler.Tree?.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void AddAliasToCreator_AddsChildRow()
  {
    var dm = _driver.AliasHandler.Root?.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Aliases.Add(new CreatorAlias { MainName = "测试创作者" });
    _driver.AliasHandler.Root?.Refresh();

    var root = _driver.AliasHandler.Tree?.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(1);
    var creator = root.GetChild(0);
    var initialChildCount = creator.GetChildCount();

    dm.Aliases[0].Aliases.Add("新别名");
    _driver.AliasHandler.Root?.Refresh();

    root = _driver.AliasHandler.Tree?.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(1);
    creator = root.GetChild(0);
    creator.GetChildCount().ShouldBeGreaterThan(initialChildCount);
  }

  [Test]
  public void AddBoss_AddsToDropdown()
  {
    var dm = _driver.SpellCardHandler.Root?.GetDataManager();
    dm.ShouldNotBeNull();

    var bossSelect = _driver.SpellCardHandler.Root?.GetNode<OptionButton>("../../../BossSelect");
    bossSelect.ShouldNotBeNull();
    var initialCount = bossSelect.ItemCount;

    dm.Bosses.Add(new Boss { Name = "新Boss" });
    _driver.SpellCardHandler.Root?.Refresh();

    bossSelect.ItemCount.ShouldBeGreaterThan(initialCount);
  }

  [Test]
  public void AddSpellCard_AddsToTree()
  {
    var dm = _driver.SpellCardHandler.Root?.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Bosses.Add(new Boss { Name = "测试Boss" });
    _driver.SpellCardHandler.Root?.Refresh();

    _driver.SpellCardHandler.AddSpellCard();

    var root = _driver.SpellCardHandler.Tree?.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void DeleteBoss_RemovesFromDropdown()
  {
    var dm = _driver.SpellCardHandler.Root?.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Bosses.Add(new Boss { Name = "待删除Boss" });
    _driver.SpellCardHandler.Root?.Refresh();

    var bossSelect = _driver.SpellCardHandler.Root?.GetNode<OptionButton>("../../../BossSelect");
    bossSelect.ShouldNotBeNull();
    var initialCount = bossSelect.ItemCount;
    initialCount.ShouldBeGreaterThan(0);

    dm.Bosses.RemoveAt(0);
    _driver.SpellCardHandler.Root?.Refresh();

    bossSelect.ItemCount.ShouldBeLessThan(initialCount);
  }
}
