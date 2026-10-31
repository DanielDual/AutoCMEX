namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Recording;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Settings;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 录制设置分组单测：配置回填与写回、非法值不落盘、越界收敛、插件状态渲染与一键部署的联动。
/// </summary>
/// <remarks>
/// 用真实的 <see cref="RecordingConfigPanel"/> + 真实临时引擎目录：本分组的职责是「把盘上状态渲染成
/// 控件、把控件输入写回配置」，只有真实节点与真实目录才能同时验证这两条；控件事件不模拟，直接调
/// 面板留出的公开入口（<c>CommitEngineDir</c>/<c>ApplyParallelism</c>/…）。
/// </remarks>
public class RecordingConfigPanelTest : TestClass
{
  private const string RecorderDirName = "[pluginpackage]danmaku_recorder_1.0.1";

  private string _root = string.Empty;
  private string _dataDir = string.Empty;
  private string _engineDir = string.Empty;
  private string _pluginsDir = string.Empty;
  private string _sandboxRoot = string.Empty;
  private string _missingDir = string.Empty;
  private DataManager _dm = default!;
  private RecordingConfigPanel _panel = default!;
  private readonly List<RecordingConfigPanel> _panels = new();

  public RecordingConfigPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(Path.GetTempPath(), "AutoCMEX_Panel_" + Guid.NewGuid().ToString("N")[..8]);
    _dataDir = Path.Combine(_root, "data");
    _engineDir = Path.Combine(_root, "LuaSTGSub");
    _sandboxRoot = Path.Combine(_root, "sandboxes");
    _missingDir = Path.Combine(_root, "no-such-dir");
    Directory.CreateDirectory(_dataDir);
    Directory.CreateDirectory(_sandboxRoot);

    var gameDir = Path.Combine(_engineDir, EngineLocator.GameDirName);
    _pluginsDir = Path.Combine(gameDir, PluginDeployer.PluginsDirName);
    Directory.CreateDirectory(_pluginsDir);
    File.WriteAllText(Path.Combine(gameDir, EngineLocator.LaunchFileName), "launch");
    File.WriteAllText(Path.Combine(gameDir, "LuaSTGSub.exe"), "exe");
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

