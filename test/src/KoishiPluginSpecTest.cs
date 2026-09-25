namespace AutoCMEX;

using System.IO;
using AutoCMEX.Services;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// KoishiPluginSpec unit tests.
/// </summary>
public class KoishiPluginSpecTest : TestClass
{
  private string _tempDir = string.Empty;
  private string _appRoot = string.Empty;
  private string _externalDir = string.Empty;

  public KoishiPluginSpecTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_KoishiSpec_" + System.Guid.NewGuid().ToString("N")[..8]
    );
    _appRoot = Path.Combine(_tempDir, "koishi-app");
    _externalDir = Path.Combine(_appRoot, KoishiPluginSpec.ExternalDirName);
    Directory.CreateDirectory(_externalDir);
    File.WriteAllText(Path.Combine(_appRoot, KoishiPluginSpec.AppManifestFileName), "plugins: {}");
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  [Test]
  public void PackageName_MatchesShortName()
  {
    // The console indexes plugins by short name, so the package name must be the
    // short name prefixed with koishi-plugin-.
    KoishiPluginSpec.PackageName.ShouldBe("koishi-plugin-" + KoishiPluginSpec.ShortName);
  }

  [Test]
  public void TryResolve_AppRoot_InstallsIntoExternalShortName()
  {
    // Act
    var ok = KoishiPluginSpec.TryResolve(_appRoot, out var plan, out var error);

    // Assert
    ok.ShouldBeTrue();
    error.ShouldBeEmpty();
    plan.AppRoot.ShouldBe(_appRoot);
    plan.PluginsDir.ShouldBe(_externalDir);
    plan.InstallDir.ShouldBe(Path.Combine(_externalDir, KoishiPluginSpec.ShortName));
    plan.InstallDir.ShouldEndWith(KoishiPluginSpec.ShortName);
  }

  [Test]
  public void TryResolve_ExternalDir_ResolvesParentAppRoot()
  {
    // Act
    var ok = KoishiPluginSpec.TryResolve(_externalDir, out var plan, out _);

    // Assert
    ok.ShouldBeTrue();
    plan.AppRoot.ShouldBe(_appRoot);
    plan.InstallDir.ShouldBe(Path.Combine(_externalDir, KoishiPluginSpec.ShortName));
  }

  [Test]
  public void TryResolve_TrailingSeparator_ResolvesSamePlan()
  {
    // Act
    var ok = KoishiPluginSpec.TryResolve(
      _appRoot + Path.DirectorySeparatorChar,
      out var plan,
      out _
    );

    // Assert
    ok.ShouldBeTrue();
    plan.InstallDir.ShouldBe(Path.Combine(_externalDir, KoishiPluginSpec.ShortName));
  }

  [Test]
  public void TryResolve_UnrelatedDirectory_FailsWithoutPlan()
  {
    // Arrange
    var unrelated = Path.Combine(_tempDir, "somewhere-else");
    Directory.CreateDirectory(unrelated);

    // Act
    var ok = KoishiPluginSpec.TryResolve(unrelated, out var plan, out var error);

    // Assert
    ok.ShouldBeFalse();
    plan.ShouldBeNull();
    error.ShouldContain(unrelated);
    error.ShouldContain(KoishiPluginSpec.AppManifestFileName);
  }

  [Test]
  public void TryResolve_ExternalDirOfNonKoishiApp_Fails()
  {
    // Arrange: a directory that happens to be called external but has no Koishi
    // manifest next to it, so it must not be accepted on its name alone.
    var orphanExternal = Path.Combine(
      _tempDir,
      "not-a-koishi-app",
      KoishiPluginSpec.ExternalDirName
    );
    Directory.CreateDirectory(orphanExternal);

    // Act
    var ok = KoishiPluginSpec.TryResolve(orphanExternal, out var plan, out var error);

    // Assert
    ok.ShouldBeFalse();
    plan.ShouldBeNull();
    error.ShouldNotBeEmpty();
  }

  [Test]
  public void TryResolve_EmptySelection_Fails()
  {
    // Act
    var ok = KoishiPluginSpec.TryResolve("   ", out var plan, out var error);

    // Assert
    ok.ShouldBeFalse();
    plan.ShouldBeNull();
    error.ShouldNotBeEmpty();
  }

  [Test]
  public void TryResolve_MissingLinkAndLegacyDir_ReportsBoth()
  {
    // Arrange
    Directory.CreateDirectory(Path.Combine(_externalDir, KoishiPluginSpec.LegacyDirName));

    // Act
    var ok = KoishiPluginSpec.TryResolve(_appRoot, out var plan, out _);

    // Assert
    ok.ShouldBeTrue();
    plan.LegacyDirExists.ShouldBeTrue();
    plan.LinkExists.ShouldBeFalse();
  }

  [Test]
  public void TryResolve_WorkspaceLinkPresent_IsDetected()
  {
    // Arrange
    Directory.CreateDirectory(
      Path.Combine(_appRoot, KoishiPluginSpec.NodeModulesDirName, KoishiPluginSpec.PackageName)
    );

    // Act
    var ok = KoishiPluginSpec.TryResolve(_appRoot, out var plan, out _);

    // Assert
    ok.ShouldBeTrue();
    plan.LinkExists.ShouldBeTrue();
  }

  [Test]
  public void DescribeInstalled_GivesEnablePathAndSearchKeyword()
  {
    // Arrange
    KoishiPluginSpec.TryResolve(_appRoot, out var plan, out _);

    // Act
    var text = KoishiPluginSpec.DescribeInstalled(plan);

    // Assert
    text.ShouldContain(plan.InstallDir);
    text.ShouldContain(KoishiPluginSpec.ShortName);
    text.ShouldContain("添加插件");
  }

  [Test]
  public void DescribeInstalled_WithoutLink_AsksForPackageManagerInstall()
  {
    // Arrange
    KoishiPluginSpec.TryResolve(_appRoot, out var plan, out _);

    // Act
    var text = KoishiPluginSpec.DescribeInstalled(plan);

    // Assert
    text.ShouldContain("yarn install");
    text.ShouldContain(plan.AppRoot);
  }

  [Test]
  public void DescribeInstalled_WithLink_OmitsInstallHint()
  {
    // Arrange
    Directory.CreateDirectory(
      Path.Combine(_appRoot, KoishiPluginSpec.NodeModulesDirName, KoishiPluginSpec.PackageName)
    );
    KoishiPluginSpec.TryResolve(_appRoot, out var plan, out _);

    // Act
    var text = KoishiPluginSpec.DescribeInstalled(plan);

    // Assert
    text.ShouldNotContain("yarn install");
  }

  [Test]
  public void DescribeInstalled_LegacyDir_AsksToMoveItAway()
  {
    // Arrange
    var legacyDir = Path.Combine(_externalDir, KoishiPluginSpec.LegacyDirName);
    Directory.CreateDirectory(legacyDir);
    KoishiPluginSpec.TryResolve(_appRoot, out var plan, out _);

    // Act
    var text = KoishiPluginSpec.DescribeInstalled(plan);

    // Assert
    text.ShouldContain(legacyDir);
  }
}
