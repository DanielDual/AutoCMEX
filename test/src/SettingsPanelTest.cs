namespace AutoCMEX;

using System.Threading.Tasks;
using AutoCMEX.UI.Settings;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
using Godot;
using Shouldly;

/// <summary>
/// SettingsPanel UI 集成测试
/// </summary>
public class SettingsPanelTest : TestClass
{
    private Fixture _fixture = default!;
    private SettingsPanel _panel = default!;

    public SettingsPanelTest(Node testScene) : base(testScene) { }

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
        var searchBar = _panel.GetNodeOrNull<LineEdit>("%SearchBar");
        searchBar.ShouldNotBeNull();
    }

    [Test]
    public void SettingsPanel_CategoryList_Exists()
    {
        var list = _panel.GetNodeOrNull<ItemList>("%CategoryList");
        list.ShouldNotBeNull();
    }

    [Test]
    public void SettingsPanel_ConfigArea_Exists()
    {
        var area = _panel.GetNodeOrNull<Control>("%ConfigArea");
        area.ShouldNotBeNull();
    }

    [Test]
    public void SettingsPanel_CategoryList_HasSevenCategories()
    {
        var list = _panel.GetNodeOrNull<ItemList>("%CategoryList");
        list.ShouldNotBeNull();
        list.ItemCount.ShouldBe(7);
    }

    [Test]
    public void SettingsPanel_SearchBar_HasPlaceholder()
    {
        var searchBar = _panel.GetNodeOrNull<LineEdit>("%SearchBar");
        searchBar.ShouldNotBeNull();
        searchBar.PlaceholderText.ShouldContain("搜索");
    }
}
