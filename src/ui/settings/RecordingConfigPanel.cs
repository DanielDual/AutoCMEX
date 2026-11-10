namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Recording;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 符卡 GIF 录制的设置分组：引擎目录、插件部署、并行度、沙箱根与高级录制参数。
/// </summary>
/// <remarks>
/// <para>
/// 挂在设置面板「信息」类别页（<see cref="InfoConfigPanel"/>）的目标群列表下方，由该页在 <c>OnResolved</c> 时创建。
/// 「信息」类别页没有独立场景（节点直接写在 <c>SettingsPanel.tscn</c> 里），故这里沿用该页既有的
/// 「C# 构建行」范式，不为一个分组改动共享场景。
/// </para>
/// <para>
/// 整组常显（不再有折叠复选框）：页面本身纵向滚动，往下翻即可看全；目标群列表保留自己的定高滚动区，
/// 故本页是有意保留的双滚动结构（内层列表 + 外层页面），滚轮到列表边界后由 Godot 默认冒泡给外层页面。
/// </para>
/// <para>
/// 写入纪律：配置项只在通过校验时写回模型（非法值保留原值并只提示）；插件的安装/启用只响应用户点击，
/// 未点击时对引擎目录零写入；状态渲染一律走只读查询（<see cref="PluginDeployer.Inspect"/>、
/// <see cref="EngineLocator.TryValidate"/>、<see cref="RecordingSandbox.EstimateFootprint"/>）。
/// </para>
/// <para>
/// 配置值由 Sync 绑定驱动回填（规则 18/19）；磁盘占用估算不参与构造与每次输入，而是交给
/// 300ms 去抖的后台探针，结果按「引擎目录 + game/ 修改时间」缓存后回主线程渲染。
/// </para>
/// <para>
/// 按规则 1/5 接入 AutoInject：<c>[Meta(typeof(IAutoNode))]</c>、<c>_Notification</c> 转发给
/// <c>this.Notify</c>、DataManager 走 <c>[Dependency]</c>；控件来自场景，<see cref="OnReady"/> 只连信号
/// 与赋值运行期文本，配置回填与绑定在 <see cref="OnResolved"/> 装配（依赖重新解析时先释放旧绑定）。
/// </para>
/// <para>
/// 控件全部由 <c>RecordingConfigPanel.tscn</c> 声明并经 <c>[Node("%…")]</c> 注入（规则 7/8/13）：
/// <see cref="OnReady"/> 只连接信号与赋值运行期才能算出的文本，结构/文案/数值范围都在场景里。
/// 两个 <see cref="FileDialog"/> 的标题也写在场景里，靠 <c>mode_overrides_title = false</c> 保住
/// （默认 true 时引擎会在应用 <c>file_mode</c> 后把 title 覆盖成英文默认值）。
/// </para>
/// <para>
/// 交互入口都留了公开方法（<see cref="CommitEngineDir"/>、<see cref="ApplyParallelism"/> 等），
/// 便于单测直接驱动，不必模拟控件事件。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public sealed partial class RecordingConfigPanel : VBoxContainer
{
  private static readonly Color _okColor = new(0.62f, 0.86f, 0.52f);
  private static readonly Color _warnColor = new(0.96f, 0.68f, 0.36f);

  /// <summary>磁盘探针的去抖时延（秒）：连续 tick 只落一次递归枚举；与场景里 ProbeTimer 的 wait_time 对照。</summary>
  internal const double ProbeDebounceSeconds = 0.3;

  /// <summary>数据管理器（在 <see cref="OnResolved"/> 里取；之后的公开入口都依赖它已就绪）。</summary>
  private DataManager _dm = default!;

  /// <summary>录制配置（与 <see cref="DataManager"/> 同一实例，在 <see cref="OnResolved"/> 里取）。</summary>
  private RecordingConfig _config = default!;

  private bool _syncing;

  /// <summary>8 个配置项的 Sync 绑定（规则 20：在 <see cref="_ExitTree"/> 释放）。</summary>
  private readonly List<IDisposable> _bindings = new();

  /// <summary>正在建立配置绑定：订阅回放不排回填（随后会整体回填一次）。</summary>
  private bool _isBindingConfigValues;

  /// <summary>上次估算结果对应的引擎目录（缓存键的一半）。</summary>
  private string _cachedEngineDir = string.Empty;

  /// <summary>上次估算时 game/ 的 UTC 修改时间（缓存键的另一半）。</summary>
  private DateTime _cachedGameDirWriteUtc = DateTime.MinValue;

  /// <summary>上次估算的单 worker 字节数。</summary>
  private long _cachedFootprintBytes;

  /// <summary>是否已有可复用的估算缓存。</summary>
  private bool _hasFootprintCache;

  /// <summary>本面板实际执行过几次磁盘占用估算（测试用探针计数；生产代码不读取）。</summary>
  private int _footprintProbeRuns;

  #region Dependencies

  /// <summary>数据管理器（由设置面板树内的 provider 就近提供，与页面共用同一实例）。</summary>
  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  #endregion

  /// <inheritdoc/>
  public override void _Notification(int what) => this.Notify(what);

  /// <summary>AutoInject 节点注入完成：只做运行期文本与信号连接（控件、标题与静态文案都在场景里）。</summary>
  /// <remarks>
  /// 占位符、默认沙箱路径与常量上限提示依赖运行期才能算出的值，故留在这里赋值。
  /// </remarks>
  public void OnReady()
  {
    EngineDirEdit.PlaceholderText = @"例如 LuaSTG 引擎目录";
    SandboxRootEdit.PlaceholderText = RecordingSandbox.GetDefaultRootDir();
    ResetSandboxRootButton.TooltipText = RecordingSandbox.GetDefaultRootDir();
    ParallelismBox.TooltipText =
      $"默认 {RecordingConfig.DefaultParallelism}，上限 {RecordingConfig.MaxParallelism}";

    EngineDirEdit.TextSubmitted += text => CommitEngineDir(text);
    EngineDirEdit.FocusExited += () => CommitEngineDir(EngineDirEdit.Text);
    ChooseEngineDirButton.Pressed += OnChooseEngineDirPressed;
    InferEngineDirButton.Pressed += InferEngineDir;
    ParallelismBox.ValueChanged += value =>
      OnSpinBoxChanged(() => ApplyParallelism((int)Math.Round(value)));

    SandboxRootEdit.TextSubmitted += text => CommitSandboxRoot(text);
    SandboxRootEdit.FocusExited += () => CommitSandboxRoot(SandboxRootEdit.Text);
    ChooseSandboxRootButton.Pressed += OnChooseSandboxRootPressed;
    ResetSandboxRootButton.Pressed += ResetSandboxRoot;

    InstallPluginButton.Pressed += InstallPlugin;
    EnableRecorderButton.Pressed += EnableRecorder;
    RefreshPluginButton.Pressed += RefreshProbes;

    MaxFrameBox.ValueChanged += value =>
      OnSpinBoxChanged(() => ApplyMaxFrame((int)Math.Round(value)));
    FirstIntervalBox.ValueChanged += value =>
      OnSpinBoxChanged(() => ApplyFirstInterval((int)Math.Round(value)));
    SecondIntervalBox.ValueChanged += value =>
      OnSpinBoxChanged(() => ApplySecondInterval((int)Math.Round(value)));
    ScalePercentBox.ValueChanged += value =>
      OnSpinBoxChanged(() => ApplyScalePercent((int)Math.Round(value)));

    EngineDirDialog.DirSelected += CommitEngineDir;
    SandboxRootDialog.DirSelected += CommitSandboxRoot;
    ProbeTimer.Timeout += OnFootprintDebounceElapsed;
  }

  /// <summary>点「选择目录…」：定位到当前引擎目录后弹出目录选择框。</summary>
  private void OnChooseEngineDirPressed()
  {
    PrepareDialog(EngineDirDialog, _config.EngineDir.Value);
    FileDialogSizing.Popup(EngineDirDialog);
  }

  /// <summary>点「选择目录…」：定位到当前沙箱根后弹出目录选择框。</summary>
  private void OnChooseSandboxRootPressed()
  {
    PrepareDialog(SandboxRootDialog, _config.SandboxRoot.Value);
    FileDialogSizing.Popup(SandboxRootDialog);
  }

  /// <summary>AutoInject 依赖解析完成：取配置、挂绑定并首次渲染；重复解析时先释放上一轮绑定。</summary>
  /// <remarks>
  /// 依赖解析可能因节点重新入树而再次触发，故这里必须先 <see cref="DisposeBindings"/> 再重挂，
  /// 否则同一条 <c>AutoValue</c> 会被订阅多次（回填回调重复、探针重复排）。
  /// </remarks>
  public void OnResolved()
  {
    _dm = DataManager;
    _config = _dm.RecordingConfig;

    DisposeBindings();

    BindConfigValues();
    SyncControlsFromConfig();
    RefreshProbes();
  }

  /// <summary>本面板实际执行过几次磁盘占用估算（测试用探针计数；生产代码不读取）。</summary>
  internal int FootprintProbeRuns => _footprintProbeRuns;

  /// <summary>清零磁盘估算计数（测试用；生产代码不调用）。</summary>
  internal void ResetFootprintProbeCounterForTests() => _footprintProbeRuns = 0;

  #region Controls

  /// <summary>分组标题（普通文本，整组常显，不再有折叠开关）。</summary>
  [Node("%SectionTitle")]
  public ILabel SectionTitle { get; set; } = default!;

  /// <summary>引擎目录输入框（提交或失焦时才写回）。</summary>
  [Node("%EngineDirEdit")]
  public ILineEdit EngineDirEdit { get; set; } = default!;

  /// <summary>引擎目录选择按钮。</summary>
  [Node("%ChooseEngineDirButton")]
  public IButton ChooseEngineDirButton { get; set; } = default!;

  /// <summary>从 Sharp 目录推断引擎目录的按钮。</summary>
  [Node("%InferEngineDirButton")]
  public IButton InferEngineDirButton { get; set; } = default!;

  /// <summary>引擎目录校验状态。</summary>
  [Node("%EngineStatusLabel")]
  public ILabel EngineStatusLabel { get; set; } = default!;

  /// <summary>并行度输入框。</summary>
  [Node("%ParallelismBox")]
  public ISpinBox ParallelismBox { get; set; } = default!;

  /// <summary>沙箱根目录输入框（留空即默认目录）。</summary>
  [Node("%SandboxRootEdit")]
  public ILineEdit SandboxRootEdit { get; set; } = default!;

  /// <summary>沙箱根目录选择按钮。</summary>
  [Node("%ChooseSandboxRootButton")]
  public IButton ChooseSandboxRootButton { get; set; } = default!;

  /// <summary>沙箱根恢复默认按钮。</summary>
  [Node("%ResetSandboxRootButton")]
  public IButton ResetSandboxRootButton { get; set; } = default!;

  /// <summary>沙箱空间与目录提示。</summary>
  [Node("%DiskHintLabel")]
  public ILabel DiskHintLabel { get; set; } = default!;

  /// <summary>两个插件的部署状态。</summary>
  [Node("%PluginStatusLabel")]
  public ILabel PluginStatusLabel { get; set; } = default!;

  /// <summary>一键安装并启用本插件的按钮。</summary>
  [Node("%InstallPluginButton")]
  public IButton InstallPluginButton { get; set; } = default!;

  /// <summary>一键启用弹幕录制器的按钮。</summary>
  [Node("%EnableRecorderButton")]
  public IButton EnableRecorderButton { get; set; } = default!;

  /// <summary>重新检查插件状态的按钮。</summary>
  [Node("%RefreshPluginButton")]
  public IButton RefreshPluginButton { get; set; } = default!;

  /// <summary>帧数上限输入框。</summary>
  [Node("%MaxFrameBox")]
  public ISpinBox MaxFrameBox { get; set; } = default!;

  /// <summary>首次抽帧间隔输入框。</summary>
  [Node("%FirstIntervalBox")]
  public ISpinBox FirstIntervalBox { get; set; } = default!;

  /// <summary>换挡重录用抽帧间隔输入框。</summary>
  [Node("%SecondIntervalBox")]
  public ISpinBox SecondIntervalBox { get; set; } = default!;

  /// <summary>产物放缩比（分辨率）输入框，单位为百分比。</summary>
  [Node("%ScalePercentBox")]
  public ISpinBox ScalePercentBox { get; set; } = default!;

  /// <summary>高级参数说明。</summary>
  [Node("%AdvancedHintLabel")]
  public ILabel AdvancedHintLabel { get; set; } = default!;

  /// <summary>上次输出目录展示。</summary>
  [Node("%LastOutputLabel")]
  public ILabel LastOutputLabel { get; set; } = default!;

  /// <summary>最近一次操作的结果提示（安装结果、被拒的原因等）。</summary>
  [Node("%ActionLabel")]
  public ILabel ActionLabel { get; set; } = default!;

  /// <summary>引擎目录选择对话框。</summary>
  [Node("%EngineDirDialog")]
  public IFileDialog EngineDirDialog { get; set; } = default!;

  /// <summary>沙箱根目录选择对话框。</summary>
  [Node("%SandboxRootDialog")]
  public IFileDialog SandboxRootDialog { get; set; } = default!;

  /// <summary>磁盘占用探针的去抖定时器（场景注入；单次触发，重复请求只重启它）。</summary>
  [Node("%ProbeTimer")]
  public ITimer ProbeTimer { get; set; } = default!;

  #endregion

  /// <summary>进页刷新：清空上一次的操作提示，回填配置值并重渲染状态（每次「信息」类别页显示时调用）。</summary>
  public void Refresh()
  {
    // 进页清掉上一次的提示：否则一条旧提示会永久驻留并盖住本次操作结果
    ActionLabel.Text = string.Empty;

    SyncControlsFromConfig();
    RefreshProbes();
  }

  /// <summary>提交引擎目录：校验通过才写回模型，否则保留原值并提示原因。</summary>
  /// <param name="text">输入框内容。</param>
  public void CommitEngineDir(string? text)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(_config.EngineDir.Value, value, StringComparison.Ordinal))
    {
      SyncEngineDirControl();
      RefreshProbes();
      return;
    }

    if (value.Length > 0 && !EngineLocator.TryValidate(value, out var reason))
    {
      SyncEngineDirControl();
      ActionLabel.Text = $"引擎目录无效：{reason}（已保留原值）";
      RefreshProbes();
      return;
    }

    _config.EngineDir.Value = value;
    _dm.TriggerAutoSave();
    SyncEngineDirControl();
    ActionLabel.Text = value.Length == 0 ? "已清空引擎目录。" : $"已设置引擎目录：{value}";
    RefreshProbes();
  }

  /// <summary>提交沙箱根目录：留空即默认目录；非空且不可用时保留原值并提示。</summary>
  /// <param name="text">输入框内容。</param>
  public void CommitSandboxRoot(string? text)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(_config.SandboxRoot.Value, value, StringComparison.Ordinal))
    {
      SyncSandboxRootControl();
      RefreshProbes();
      return;
    }

    if (value.Length > 0 && !RecordingSandbox.TryValidateRoot(value, out _, out var reason))
    {
      SyncSandboxRootControl();
      ActionLabel.Text = $"{reason}（已保留原值）";
      RefreshProbes();
      return;
    }

    _config.SandboxRoot.Value = value;
    _dm.TriggerAutoSave();
    SyncSandboxRootControl();
    ActionLabel.Text =
      value.Length == 0
        ? $"已恢复默认沙箱根：{RecordingSandbox.GetDefaultRootDir()}"
        : $"已设置沙箱根：{value}";
    RefreshProbes();
  }

  /// <summary>恢复默认沙箱根（等价于清空）。</summary>
  public void ResetSandboxRoot() => CommitSandboxRoot(string.Empty);

  /// <summary>写入并行度（越界值收敛）。</summary>
  /// <param name="value">用户输入值。</param>
  public void ApplyParallelism(int value) =>
    ApplyInt(RecordingConfig.ClampParallelism(value), _config.Parallelism, ParallelismBox);

  /// <summary>写入帧数上限（越界值收敛）。</summary>
  /// <param name="value">用户输入值。</param>
  public void ApplyMaxFrame(int value) =>
    ApplyInt(RecordingConfig.ClampMaxFrame(value), _config.MaxFrame, MaxFrameBox);

  /// <summary>写入首次抽帧间隔（越界值收敛）。</summary>
  /// <param name="value">用户输入值。</param>
  public void ApplyFirstInterval(int value) =>
    ApplyInt(RecordingConfig.ClampInterval(value), _config.FirstInterval, FirstIntervalBox);

  /// <summary>写入换挡重录用抽帧间隔（越界值收敛）。</summary>
  /// <param name="value">用户输入值。</param>
  public void ApplySecondInterval(int value) =>
    ApplyInt(RecordingConfig.ClampInterval(value), _config.SecondInterval, SecondIntervalBox);

  /// <summary>写入产物放缩比（越界值收敛）。</summary>
  /// <param name="value">用户输入值（%）。</param>
  public void ApplyScalePercent(int value) =>
    ApplyInt(RecordingConfig.ClampScalePercent(value), _config.ScalePercent, ScalePercentBox);

  /// <summary>从整合板块配置的 Sharp 目录推断引擎目录（只作建议值，仍须通过校验）。</summary>
  public void InferEngineDir()
  {
    var sharpEditorPath = _dm.MergeConfig.SharpEditorPath.Value;
    if (string.IsNullOrWhiteSpace(sharpEditorPath))
    {
      ActionLabel.Text = "请先在「整合」板块设置 Editor Sharp 目录，再来推断引擎目录。";
      RefreshProbes();
      return;
    }

    var inferred = EngineLocator.TryInferFromSharpDir(sharpEditorPath);
    if (string.IsNullOrWhiteSpace(inferred))
    {
      ActionLabel.Text = "未从 Sharp 目录推断出引擎目录，请手工选择。";
      RefreshProbes();
      return;
    }

    var hadValue = _config.EngineDir.Value.Length > 0;
    CommitEngineDir(inferred);
    if (string.Equals(_config.EngineDir.Value, inferred, StringComparison.Ordinal))
    {
      ActionLabel.Text = hadValue
        ? $"已从 Sharp 目录推断并覆盖原值：{inferred}，请确认。"
        : $"已从 Sharp 目录推断为 {inferred}，请确认。";
    }
  }

  /// <summary>一键安装并启用本插件（会写引擎目录，仅在用户点击后调用）。</summary>
  public void InstallPlugin()
  {
    try
    {
      ActionLabel.Text = PluginDeployer.InstallAutocmex(_config.EngineDir.Value);
    }
    catch (PluginDeployException ex)
    {
      ActionLabel.Text = ex.Message;
    }
    RefreshProbes();
  }

  /// <summary>一键启用弹幕录制器（会写引擎目录，仅在用户点击后调用）。</summary>
  public void EnableRecorder()
  {
    try
    {
      ActionLabel.Text = PluginDeployer.EnableRecorder(_config.EngineDir.Value);
    }
    catch (PluginDeployException ex)
    {
      ActionLabel.Text = ex.Message;
    }
    RefreshProbes();
  }

  /// <summary>
  /// 重渲染状态：插件检查与目录校验是常数级操作，保持同步；磁盘占用估算交给去抖后的后台探针。
  /// </summary>
  /// <remarks>
  /// 进页、提交引擎/沙箱目录、改并行度都会走到这里；300ms 内的多次请求只落一次磁盘枚举，
  /// 且结果按「引擎目录 + game/ 修改时间」缓存，两者没变就直接沿用，不再递归扫盘。
  /// </remarks>
  public void RefreshProbes()
  {
    var engineDir = _config.EngineDir.Value;
    var inspection = PluginDeployer.Inspect(engineDir);

    RefreshEngineStatus(engineDir, inspection);
    RefreshPluginStatus(inspection);

    // 操作提示只在有内容时占位（各写入动作都以 RefreshProbes() 收尾，故这里统一收敛可见性）
    ActionLabel.Visible = ActionLabel.Text.Length > 0;

    InferEngineDirButton.Disabled = string.IsNullOrWhiteSpace(
      _dm.MergeConfig.SharpEditorPath.Value
    );

    if (!inspection.EngineDirUsable)
    {
      SetStatus(DiskHintLabel, "沙箱空间提示：先设置有效的引擎目录，之后才能估算占用。", false);
      return;
    }

    var gameDirWriteUtc = ReadLastWriteTimeUtc(EngineLocator.GetGameDir(engineDir));
    if (
      _hasFootprintCache
      && string.Equals(_cachedEngineDir, engineDir, StringComparison.Ordinal)
      && _cachedGameDirWriteUtc == gameDirWriteUtc
    )
    {
      RenderDiskHint(_cachedFootprintBytes);
      return;
    }

    QueueFootprintProbe();
  }

  /// <summary>把对话框定位到当前目录（目录不存在时保持引擎给的默认位置）。</summary>
  private static void PrepareDialog(IFileDialog dialog, string currentDir)
  {
    if (Directory.Exists(currentDir))
    {
      dialog.CurrentDir = currentDir;
    }
  }

  /// <summary>数值框变化回调：同步控件回填期间不触发写入，避免自激。</summary>
  /// <param name="apply">真正的写入动作。</param>
  private void OnSpinBoxChanged(Action apply)
  {
    if (_syncing)
    {
      return;
    }
    apply();
  }

  /// <summary>写入一个整型配置项：收敛后写回、自动保存、回填控件并重渲染状态。</summary>
  /// <param name="clamped">收敛后的值。</param>
  /// <param name="target">配置项。</param>
  /// <param name="box">对应控件。</param>
  private void ApplyInt(int clamped, AutoValue<int> target, ISpinBox box)
  {
    if (target.Value != clamped)
    {
      target.Value = clamped;
      _dm.TriggerAutoSave();
      ActionLabel.Text = $"已更新为 {clamped}。";
    }
    SyncSpinBox(box, clamped);
    RefreshProbes();
  }

  /// <summary>把模型里的配置值一次性回填到控件（纯内存操作，不读写磁盘）。</summary>
  /// <remarks>
  /// 只做「模型 → 控件」的回填；插件/目录/磁盘提示等状态渲染由 <see cref="RefreshProbes"/> 负责，
  /// 回填期间挂起写入回调，避免刷新把界面上的值当成用户输入再写回模型。
  /// </remarks>
  private void SyncControlsFromConfig()
  {
    _syncing = true;
    try
    {
      SyncEngineDirControl();
      SyncSandboxRootControl();
      SyncSpinBox(ParallelismBox, RecordingConfig.ClampParallelism(_config.Parallelism.Value));
      SyncSpinBox(MaxFrameBox, RecordingConfig.ClampMaxFrame(_config.MaxFrame.Value));
      SyncSpinBox(FirstIntervalBox, RecordingConfig.ClampInterval(_config.FirstInterval.Value));
      SyncSpinBox(SecondIntervalBox, RecordingConfig.ClampInterval(_config.SecondInterval.Value));
      SyncSpinBox(ScalePercentBox, RecordingConfig.ClampScalePercent(_config.ScalePercent.Value));
      RefreshLastOutputLabel();
    }
    finally
    {
      _syncing = false;
    }
  }

  /// <summary>把上次输出目录配置值回填到展示行。</summary>
  private void RefreshLastOutputLabel() =>
    LastOutputLabel.Text =
      _config.LastOutputDir.Value.Length == 0
        ? "上次输出目录：尚未录制过。"
        : $"上次输出目录：{_config.LastOutputDir.Value}";

  /// <summary>把引擎目录配置值回填到输入框。</summary>
  private void SyncEngineDirControl() => EngineDirEdit.Text = _config.EngineDir.Value;

  /// <summary>把沙箱根配置值回填到输入框。</summary>
  private void SyncSandboxRootControl() => SandboxRootEdit.Text = _config.SandboxRoot.Value;

  /// <summary>回填数值框（临时置起同步标志，避免触发写入）。</summary>
  /// <param name="box">目标数值框。</param>
  /// <param name="value">要显示的值。</param>
  private void SyncSpinBox(ISpinBox box, int value)
  {
    var outer = _syncing;
    _syncing = true;
    try
    {
      box.Value = value;
    }
    finally
    {
      _syncing = outer;
    }
  }

  #region 配置绑定与磁盘探针

  /// <summary>为 8 个录制配置项挂值绑定：别处改配置时把值回填进控件（规则 18/19）。</summary>
  /// <remarks>
  /// 订阅会立刻用当前值回放一次，首次解析时先抑制这次回放（紧接着就整体回填）；之后的真实变化
  /// 只排 <c>CallDeferred</c>，不在绑定回调栈里碰控件。回填期间由 <c>_syncing</c> 护栏阻断
  /// SpinBox 的写回，避免「回填 → 写回 → 再回填」自激。只有引擎目录/沙箱根/并行度会改变
  /// 磁盘提示，故这三项连带重排探针，其余只回填控件。
  /// </remarks>
  private void BindConfigValues()
  {
    // 订阅即回放一次：首次解析时紧接着会整体回填，这里的回放不必再排 8 次延迟任务
    _isBindingConfigValues = true;
    try
    {
      _bindings.Add(
        _config.EngineDir.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: true))
      );
      _bindings.Add(
        _config.SandboxRoot.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: true))
      );
      _bindings.Add(
        _config.Parallelism.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: true))
      );
      _bindings.Add(
        _config.MaxFrame.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: false))
      );
      _bindings.Add(
        _config.FirstInterval.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: false))
      );
      _bindings.Add(
        _config.SecondInterval.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: false))
      );
      _bindings.Add(
        _config.ScalePercent.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: false))
      );
      _bindings.Add(
        _config.LastOutputDir.Bind().OnValue(_ => OnConfigValueChanged(refreshProbes: false))
      );
    }
    finally
    {
      _isBindingConfigValues = false;
    }
  }

  /// <summary>配置值被别处改动：回填与探针都调度到主线程（规则 19：绑定回调里不碰控件）。</summary>
  /// <param name="refreshProbes">是否连带重排磁盘探针。</param>
  private void OnConfigValueChanged(bool refreshProbes)
  {
    if (_isBindingConfigValues)
    {
      return;
    }

    CallDeferred(nameof(FlushConfigValues), refreshProbes);
  }

  /// <summary>回填配置值控件，必要时重排磁盘探针（公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约）。</summary>
  /// <param name="refreshProbes">是否连带重排磁盘探针。</param>
  public void FlushConfigValues(bool refreshProbes)
  {
    SyncControlsFromConfig();
    if (refreshProbes)
    {
      RefreshProbes();
    }
  }

  /// <summary>释放全部配置绑定（重新解析依赖前与节点退出时复用）。</summary>
  private void DisposeBindings()
  {
    foreach (var binding in _bindings)
    {
      binding.Dispose();
    }

    _bindings.Clear();
  }

  /// <inheritdoc/>
  public override void _ExitTree()
  {
    // 规则 20：绑定必须在节点退出时释放；探针定时器也停掉，避免对已离场面板回填
    DisposeBindings();
    ProbeTimer?.Stop();

    base._ExitTree();
  }

  /// <summary>排一次去抖的磁盘探针：300ms 内的多次请求合并成一次（Timer 由场景注入）。</summary>
  private void QueueFootprintProbe()
  {
    // 依赖解析与进页刷新都在树内；未入树时 Timer 不会走，等首次进页刷新即可。
    if (IsInsideTree())
    {
      ProbeTimer.Start();
    }
  }

  /// <summary>去抖到期：在线程池里递归枚举 game/ 估算沙箱占用，结果回主线程渲染。</summary>
  /// <remarks>
  /// <see cref="RecordingSandbox.EstimateFootprint"/> 会递归枚举 game/ 全树，是主线程卡顿的来源；
  /// <c>await Task.Run</c> 的续体由 Godot 同步上下文回到主线程，回填仍走 <c>CallDeferred</c>。
  /// </remarks>
  private async void OnFootprintDebounceElapsed()
  {
    try
    {
      var engineDir = _config.EngineDir.Value;
      if (!EngineLocator.TryValidate(engineDir, out _))
      {
        return;
      }

      var engineExe = EngineLocator.FindEngineExe(engineDir);
      var engineExeName = engineExe is null ? string.Empty : Path.GetFileName(engineExe);
      var gameDirWriteUtc = ReadLastWriteTimeUtc(EngineLocator.GetGameDir(engineDir));

      var perWorker = await Task.Run(() =>
        RecordingSandbox.EstimateFootprint(engineDir, string.Empty, engineExeName ?? string.Empty)
      );

      _footprintProbeRuns++;

      if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
      {
        return;
      }

      CallDeferred(nameof(ApplyFootprintResult), perWorker, engineDir, gameDirWriteUtc.Ticks);
    }
    catch (Exception ex)
    {
      // 扫描期间目录被删/没有权限/面板已释放：保留上一次提示，等下一次刷新再试
      GD.PushWarning($"RecordingConfigPanel: 磁盘占用估算失败：{ex.Message}");
    }
  }

  /// <summary>把后台探针结果写进缓存并渲染磁盘提示（公开以便 <c>CallDeferred</c> 按名调用）。</summary>
  /// <param name="perWorkerBytes">单 worker 的估算字节数。</param>
  /// <param name="engineDir">发起探针时的引擎目录。</param>
  /// <param name="gameDirWriteUtcTicks">发起探针时 game/ 的 UTC 修改时间（ticks）。</param>
  public void ApplyFootprintResult(long perWorkerBytes, string engineDir, long gameDirWriteUtcTicks)
  {
    // 探针跑完前用户已换引擎目录：这次结果作废，等新目录那轮探针
    if (!string.Equals(_config.EngineDir.Value, engineDir, StringComparison.Ordinal))
    {
      return;
    }

    _cachedEngineDir = engineDir;
    _cachedGameDirWriteUtc = new DateTime(gameDirWriteUtcTicks, DateTimeKind.Utc);
    _cachedFootprintBytes = perWorkerBytes;
    _hasFootprintCache = true;

    RenderDiskHint(perWorkerBytes);
  }

  /// <summary>读取目录的 UTC 修改时间；目录不存在或不可读时按「无时间戳」处理。</summary>
  /// <param name="path">目录路径。</param>
  /// <returns>修改时间；无法读取时为 <see cref="DateTime.MinValue"/>。</returns>
  private static DateTime ReadLastWriteTimeUtc(string path)
  {
    try
    {
      return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return DateTime.MinValue;
    }
  }

  #endregion

  /// <summary>渲染引擎目录状态行。</summary>
  /// <param name="engineDir">当前配置的引擎目录。</param>
  /// <param name="inspection">插件检查结果（含引擎目录可用性）。</param>
  private void RefreshEngineStatus(string engineDir, RecordingPluginInspection inspection)
  {
    if (engineDir.Length == 0)
    {
      SetStatus(
        EngineStatusLabel,
        "未设置引擎目录：请选择 LuaSTG 根目录（需含 game/，且 game/ 内有 launch 文件）。",
        false
      );
      return;
    }

    if (!inspection.EngineDirUsable)
    {
      EngineLocator.TryValidate(engineDir, out var reason);
      SetStatus(EngineStatusLabel, $"引擎目录无效：{reason}", false);
      return;
    }

    var engineExe = EngineLocator.FindEngineExe(engineDir);
    SetStatus(
      EngineStatusLabel,
      engineExe is null
        ? $"引擎目录可用，但没找到 LuaSTGSub*.exe：{engineDir}"
        : $"引擎目录可用，可执行文件：{Path.GetFileName(engineExe)}",
      engineExe is not null
    );
  }

  /// <summary>渲染插件状态行，并按「能不能做」决定两个部署按钮的可用性。</summary>
  /// <param name="inspection">插件检查结果。</param>
  private void RefreshPluginStatus(RecordingPluginInspection inspection)
  {
    if (!inspection.EngineDirUsable)
    {
      SetStatus(PluginStatusLabel, "插件状态：引擎目录未就绪，设置有效目录后再检查。", false);
      InstallPluginButton.Disabled = true;
      EnableRecorderButton.Disabled = true;
      return;
    }

    SetStatus(
      PluginStatusLabel,
      $"{DescribePlugin(PluginDeployer.AutocmexPluginDirName, inspection.Autocmex)}\n"
        + DescribePlugin(PluginDeployer.RecorderPluginKeyword, inspection.Recorder),
      inspection.AllReady
    );
    InstallPluginButton.Disabled = !inspection.Autocmex.CanInstall;
    EnableRecorderButton.Disabled = !inspection.Recorder.CanEnable;
  }

  /// <summary>把一个插件状态转成一行中文说明。</summary>
  /// <param name="displayName">展示名（目录名）。</param>
  /// <param name="status">插件状态快照。</param>
  /// <returns>形如 <c>autocmex：未安装（…）</c> 的说明。</returns>
  private static string DescribePlugin(string displayName, RecordingPluginStatus status)
  {
    var state = status.State switch
    {
      RecordingPluginState.Ready => "已安装并启用",
      RecordingPluginState.InstalledDisabled => "已安装但被禁用（引擎不会加载）",
      RecordingPluginState.Missing => "未安装",
      RecordingPluginState.ManifestBroken => "插件清单损坏，已停止一切部署动作",
      _ => "状态未知",
    };
    return status.Detail.Length == 0
      ? $"{displayName}：{state}"
      : $"{displayName}：{state}（{status.Detail}）";
  }

  /// <summary>按单 worker 估算值渲染沙箱空间提示：并行度 × 单份占用 与所在卷剩余空间对比。</summary>
  /// <param name="perWorker">单 worker 的估算字节数（探针结果或缓存）。</param>
  private void RenderDiskHint(long perWorker)
  {
    var parallelism = RecordingConfig.ClampParallelism(_config.Parallelism.Value);
    var required = perWorker * parallelism;

    if (
      !RecordingSandbox.TryValidateRoot(_config.SandboxRoot.Value, out var root, out var rootReason)
    )
    {
      SetStatus(DiskHintLabel, $"沙箱根目录不可用：{rootReason}", false);
      return;
    }

    var enough = RecordingSandbox.HasEnoughFreeSpace(root, required, out var freeBytes);
    var text =
      $"沙箱根：{root}\n单 worker 约 {FormatBytes(perWorker)} × 并行度 {parallelism} = "
      + $"约 {FormatBytes(required)}；所在卷剩余 {FormatBytes(freeBytes)}。";
    SetStatus(
      DiskHintLabel,
      enough ? text : text + "空间不足时会自动下调并行度，降到 1 仍不够则起录中止。",
      enough
    );
  }

  /// <summary>设置状态行文案与颜色（绿=可用，橙=需处理）。</summary>
  /// <param name="label">状态行。</param>
  /// <param name="text">文案。</param>
  /// <param name="ok">是否处于可用状态。</param>
  private static void SetStatus(ILabel label, string text, bool ok)
  {
    label.Text = text;
    label.AddThemeColorOverride("font_color", ok ? _okColor : _warnColor);
    label.TooltipText = text;
  }

  /// <summary>把字节数格式化成人类可读的短串。</summary>
  /// <param name="bytes">字节数（负数表示未知）。</param>
  /// <returns>形如 <c>1.2 GB</c> 的文本。</returns>
  private static string FormatBytes(long bytes)
  {
    if (bytes < 0)
    {
      return "未知";
    }

    var units = new[] { "B", "KB", "MB", "GB", "TB" };
    double value = bytes;
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1)
    {
      value /= 1024;
      unit++;
    }
    return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
  }
}