    _dm = new DataManager(
      _dataDir,
      new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_dataDir)),
      new Mock<ILog>().Object
    );

    _panel = NewPanel();
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var panel in _panels)
    {
      panel.QueueFree();
    }

    _panels.Clear();
    _dm?.Dispose();

    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  [Test]
  public void Constructor_BackfillsAllConfigValuesIntoControls()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    _dm.RecordingConfig.Parallelism.Value = 6;
    _dm.RecordingConfig.MaxFrame.Value = 400;
    _dm.RecordingConfig.FirstInterval.Value = 4;
    _dm.RecordingConfig.SecondInterval.Value = 7;
    _dm.RecordingConfig.SandboxRoot.Value = _sandboxRoot;
    _dm.RecordingConfig.LastOutputDir.Value = Path.Combine(_root, "out");

    var panel = NewPanel();

    panel.EngineDirEdit.Text.ShouldBe(_engineDir);
    ((int)panel.ParallelismBox.Value).ShouldBe(6);
    ((int)panel.MaxFrameBox.Value).ShouldBe(400);
    ((int)panel.FirstIntervalBox.Value).ShouldBe(4);
    ((int)panel.SecondIntervalBox.Value).ShouldBe(7);
    panel.SandboxRootEdit.Text.ShouldBe(_sandboxRoot);
    panel.LastOutputLabel.Text.ShouldContain("out");
  }

  [Test]
  public void Refresh_PicksUpConfigChangedOutsideThePanel()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;

    _panel.Refresh();

    _panel.EngineDirEdit.Text.ShouldBe(_engineDir);
    _panel.EngineStatusLabel.Text.ShouldContain("引擎目录可用");
  }

  [Test]
  public void Section_StartsCollapsedThenExpandsOnDemand()
  {
    _panel.SectionBody.Visible.ShouldBeFalse();
    _panel.IsExpanded.ShouldBeFalse();

    _panel.SetExpanded(true);

    _panel.IsExpanded.ShouldBeTrue();
    _panel.SectionBody.Visible.ShouldBeTrue();
    // 展开即刷新：引擎目录可能在「整合」板块刚改过
    _panel.EngineStatusLabel.Text.ShouldNotBeEmpty();
  }

  [Test]
  public void CommitEngineDir_WithInvalidPath_KeepsOriginalValueAndExplains()
  {
    _panel.CommitEngineDir(_missingDir);

    _dm.RecordingConfig.EngineDir.Value.ShouldBeEmpty();
    _panel.EngineDirEdit.Text.ShouldBeEmpty();
    _panel.ActionLabel.Text.ShouldContain("引擎目录无效");
    _panel.ActionLabel.Text.ShouldContain("已保留原值");
    _panel.ActionLabel.Visible.ShouldBeTrue();
  }

  [Test]
  public void CommitEngineDir_WithValidPath_WritesConfigAndReportsUsable()
  {
    _panel.CommitEngineDir(_engineDir);

    _dm.RecordingConfig.EngineDir.Value.ShouldBe(_engineDir);
    _panel.ActionLabel.Text.ShouldContain("已设置引擎目录");
    _panel.EngineStatusLabel.Text.ShouldContain("引擎目录可用");
    _panel.InstallPluginButton.Disabled.ShouldBeFalse();
  }

  [Test]
  public void CommitEngineDir_WithEmptyText_ClearsConfig()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.CommitEngineDir(string.Empty);

    _dm.RecordingConfig.EngineDir.Value.ShouldBeEmpty();
    panel.ActionLabel.Text.ShouldContain("已清空引擎目录");
    panel.EngineStatusLabel.Text.ShouldContain("未设置引擎目录");
  }

  [Test]
  public void CommitSandboxRoot_WithMissingDirectory_KeepsOriginalValue()
  {
    _panel.CommitSandboxRoot(_missingDir);

    _dm.RecordingConfig.SandboxRoot.Value.ShouldBeEmpty();
    _panel.SandboxRootEdit.Text.ShouldBeEmpty();
    _panel.ActionLabel.Text.ShouldContain("沙箱根目录不存在");
    _panel.ActionLabel.Text.ShouldContain("已保留原值");
  }

  [Test]
  public void CommitSandboxRoot_WithExistingDirectory_WritesConfig()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.CommitSandboxRoot(_sandboxRoot);

    _dm.RecordingConfig.SandboxRoot.Value.ShouldBe(_sandboxRoot);
    panel.ActionLabel.Text.ShouldContain("已设置沙箱根");
    panel.DiskHintLabel.Text.ShouldContain(_sandboxRoot);
  }

  [Test]
  public void ResetSandboxRoot_RestoresDefaultDirectory()
  {
    _dm.RecordingConfig.SandboxRoot.Value = _sandboxRoot;
    var panel = NewPanel();

    panel.ResetSandboxRoot();

    _dm.RecordingConfig.SandboxRoot.Value.ShouldBeEmpty();
    panel.SandboxRootEdit.Text.ShouldBeEmpty();
    panel.ActionLabel.Text.ShouldContain("默认沙箱根");
  }

  [Test]
  public void ApplyParallelism_ClampsOutOfRangeValues()
  {
    _panel.ApplyParallelism(99);

    _dm.RecordingConfig.Parallelism.Value.ShouldBe(RecordingConfig.MaxParallelism);
    ((int)_panel.ParallelismBox.Value).ShouldBe(RecordingConfig.MaxParallelism);
    _panel.ActionLabel.Text.ShouldContain("已更新");

    _panel.ApplyParallelism(0);

    _dm.RecordingConfig.Parallelism.Value.ShouldBe(RecordingConfig.MinParallelism);
    ((int)_panel.ParallelismBox.Value).ShouldBe(RecordingConfig.MinParallelism);
  }

  [Test]
  public void ApplyAdvancedValues_ClampToRecorderRange()
  {
    _panel.ApplyMaxFrame(0);
    _panel.ApplyMaxFrame(9999);
    _panel.ApplyFirstInterval(99);
    _panel.ApplySecondInterval(0);

    _dm.RecordingConfig.MaxFrame.Value.ShouldBe(RecordingConfig.MaxMaxFrame);
    _dm.RecordingConfig.FirstInterval.Value.ShouldBe(RecordingConfig.MaxInterval);
    _dm.RecordingConfig.SecondInterval.Value.ShouldBe(RecordingConfig.MinInterval);
    ((int)_panel.MaxFrameBox.Value).ShouldBe(RecordingConfig.MaxMaxFrame);
    ((int)_panel.FirstIntervalBox.Value).ShouldBe(RecordingConfig.MaxInterval);
    ((int)_panel.SecondIntervalBox.Value).ShouldBe(RecordingConfig.MinInterval);
  }

  [Test]
  public void RefreshStatuses_WithReadyPlugins_ReportsReadyAndActionableButtons()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    _dm.RecordingConfig.SandboxRoot.Value = _sandboxRoot;
    var panel = NewPanel();

    panel.RefreshStatuses();

    panel.PluginStatusLabel.Text.ShouldContain("autocmex：已安装并启用");
    panel.PluginStatusLabel.Text.ShouldContain("danmaku_recorder：已安装并启用");
    // 自家插件可随时重装（升级后刷新），第三方录制器已启用时无事可做
    panel.InstallPluginButton.Disabled.ShouldBeFalse();
    panel.EnableRecorderButton.Disabled.ShouldBeTrue();
    panel.DiskHintLabel.Text.ShouldContain(_sandboxRoot);
    panel.DiskHintLabel.Text.ShouldContain("并行度");
  }

  [Test]
  public void InstallPlugin_DeploysPluginAndRefreshesStatusWithoutChangingConfig()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    WriteManifest(
      $$"""
      [
        { "path": "plugins/{{RecorderDirName}}/", "enable": true }
      ]
      """
    );
    Directory.Delete(
      Path.Combine(_pluginsDir, PluginDeployer.AutocmexPluginDirName),
      recursive: true
    );
    var panel = NewPanel();
    panel.PluginStatusLabel.Text.ShouldContain("autocmex：未安装");

    panel.InstallPlugin();

    panel.ActionLabel.Text.ShouldContain("已安装并启用");
    panel.PluginStatusLabel.Text.ShouldContain("autocmex：已安装并启用");
    File.Exists(
        Path.Combine(
          _pluginsDir,
          PluginDeployer.AutocmexPluginDirName,
          PluginDeployer.AutocmexEntryFileName
        )
      )
      .ShouldBeTrue();
    // 部署动作只写引擎目录，不改用户的配置项
    _dm.RecordingConfig.EngineDir.Value.ShouldBe(_engineDir);
  }

  [Test]
  public void InstallPlugin_WithoutEngineDir_ReportsFailureAndKeepsEngineUntouched()
  {
    var before = SnapshotTree(_engineDir);

    _panel.InstallPlugin();

    _panel.ActionLabel.Text.ShouldContain("引擎目录不可用");
    SnapshotTree(_engineDir).ShouldBe(before);
  }

  [Test]
  public void InferEngineDir_WithoutSharpEditorPath_HintsInsteadOfWriting()
  {
    _panel.InferEngineDirButton.Disabled.ShouldBeTrue();

    _panel.InferEngineDir();

    _panel.ActionLabel.Text.ShouldContain("整合");
    _dm.RecordingConfig.EngineDir.Value.ShouldBeEmpty();
  }

  [Test]
  public void InferEngineDir_FromSharpEditorPath_FillsSuggestionAndStaysValid()
  {
    var sharpDir = Path.Combine(_engineDir, "sharp", "editor");
    Directory.CreateDirectory(sharpDir);
    _dm.MergeConfig.SharpEditorPath.Value = sharpDir;

    _panel.Refresh();
    _panel.InferEngineDirButton.Disabled.ShouldBeFalse();

    _panel.InferEngineDir();

    _dm.RecordingConfig.EngineDir.Value.ShouldBe(_engineDir);
    _panel.EngineDirEdit.Text.ShouldBe(_engineDir);
    _panel.ActionLabel.Text.ShouldContain("已从 Sharp 目录推断");
    _panel.EngineStatusLabel.Text.ShouldContain("引擎目录可用");
  }

  [Test]
  public void InferEngineDir_WithSharpDirThatHasNoEngineAbove_KeepsConfigAndExplains()
  {
    // Sharp 目录在，但上溯不到 game/launch：推断失败不能写进配置
    var sharpDir = Path.Combine(_root, "sharp-alone");
    Directory.CreateDirectory(sharpDir);
    _dm.MergeConfig.SharpEditorPath.Value = sharpDir;

    _panel.Refresh();
    _panel.InferEngineDirButton.Disabled.ShouldBeFalse();

    _panel.InferEngineDir();

    _dm.RecordingConfig.EngineDir.Value.ShouldBeEmpty();
    _panel.EngineDirEdit.Text.ShouldBeEmpty();
    _panel.ActionLabel.Text.ShouldContain("未从 Sharp 目录推断出引擎目录");
  }

  [Test]
  public void RefreshStatuses_WithDisabledPlugins_ReportsReasonAndKeepsButtonsConsistent()
  {
    WriteManifest(
      $$"""
      [
        { "name": "{{PluginDeployer.AutocmexPluginDirName}}", "enable": false },
        { "path": "plugins/{{RecorderDirName}}/", "enable": false }
      ]
      """
    );
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.RefreshStatuses();

    panel.PluginStatusLabel.Text.ShouldContain("autocmex：已安装但被禁用（引擎不会加载）");
    panel.PluginStatusLabel.Text.ShouldContain("danmaku_recorder：已安装但被禁用");
    // 都被禁用：一个按钮是「重装」、一个是「启用」，且都不能当成就绪
    panel.InstallPluginButton.Disabled.ShouldBeFalse();
    panel.EnableRecorderButton.Disabled.ShouldBeFalse();
  }

  [Test]
  public void RefreshStatuses_WithBrokenManifest_DisablesEveryDeployButton()
  {
    WriteManifest("{ not json");
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.RefreshStatuses();

    panel.PluginStatusLabel.Text.ShouldContain("插件清单损坏，已停止一切部署动作");
    // 坏清单不拿去写：两个部署按钮都要停
    panel.InstallPluginButton.Disabled.ShouldBeTrue();
    panel.EnableRecorderButton.Disabled.ShouldBeTrue();
  }

  [Test]
  public void RefreshStatuses_WithoutManifest_KeepsInstallAvailableWithReason()
  {
    File.Delete(Path.Combine(_pluginsDir, PluginDeployer.ManifestFileName));
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.RefreshStatuses();

    // 全新引擎（清单还没生成）上不能把安装也堵死，否则用户无从下手
    panel.PluginStatusLabel.Text.ShouldContain("插件清单不存在");
    panel.InstallPluginButton.Disabled.ShouldBeFalse();
    // 「启用」要求清单里先有条目，这里没有，只能禁用按钮并在状态行指路
    panel.EnableRecorderButton.Disabled.ShouldBeTrue();
  }

  [Test]
  public void EnableRecorder_WithBrokenManifest_ReportsFailureAndKeepsManifestBytes()
  {
    var broken = "{ not json";
    WriteManifest(broken);
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.EnableRecorder();

    panel.ActionLabel.Text.ShouldContain("插件清单损坏");
    File.ReadAllText(Path.Combine(_pluginsDir, PluginDeployer.ManifestFileName)).ShouldBe(broken);
  }

  [Test]
  public void EnableRecorder_WithoutEntry_ReportsFailureInsteadOfSilentlyDoingNothing()
  {
    WriteManifest($$"""[{ "name": "{{PluginDeployer.AutocmexPluginDirName}}", "enable": true }]""");
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.EnableRecorder();

    panel.ActionLabel.Text.ShouldContain("没有弹幕录制器条目");
  }

  /// <summary>新建一个面板（同一 DataManager，便于验证「从配置回填」）。</summary>
  /// <returns>已建好的面板。</returns>
  private RecordingConfigPanel NewPanel()
  {
    var panel = new RecordingConfigPanel(_dm);
    _panels.Add(panel);
    return panel;
  }

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
  private void WriteManifest(string json) =>
    File.WriteAllText(Path.Combine(_pluginsDir, PluginDeployer.ManifestFileName), json);

  /// <summary>记录目录树的相对路径、长度与修改时间，用于证明「拒写场景没碰盘」。</summary>
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
}
