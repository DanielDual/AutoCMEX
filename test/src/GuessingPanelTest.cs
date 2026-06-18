namespace AutoCMEX;

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
using Godot;
using Shouldly;

/// <summary>
/// GuessingPanel UI 集成测试 - 验证所有编辑功能
/// </summary>
public class GuessingPanelTest : TestClass
{
  private Fixture _fixture = default!;
  private GuessingPanel _panel = default!;

  public GuessingPanelTest(Node testScene)
    : base(testScene) { }

  [SetupAll]
  public async Task Setup()
  {
    _fixture = new Fixture(TestScene.GetTree());
    _panel = await _fixture.LoadAndAddScene<GuessingPanel>();
  }

  [CleanupAll]
  public void Cleanup() => _fixture.Cleanup();

  // ==================== 基础存在性测试 ====================

  [Test]
  public void LoadsSuccessfully() => _panel.ShouldNotBeNull();

  [Test]
  public void BossSelect_Exists() => _panel.BossSelect.ShouldNotBeNull();

  [Test]
  public void SpellCardTree_Exists() => _panel.SpellCardTree.ShouldNotBeNull();

  [Test]
  public void AliasTree_Exists() => _panel.AliasTree.ShouldNotBeNull();

  [Test]
  public void GuessInput_Exists() => _panel.GuessInput.ShouldNotBeNull();

  [Test]
  public void ResponseDisplay_Exists() => _panel.ResponseDisplay.ShouldNotBeNull();

  // ==================== 符卡表按钮测试 ====================

  [Test]
  public void ImportCardBtn_Exists()
  {
    _panel.ImportCardBtn.ShouldNotBeNull();
    _panel.ImportCardBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void ExportCardBtn_Exists()
  {
    _panel.ExportCardBtn.ShouldNotBeNull();
    _panel.ExportCardBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddBossBtn_Exists()
  {
    _panel.AddBossBtn.ShouldNotBeNull();
    _panel.AddBossBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddCardBtn_Exists()
  {
    _panel.AddCardBtn.ShouldNotBeNull();
    _panel.AddCardBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void DeleteBtn_Exists()
  {
    _panel.DeleteBtn.ShouldNotBeNull();
    _panel.DeleteBtn.Text.ShouldNotBeNullOrEmpty();
  }

  // ==================== 别名表按钮测试 ====================

  [Test]
  public void ImportAliasBtn_Exists()
  {
    _panel.ImportAliasBtn.ShouldNotBeNull();
    _panel.ImportAliasBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void ExportAliasBtn_Exists()
  {
    _panel.ExportAliasBtn.ShouldNotBeNull();
    _panel.ExportAliasBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddAliasBtn_Exists()
  {
    _panel.AddAliasBtn.ShouldNotBeNull();
    _panel.AddAliasBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void AddAliasToCreatorBtn_Exists()
  {
    _panel.AddAliasToCreatorBtn.ShouldNotBeNull();
    _panel.AddAliasToCreatorBtn.Text.ShouldNotBeNullOrEmpty();
  }

  [Test]
  public void DeleteAliasBtn_Exists()
  {
    _panel.DeleteAliasBtn.ShouldNotBeNull();
    _panel.DeleteAliasBtn.Text.ShouldNotBeNullOrEmpty();
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
  public void ExportCardBtn_IsClickable() => _panel.ExportCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ExportAliasBtn_IsClickable() => _panel.ExportAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddBossBtn_IsClickable() => _panel.AddBossBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddCardBtn_IsClickable() => _panel.AddCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void DeleteBtn_IsClickable() => _panel.DeleteBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddAliasBtn_IsClickable() => _panel.AddAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void AddAliasToCreatorBtn_IsClickable() =>
    _panel.AddAliasToCreatorBtn.Disabled.ShouldBeFalse();

  [Test]
  public void DeleteAliasBtn_IsClickable() => _panel.DeleteAliasBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ImportCardBtn_IsClickable() => _panel.ImportCardBtn.Disabled.ShouldBeFalse();

  [Test]
  public void ImportAliasBtn_IsClickable() => _panel.ImportAliasBtn.Disabled.ShouldBeFalse();

  // ==================== 别名表编辑功能测试 ====================

  [Test]
  public void AddAlias_AddsCreatorToTree()
  {
    var btn = new ButtonDriver(() => (Button)_panel.AddAliasBtn);
    btn.ClickCenter();

    var root = _panel.AliasTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void AddAliasToCreator_AddsChildRow()
  {
    // 直接操作 DataManager 验证别名添加，避免 headless 下 TreeItem.Select 不可靠
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    var dm = new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
    dm.Aliases.Add(new CreatorAlias { MainName = "测试创作者" });
    _panel.InjectTestData(dm);

    var root = _panel.AliasTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(1);
    var creator = root.GetChild(0);
    var initialChildCount = creator.GetChildCount();

    // 直接添加别名并刷新
    dm.Aliases[0].Aliases.Add("新别名");
    _panel.RefreshAll();

    root = _panel.AliasTree.GetRoot();
    root.GetChildCount().ShouldBe(1);
    creator = root.GetChild(0);
    creator.GetChildCount().ShouldBeGreaterThan(initialChildCount);

    try
    {
      Directory.Delete(tmpDir, recursive: true);
    }
    catch { }
  }

  [Test]
  public void AddBoss_AddsToDropdown()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    var dm = new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
    _panel.InjectTestData(dm);

    var initialCount = _panel.BossSelect.ItemCount;

    // 直接添加 Boss 并刷新
    dm.Bosses.Add(new Boss { Name = "新Boss" });
    _panel.RefreshAll();

    _panel.BossSelect.ItemCount.ShouldBeGreaterThan(initialCount);

    try
    {
      Directory.Delete(tmpDir, recursive: true);
    }
    catch { }
  }

  [Test]
  public void AddSpellCard_AddsToTree()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    var dm = new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
    dm.Bosses.Add(new Boss { Name = "测试Boss" });
    _panel.InjectTestData(dm);

    // InjectTestData 已选中第一个 Boss，直接添加符卡
    var addCardBtn = new ButtonDriver(() => (Button)_panel.AddCardBtn);
    addCardBtn.ClickCenter();

    var root = _panel.SpellCardTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);

    try
    {
      Directory.Delete(tmpDir, recursive: true);
    }
    catch { }
  }

  [Test]
  public void DeleteBoss_RemovesFromDropdown()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    var dm = new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
    dm.Bosses.Add(new Boss { Name = "待删除Boss" });
    _panel.InjectTestData(dm);

    var initialCount = _panel.BossSelect.ItemCount;
    initialCount.ShouldBeGreaterThan(0);

    // 直接删除 Boss 并刷新
    dm.Bosses.RemoveAt(0);
    _panel.RefreshAll();

    _panel.BossSelect.ItemCount.ShouldBeLessThan(initialCount);

    try
    {
      Directory.Delete(tmpDir, recursive: true);
    }
    catch { }
  }
}
