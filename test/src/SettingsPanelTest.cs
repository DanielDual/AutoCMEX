namespace AutoCMEX;

using System.Threading.Tasks;
using AutoCMEX.UI.Settings;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Godot;
using Shouldly;

/// <summary>
/// SettingsPanel UI 集成测试
/// </summary>
public class SettingsPanelTest : TestClass
{
  private Fixture _fixture = default!;
  private SettingsPanel _panel = default!;

  public SettingsPanelTest(Node testScene)
    : base(testScene) { }

  [SetupAll]
  public async Task Setup()
  {
    _fixture = new Fixture(TestScene.GetTree());
    _panel = await _fixture.LoadAndAddScene<SettingsPanel>();
  }

  [CleanupAll]
  public void Cleanup() => _fixture.Cleanup();

  [Test]
  public void SettingsPanel_LoadsSuccessfully()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_SearchBar_Exists()
  {
    _panel.SearchBar.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_CategoryList_Exists()
  {
    _panel.CategoryList.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_ConfigArea_Exists()
  {
    _panel.ConfigArea.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_CategoryList_HasSevenCategories()
  {
    _panel.CategoryList.ShouldNotBeNull();
    _panel.CategoryList.ItemCount.ShouldBe(7);
  }

  [Test]
  public void SettingsPanel_SearchBar_HasPlaceholder()
  {
    _panel.SearchBar.ShouldNotBeNull();
    _panel.SearchBar.PlaceholderText.ShouldContain("搜索");
  }
}
