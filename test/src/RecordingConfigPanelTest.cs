namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Recording;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
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
/// 面板从真实场景 <c>RecordingConfigPanel.tscn</c> 实例化后挂到宿主节点下：<c>[Node("%…")]</c> 注入、
/// 300ms 去抖探针的 <c>ITimer</c> 与 <c>_ExitTree</c> 解绑都只有在场景树里才会真的走；
/// 进树同时真实触发 EnterTree/Ready，由 AutoInject 驱动 <c>OnReady</c>（信号/运行期文本）与
/// <c>OnResolved</c>（绑配置）。DataManager 按要求用 <c>IsTesting + FakeDependency</c> 注入。
/// </remarks>
public class RecordingConfigPanelTest : TestClass
{
  private const string RecorderDirName = "[pluginpackage]danmaku_recorder_1.0.1";

  /// <summary>被测试面板的场景路径（控件结构/文案/数值范围都在这里声明）。</summary>
  private const string ScenePath = "res://src/ui/settings/RecordingConfigPanel.tscn";

  private string _root = string.Empty;
  private string _dataDir = string.Empty;
  private string _engineDir = string.Empty;
  private string _pluginsDir = string.Empty;
  private string _sandboxRoot = string.Empty;
  private string _missingDir = string.Empty;
  private DataManager _dm = default!;
  private RecordingConfigPanel _panel = default!;

  /// <summary>面板宿主：面板必须进场景树，磁盘探针的 300ms 去抖 Timer 才会走。</summary>
  private Node _host = default!;

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

