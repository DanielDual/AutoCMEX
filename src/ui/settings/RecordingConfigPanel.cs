namespace AutoCMEX.UI.Settings;

using System;
using System.IO;
using AutoCMEX.Core.Recording;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 符卡 GIF 录制的设置分组：引擎目录、插件部署、并行度、沙箱根与高级录制参数。
/// </summary>
/// <remarks>
/// <para>
/// 挂在设置面板「信息」类别页（<see cref="InfoConfigPanel"/>）底部，由该页在 <c>OnResolved</c> 时创建。
/// 「信息」类别页没有独立场景（节点直接写在 <c>SettingsPanel.tscn</c> 里），故这里沿用该页既有的
/// 「C# 构建行」范式，不为一个分组改动共享场景。
/// </para>
/// <para>
/// 写入纪律：配置项只在通过校验时写回模型（非法值保留原值并只提示）；插件的安装/启用只响应用户点击，
/// 未点击时对引擎目录零写入；状态渲染一律走只读查询（<see cref="PluginDeployer.Inspect"/>、
/// <see cref="EngineLocator.TryValidate"/>、<see cref="RecordingSandbox.EstimateFootprint"/>）。
/// </para>
/// <para>
/// 交互入口都留了公开方法（<see cref="CommitEngineDir"/>、<see cref="ApplyParallelism"/> 等），
/// 便于单测直接驱动，不必模拟控件事件。
/// </para>
/// </remarks>
public sealed partial class RecordingConfigPanel : VBoxContainer
{
  private static readonly Color _okColor = new(0.62f, 0.86f, 0.52f);
  private static readonly Color _warnColor = new(0.96f, 0.68f, 0.36f);

  private readonly DataManager _dm;
  private readonly RecordingConfig _config;
  private bool _syncing;

  /// <summary>构建分组：控件一次建好，随后同步配置值并渲染状态。</summary>
  /// <param name="dataManager">数据管理器（提供录制配置、合并配置与自动保存）。</param>
  /// <exception cref="ArgumentNullException"><paramref name="dataManager"/> 为空。</exception>
  public RecordingConfigPanel(DataManager dataManager)
  {
    _dm = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
    _config = _dm.RecordingConfig;
    Name = "RecordingConfigPanel";
    BuildToggle();
    BuildBody();
    BuildDialogs();
    SyncFromConfig();
    RefreshStatuses();
  }

  #region Controls

  /// <summary>整组的折叠开关（默认折叠，避免挤压目标群列表）。</summary>
  public CheckBox SectionToggle { get; private set; } = default!;

  /// <summary>折叠开关控制的设置主体。</summary>
  public VBoxContainer SectionBody { get; private set; } = default!;

  /// <summary>引擎目录输入框（提交或失焦时才写回）。</summary>
  public LineEdit EngineDirEdit { get; private set; } = default!;

  /// <summary>引擎目录选择按钮。</summary>
  public Button ChooseEngineDirButton { get; private set; } = default!;

  /// <summary>从 Sharp 目录推断引擎目录的按钮。</summary>
  public Button InferEngineDirButton { get; private set; } = default!;

  /// <summary>引擎目录校验状态。</summary>
  public Label EngineStatusLabel { get; private set; } = default!;

  /// <summary>并行度输入框。</summary>
  public SpinBox ParallelismBox { get; private set; } = default!;

  /// <summary>沙箱根目录输入框（留空即默认目录）。</summary>
  public LineEdit SandboxRootEdit { get; private set; } = default!;

  /// <summary>沙箱根目录选择按钮。</summary>
  public Button ChooseSandboxRootButton { get; private set; } = default!;

  /// <summary>沙箱根恢复默认按钮。</summary>
  public Button ResetSandboxRootButton { get; private set; } = default!;

  /// <summary>沙箱空间与目录提示。</summary>
  public Label DiskHintLabel { get; private set; } = default!;

  /// <summary>两个插件的部署状态。</summary>
  public Label PluginStatusLabel { get; private set; } = default!;

  /// <summary>一键安装并启用本插件的按钮。</summary>
  public Button InstallPluginButton { get; private set; } = default!;

  /// <summary>一键启用弹幕录制器的按钮。</summary>
  public Button EnableRecorderButton { get; private set; } = default!;

  /// <summary>重新检查插件状态的按钮。</summary>
  public Button RefreshPluginButton { get; private set; } = default!;

