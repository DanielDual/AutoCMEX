namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using AutoCMEX.Core.Recording;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 插件部署单测：三态判定、一键安装的幂等与备份、一键启用的最小改动，以及「只读检查不写盘」。
/// </summary>
/// <remarks>
/// 用**真实临时引擎目录**而不是替身：这里的核心价值就是「盘上结果对不对」（复制了什么、清单被改成
/// 什么样、备份在不在），替身只会把断言变成对 mock 调用次数的检查。
/// </remarks>
public class PluginDeployerTest : TestClass
{
  private const string RecorderDirName = "[pluginpackage]danmaku_recorder_1.0.1";

  private string _root = string.Empty;
  private string _engineDir = string.Empty;
  private string _pluginsDir = string.Empty;
  private string _manifestPath = string.Empty;

  public PluginDeployerTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Deploy_" + Guid.NewGuid().ToString("N")[..8]
    );
    _engineDir = Path.Combine(_root, "LuaSTGSub");
    var gameDir = Path.Combine(_engineDir, EngineLocator.GameDirName);
    _pluginsDir = Path.Combine(gameDir, PluginDeployer.PluginsDirName);
    _manifestPath = Path.Combine(_pluginsDir, PluginDeployer.ManifestFileName);

    // 引擎骨架：校验只认 game/launch，可执行文件用于引擎目录状态行的文案
    Directory.CreateDirectory(_pluginsDir);
    File.WriteAllText(Path.Combine(gameDir, EngineLocator.LaunchFileName), "launch");
    File.WriteAllText(Path.Combine(gameDir, "LuaSTGSub.exe"), "exe");
    WriteManifest("[]");
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  [Test]
  public void Inspect_WithoutEngineDir_MarksBothPluginsUnusable()
  {
    var inspection = PluginDeployer.Inspect(string.Empty);

    inspection.EngineDirUsable.ShouldBeFalse();
    inspection.AllReady.ShouldBeFalse();
    inspection.Autocmex.State.ShouldBe(RecordingPluginState.Missing);
    inspection.Recorder.State.ShouldBe(RecordingPluginState.Missing);
    // 引擎目录都没定，不能给出任何可执行的部署动作
    inspection.Autocmex.CanInstall.ShouldBeFalse();
    inspection.Recorder.CanEnable.ShouldBeFalse();
    inspection.Autocmex.Detail.ShouldContain("未设置引擎目录");
  }

  [Test]
  public void Inspect_WithoutManifest_ReportsBrokenAndBlocksDeploy()
  {
    File.Delete(_manifestPath);

    var inspection = PluginDeployer.Inspect(_engineDir);

    inspection.EngineDirUsable.ShouldBeTrue();
    inspection.ManifestExists.ShouldBeFalse();
    inspection.Autocmex.State.ShouldBe(RecordingPluginState.ManifestBroken);
    inspection.Recorder.State.ShouldBe(RecordingPluginState.ManifestBroken);
    inspection.Autocmex.CanInstall.ShouldBeFalse();
    inspection.Recorder.CanEnable.ShouldBeFalse();
    inspection.Autocmex.Detail.ShouldContain("文件不存在");
  }

  [Test]
  public void Inspect_WithoutPlugins_ReportsMissingWithInstallabilityBySource()
  {
    var inspection = PluginDeployer.Inspect(_engineDir);

    inspection.Autocmex.State.ShouldBe(RecordingPluginState.Missing);
    // 自带插件可一键安装；第三方录制器只能由用户自行放入
    inspection.Autocmex.CanInstall.ShouldBeTrue();
    inspection.Recorder.State.ShouldBe(RecordingPluginState.Missing);
    inspection.Recorder.CanInstall.ShouldBeFalse();
    inspection.Recorder.Detail.ShouldContain("第三方插件");
    inspection.AllReady.ShouldBeFalse();
  }

  [Test]
  public void Inspect_PluginDirsWithoutManifestEntries_AreReadyByEngineDefault()
  {
    WritePluginFile($"{PluginDeployer.AutocmexPluginDirName}/__init__.lua", "autocmex");
    WritePluginFile($"{RecorderDirName}/recorder.lua", "rec");

    var inspection = PluginDeployer.Inspect(_engineDir);

    inspection.Autocmex.State.ShouldBe(RecordingPluginState.Ready);
    inspection.Recorder.State.ShouldBe(RecordingPluginState.Ready);
    inspection.AllReady.ShouldBeTrue();
    inspection.Autocmex.Detail.ShouldContain("引擎会自动登记并启用");
  }

  [Test]
  public void Inspect_DisabledEntries_ReportInstalledDisabledAndEnableable()
  {
    WritePluginFile($"{PluginDeployer.AutocmexPluginDirName}/__init__.lua", "autocmex");
    WritePluginFile($"{RecorderDirName}/recorder.lua", "rec");
    WriteManifest(
      $$"""
      [
        { "name": "{{PluginDeployer.AutocmexPluginDirName}}", "path": "plugins/{{PluginDeployer.AutocmexPluginDirName}}/", "enable": false },
        { "path": "plugins/{{RecorderDirName}}/", "enable": false }
      ]
      """
    );

    var inspection = PluginDeployer.Inspect(_engineDir);

    inspection.Autocmex.State.ShouldBe(RecordingPluginState.InstalledDisabled);
    inspection.Autocmex.Installed.ShouldBeTrue();
    inspection.Autocmex.Enabled.ShouldBeFalse();
    inspection.Autocmex.CanEnable.ShouldBeTrue();
    // 自家插件即使已装也可重装（升级后刷新插件文件），第三方录制器不可
    inspection.Autocmex.CanInstall.ShouldBeTrue();
    inspection.Recorder.CanInstall.ShouldBeFalse();
    inspection.Recorder.State.ShouldBe(RecordingPluginState.InstalledDisabled);
    inspection.Recorder.CanEnable.ShouldBeTrue();
    inspection.AllReady.ShouldBeFalse();
  }

  [Test]
  public void Inspect_DoesNotTouchEngineDir()
  {
    WritePluginFile($"{PluginDeployer.AutocmexPluginDirName}/__init__.lua", "autocmex");
    WritePluginFile($"{RecorderDirName}/recorder.lua", "rec");
    var before = SnapshotTree(_root);

    PluginDeployer.Inspect(_engineDir);

    SnapshotTree(_root).ShouldBe(before);
  }

  [Test]
  public void InstallAutocmex_CopiesPluginAndRegistersEnabledEntry()
  {
    var message = PluginDeployer.InstallAutocmex(_engineDir);

    File.Exists(
        Path.Combine(
          _pluginsDir,
          PluginDeployer.AutocmexPluginDirName,
          PluginDeployer.AutocmexEntryFileName
        )
      )
      .ShouldBeTrue();
    var entry = Entry(PluginDeployer.AutocmexPluginDirName);
    entry.ShouldNotBeNull();
    entry!["enable"]!.GetValue<bool>().ShouldBeTrue();
    entry["path"]!.GetValue<string>().ShouldBe("plugins/autocmex/");
    message.ShouldContain("已安装并启用");
    // 装完立刻检查就应显示就绪，不留「装完还没生效」的中间态
    PluginDeployer.Inspect(_engineDir).Autocmex.State.ShouldBe(RecordingPluginState.Ready);
  }

  [Test]
  public void InstallAutocmex_IsIdempotentAndPreservesOtherEntries()
  {
    WritePluginFile($"{PluginDeployer.AutocmexPluginDirName}/stale.lua", "stale");
    WriteManifest(
      $$"""
      [
        { "name": "custom_other", "path": "plugins/custom_other/", "enable": true, "note": "keep" },
        { "path": "plugins/{{RecorderDirName}}/", "enable": false }
      ]
      """
    );

    var first = PluginDeployer.InstallAutocmex(_engineDir);
    var second = PluginDeployer.InstallAutocmex(_engineDir);

    // 首次安装把旧目录改名备份，且只备份一次
    first.ShouldContain(PluginDeployer.AutocmexPluginDirName + PluginDeployer.BackupSuffix);
    second.ShouldNotContain(PluginDeployer.BackupSuffix);
    Directory
      .Exists(
        Path.Combine(
          _pluginsDir,
          PluginDeployer.AutocmexPluginDirName + PluginDeployer.BackupSuffix
        )
      )
      .ShouldBeTrue();
    Directory
      .Exists(
        Path.Combine(
          _pluginsDir,
          PluginDeployer.AutocmexPluginDirName
            + PluginDeployer.BackupSuffix
            + PluginDeployer.BackupSuffix
        )
      )
      .ShouldBeFalse();
    File.Exists(
        Path.Combine(
          _pluginsDir,
          PluginDeployer.AutocmexPluginDirName,
          PluginDeployer.AutocmexEntryFileName
        )
      )
      .ShouldBeTrue();

    var entries = ManifestEntries();
    entries.Count.ShouldBe(3);
    Entry(PluginDeployer.AutocmexPluginDirName)!["enable"]!.GetValue<bool>().ShouldBeTrue();
    // 与本次安装无关的条目必须逐字保留（含自定义字段），否则会毁掉用户的插件配置
    var other = entries.First(e => e["name"]?.GetValue<string>() == "custom_other");
    other["note"]!.GetValue<string>().ShouldBe("keep");
    other["enable"]!.GetValue<bool>().ShouldBeTrue();
    entries.First(e =>
      e["path"]!.GetValue<string>().Contains(PluginDeployer.RecorderPluginKeyword)
    )["enable"]!
      .GetValue<bool>()
      .ShouldBeFalse();
  }

  [Test]
  public void InstallAutocmex_WithBrokenManifest_ThrowsWithoutSideEffects()
  {
    var broken = "{ not json";
    WriteManifest(broken);

    Should
      .Throw<PluginDeployException>(() => PluginDeployer.InstallAutocmex(_engineDir))
      .Message.ShouldContain("插件清单损坏");

    File.ReadAllText(_manifestPath).ShouldBe(broken);
    File.Exists(_manifestPath + PluginDeployer.BackupSuffix).ShouldBeFalse();
    Directory
      .Exists(Path.Combine(_pluginsDir, PluginDeployer.AutocmexPluginDirName))
      .ShouldBeFalse();
  }

  [Test]
  public void EnableRecorder_EnablesEntryAndBacksUpManifest()
  {
    WritePluginFile($"{RecorderDirName}/recorder.lua", "rec");
    var original = $$"""
      [
        { "path": "plugins/{{RecorderDirName}}/", "enable": false, "note": "keep" }
      ]
      """;
    WriteManifest(original);

    var message = PluginDeployer.EnableRecorder(_engineDir);

    var entry = Entry(PluginDeployer.RecorderPluginKeyword);
    entry.ShouldNotBeNull();
    entry!["enable"]!.GetValue<bool>().ShouldBeTrue();
    entry["note"]!.GetValue<string>().ShouldBe("keep");
    File.ReadAllText(_manifestPath + PluginDeployer.BackupSuffix).ShouldBe(original);
    message.ShouldContain("已启用弹幕录制器插件");
    PluginDeployer.Inspect(_engineDir).Recorder.State.ShouldBe(RecordingPluginState.Ready);
  }

  [Test]
  public void EnableRecorder_WithoutEntry_Throws()
  {
    Should
      .Throw<PluginDeployException>(() => PluginDeployer.EnableRecorder(_engineDir))
      .Message.ShouldContain("没有弹幕录制器条目");
    File.ReadAllText(_manifestPath).ShouldBe("[]");
  }

  [Test]
  public void TryValidateForRecording_WithoutAutocmex_ReportsInstallHint()
  {
    PluginDeployer.TryValidateForRecording(_engineDir, out var reason).ShouldBeFalse();

    reason.ShouldContain("录制插件未安装到引擎");
    reason.ShouldContain("设置页");
  }

  [Test]
  public void TryValidateForRecording_DisabledAutocmex_ReportsEnableHint()
  {
    WritePluginFile($"{PluginDeployer.AutocmexPluginDirName}/__init__.lua", "autocmex");
    WriteManifest(
      $$"""
      [
        { "name": "{{PluginDeployer.AutocmexPluginDirName}}", "enable": false }
      ]
      """
    );

    PluginDeployer.TryValidateForRecording(_engineDir, out var reason).ShouldBeFalse();

    reason.ShouldContain("录制插件未启用");
  }

  [Test]
  public void TryValidateForRecording_WithoutRecorder_ReportsThirdPartyHint()
  {
    WritePluginFile($"{PluginDeployer.AutocmexPluginDirName}/__init__.lua", "autocmex");

    PluginDeployer.TryValidateForRecording(_engineDir, out var reason).ShouldBeFalse();

    reason.ShouldContain("未找到弹幕录制器插件");
  }

  [Test]
  public void TryValidateForRecording_AllReady_ReturnsTrueWithoutReason()
  {
    WritePluginFile($"{PluginDeployer.AutocmexPluginDirName}/__init__.lua", "autocmex");
    WritePluginFile($"{RecorderDirName}/recorder.lua", "rec");
    WriteManifest(
      $$"""
      [
        { "name": "{{PluginDeployer.AutocmexPluginDirName}}", "enable": true },
        { "path": "plugins/{{RecorderDirName}}/", "enable": true }
      ]
      """
    );

    PluginDeployer.TryValidateForRecording(_engineDir, out var reason).ShouldBeTrue();

    reason.ShouldBeEmpty();
  }

  /// <summary>读回清单数组。</summary>
  /// <returns>清单里的对象条目。</returns>
  private List<JsonObject> ManifestEntries() =>
    ((JsonArray)JsonNode.Parse(File.ReadAllText(_manifestPath))!).OfType<JsonObject>().ToList();

  /// <summary>按关键字取清单条目（与实现的匹配口径一致：<c>name</c> 或 <c>path</c> 命中）。</summary>
  /// <param name="keyword">插件目录名关键字。</param>
  /// <returns>命中的条目；未命中为 null。</returns>
  private JsonObject? Entry(string keyword) =>
    ManifestEntries().FirstOrDefault(e => Hit(e, "name", keyword) || Hit(e, "path", keyword));

  /// <summary>判断条目的字段是否含关键字。</summary>
  /// <param name="entry">清单条目。</param>
  /// <param name="field">字段名。</param>
  /// <param name="keyword">关键字。</param>
  /// <returns>命中返回 true。</returns>
  private static bool Hit(JsonObject entry, string field, string keyword) =>
    entry[field]?.GetValue<string>().Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;

  /// <summary>记录目录树的相对路径、长度与修改时间，用于证明「只读检查真的没写盘」。</summary>
  /// <param name="root">根目录。</param>
  /// <returns>排序后的快照行。</returns>
  private static List<string> SnapshotTree(string root) =>
    Directory
      .EnumerateFiles(root, "*", SearchOption.AllDirectories)
      .Select(p =>
        $"{Path.GetRelativePath(root, p)}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p):O}"
      )
      .OrderBy(line => line, StringComparer.Ordinal)
      .ToList();

  /// <summary>写一个插件文件（相对 <c>game/plugins/</c>）。</summary>
  /// <param name="relativePath">相对路径，用 <c>/</c> 分隔。</param>
  /// <param name="content">文件内容。</param>
  private void WritePluginFile(string relativePath, string content)
  {
    var path = Path.Combine(_pluginsDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
  }

  /// <summary>覆盖写插件清单。</summary>
  /// <param name="json">清单内容。</param>
  private void WriteManifest(string json) => File.WriteAllText(_manifestPath, json);
}
