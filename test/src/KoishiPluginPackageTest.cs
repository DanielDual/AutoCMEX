namespace AutoCMEX;

using AutoCMEX.Services;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// Guards the shipped Koishi plugin package against the install spec: the console
/// resolves plugins by package short name and by koishi.category, and loads the module
/// pointed to by package.json main, so all three must stay aligned with KoishiPluginSpec.
/// </summary>
public class KoishiPluginPackageTest : TestClass
{
  private Godot.Collections.Dictionary _manifest = new();

  public KoishiPluginPackageTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    var text = FileAccess.GetFileAsString(KoishiPluginSpec.SourceDir + "package.json");
    text.ShouldNotBeNullOrEmpty();
    _manifest = Json.ParseString(text).AsGodotDictionary();
  }

  [Test]
  public void PackageName_IsTheKoishiPluginPackageName()
  {
    // The console scan turns this into the short name used by the
    // "add plugin" search box.
    _manifest["name"].AsString().ShouldBe(KoishiPluginSpec.PackageName);
  }

  [Test]
  public void MainEntry_ExistsAndLoadsWithoutABuildStep()
  {
    // Arrange
    var main = _manifest["main"].AsString();
    main.ShouldNotBeNullOrEmpty();

    // Act
    var entryPath = KoishiPluginSpec.SourceDir + main;

    // Assert — a main entry that is never produced makes yarn start fail.
    FileAccess.FileExists(entryPath).ShouldBeTrue();
    FileAccess
      .GetFileAsString(entryPath)
      .ShouldContain($"module.exports.name = \"{KoishiPluginSpec.ShortName}\"");
  }

  [Test]
  public void KoishiCategory_IsAdapter()
  {
    // Without koishi.category the console files the plugin under 未分类 only,
    // so it is missing from the 适配器 tab users look at.
    _manifest["koishi"].AsGodotDictionary()["category"].AsString().ShouldBe("adapter");
  }

  [Test]
  public void PluginSource_IsPresent()
  {
    FileAccess.FileExists(KoishiPluginSpec.SourceDir + "package.json").ShouldBeTrue();
  }
}
