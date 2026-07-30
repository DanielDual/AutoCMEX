namespace AutoCMEX;

using System;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Input;
using Chickensoft.GodotTestDriver.Util;
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
/// GuessingPanel UI 集成测试 — 使用 AutoInject 推荐的测试模式。
/// </summary>
public class GuessingPanelTest : TestClass
{
  private Fixture _fixture = default!;
  private GuessingPanelDriver _driver = default!;
  private GuessingPanel _panel = default!;
  private DataManager _dm = default!;

  public GuessingPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public async Task Setup()
  {
    _fixture = new Fixture(TestScene.GetTree());

    // 创建测试 DataManager
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_TestSetup_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    _dm = new DataManager(tmpDir, new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir)));

    // 创建 Provider（子处理器需要从 Provider 解析 DataManager）
    var provider = new DataManagerProvider { DataManagerInstance = _dm };

    // 实例化场景（获取子节点）
    var scene = GD.Load<PackedScene>("res://src/ui/guessing/GuessingPanel.tscn");
    _panel = scene.Instantiate<GuessingPanel>();

    // 伪造 IGuessProcessingService 依赖（面板专用）
    _panel.FakeDependency<IGuessProcessingService>(
      new GuessProcessingService(
        _dm,
        new AiServiceFactory(_dm),
        new GuessResponseHandler(),
        new DroppedGuessRepository()
      )
    );

    // 将 Provider 加入场景树
    TestScene.GetTree().Root.AddChild(provider);

    // 将 Panel 加入 Provider（触发 _Ready → OnReady → OnResolved）
    provider.AddChild(_panel);

    _driver = new GuessingPanelDriver(() => _panel);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (_panel != null)
    {
      _panel.GetParent()?.RemoveChild(_panel);
      _panel.QueueFree();
      _panel = null;
    }
    _fixture.Cleanup();
  }

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
  public async Task ProcessEmptyInput_ShowsError()
  {
    await TestScene.GetTree().NextFrame();
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

    var bossSelect = _driver.SpellCardHandler.BossSelect.Root;
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

    var bossSelect = _driver.SpellCardHandler.BossSelect.Root;
    bossSelect.ShouldNotBeNull();
    var initialCount = bossSelect.ItemCount;
    initialCount.ShouldBeGreaterThan(0);

    dm.Bosses.RemoveAt(0);
    _driver.SpellCardHandler.Root?.Refresh();

    bossSelect.ItemCount.ShouldBeLessThan(initialCount);
  }

  // ==================== 符卡表删除测试 ====================

  [Test]
  public void DeleteSpellCard_RemovesFromTree()
  {
    var dm = _driver.SpellCardHandler.Root?.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Bosses.Add(
      new Boss
      {
        Name = "测试Boss",
        SpellCards = new System.Collections.ObjectModel.ObservableCollection<SpellCard>
        {
          new() { Name = "Card1", Creator = "Alice" },
          new() { Name = "Card2", Creator = "Bob" },
        },
      }
    );
    _driver.SpellCardHandler.Root?.Refresh();

    var root = _driver.SpellCardHandler.Tree?.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(1); // boss
    var bossItem = root.GetChild(0);
    bossItem.GetChildCount().ShouldBe(2); // 2 cards

    // Remove a card and refresh
    dm.Bosses[0].SpellCards.RemoveAt(0);
    _driver.SpellCardHandler.Root?.Refresh();

    root = _driver.SpellCardHandler.Tree?.GetRoot();
    bossItem = root?.GetChild(0);
    bossItem.GetChildCount().ShouldBe(1); // 1 card left
  }

  // ==================== 别名表删除测试 ====================

  [Test]
  public void DeleteAlias_CreatorRemovesFromTree()
  {
    var dm = _driver.AliasHandler.Root?.GetDataManager();
    dm.ShouldNotBeNull();

    dm.Aliases.Add(new CreatorAlias { MainName = "创作者1" });
    dm.Aliases.Add(new CreatorAlias { MainName = "创作者2" });
    _driver.AliasHandler.Root?.Refresh();

    var root = _driver.AliasHandler.Tree?.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(2);

    // Remove a creator and refresh
    dm.Aliases.RemoveAt(0);
    _driver.AliasHandler.Root?.Refresh();

    root = _driver.AliasHandler.Tree?.GetRoot();
    root.GetChildCount().ShouldBe(1);
  }

  // ==================== 猜测处理行为测试 ====================

  [Test]
  public void DroppedList_Empty_InitiallyDisabled() =>
    _driver.RetryDroppedBtnDisabled.ShouldBeTrue();

  [Test]
  public void ClearDropped_NoGuesses_ButtonDisabled()
  {
    var clearBtn = _panel.GetNode<Button>(
      "MainContainer/ContentArea/RightPanel/DroppedSection/DroppedButtons/ClearDroppedBtn"
    );
    clearBtn.ShouldNotBeNull();
    clearBtn.Disabled.ShouldBeTrue();
  }

  [Test]
  public void GuessInput_CanTypeText()
  {
    var input = _panel.GetNode<TextEdit>("MainContainer/ContentArea/RightPanel/GuessInput");
    input.ShouldNotBeNull();
    input.Text = "test input";
    input.Text.ShouldBe("test input");
  }

  [Test]
  public void GuessInput_DriverIsAccessible() => _driver.GuessInput.ShouldNotBeNull();

  // ==================== 按钮状态测试 ====================

  [Test]
  public void FuzzifyBtn_NoAiModel_IsDisabled()
  {
    _dm.Bosses.Add(
      new Boss
      {
        Name = "TestBoss",
        SpellCards = new System.Collections.ObjectModel.ObservableCollection<SpellCard>
        {
          new() { Name = "Card1", Creator = "Alice" },
        },
      }
    );
    _dm.Settings.SelectedBossIndex = 0;

    _driver.FuzzifyBtnDisabled.ShouldBeTrue();
  }

  [Test]
  public async Task FuzzifyBtn_HasAiModel_IsEnabled()
  {
    _dm.Bosses.Add(
      new Boss
      {
        Name = "TestBoss",
        SpellCards = new System.Collections.ObjectModel.ObservableCollection<SpellCard>
        {
          new() { Name = "Card1", Creator = "Alice" },
        },
      }
    );
    _dm.Settings.SelectedBossIndex = 0;
    _dm.Settings.ActiveAiModelId = "test-model";
    _dm.Settings.AiModels.Add(
      new AiModelConfig
      {
        Id = "test-model",
        ApiFormat = "OpenAI",
        EndpointUrl = "https://example.com",
        ModelId = "gpt-4",
        EncryptedApiKey = "sk-test",
      }
    );

    // Wait for PropertyChanged → NotifyDataChanged → CallDeferred → UpdateFuzzifyButtonState
    await TestScene.GetTree().NextFrame();

    _driver.FuzzifyBtn.Disabled.ShouldBeFalse();
  }

  [Test]
  public void RetryDroppedBtn_NoDropped_IsDisabled() =>
    _driver.RetryDroppedBtnDisabled.ShouldBeTrue();

  [Test]
  public void ClearDroppedBtn_NoDropped_IsDisabled()
  {
    var clearBtn = _panel.GetNode<Button>(
      "MainContainer/ContentArea/RightPanel/DroppedSection/DroppedButtons/ClearDroppedBtn"
    );
    clearBtn.ShouldNotBeNull();
    clearBtn.Disabled.ShouldBeTrue();
  }

  [Test]
  public void ProcessBtn_IsEnabled() => _driver.ProcessBtnDisabled.ShouldBeFalse();

  // ==================== 异步集成测试 ====================

  [Test]
  [Timeout(15000)]
  public async Task OnProcessGuess_StrictMode_Result()
  {
    _dm.Bosses.Add(
      new Boss
      {
        Name = "TestBoss",
        SpellCards = new System.Collections.ObjectModel.ObservableCollection<SpellCard>
        {
          new() { Name = "Card1", Creator = "Alice" },
          new() { Name = "Card2", Creator = "Bob" },
        },
      }
    );
    _dm.Settings.SelectedBossIndex = 0;
    _dm.Settings.MessageFilterMode = "strict";

    await TestScene.GetTree().NextFrame();
    _driver.SubmitGuess("1Alice 2Bob");
    await TestScene.GetTree().WithinSeconds(5f, () => _driver.ResponseText.Contains("✔️"));
  }
}
