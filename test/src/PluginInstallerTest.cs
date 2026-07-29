namespace AutoCMEX;

using System.IO;
using AutoCMEX.Services;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// PluginInstaller unit tests.
/// </summary>
public class PluginInstallerTest : TestClass
{
  private string _tempDir = string.Empty;
  private string _sourceDir = string.Empty;
  private string _destDir = string.Empty;

  public PluginInstallerTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Test_" + System.Guid.NewGuid().ToString("N")[..8]
    );
    _sourceDir = Path.Combine(_tempDir, "source");
    _destDir = Path.Combine(_tempDir, "dest");
    Directory.CreateDirectory(_tempDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  [Test]
  public void CopyPluginDir_CopiesSingleFile()
  {
    // Arrange
    Directory.CreateDirectory(_sourceDir);
    File.WriteAllText(Path.Combine(_sourceDir, "plugin.json"), "{\"name\":\"test\"}");

    // Act
    PluginInstaller.CopyPluginDir(_sourceDir, _destDir);

    // Assert
    var destFile = Path.Combine(_destDir, "plugin.json");
    File.Exists(destFile).ShouldBeTrue();
    File.ReadAllText(destFile).ShouldBe("{\"name\":\"test\"}");
  }

  [Test]
  public void CopyPluginDir_CopiesNestedDirectories()
  {
    // Arrange
    var nestedDir = Path.Combine(_sourceDir, "sub", "deep");
    Directory.CreateDirectory(nestedDir);
    File.WriteAllText(Path.Combine(nestedDir, "data.txt"), "nested content");

    // Act
    PluginInstaller.CopyPluginDir(_sourceDir, _destDir);

    // Assert
    var destFile = Path.Combine(_destDir, "sub", "deep", "data.txt");
    File.Exists(destFile).ShouldBeTrue();
    File.ReadAllText(destFile).ShouldBe("nested content");
  }

  [Test]
  public void CopyPluginDir_CopiesMultipleFiles()
  {
    // Arrange
    Directory.CreateDirectory(_sourceDir);
    File.WriteAllText(Path.Combine(_sourceDir, "a.txt"), "aaa");
    File.WriteAllText(Path.Combine(_sourceDir, "b.txt"), "bbb");
    File.WriteAllText(Path.Combine(_sourceDir, "c.txt"), "ccc");

    // Act
    PluginInstaller.CopyPluginDir(_sourceDir, _destDir);

    // Assert
    File.Exists(Path.Combine(_destDir, "a.txt")).ShouldBeTrue();
    File.Exists(Path.Combine(_destDir, "b.txt")).ShouldBeTrue();
    File.Exists(Path.Combine(_destDir, "c.txt")).ShouldBeTrue();
  }

  [Test]
  public void CopyPluginDir_NonExistentSource_DoesNothing()
  {
    // Act — should not throw
    PluginInstaller.CopyPluginDir(Path.Combine(_tempDir, "nonexistent"), _destDir);

    // Assert
    Directory.Exists(_destDir).ShouldBeFalse();
  }

  [Test]
  public void CopyPluginDir_CreatesDestinationDirectory()
  {
    // Arrange
    Directory.CreateDirectory(_sourceDir);
    File.WriteAllText(Path.Combine(_sourceDir, "file.txt"), "content");

    // Act
    PluginInstaller.CopyPluginDir(_sourceDir, _destDir);

    // Assert
    Directory.Exists(_destDir).ShouldBeTrue();
  }
}
