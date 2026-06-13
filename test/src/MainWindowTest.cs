namespace AutoCMEX;

using System.Threading.Tasks;
using AutoCMEX.UI.Main;
using Chickensoft.GoDotTest;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
using Godot;
using Shouldly;

/// <summary>
/// MainWindow UI 集成测试
/// </summary>
public class MainWindowTest : TestClass
{
  private Fixture _fixture = default!;
  private MainWindow _mainWindow = default!;

  public MainWindowTest(Node testScene)
    : base(testScene) { }

  [SetupAll]
  public async Task Setup()
  {
    _fixture = new Fixture(TestScene.GetTree());
    _mainWindow = await _fixture.LoadAndAddScene<MainWindow>();
  }

  [CleanupAll]
  public void Cleanup() => _fixture.Cleanup();

  [Test]
  public void MainWindow_LoadsSuccessfully()
  {
    _mainWindow.ShouldNotBeNull();
  }

  [Test]
  public void MainWindow_HasLeftPanelWidth()
  {
    _mainWindow.LeftPanelWidth.ShouldBe(200);
  }

  [Test]
  public void MainWindow_IsControlType()
  {
    _mainWindow.ShouldBeAssignableTo<Control>();
  }
}
