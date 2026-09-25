namespace AutoCMEX;

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Recording;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 沙箱单测：清单同构、与真引擎的隔离、失效场景报错、清理与残留清扫。
/// </summary>
/// <remarks>
/// 用**假引擎目录**（只有清单要求的骨架 + 若干杂物），既快又能证明「杂物不会被带进沙箱」；
/// 真机沙箱见临时探针。
/// </remarks>
public class RecordingSandboxTest : TestClass
{
  private const string ExeFileName = "LuaSTGSub.exe";
  private const string PackFileName = "sample_pre_project.zip";
  private const string VersionMarker = "0.20.16-0.21.103";

  private string _root = string.Empty;
  private string _engineDir = string.Empty;
  private string _gameDir = string.Empty;
  private string _packPath = string.Empty;
  private string _sandboxRoot = string.Empty;
  private readonly Mock<ILog> _log = new();

  public RecordingSandboxTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Sandbox_" + Guid.NewGuid().ToString("N")[..8]
    );
    _engineDir = Path.Combine(_root, "LuaSTGSub");
    _gameDir = Path.Combine(_engineDir, EngineLocator.GameDirName);
    _sandboxRoot = Path.Combine(_root, "sandboxes");

    // 引擎骨架：清单要求的那部分
    Directory.CreateDirectory(_gameDir);
    File.WriteAllText(Path.Combine(_gameDir, EngineLocator.LaunchFileName), "launch");
    File.WriteAllText(Path.Combine(_gameDir, ExeFileName), "exe");
    File.WriteAllText(Path.Combine(_gameDir, "config.json"), "{}");
    File.WriteAllText(Path.Combine(_gameDir, "imgui.ini"), "[window]");
    File.WriteAllText(Path.Combine(_gameDir, VersionMarker), "marker");
    File.WriteAllText(Path.Combine(_gameDir, "d3dcompiler_47.dll"), "dll");

    WriteFile("packages/script/core.lua", "core");
    WriteFile("packages/script/deep/nested/leaf.lua", "leaf");
    WriteFile("plugins/plugins.json", "[]");
    WriteFile("plugins/autocmex/__init__.lua", "autocmex");
    WriteFile("plugins/[pluginpackage]danmaku_recorder_1.0.1/danmaku_recorder/recorder.lua", "rec");
    WriteFile("userdata/setting.json", "{\"volume\":1}");

    var modDir = Path.Combine(_gameDir, "mod");
    Directory.CreateDirectory(modDir);
    _packPath = Path.Combine(modDir, PackFileName);
    File.WriteAllText(_packPath, "zip");

    // 杂物：根级日志、旁挂脚本、第二个引擎 exe、陈旧产物目录
    File.WriteAllText(Path.Combine(_gameDir, "engine.log"), "noise");
    File.WriteAllText(Path.Combine(_gameDir, "noise_spell_1.txt"), "noise");
    File.WriteAllText(Path.Combine(_gameDir, "LuaSTGSub_old.exe"), "noise");
    var staleRecorder = Path.Combine(_gameDir, "danmaku_recorder", "output");
    Directory.CreateDirectory(staleRecorder);
    File.WriteAllText(Path.Combine(staleRecorder, "old.gif"), "stale");
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  private void WriteFile(string relativePath, string content)
  {
    var path = Path.Combine(_gameDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
  }

  private Task<RecordingSandbox> CreateAsync(int workerIndex = 1) =>
    RecordingSandbox.CreateAsync(
      _engineDir,
      _packPath,
      ExeFileName,
      _sandboxRoot,
      workerIndex,
      _log.Object,
      CancellationToken.None
    );

  [Test]
  public async Task CreateAsync_MirrorsWhitelistAndPack()
  {
    var sandbox = await CreateAsync(workerIndex: 3);

    sandbox.WorkerIndex.ShouldBe(3);
    sandbox.EngineDir.ShouldStartWith(_sandboxRoot);
    sandbox.GameDir.ShouldBe(EngineLocator.GetGameDir(sandbox.EngineDir));

    // 清单内的：只读数据、可写文件与版本标记、被测包
    File.Exists(Path.Combine(sandbox.GameDir, "packages", "script", "core.lua")).ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, "packages", "script", "deep", "nested", "leaf.lua"))
      .ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, "plugins", "plugins.json")).ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, "plugins", "autocmex", "__init__.lua"))
      .ShouldBeTrue();
    File.Exists(
        Path.Combine(
          sandbox.GameDir,
          "plugins",
          "[pluginpackage]danmaku_recorder_1.0.1",
          "danmaku_recorder",
          "recorder.lua"
        )
      )
      .ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, "userdata", "setting.json")).ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, EngineLocator.LaunchFileName)).ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, ExeFileName)).ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, VersionMarker)).ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, "d3dcompiler_47.dll")).ShouldBeTrue();
    File.Exists(Path.Combine(sandbox.GameDir, "mod", PackFileName)).ShouldBeTrue();

    // 任务通道
    Directory.Exists(RecordingJobWriter.GetJobsDir(sandbox.EngineDir)).ShouldBeTrue();
    Directory.Exists(RecordingJobWriter.GetResultsDir(sandbox.EngineDir)).ShouldBeTrue();

    // 清单外的杂物一律不带
    File.Exists(Path.Combine(sandbox.GameDir, "engine.log")).ShouldBeFalse();
    File.Exists(Path.Combine(sandbox.GameDir, "noise_spell_1.txt")).ShouldBeFalse();
    File.Exists(Path.Combine(sandbox.GameDir, "LuaSTGSub_old.exe")).ShouldBeFalse();
  }

  [Test]
  public async Task CreateAsync_DoesNotPrecreateRecorderDirectory()
  {
    var sandbox = await CreateAsync();

    // 录制器只在 danmaku_recorder 不存在时才创建 output/，预建会让 ffmpeg 无输出（真机踩过）
    Directory.Exists(Path.Combine(sandbox.GameDir, "danmaku_recorder")).ShouldBeFalse();
  }

  [Test]
  public async Task CreateAsync_SandboxWritesLeaveSourceUntouched()
  {
    var sandbox = await CreateAsync();
    var sourcePluginsJson = Path.Combine(_gameDir, "plugins", "plugins.json");
    var sourceConfig = Path.Combine(_gameDir, "config.json");

    File.WriteAllText(Path.Combine(sandbox.GameDir, "plugins", "plugins.json"), "[\"hacked\"]");
    File.WriteAllText(Path.Combine(sandbox.GameDir, "config.json"), "{\"hacked\":true}");
    File.WriteAllText(Path.Combine(sandbox.GameDir, "new_file.txt"), "new");
    File.WriteAllText(Path.Combine(sandbox.GameDir, "mod", "extra.zip"), "extra");

    File.ReadAllText(sourcePluginsJson).ShouldBe("[]");
    File.ReadAllText(sourceConfig).ShouldBe("{}");
    File.Exists(Path.Combine(_gameDir, "new_file.txt")).ShouldBeFalse();
    File.Exists(Path.Combine(_gameDir, "mod", "extra.zip")).ShouldBeFalse();
  }

  [Test]
  public async Task CreateAsync_MissingPlugin_ThrowsWithUserFacingReason()
  {
    Directory.Delete(Path.Combine(_gameDir, "plugins", "autocmex"), recursive: true);

    var error = await Should.ThrowAsync<RecordingSandboxException>(() => CreateAsync());

    error.Message.ShouldContain("插件");
    Directory.Exists(_sandboxRoot).ShouldBeFalse(); // 校验没过就不该动磁盘
  }

  [Test]
  public async Task CreateAsync_MissingPluginEntryFile_ThrowsWithInstallHint()
  {
    File.Delete(Path.Combine(_gameDir, "plugins", "autocmex", "__init__.lua"));

    var error = await Should.ThrowAsync<RecordingSandboxException>(() => CreateAsync());

    // 目录在但入口文件不在：引擎加载不到插件，与「没装」同等处理，文案沿用设置页那句指引
    error.Message.ShouldBe("录制插件未安装到引擎，请先在设置页安装或启用插件");
    Directory.Exists(_sandboxRoot).ShouldBeFalse();
  }

  [Test]
  public async Task CreateAsync_DisabledPlugin_ThrowsWithEnableHint()
  {
    WriteFile("plugins/plugins.json", """[{ "name": "autocmex", "enable": false }]""");

    var error = await Should.ThrowAsync<RecordingSandboxException>(() => CreateAsync());

    error.Message.ShouldBe("录制插件未启用，请先在设置页启用插件");
    Directory.Exists(_sandboxRoot).ShouldBeFalse();
  }

  [Test]
  public async Task CreateAsync_WithoutManifest_ThrowsBeforeTouchingDisk()
  {
    var manifestPath = Path.Combine(_gameDir, "plugins", "plugins.json");
    File.Delete(manifestPath);

    var error = await Should.ThrowAsync<RecordingSandboxException>(() => CreateAsync());

    // 清单不在，引擎一个插件都不会加载：起录只会白跑一轮
    error.Message.ShouldBe($"引擎插件清单不存在：{manifestPath}");
    Directory.Exists(_sandboxRoot).ShouldBeFalse();
  }

  [Test]
  public async Task CreateAsync_WithoutRecorder_ThrowsWithThirdPartyHint()
  {
    Directory.Delete(
      Path.Combine(_gameDir, "plugins", "[pluginpackage]danmaku_recorder_1.0.1"),
      recursive: true
    );

    var error = await Should.ThrowAsync<RecordingSandboxException>(() => CreateAsync());

    error.Message.ShouldBe("未找到弹幕录制器插件，请先在设置页安装或启用插件");
    Directory.Exists(_sandboxRoot).ShouldBeFalse();
  }

  [Test]
  public void TryValidateRoot_WithEmptyConfig_FallsBackToDefaultDirAndCreatesIt()
  {
    var ok = RecordingSandbox.TryValidateRoot(null, out var root, out var reason);

    ok.ShouldBeTrue();
    reason.ShouldBeEmpty();
    root.ShouldBe(RecordingSandbox.GetDefaultRootDir());
    Directory.Exists(root).ShouldBeTrue();
    // 探针只证明「能写」，不留文件
    Directory.EnumerateFiles(root, ".autocmex-probe-*").ShouldBeEmpty();
  }

  [Test]
  public void TryValidateRoot_WithMissingConfiguredDir_RejectsInsteadOfCreating()
  {
    var missing = Path.Combine(_root, "not-created-yet");

    RecordingSandbox.TryValidateRoot(missing, out var root, out var reason).ShouldBeFalse();

    root.ShouldBe(missing);
    reason.ShouldContain("沙箱根目录不存在");
    // 手选的目录必须已存在：路径很可能是用户打错的，不替他造
    Directory.Exists(missing).ShouldBeFalse();
  }

  [Test]
  public void TryValidateRoot_WithFileInsteadOfDir_Rejects()
  {
    var filePath = Path.Combine(_root, "picked-a-file.txt");
    File.WriteAllText(filePath, "not a dir");

    RecordingSandbox.TryValidateRoot(filePath, out _, out var reason).ShouldBeFalse();

    reason.ShouldContain("沙箱根目录不存在");
  }

  [Test]
  public void TryValidateRoot_WithWritableDir_AcceptsAndLeavesNoProbeFile()
  {
    Directory.CreateDirectory(_sandboxRoot);

    RecordingSandbox.TryValidateRoot(_sandboxRoot, out var root, out var reason).ShouldBeTrue();

    root.ShouldBe(_sandboxRoot);
    reason.ShouldBeEmpty();
    Directory.EnumerateFileSystemEntries(_sandboxRoot).ShouldBeEmpty();
  }

  [Test]
  public async Task CreateAsync_MissingEngineExe_Throws()
  {
    File.Delete(Path.Combine(_gameDir, ExeFileName));

    var error = await Should.ThrowAsync<RecordingSandboxException>(() => CreateAsync());

    error.Message.ShouldContain(ExeFileName);
  }

  [Test]
  public async Task CreateAsync_MissingPack_Throws()
  {
    File.Delete(_packPath);

    var error = await Should.ThrowAsync<RecordingSandboxException>(() => CreateAsync());

    error.Message.ShouldContain(PackFileName);
  }

  [Test]
  public async Task Delete_IsIdempotentAndRemovesTree()
  {
    var sandbox = await CreateAsync();
    var dir = sandbox.EngineDir;

    sandbox.Delete();
    Directory.Exists(dir).ShouldBeFalse();

    Should.NotThrow(sandbox.Delete); // 再删一次不炸
  }

  [Test]
  public void SweepLeftovers_RemovesOnlyOwnStaleSandboxes()
  {
    Directory.CreateDirectory(_sandboxRoot);
    var stale = Path.Combine(_sandboxRoot, "sb_20000101_000000_000_abcd_w1");
    var fresh = Path.Combine(
      _sandboxRoot,
      $"sb_{DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)}_abcd_w1"
    );
    var foreign = Path.Combine(_sandboxRoot, "otherapp_cache");
    foreach (var dir in new[] { stale, fresh, foreign })
    {
      Directory.CreateDirectory(dir);
      File.WriteAllText(Path.Combine(dir, "x.txt"), "x");
    }

    RecordingSandbox
      .SweepLeftovers(_sandboxRoot, RecordingSandbox.LeftoverMaxAge, _log.Object)
      .ShouldBe(1);

    Directory.Exists(stale).ShouldBeFalse();
    Directory.Exists(fresh).ShouldBeTrue();
    Directory.Exists(foreign).ShouldBeTrue();

    // 根目录不存在时不该炸
    RecordingSandbox
      .SweepLeftovers(Path.Combine(_root, "nope"), RecordingSandbox.LeftoverMaxAge, _log.Object)
      .ShouldBe(0);
  }

  [Test]
  public void EstimateFootprint_CountsMirroredContentPlusPack()
  {
    var footprint = RecordingSandbox.EstimateFootprint(_engineDir, _packPath, ExeFileName);

    // 骨架内容 + 包；杂物（engine.log 等）不计
    var expected =
      new FileInfo(Path.Combine(_gameDir, "packages", "script", "core.lua")).Length
      + new FileInfo(
        Path.Combine(_gameDir, "packages", "script", "deep", "nested", "leaf.lua")
      ).Length
      + new FileInfo(Path.Combine(_gameDir, "plugins", "plugins.json")).Length
      + new FileInfo(Path.Combine(_gameDir, "plugins", "autocmex", "__init__.lua")).Length
      + new FileInfo(
        Path.Combine(
          _gameDir,
          "plugins",
          "[pluginpackage]danmaku_recorder_1.0.1",
          "danmaku_recorder",
          "recorder.lua"
        )
      ).Length
      + new FileInfo(Path.Combine(_gameDir, "userdata", "setting.json")).Length
      + new FileInfo(Path.Combine(_gameDir, EngineLocator.LaunchFileName)).Length
      + new FileInfo(Path.Combine(_gameDir, "config.json")).Length
      + new FileInfo(Path.Combine(_gameDir, "imgui.ini")).Length
      + new FileInfo(Path.Combine(_gameDir, VersionMarker)).Length
      + new FileInfo(Path.Combine(_gameDir, "d3dcompiler_47.dll")).Length
      + new FileInfo(Path.Combine(_gameDir, ExeFileName)).Length
      + new FileInfo(_packPath).Length;

    footprint.ShouldBe(expected);
  }

  [Test]
  public void ResolveRootDir_FallsBackToTempDirectory()
  {
    RecordingSandbox.ResolveRootDir(string.Empty).ShouldBe(RecordingSandbox.GetDefaultRootDir());
    RecordingSandbox.ResolveRootDir(null).ShouldBe(RecordingSandbox.GetDefaultRootDir());
    RecordingSandbox.ResolveRootDir(@"D:\mine").ShouldBe(@"D:\mine");
  }
}
