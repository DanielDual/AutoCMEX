namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
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
/// </summary>
public class GuessingPanelTest : TestClass
{
  private Fixture _fixture = default!;
  private GuessingPanel _panel = default!;
  private SpellCardTreeHandler _spellCardHandler = default!;
  private AliasTreeHandler _aliasHandler = default!;

  public GuessingPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public async Task Setup()
  {
    _fixture = new Fixture(TestScene.GetTree());

    // 创建 DataManager 并通过 AutoInject 提供给子节点处理器
    // 提供者必须是处理器的祖先节点才能被 AutoInject 发现
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_TestSetup_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    var dm = new DataManager(tmpDir, new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir)));
    var provider = new DataManagerProvider { DataManagerInstance = dm };
    TestScene.GetTree().Root.AddChild(provider);

    // 手动加载 GuessingPanel 场景并添加为提供者的子节点
    // 这样 AutoInject 在处理处理器时能找到提供者
    var scene = GD.Load<PackedScene>("res://src/ui/guessing/GuessingPanel.tscn");
    _panel = scene.Instantiate<GuessingPanel>();
    provider.AddChild(_panel);

    _spellCardHandler = _panel.GetNode<SpellCardTreeHandler>(
      "MainContainer/ContentArea/LeftSplit/SpellCardArea"
    );
    _aliasHandler = _panel.GetNode<AliasTreeHandler>(
      "MainContainer/ContentArea/LeftSplit/AliasArea"
    );
  }

  [Cleanup]
  public void Cleanup() => _fixture.Cleanup();

  // ==================== 基础存在性测试 ====================

  [Test]
  public void LoadsSuccessfully() => _panel.ShouldNotBeNull();

  [Test]
  public void BossSelect_Exists() => _spellCardHandler.BossSelect.ShouldNotBeNull();

  [Test]
  public void SpellCardTree_Exists() => _spellCardHandler.SpellCardTree.ShouldNotBeNull();

  [Test]
  public void AliasTree_Exists() => _aliasHandler.AliasTree.ShouldNotBeNull();

  [Test]
  public void GuessInput_Exists() => _panel.GuessInput.ShouldNotBeNull();

  [Test]
  public void ResponseDisplay_Exists() => _panel.ResponseDisplay.ShouldNotBeNull();

  // ==================== 符卡表按钮测试 ====================

  [Test]
  public void ImportCardBtn_Exists()
  {
    _spellCardHandler.ImportCardBtn.ShouldNotBeNull();
    _spellCardHandler.ImportCardBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void ExportCardBtn_Exists()
  {
    _spellCardHandler.ExportCardBtn.ShouldNotBeNull();
    _spellCardHandler.ExportCardBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddBossBtn_Exists()
  {
    _spellCardHandler.AddBossBtn.ShouldNotBeNull();
    _spellCardHandler.AddBossBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddCardBtn_Exists()
  {
    _spellCardHandler.AddCardBtn.ShouldNotBeNull();
    _spellCardHandler.AddCardBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void DeleteBtn_Exists()
  {
    _spellCardHandler.DeleteBtn.ShouldNotBeNull();
    _spellCardHandler.DeleteBtn.Text.ShouldNotBeNullOrEmpty();
  }

  // ==================== 别名表按钮测试 ====================

  [Test]
  public void ImportAliasBtn_Exists()
  {
    _aliasHandler.ImportAliasBtn.ShouldNotBeNull();
    _aliasHandler.ImportAliasBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void ExportAliasBtn_Exists()
  {
    _aliasHandler.ExportAliasBtn.ShouldNotBeNull();
    _aliasHandler.ExportAliasBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddAliasBtn_Exists()
  {
    _aliasHandler.AddAliasBtn.ShouldNotBeNull();
    _aliasHandler.AddAliasBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddAliasToCreatorBtn_Exists()
  {
    _aliasHandler.AddAliasToCreatorBtn.ShouldNotBeNull();
    _aliasHandler.AddAliasToCreatorBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void DeleteAliasBtn_Exists()
  {
    _aliasHandler.DeleteAliasBtn.ShouldNotBeNull();
    _aliasHandler.DeleteAliasBtn.Text.ShouldNotBeNullOrEmpty();
  }

  // ==================== 猜测区测试 ====================

  [Test]
  public void FuzzifyBtn_InitiallyDisabled()
  {
    _panel.FuzzifyBtn.ShouldNotBeNull();
    _panel.FuzzifyBtn.Disabled.ShouldBeTrue();
  }

  [Test]
  public void ProcessBtn_Exists()
  {
    _panel.ProcessBtn.ShouldNotBeNull();
    _panel.ProcessBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void ProcessEmptyInput_ShowsError()
  {
    var btn = new ButtonDriver(() => (Button)_panel.ProcessBtn);
    btn.ClickCenter();
    _panel.ResponseDisplay.Text.ShouldNotBeNullOrEmpty();
  }

  // ==================== 按钮可点击测试 ====================

  [Test]
  public void ExportCardBtn_IsClickable() =>
    _spellCardHandler.ExportCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ExportAliasBtn_IsClickable() => _aliasHandler.ExportAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddBossBtn_IsClickable() => _spellCardHandler.AddBossBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddCardBtn_IsClickable() => _spellCardHandler.AddCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void DeleteBtn_IsClickable() => _spellCardHandler.DeleteBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddAliasBtn_IsClickable() => _aliasHandler.AddAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddAliasToCreatorBtn_IsClickable() =>
    _aliasHandler.AddAliasToCreatorBtn.Disabled.ShouldBeFalse();

  [Test]
  public void DeleteAliasBtn_IsClickable() => _aliasHandler.DeleteAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ImportCardBtn_IsClickable() =>
    _spellCardHandler.ImportCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ImportAliasBtn_IsClickable() => _aliasHandler.ImportAliasBtn.Disabled.ShouldBeFalse();

  // ==================== 别名表编辑功能测试 ====================

  [Test]
  public void AddAlias_AddsCreatorToTree()
  {
    // 直接调用处理器方法，避免 headless 下 ButtonDriver 不可靠
    _aliasHandler.GetOnAlias()();

    var root = _aliasHandler.AliasTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void AddAliasToCreator_AddsChildRow()
  {
    // 使用处理器的 DataManager
    var dm = _aliasHandler.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Aliases.Add(new CreatorAlias { MainName = "测试创作者" });
    _aliasHandler.Refresh();

    var root = _aliasHandler.AliasTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(1);
    var creator = root.GetChild(0);
    var initialChildCount = creator.GetChildCount();

    // 直接添加别名并刷新
    dm.Aliases[0].Aliases.Add("新别名");
    _aliasHandler.Refresh();

    root = _aliasHandler.AliasTree.GetRoot();
    root.GetChildCount().ShouldBe(1);
    creator = root.GetChild(0);
    creator.GetChildCount().ShouldBeGreaterThan(initialChildCount);
  }

  [Test]
  public void AddBoss_AddsToDropdown()
  {
    var dm = _spellCardHandler.GetDataManager();
    dm.ShouldNotBeNull();

    var initialCount = _spellCardHandler.BossSelect.ItemCount;

    // 直接添加 Boss 并刷新
    dm.Bosses.Add(new Boss { Name = "新Boss" });
    _spellCardHandler.Refresh();

    _spellCardHandler.BossSelect.ItemCount.ShouldBeGreaterThan(initialCount);
  }

  [Test]
  public void AddSpellCard_AddsToTree()
  {
    var dm = _spellCardHandler.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Bosses.Add(new Boss { Name = "测试Boss" });
    _spellCardHandler.Refresh();

    // 直接调用处理器方法添加符卡
    _spellCardHandler.GetOnAddSpellCard()();

    var root = _spellCardHandler.SpellCardTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void DeleteBoss_RemovesFromDropdown()
  {
    var dm = _spellCardHandler.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Bosses.Add(new Boss { Name = "待删除Boss" });
    _spellCardHandler.Refresh();

    var initialCount = _spellCardHandler.BossSelect.ItemCount;
    initialCount.ShouldBeGreaterThan(0);

    // 直接删除 Boss 并刷新
    dm.Bosses.RemoveAt(0);
    _spellCardHandler.Refresh();

    _spellCardHandler.BossSelect.ItemCount.ShouldBeLessThan(initialCount);
  }
}
