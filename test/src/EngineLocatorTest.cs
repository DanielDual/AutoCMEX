namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Recording;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 引擎目录校验、可执行文件定位与从 Sharp 目录推断的单测：覆盖有效/无效目录、
/// 通配定位的优先级以及推断失败的分支。
/// </summary>
public class EngineLocatorTest : TestClass
{
  private string _root = string.Empty;
  private string _engineDir = string.Empty;
  private string _gameDir = string.Empty;

  public EngineLocatorTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_EngineLocator_" + Guid.NewGuid().ToString("N")[..8]
    );
    _engineDir = Path.Combine(_root, "LuaSTGSub");
    _gameDir = Path.Combine(_engineDir, EngineLocator.GameDirName);
    Directory.CreateDirectory(_gameDir);
    File.WriteAllText(Path.Combine(_gameDir, EngineLocator.LaunchFileName), string.Empty);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  /// <summary>在 game/ 下造一个空的引擎可执行文件。</summary>
  /// <param name="fileName">文件名。</param>
  private void CreateExe(string fileName) =>
    File.WriteAllText(Path.Combine(_gameDir, fileName), string.Empty);

  [Test]
  public void Validate_EngineDirWithLaunch_ReturnsTrue()
  {
    EngineLocator.TryValidate(_engineDir, out var reason).ShouldBeTrue();
    reason.ShouldBeEmpty();
    EngineLocator.GetGameDir(_engineDir).ShouldBe(_gameDir);
  }

  [Test]
  public void Validate_EmptyOrIncompleteDir_ReturnsFalseWithReason()
  {
    EngineLocator.TryValidate(null, out var emptyReason).ShouldBeFalse();
    emptyReason.ShouldBe("未设置引擎目录");

    EngineLocator.TryValidate(string.Empty, out var blankReason).ShouldBeFalse();
    blankReason.ShouldBe("未设置引擎目录");

    var missing = Path.Combine(_root, "not_exists");
    EngineLocator.TryValidate(missing, out var missingReason).ShouldBeFalse();
    missingReason.ShouldBe("引擎目录不存在");

    var noGameDir = Path.Combine(_root, "no_game");
    Directory.CreateDirectory(noGameDir);
    EngineLocator.TryValidate(noGameDir, out var noGameReason).ShouldBeFalse();
    noGameReason.ShouldBe("目录内没有 game/ 子目录");

    // game/ 在但缺少 launch：最常见的选择错误（选到了工程包目录之类）
    var noLaunch = Path.Combine(_root, "no_launch");
    Directory.CreateDirectory(Path.Combine(noLaunch, EngineLocator.GameDirName));
    EngineLocator.TryValidate(noLaunch, out var noLaunchReason).ShouldBeFalse();
    noLaunchReason.ShouldContain("launch");
  }

  [Test]
  public void FindEngineExe_PlainAndVersionedExist_PrefersPlain()
  {
    CreateExe("LuaSTGSub-v0.21.129.exe");
    CreateExe("LuaSTGSub.exe");

    EngineLocator.FindEngineExe(_engineDir).ShouldBe(Path.Combine(_gameDir, "LuaSTGSub.exe"));
  }

  [Test]
  public void FindEngineExe_OnlyVersioned_FallsBackToPattern()
  {
    CreateExe("LuaSTGSub+v0.20.16.exe");

    EngineLocator
      .FindEngineExe(_engineDir)
      .ShouldBe(Path.Combine(_gameDir, "LuaSTGSub+v0.20.16.exe"));
  }

  [Test]
  public void FindEngineExe_ExeOrDirMissing_ReturnsNull()
  {
    EngineLocator.FindEngineExe(_engineDir).ShouldBeNull();
    EngineLocator.FindEngineExe(null).ShouldBeNull();
    EngineLocator.FindEngineExe(Path.Combine(_root, "not_exists")).ShouldBeNull();
  }

  [Test]
  public void Infer_SharpSubDirOrExe_WalksUpToEngineDir()
  {
    var sharpDir = Path.Combine(_engineDir, "tools", "sharp", "editor");
    Directory.CreateDirectory(sharpDir);

    EngineLocator.TryInferFromSharpDir(sharpDir).ShouldBe(_engineDir);
    EngineLocator.TryInferFromSharpDir(_engineDir).ShouldBe(_engineDir);

    // 传可执行文件路径时按所在目录上溯
    var sharpExe = Path.Combine(sharpDir, "SharpEditor.exe");
    File.WriteAllText(sharpExe, string.Empty);
    EngineLocator.TryInferFromSharpDir(sharpExe).ShouldBe(_engineDir);
  }

  [Test]
  public void Infer_TooDeepOrUnset_ReturnsNull()
  {
    var deep = Path.Combine(_engineDir, "a", "b", "c", "d", "e");
    Directory.CreateDirectory(deep);

    EngineLocator.TryInferFromSharpDir(deep).ShouldBeNull();
    EngineLocator.TryInferFromSharpDir(null).ShouldBeNull();
    EngineLocator.TryInferFromSharpDir(string.Empty).ShouldBeNull();
    EngineLocator.TryInferFromSharpDir(Path.Combine(_root, "no_such_dir")).ShouldBeNull();
  }
}