  /// <summary>帧数上限输入框。</summary>
  public SpinBox MaxFrameBox { get; private set; } = default!;

  /// <summary>首次抽帧间隔输入框。</summary>
  public SpinBox FirstIntervalBox { get; private set; } = default!;

  /// <summary>换挡重录用抽帧间隔输入框。</summary>
  public SpinBox SecondIntervalBox { get; private set; } = default!;

  /// <summary>产物放缩比（分辨率）输入框，单位为百分比。</summary>
  public SpinBox ScalePercentBox { get; private set; } = default!;

  /// <summary>高级参数说明。</summary>
  public Label AdvancedHintLabel { get; private set; } = default!;

  /// <summary>上次输出目录展示。</summary>
  public Label LastOutputLabel { get; private set; } = default!;

  /// <summary>最近一次操作的结果提示（安装结果、被拒的原因等）。</summary>
  public Label ActionLabel { get; private set; } = default!;

  /// <summary>引擎目录选择对话框。</summary>
  public FileDialog EngineDirDialog { get; private set; } = default!;

  /// <summary>沙箱根目录选择对话框。</summary>
  public FileDialog SandboxRootDialog { get; private set; } = default!;

  #endregion

  /// <summary>是否已展开。</summary>
  public bool IsExpanded => SectionToggle.ButtonPressed;

  /// <summary>把配置值同步到控件，并重渲染全部状态（每次「信息」类别页显示时调用）。</summary>
  public void Refresh()
  {
    SyncFromConfig();
    RefreshStatuses();
  }

  /// <summary>展开或折叠设置主体（展开时顺带刷新一次状态）。</summary>
  /// <param name="expanded">是否展开。</param>
  public void SetExpanded(bool expanded)
  {
    SectionToggle.ButtonPressed = expanded;
    SectionBody.Visible = expanded;
    if (expanded)
    {
      Refresh();
    }
  }

