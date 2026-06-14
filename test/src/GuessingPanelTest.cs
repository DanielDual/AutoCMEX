namespace AutoCMEX;

using System.Threading.Tasks;
using AutoCMEX.UI.Guessing;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
using Godot;
using Shouldly;

/// <summary>
/// GuessingPanel UI 集成测试
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

  [Test]
  public void GuessingPanel_LoadsSuccessfully()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void GuessingPanel_BossSelect_Exists()
  {
    _panel.BossSelect.ShouldNotBeNull();
  }

  [Test]
  public void GuessingPanel_SpellCardTree_Exists()
  {
    _panel.SpellCardTree.ShouldNotBeNull();
  }

  [Test]
  public void GuessingPanel_AliasList_Exists()
  {
    _panel.AliasList.ShouldNotBeNull();
  }

  [Test]
  public void GuessingPanel_GuessInput_Exists()
  {
    _panel.GuessInput.ShouldNotBeNull();
  }

  [Test]
  public void GuessingPanel_ResponseDisplay_Exists()
  {
    _panel.ResponseDisplay.ShouldNotBeNull();
  }

  [Test]
  public void GuessingPanel_ImportCardBtn_Exists()
  {
    _panel.ImportCardBtn.ShouldNotBeNull();
    _panel.ImportCardBtn.Text.ShouldBe("导入对应表");
  }

  [Test]
  public void GuessingPanel_AddBossBtn_Exists()
  {
    _panel.AddBossBtn.ShouldNotBeNull();
    _panel.AddBossBtn.Text.ShouldBe("添加 Boss");
  }

  [Test]
  public void GuessingPanel_DeleteBtn_Exists()
  {
    _panel.DeleteBtn.ShouldNotBeNull();
    _panel.DeleteBtn.Text.ShouldBe("删除");
  }

  [Test]
  public void GuessingPanel_ImportAliasBtn_Exists()
  {
    _panel.ImportAliasBtn.ShouldNotBeNull();
    _panel.ImportAliasBtn.Text.ShouldBe("导入别名表");
  }

  [Test]
  public void GuessingPanel_FuzzifyBtn_InitiallyDisabled()
  {
    _panel.FuzzifyBtn.ShouldNotBeNull();
    _panel.FuzzifyBtn.Disabled.ShouldBeTrue();
  }

  [Test]
  public void GuessingPanel_ProcessBtn_Exists()
  {
    _panel.ProcessBtn.ShouldNotBeNull();
    _panel.ProcessBtn.Text.ShouldBe("处理");
  }

  [Test]
  public void GuessingPanel_ProcessEmptyInput_ShowsError()
  {
    var processBtn = new ButtonDriver(() => (Button)_panel.ProcessBtn);
    processBtn.ClickCenter();

    _panel.ResponseDisplay.ShouldNotBeNull();
    // Should show error about no boss selected or empty input
    _panel.ResponseDisplay.Text.ShouldNotBeNullOrEmpty();
  }
}