    _host = new Node();
    TestScene.AddChild(_host);
    _panel = NewPanel();
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var panel in _panels)
    {
      if (GodotObject.IsInstanceValid(panel) && !panel.IsQueuedForDeletion())
      {
        panel.QueueFree();
      }
    }

    _panels.Clear();

    if (_host is not null && GodotObject.IsInstanceValid(_host) && !_host.IsQueuedForDeletion())
    {
      _host.QueueFree();
    }

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
  public async Task EngineDirChange_RefreshesControlsThroughBinding()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;

    // 走绑定路径（OnValue → CallDeferred 回填），不手动 Refresh。
    for (var i = 0; i < 30; i++)
    {
      if (!string.Equals(_panel.EngineDirEdit.Text, _engineDir, StringComparison.Ordinal))
        await _panel.ToSignal(TestScene.GetTree(), "process_frame");
      else
        break;
    }

    _panel.EngineDirEdit.Text.ShouldBe(_engineDir);
    for (var i = 0; i < 30 && !_panel.EngineStatusLabel.Text.Contains("引擎目录可用"); i++)
      await _panel.ToSignal(TestScene.GetTree(), "process_frame");
    _panel.EngineStatusLabel.Text.ShouldContain("引擎目录可用");
  }

  [Test]
  public void Section_ShowsAllSettingsWithoutCollapseToggle()
  {
    // 整组常显：标题是普通文本，面板里没有任何折叠用的复选框
    _panel.SectionTitle.Text.ShouldBe("符卡 GIF 录制设置");
    _panel.SectionTitle.ShouldNotBeAssignableTo<BaseButton>();

    for (var i = 0; i < _panel.GetChildCount(); i++)
    {
      _panel.GetChild(i).ShouldNotBeAssignableTo<CheckBox>();
    }

    _panel.EngineDirEdit.Visible.ShouldBeTrue();
    _panel.EngineStatusLabel.Visible.ShouldBeTrue();
    _panel.ParallelismBox.Visible.ShouldBeTrue();
    _panel.MaxFrameBox.Visible.ShouldBeTrue();
    // 状态在构造时已渲染，不需要先「展开」一次
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
  public async Task CommitSandboxRoot_WithExistingDirectory_WritesConfig()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.CommitSandboxRoot(_sandboxRoot);

    _dm.RecordingConfig.SandboxRoot.Value.ShouldBe(_sandboxRoot);
    panel.ActionLabel.Text.ShouldContain("已设置沙箱根");
    // 磁盘占用是异步探针算的：等去抖 + 后台估算 + 回填后再断言文案
    await WaitForDiskHintAsync(panel, _sandboxRoot);
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
  public void Constructor_DoesNotProbeDiskFootprint()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    _dm.RecordingConfig.SandboxRoot.Value = _sandboxRoot;

    var panel = NewPanel();

    // 构造只做常数级检查（插件/目录校验）与纯内存回填：递归枚举推迟到进树后的去抖探针
    panel.FootprintProbeRuns.ShouldBe(0);
  }

  [Test]
  public async Task ContinuousParallelismChanges_ProbeDiskFootprintOnlyOnce()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    _dm.RecordingConfig.SandboxRoot.Value = _sandboxRoot;
    var panel = NewPanel();

    // 先等首轮探针落定并清零计数，避免把构造期排的那次算进本次观测
    await WaitForDiskHintAsync(panel, $"并行度 {RecordingConfig.DefaultParallelism}");
    panel.ResetFootprintProbeCounterForTests();

    // 改掉 game/ 的修改时间让缓存失效，下一次刷新必须真正重算
    File.WriteAllText(Path.Combine(_engineDir, EngineLocator.GameDirName, "touched.txt"), "x");

    // 同一个去抖窗口内连改三次并行度：只应落一次磁盘枚举
    panel.ApplyParallelism(4);
    panel.ApplyParallelism(5);
    panel.ApplyParallelism(6);

    await WaitForDiskHintAsync(panel, "并行度 6");

    panel.FootprintProbeRuns.ShouldBe(1);
  }

  [Test]
  public async Task ChangeParallelismAfterFirstProbe_ReusesCachedFootprint()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    _dm.RecordingConfig.SandboxRoot.Value = _sandboxRoot;
    var panel = NewPanel();

    await WaitForDiskHintAsync(panel, $"并行度 {RecordingConfig.DefaultParallelism}");
    panel.FootprintProbeRuns.ShouldBe(1);

    _dm.RecordingConfig.Parallelism.Value = 3;

    await WaitForDiskHintAsync(panel, "并行度 3");

    // 引擎目录与 game/ 修改时间都没变：复用缓存，不再递归扫盘
    panel.FootprintProbeRuns.ShouldBe(1);
  }

  [Test]
  public async Task LastOutputDirChange_RefreshesLabelThroughBinding()
  {
    var panel = NewPanel();
    var outputDir = Path.Combine(_root, "out");

    _dm.RecordingConfig.LastOutputDir.Value = outputDir;

    // 绑定回调只排 CallDeferred，等一帧落到主线程后再断言
    await panel.ToSignal(TestScene.GetTree(), "process_frame");

    panel.LastOutputLabel.Text.ShouldContain(outputDir);
  }

  [Test]
  public void Refresh_ClearsStaleActionHintOnPageEntry()
  {
    _panel.CommitEngineDir(_missingDir);
    _panel.ActionLabel.Text.ShouldNotBeEmpty();

    // 进页清空是 Refresh() 的显式行为，没有对应的模型字段可绑定，故保留手动调用。
    _panel.Refresh();

    _panel.ActionLabel.Text.ShouldBeEmpty();
    _panel.ActionLabel.Visible.ShouldBeFalse();
  }

  /// <summary>
  /// 回归：主题变化的整页重画必须合并到帧末执行——父控件在 <c>theme_changed</c> 回调里解析主题项
  /// 仍拿到旧值，同步重算会把旧主题色固化成状态行的 <c>font_color</c>。
  /// </summary>
  /// <remarks>
  /// <c>ActionLabel.Visible</c> 只由 <see cref="RecordingConfigPanel.RefreshProbes"/> 写入，
  /// 数它的 setter 调用次数即「重算了几轮」；真实场景控件没有计数能力，故这里挂一个记录用的 mock。
  /// </remarks>
  [Test]
  public async Task ThemeChanged_DefersSingleProbesRefresh()
  {
    var actionLabel = new Mock<ILabel>();
    actionLabel.SetupProperty(m => m.Text, "占位提示");
    actionLabel.SetupProperty(m => m.Visible);
    _panel.ActionLabel = actionLabel.Object;
    actionLabel.Invocations.Clear();

    _panel.EmitSignal(Control.SignalName.ThemeChanged);
    _panel.EmitSignal(Control.SignalName.ThemeChanged);

    CountProbeRefreshes(actionLabel).ShouldBe(0, "主题变化不得在信号回调里同步重算状态行");

    // 帧末那次 deferred 由这里显式顶替：两次通知只排了一次
    _panel.FlushThemeRefresh();
    CountProbeRefreshes(actionLabel).ShouldBe(1);

    // 再等帧末：排队的那次 deferred 只补刷一轮（未合并的话两次通知会补刷两轮）
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    CountProbeRefreshes(actionLabel).ShouldBe(2, "连发两次主题通知只应合并成一次帧末重算");
  }

  [Test]
  public async Task RefreshProbes_WithReadyPlugins_ReportsReadyAndActionableButtons()
  {
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    _dm.RecordingConfig.SandboxRoot.Value = _sandboxRoot;
    var panel = NewPanel();

    panel.RefreshProbes();

    panel.PluginStatusLabel.Text.ShouldContain("autocmex：已安装并启用");
    panel.PluginStatusLabel.Text.ShouldContain("danmaku_recorder：已安装并启用");
    // 自家插件可随时重装（升级后刷新），第三方录制器已启用时无事可做
    panel.InstallPluginButton.Disabled.ShouldBeFalse();
    panel.EnableRecorderButton.Disabled.ShouldBeTrue();
    await WaitForDiskHintAsync(panel, "并行度");
    panel.DiskHintLabel.Text.ShouldContain(_sandboxRoot);
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

    // 面板只绑 RecordingConfig；SharpEditorPath 属 MergeConfig，无绑定路径，刷新按钮态只能手动 Refresh。
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

    // 同上：SharpEditorPath 无绑定路径，保留手动刷新。
    _panel.Refresh();
    _panel.InferEngineDirButton.Disabled.ShouldBeFalse();

    _panel.InferEngineDir();

    _dm.RecordingConfig.EngineDir.Value.ShouldBeEmpty();
    _panel.EngineDirEdit.Text.ShouldBeEmpty();
    _panel.ActionLabel.Text.ShouldContain("未从 Sharp 目录推断出引擎目录");
  }

  [Test]
  public void RefreshProbes_WithDisabledPlugins_ReportsReasonAndKeepsButtonsConsistent()
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

    panel.RefreshProbes();

    panel.PluginStatusLabel.Text.ShouldContain("autocmex：已安装但被禁用（引擎不会加载）");
    panel.PluginStatusLabel.Text.ShouldContain("danmaku_recorder：已安装但被禁用");
    // 都被禁用：一个按钮是「重装」、一个是「启用」，且都不能当成就绪
    panel.InstallPluginButton.Disabled.ShouldBeFalse();
    panel.EnableRecorderButton.Disabled.ShouldBeFalse();
  }

  [Test]
  public void RefreshProbes_WithBrokenManifest_DisablesEveryDeployButton()
  {
    WriteManifest("{ not json");
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.RefreshProbes();

    panel.PluginStatusLabel.Text.ShouldContain("插件清单损坏，已停止一切部署动作");
    // 坏清单不拿去写：两个部署按钮都要停
    panel.InstallPluginButton.Disabled.ShouldBeTrue();
    panel.EnableRecorderButton.Disabled.ShouldBeTrue();
  }

  [Test]
  public void RefreshProbes_WithoutManifest_KeepsInstallAvailableWithReason()
  {
    File.Delete(Path.Combine(_pluginsDir, PluginDeployer.ManifestFileName));
    _dm.RecordingConfig.EngineDir.Value = _engineDir;
    var panel = NewPanel();

    panel.RefreshProbes();

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

  [Test]
  public void Scene_InjectsEveryDeclaredNode()
  {
    // 24 个 [Node] 属性必须全部由场景装配（缺一个 AutoConnect 会直接抛错，这里再逐项确认）
    _panel.SectionTitle.ShouldNotBeNull();
    _panel.EngineDirEdit.ShouldNotBeNull();
    _panel.ChooseEngineDirButton.ShouldNotBeNull();
    _panel.InferEngineDirButton.ShouldNotBeNull();
    _panel.EngineStatusLabel.ShouldNotBeNull();
    _panel.ParallelismBox.ShouldNotBeNull();
    _panel.SandboxRootEdit.ShouldNotBeNull();
    _panel.ChooseSandboxRootButton.ShouldNotBeNull();
    _panel.ResetSandboxRootButton.ShouldNotBeNull();
    _panel.DiskHintLabel.ShouldNotBeNull();
    _panel.PluginStatusLabel.ShouldNotBeNull();
    _panel.InstallPluginButton.ShouldNotBeNull();
    _panel.EnableRecorderButton.ShouldNotBeNull();
    _panel.RefreshPluginButton.ShouldNotBeNull();
    _panel.MaxFrameBox.ShouldNotBeNull();
    _panel.FirstIntervalBox.ShouldNotBeNull();
    _panel.SecondIntervalBox.ShouldNotBeNull();
    _panel.ScalePercentBox.ShouldNotBeNull();
    _panel.AdvancedHintLabel.ShouldNotBeNull();
    _panel.LastOutputLabel.ShouldNotBeNull();
    _panel.ActionLabel.ShouldNotBeNull();
    _panel.EngineDirDialog.ShouldNotBeNull();
    _panel.SandboxRootDialog.ShouldNotBeNull();
    _panel.ProbeTimer.ShouldNotBeNull();
  }

  [Test]
  public void Scene_SpinBoxRangesMatchRecordingConfigConstants()
  {
    _panel.ParallelismBox.MinValue.ShouldBe(RecordingConfig.MinParallelism);
    _panel.ParallelismBox.MaxValue.ShouldBe(RecordingConfig.MaxParallelism);
    _panel.ParallelismBox.Step.ShouldBe(1.0);

    _panel.MaxFrameBox.MinValue.ShouldBe(RecordingConfig.MinMaxFrame);
    _panel.MaxFrameBox.MaxValue.ShouldBe(RecordingConfig.MaxMaxFrame);
    _panel.MaxFrameBox.Step.ShouldBe(1.0);

    _panel.FirstIntervalBox.MinValue.ShouldBe(RecordingConfig.MinInterval);
    _panel.FirstIntervalBox.MaxValue.ShouldBe(RecordingConfig.MaxInterval);
    _panel.FirstIntervalBox.Step.ShouldBe(1.0);

    _panel.SecondIntervalBox.MinValue.ShouldBe(RecordingConfig.MinInterval);
    _panel.SecondIntervalBox.MaxValue.ShouldBe(RecordingConfig.MaxInterval);
    _panel.SecondIntervalBox.Step.ShouldBe(1.0);

    _panel.ScalePercentBox.MinValue.ShouldBe(RecordingConfig.MinScalePercent);
    _panel.ScalePercentBox.MaxValue.ShouldBe(RecordingConfig.MaxScalePercent);
    _panel.ScalePercentBox.Step.ShouldBe(5.0);
  }

  [Test]
  public void Scene_SpinBoxInitialValuesMatchRecorderDefaults()
  {
    // 运行期 OnResolved 会用模型值回填；这里检查「入树前」的场景初始值，防止 .tscn 与常量两处漂移。
    // 未入树时 unique name 不可用，按节点名在场景实例里查找（不依赖层级）。
    var scene = GD.Load<PackedScene>(ScenePath).Instantiate<RecordingConfigPanel>();
    try
    {
      SpinBoxBox(scene, "ParallelismBox").Value.ShouldBe(RecordingConfig.DefaultParallelism);
      SpinBoxBox(scene, "MaxFrameBox").Value.ShouldBe(RecordingConfig.MinMaxFrame);
      SpinBoxBox(scene, "FirstIntervalBox").Value.ShouldBe(RecordingConfig.MinInterval);
      SpinBoxBox(scene, "SecondIntervalBox").Value.ShouldBe(RecordingConfig.MinInterval);
      SpinBoxBox(scene, "ScalePercentBox").Value.ShouldBe(RecordingConfig.MinScalePercent);
    }
    finally
    {
      scene.Free();
    }
  }

  [Test]
  public void Scene_ProbeTimerMatchesDebounceConstant()
  {
    var timer = _panel.GetNode<Timer>("%ProbeTimer");
    timer.OneShot.ShouldBeTrue();
    timer.WaitTime.ShouldBe(RecordingConfigPanel.ProbeDebounceSeconds);
  }

  [Test]
  public void Scene_FileDialogsAreDirectoryPickersWithDeclaredTitles()
  {
    foreach (
      var dialog in new[]
      {
        _panel.GetNode<FileDialog>("%EngineDirDialog"),
        _panel.GetNode<FileDialog>("%SandboxRootDialog"),
      }
    )
    {
      dialog.FileMode.ShouldBe(FileDialog.FileModeEnum.OpenDir);
      dialog.Access.ShouldBe(FileDialog.AccessEnum.Filesystem);
      dialog.UseNativeDialog.ShouldBeFalse();
      // 默认 true 时引擎会用 file_mode 覆盖 title；必须关掉，中文标题才能留在场景里
      dialog.ModeOverridesTitle.ShouldBeFalse();
    }

    _panel.GetNode<FileDialog>("%EngineDirDialog").Title.ShouldBe("选择 LuaSTG 引擎根目录");
    _panel.GetNode<FileDialog>("%SandboxRootDialog").Title.ShouldBe("选择沙箱根目录");
  }

  [Test]
  public void Scene_DoesNotBuildNodesAtRuntime()
  {
    // 场景固定 23 个直接子节点（含 2 个 FileDialog 与 ProbeTimer）；运行期再建节点说明又回到手搭
    _panel.GetChildCount().ShouldBe(23);
  }

  /// <summary>在未入树的场景实例里按节点名取 SpinBox（契约测试用）。</summary>
  /// <param name="scene">场景根节点。</param>
  /// <param name="name">节点名。</param>
  /// <returns>找到的数值框。</returns>
  private static SpinBox SpinBoxBox(Node scene, string name) =>
    scene.FindChild(name, recursive: true, owned: false).ShouldBeOfType<SpinBox>();

  /// <summary>数 <c>RefreshProbes</c> 写入 <c>ActionLabel.Visible</c> 的次数（即重算了几轮）。</summary>
  /// <param name="actionLabel">记录用的标签 mock。</param>
  /// <returns>写入次数。</returns>
  private static int CountProbeRefreshes(Mock<ILabel> actionLabel) =>
    actionLabel.Invocations.Count(call => call.Method.Name == "set_Visible");

  /// <summary>新建一个接好依赖并进场景树的面板（同一 DataManager，便于验证回填与去抖探针）。</summary>
  /// <returns>已建好的面板（<c>OnReady</c>/<c>OnResolved</c> 已随进树执行完毕）。</returns>
  private RecordingConfigPanel NewPanel()
  {
    var panel = GD.Load<PackedScene>(ScenePath).Instantiate<RecordingConfigPanel>();
    (panel as IAutoInit).IsTesting = true;
    panel.FakeDependency<DataManager>(_dm);
    // 进树即真实触发 EnterTree/Ready：AutoInject 先注入 [Node] 再调 OnReady/OnResolved，
    // 同时让去抖 Timer 与 _ExitTree 走真机路径。
    _host.AddChild(panel);
    _panels.Add(panel);
    return panel;
  }

  /// <summary>等磁盘提示出现指定文案：覆盖 300ms 去抖 + 后台估算 + <c>CallDeferred</c> 回填。</summary>
  /// <param name="panel">目标面板（必须已在场景树里）。</param>
  /// <param name="expected">期望包含的文案。</param>
  private async Task WaitForDiskHintAsync(RecordingConfigPanel panel, string expected)
  {
    var tree = TestScene.GetTree();
    for (var attempt = 0; attempt < 60; attempt++)
    {
      if (panel.DiskHintLabel.Text.Contains(expected, StringComparison.Ordinal))
      {
        return;
      }

      await panel.ToSignal(tree, "process_frame");
      await panel.ToSignal(tree.CreateTimer(0.05), "timeout");
    }

    panel.DiskHintLabel.Text.ShouldContain(expected);
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