  /// <summary>提交引擎目录：校验通过才写回模型，否则保留原值并提示原因。</summary>
  /// <param name="text">输入框内容。</param>
  public void CommitEngineDir(string? text)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(_config.EngineDir.Value, value, StringComparison.Ordinal))
    {
      SyncEngineDirControl();
      RefreshStatuses();
      return;
    }

    if (value.Length > 0 && !EngineLocator.TryValidate(value, out var reason))
    {
      SyncEngineDirControl();
      ActionLabel.Text = $"引擎目录无效：{reason}（已保留原值）";
      RefreshStatuses();
      return;
    }

    _config.EngineDir.Value = value;
    _dm.TriggerAutoSave();
    SyncEngineDirControl();
    ActionLabel.Text = value.Length == 0 ? "已清空引擎目录。" : $"已设置引擎目录：{value}";
    RefreshStatuses();
  }

  /// <summary>提交沙箱根目录：留空即默认目录；非空且不可用时保留原值并提示。</summary>
  /// <param name="text">输入框内容。</param>
  public void CommitSandboxRoot(string? text)
  {
    var value = (text ?? string.Empty).Trim();
    if (string.Equals(_config.SandboxRoot.Value, value, StringComparison.Ordinal))
    {
      SyncSandboxRootControl();
      RefreshStatuses();
      return;
    }

    if (value.Length > 0 && !RecordingSandbox.TryValidateRoot(value, out _, out var reason))
    {
      SyncSandboxRootControl();
      ActionLabel.Text = $"{reason}（已保留原值）";
      RefreshStatuses();
      return;
    }

    _config.SandboxRoot.Value = value;
    _dm.TriggerAutoSave();
    SyncSandboxRootControl();
    ActionLabel.Text =
      value.Length == 0
        ? $"已恢复默认沙箱根：{RecordingSandbox.GetDefaultRootDir()}"
        : $"已设置沙箱根：{value}";
    RefreshStatuses();
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
      RefreshStatuses();
      return;
    }

    var inferred = EngineLocator.TryInferFromSharpDir(sharpEditorPath);
    if (string.IsNullOrWhiteSpace(inferred))
    {
      ActionLabel.Text = "未从 Sharp 目录推断出引擎目录，请手工选择。";
      RefreshStatuses();
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
    RefreshStatuses();
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
    RefreshStatuses();
  }

  /// <summary>只重渲染状态（不触碰配置值控件，避免打断正在进行的输入）。</summary>
  public void RefreshStatuses()
  {
    var engineDir = _config.EngineDir.Value;
    var inspection = PluginDeployer.Inspect(engineDir);

    RefreshEngineStatus(engineDir, inspection);
    RefreshPluginStatus(inspection);
    RefreshDiskHint(engineDir, inspection);

    // 操作提示只在有内容时占位（各写入动作都以 RefreshStatuses() 收尾，故这里统一收敛可见性）
    ActionLabel.Visible = ActionLabel.Text.Length > 0;

    InferEngineDirButton.Disabled = string.IsNullOrWhiteSpace(
      _dm.MergeConfig.SharpEditorPath.Value
    );
    LastOutputLabel.Text =
      _config.LastOutputDir.Value.Length == 0
        ? "上次输出目录：尚未录制过。"
        : $"上次输出目录：{_config.LastOutputDir.Value}";
  }

  /// <summary>构建折叠开关：默认折叠，避免挤压同页的目标群列表。</summary>
  private void BuildToggle()
  {
    SectionToggle = new CheckBox
    {
      Text = "符卡 GIF 录制设置",
      TooltipText = "展开后可配置引擎目录、录制插件、并行度与沙箱目录",
      ButtonPressed = false,
    };
    SectionToggle.Toggled += pressed =>
    {
      SectionBody.Visible = pressed;
      if (pressed)
      {
        Refresh();
      }
    };
    AddChild(SectionToggle);
  }

  /// <summary>按「标题 - 控件行 - 状态行」的节奏构建设置主体。</summary>
  private void BuildBody()
  {
    SectionBody = new VBoxContainer { Visible = false };
    AddChild(SectionBody);
    SectionBody.AddChild(new HSeparator());

    SectionBody.AddChild(new Label { Text = "引擎目录（LuaSTG 根目录，含 game/ 与 doc/）" });
    var engineRow = new HBoxContainer();
    EngineDirEdit = new LineEdit
    {
      PlaceholderText = @"例如 D:\LuaSTG",
      SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    EngineDirEdit.TextSubmitted += text => CommitEngineDir(text);
    EngineDirEdit.FocusExited += () => CommitEngineDir(EngineDirEdit.Text);
    engineRow.AddChild(EngineDirEdit);

    ChooseEngineDirButton = new Button { Text = "选择目录…" };
    ChooseEngineDirButton.Pressed += () =>
    {
      PrepareDialog(EngineDirDialog, _config.EngineDir.Value);
      EngineDirDialog.PopupCentered(new Vector2I(760, 520));
    };
    engineRow.AddChild(ChooseEngineDirButton);

    InferEngineDirButton = new Button
    {
      Text = "从 Sharp 目录推断",
      TooltipText = "按「整合」板块配置的 Editor Sharp 目录上溯查找引擎目录",
    };
    InferEngineDirButton.Pressed += InferEngineDir;
    engineRow.AddChild(InferEngineDirButton);
    SectionBody.AddChild(engineRow);

    EngineStatusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    SectionBody.AddChild(EngineStatusLabel);

    var parallelRow = new HBoxContainer();
    parallelRow.AddChild(new Label { Text = "并行录制的引擎实例数" });
    ParallelismBox = new SpinBox
    {
      MinValue = RecordingConfig.MinParallelism,
      MaxValue = RecordingConfig.MaxParallelism,
      Step = 1,
      Value = RecordingConfig.DefaultParallelism,
      CustomMinimumSize = new Vector2(90, 0),
      TooltipText =
        $"默认 {RecordingConfig.DefaultParallelism}，上限 {RecordingConfig.MaxParallelism}",
    };
    ParallelismBox.ValueChanged += value =>
      OnSpinBoxChanged(() => ApplyParallelism((int)Math.Round(value)));
    parallelRow.AddChild(ParallelismBox);
    parallelRow.AddChild(
      new Label
      {
        Text = "实际 worker = min(并行度, 战斗卡数)，临时空间不足时会自动下调",
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
      }
    );
    SectionBody.AddChild(parallelRow);

    SectionBody.AddChild(new HSeparator());
    SectionBody.AddChild(
      new Label { Text = "沙箱根目录（每轮为每个 worker 建一份引擎副本，留空用系统临时目录）" }
    );
    var sandboxRow = new HBoxContainer();
    SandboxRootEdit = new LineEdit
    {
      PlaceholderText = RecordingSandbox.GetDefaultRootDir(),
      SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    SandboxRootEdit.TextSubmitted += text => CommitSandboxRoot(text);
    SandboxRootEdit.FocusExited += () => CommitSandboxRoot(SandboxRootEdit.Text);
    sandboxRow.AddChild(SandboxRootEdit);

    ChooseSandboxRootButton = new Button { Text = "选择目录…" };
    ChooseSandboxRootButton.Pressed += () =>
    {
      PrepareDialog(SandboxRootDialog, _config.SandboxRoot.Value);
      SandboxRootDialog.PopupCentered(new Vector2I(760, 520));
    };
    sandboxRow.AddChild(ChooseSandboxRootButton);

    ResetSandboxRootButton = new Button
    {
      Text = "恢复默认",
      TooltipText = RecordingSandbox.GetDefaultRootDir(),
    };
    ResetSandboxRootButton.Pressed += ResetSandboxRoot;
    sandboxRow.AddChild(ResetSandboxRootButton);
    SectionBody.AddChild(sandboxRow);

    DiskHintLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    SectionBody.AddChild(DiskHintLabel);

    SectionBody.AddChild(new HSeparator());
    SectionBody.AddChild(new Label { Text = "录制插件（安装与启用只在你点击按钮后写入引擎目录）" });
    PluginStatusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    SectionBody.AddChild(PluginStatusLabel);

    var pluginRow = new HBoxContainer();
    InstallPluginButton = new Button
    {
      Text = "安装并启用 autocmex",
      TooltipText = "复制应用自带的录制插件到引擎，并在插件清单里登记为启用",
    };
    InstallPluginButton.Pressed += InstallPlugin;
    pluginRow.AddChild(InstallPluginButton);

    EnableRecorderButton = new Button
    {
      Text = "启用弹幕录制器",
      TooltipText = "只把插件清单里弹幕录制器条目的 enable 置 true，不动任何文件",
    };
    EnableRecorderButton.Pressed += EnableRecorder;
    pluginRow.AddChild(EnableRecorderButton);

    RefreshPluginButton = new Button { Text = "重新检查" };
    RefreshPluginButton.Pressed += RefreshStatuses;
    pluginRow.AddChild(RefreshPluginButton);
    SectionBody.AddChild(pluginRow);

    SectionBody.AddChild(new HSeparator());
    SectionBody.AddChild(new Label { Text = "高级参数" });
    var advancedRow = new HBoxContainer();
    MaxFrameBox = AddAdvancedSpinBox(
      advancedRow,
      "帧数上限",
      RecordingConfig.MinMaxFrame,
      RecordingConfig.MaxMaxFrame,
      ApplyMaxFrame
    );
    FirstIntervalBox = AddAdvancedSpinBox(
      advancedRow,
      "首次间隔",
      RecordingConfig.MinInterval,
      RecordingConfig.MaxInterval,
      ApplyFirstInterval
    );
    SecondIntervalBox = AddAdvancedSpinBox(
      advancedRow,
      "换挡后间隔",
      RecordingConfig.MinInterval,
      RecordingConfig.MaxInterval,
      ApplySecondInterval
    );
    ScalePercentBox = AddAdvancedSpinBox(
      advancedRow,
      "放缩比 (%)",
      RecordingConfig.MinScalePercent,
      RecordingConfig.MaxScalePercent,
      ApplyScalePercent,
      step: 5
    );
    SectionBody.AddChild(advancedRow);

    AdvancedHintLabel = new Label
    {
      Text =
        "间隔 3 ≈ 20 fps、5 ≈ 12 fps；玩家不干预时长卡会在帧数上限处截断，属正常产物（报告里标 complete = false）。"
        + "产物达到 30MB 时 QQ 发不出去，同样会自动换第二档间隔重录一次。"
        + "放缩比决定产物分辨率：50% 即捕获区域的一半像素（录制器默认），100% 为原始像素，越大越清晰、体积也越大。",
      AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };
    SectionBody.AddChild(AdvancedHintLabel);

    LastOutputLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    SectionBody.AddChild(LastOutputLabel);

    ActionLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    SectionBody.AddChild(ActionLabel);
  }

  /// <summary>构建两个目录选择对话框（隐藏的子节点，点击按钮才弹出）。</summary>
  private void BuildDialogs()
  {
    EngineDirDialog = BuildDirDialog("选择 LuaSTG 引擎根目录");
    EngineDirDialog.DirSelected += CommitEngineDir;
    SandboxRootDialog = BuildDirDialog("选择沙箱根目录");
    SandboxRootDialog.DirSelected += CommitSandboxRoot;
  }

  /// <summary>建一个只选目录的系统文件对话框。</summary>
  /// <param name="title">对话框标题。</param>
  /// <returns>已挂到本分组的对话框。</returns>
  private FileDialog BuildDirDialog(string title)
  {
    var dialog = new FileDialog
    {
      Title = title,
      FileMode = FileDialog.FileModeEnum.OpenDir,
      Access = FileDialog.AccessEnum.Filesystem,
      UseNativeDialog = false,
      Unresizable = false,
    };
    AddChild(dialog);
    return dialog;
  }

  /// <summary>把对话框定位到当前目录（目录不存在时保持引擎给的默认位置）。</summary>
  private static void PrepareDialog(FileDialog dialog, string currentDir)
  {
    if (Directory.Exists(currentDir))
    {
      dialog.CurrentDir = currentDir;
    }
  }

  /// <summary>在高级参数行里追加一个带标签的数值框。</summary>
  /// <param name="row">所在行。</param>
  /// <param name="label">标签文案。</param>
  /// <param name="min">下限。</param>
  /// <param name="max">上限。</param>
  /// <param name="apply">值变化时的写入动作（未展开同步时调用）。</param>
  /// <param name="step">数值框步长。</param>
  /// <returns>建好的数值框。</returns>
  private SpinBox AddAdvancedSpinBox(
    HBoxContainer row,
    string label,
    int min,
    int max,
    Action<int> apply,
    int step = 1
  )
  {
    row.AddChild(new Label { Text = label });
    var box = new SpinBox
    {
      MinValue = min,
      MaxValue = max,
      Step = step,
      Value = min,
      CustomMinimumSize = new Vector2(80, 0),
    };
    box.ValueChanged += value => OnSpinBoxChanged(() => apply((int)Math.Round(value)));
    row.AddChild(box);
    return box;
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
  private void ApplyInt(int clamped, AutoValue<int> target, SpinBox box)
  {
    if (target.Value != clamped)
    {
      target.Value = clamped;
      _dm.TriggerAutoSave();
      ActionLabel.Text = $"已更新为 {clamped}。";
    }
    SyncSpinBox(box, clamped);
    RefreshStatuses();
  }

  /// <summary>把模型里的配置值一次性回填到控件（回填期间挂起写入回调）。</summary>
  private void SyncFromConfig()
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
    }
    finally
    {
      _syncing = false;
    }
  }

  /// <summary>把引擎目录配置值回填到输入框。</summary>
  private void SyncEngineDirControl() => EngineDirEdit.Text = _config.EngineDir.Value;

  /// <summary>把沙箱根配置值回填到输入框。</summary>
  private void SyncSandboxRootControl() => SandboxRootEdit.Text = _config.SandboxRoot.Value;

  /// <summary>回填数值框（临时置起同步标志，避免触发写入）。</summary>
  /// <param name="box">目标数值框。</param>
  /// <param name="value">要显示的值。</param>
  private void SyncSpinBox(SpinBox box, int value)
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

  /// <summary>渲染引擎目录状态行。</summary>
  /// <param name="engineDir">当前配置的引擎目录。</param>
  /// <param name="inspection">插件检查结果（含引擎目录可用性）。</param>
  private void RefreshEngineStatus(string engineDir, RecordingPluginInspection inspection)
  {
    if (engineDir.Length == 0)
    {
      SetStatus(
        EngineStatusLabel,
        "未设置引擎目录：请选择 LuaSTG 根目录（需含 game/ 与 doc/）。",
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

  /// <summary>渲染沙箱空间提示：单份占用 × 并行度 与所在卷剩余空间对比。</summary>
  /// <param name="engineDir">当前配置的引擎目录。</param>
  /// <param name="inspection">插件检查结果（含引擎目录可用性）。</param>
  private void RefreshDiskHint(string engineDir, RecordingPluginInspection inspection)
  {
    if (!inspection.EngineDirUsable)
    {
      SetStatus(DiskHintLabel, "沙箱空间提示：先设置有效的引擎目录，之后才能估算占用。", false);
      return;
    }

    var engineExe = EngineLocator.FindEngineExe(engineDir);
    var perWorker = RecordingSandbox.EstimateFootprint(
      engineDir,
      string.Empty,
      engineExe is null ? string.Empty : Path.GetFileName(engineExe)
    );
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
  private static void SetStatus(Label label, string text, bool ok)
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
