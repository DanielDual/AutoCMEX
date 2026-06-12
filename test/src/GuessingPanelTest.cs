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

    public GuessingPanelTest(Node testScene) : base(testScene) { }

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
        var driver = new OptionButtonDriver(() => _panel.GetNodeOrNull<OptionButton>("%BossSelect"));
        driver.ShouldNotBeNull();
    }

    [Test]
    public void GuessingPanel_SpellCardTree_Exists()
    {
        var tree = _panel.GetNodeOrNull<Tree>("%SpellCardTree");
        tree.ShouldNotBeNull();
    }

    [Test]
    public void GuessingPanel_AliasList_Exists()
    {
        var list = _panel.GetNodeOrNull<ItemList>("%AliasList");
        list.ShouldNotBeNull();
    }

    [Test]
    public void GuessingPanel_GuessInput_Exists()
    {
        var input = _panel.GetNodeOrNull<TextEdit>("%GuessInput");
        input.ShouldNotBeNull();
    }

    [Test]
    public void GuessingPanel_ResponseDisplay_Exists()
    {
        var display = _panel.GetNodeOrNull<RichTextLabel>("%ResponseDisplay");
        display.ShouldNotBeNull();
    }

    [Test]
    public void GuessingPanel_ImportCardBtn_Exists()
    {
        var btn = _panel.GetNodeOrNull<Button>("%ImportCardBtn");
        btn.ShouldNotBeNull();
        btn.Text.ShouldBe("导入对应表");
    }

    [Test]
    public void GuessingPanel_AddBossBtn_Exists()
    {
        var btn = _panel.GetNodeOrNull<Button>("%AddBossBtn");
        btn.ShouldNotBeNull();
        btn.Text.ShouldBe("添加 Boss");
    }

    [Test]
    public void GuessingPanel_DeleteBtn_Exists()
    {
        var btn = _panel.GetNodeOrNull<Button>("%DeleteBtn");
        btn.ShouldNotBeNull();
        btn.Text.ShouldBe("删除");
    }

    [Test]
    public void GuessingPanel_ImportAliasBtn_Exists()
    {
        var btn = _panel.GetNodeOrNull<Button>("%ImportAliasBtn");
        btn.ShouldNotBeNull();
        btn.Text.ShouldBe("导入别名表");
    }

    [Test]
    public void GuessingPanel_FuzzifyBtn_InitiallyDisabled()
    {
        var btn = _panel.GetNodeOrNull<Button>("%FuzzifyBtn");
        btn.ShouldNotBeNull();
        btn.Disabled.ShouldBeTrue();
    }

    [Test]
    public void GuessingPanel_ProcessBtn_Exists()
    {
        var btn = _panel.GetNodeOrNull<Button>("%ProcessBtn");
        btn.ShouldNotBeNull();
        btn.Text.ShouldBe("处理");
    }

    [Test]
    public void GuessingPanel_ProcessEmptyInput_ShowsError()
    {
        var processBtn = new ButtonDriver(() => _panel.GetNodeOrNull<Button>("%ProcessBtn"));
        processBtn.ClickCenter();

        var display = _panel.GetNodeOrNull<RichTextLabel>("%ResponseDisplay");
        display.ShouldNotBeNull();
        // Should show error about no boss selected or empty input
        display.Text.ShouldNotBeNullOrEmpty();
    }
}
