namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using AutoCMEX;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.Services;
using AutoCMEX.UI;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 群聊配置面板 — 独立场景，管理 WebSocket 端口/模式/Koishi 地址、消息筛选与 Koishi 插件一键安装
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class ChatConfigPanel : VBoxContainer, IChatConfigPanel
{
  [Node("%PortInput")]
  public ISpinBox PortInput { get; set; } = default!;

  [Node("%ModeSelect")]
  public IOptionButton ModeSelect { get; set; } = default!;

  [Node("%KoishiUrlInput")]
  public ILineEdit KoishiUrlInput { get; set; } = default!;

  [Node("%KoishiUrlRow")]
  public IHBoxContainer KoishiUrlRow { get; set; } = default!;

  [Node("%FilterSelect")]
  public IOptionButton FilterSelect { get; set; } = default!;

  [Node("%InstallBtn")]
  public IButton InstallBtn { get; set; } = default!;

  [Node("%PluginPathLabel")]
  public ILabel PluginPathLabel { get; set; } = default!;

  [Node("%PluginFileDialog")]
  public IFileDialog PluginFileDialog { get; set; } = default!;

  [Node("%PluginOkDialog")]
  public IAcceptDialog PluginOkDialog { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  /// <summary>WebSocket 端口下限。</summary>
  private const int PortMinValue = 1;

  /// <summary>WebSocket 端口上限。</summary>
  private const int PortMaxValue = 65535;

  /// <summary>端口提交去抖窗口（秒）：窗口内的滚轮/连点箭头合并成一次写回。</summary>
  internal const double PortCommitDebounceSeconds = 0.3;

  private DataManager? _dm;
  private AppSettings _settings = new();

  /// <summary>五个设置项的值绑定（端口/模式/筛选/Koishi 地址/插件路径），<c>_ExitTree</c> 中统一释放（规则 20）。</summary>
  private readonly List<IDisposable> _bindings = new();

  /// <summary>本轮回填是否已排队：同帧内多个设置项变更只回填一次。</summary>
  private bool _refreshQueued;

  /// <summary>是否正在订阅设置值绑定（订阅时会立刻回放一次，抑制这类伪变更的排队）。</summary>
  private bool _isSubscribing;

  /// <summary>
  /// 正在把设置值回填到控件（程序化写控件）：此期间控件的变更事件不代表用户操作，不得回写设置。
  /// </summary>
  /// <remarks>
  /// 两个必须抑制的伪变更：真机 <see cref="SpinBox"/> 被赋越界值时会把值钳进范围并同步回发
  /// <c>value_changed</c>；回填 <see cref="KoishiUrlRow"/>.Visible 会让正聚焦的地址输入框同步发
  /// <c>focus_exited</c>。没有这道护栏，仅仅打开/对齐本页就会把配置写回去并触发一次自动保存。
  /// </remarks>
  private bool _applyingModel;

  /// <summary>端口 <see cref="SpinBox"/> 是否有等待去抖提交的用户改动。</summary>
  private bool _hasPendingPort;

  /// <summary>端口待提交值（用户在去抖窗口内的最后一次改动）。</summary>
  private int _pendingPort;

  /// <summary>端口去抖已累计的时间（秒）。</summary>
  private double _pendingPortElapsed;

  /// <inheritdoc/>
  public override void _Notification(int what)
  {
    this.Notify(what);
    if (what == (int)NotificationExitTree)
    {
      // 只释放绑定，不在退出时提交去抖中的端口改动：写模型会让 MainWindow 的端口绑定触发一次
      // WebSocket 重启（async void），而关窗路径上 Main 已关闭日志服务。代价是去抖窗口内直接关窗
      // 最多丢一笔未提交的端口编辑（重开后重设），换退出路径零副作用。
      DisposeBindings();
    }
  }

  public void OnReady()
  {
    PortInput.MinValue = PortMinValue;
    PortInput.MaxValue = PortMaxValue;
    // 端口按提交式写回：SpinBox 的箭头按钮/滚轮改值不经过输入框的「提交/失焦」路径（箭头按钮甚至不拿输入框焦点），
    // 只认失焦/回车会让点箭头改的端口一直不生效；故用 300ms 去抖统一收口所有改值入口，
    // 窗口内的滚轮/连点只产生一次写回（= 一次保存防抖重置 + 一次 WebSocket 重启）。
    PortInput.ValueChanged += OnPortChanged;
    // 去抖只在有改动时推进，平时不占每帧回调
    SetProcess(false);

    ModeSelect.AddItem("Server（等待连接）");
    ModeSelect.AddItem("Client（主动连接）");
    ModeSelect.ItemSelected += OnModeChanged;

    // 「Koishi 地址」按既有范式只在提交/失焦时回写（逐键回写会让每次输入都触发保存与重启）
    KoishiUrlInput.TextSubmitted += CommitKoishiUrl;
    KoishiUrlInput.FocusExited += () => CommitKoishiUrl(KoishiUrlInput.Text);

    FilterSelect.AddItem("仅严格格式匹配");
    FilterSelect.AddItem("仅 AI 智能匹配");
    FilterSelect.AddItem("先严格再 AI");
    FilterSelect.ItemSelected += OnFilterChanged;

    InstallBtn.Pressed += OnInstallPlugin;

    // 配置预置对话框
    PluginFileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
    PluginFileDialog.Access = FileDialog.AccessEnum.Filesystem;
    PluginFileDialog.Title = SelectDirectoryHint;
    PluginFileDialog.DirSelected += OnPluginDirSelected;

    PluginOkDialog.Title = "安装完成";
  }

  /// <inheritdoc/>
  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm is null)
      return;

    _settings = _dm.Settings;

    // UI 由设置值绑定驱动（规则 18）：此后任意一处改配置都由绑定排一次回填，不再手动 Refresh。
    // 订阅会立刻用当前值回放一次，订阅期间先抑制排队，紧接着同步回填一次——
    // 否则启动时就先排一串重复回填（与 AiModelConfigPanel 重挂元素绑定的做法一致）。
    _isSubscribing = true;
    try
    {
      _bindings.Add(_settings.WebSocketPort.Bind().OnValue(_ => QueueRefresh()));
      _bindings.Add(_settings.WebSocketMode.Bind().OnValue(_ => QueueRefresh()));
      _bindings.Add(_settings.MessageFilterMode.Bind().OnValue(_ => QueueRefresh()));
      _bindings.Add(_settings.KoishiWebSocketUrl.Bind().OnValue(_ => QueueRefresh()));
      _bindings.Add(_settings.KoishiPluginPath.Bind().OnValue(_ => QueueRefresh()));
    }
    finally
    {
      _isSubscribing = false;
    }

    Refresh();
  }

  /// <summary>释放全部设置值绑定（本面板退出时）。</summary>
  private void DisposeBindings()
  {
    foreach (var binding in _bindings)
      binding.Dispose();

    _bindings.Clear();
  }

  /// <summary>把同帧内多次回填请求合并成一次（绑定回调里只调度，规则 19）。</summary>
  private void QueueRefresh()
  {
    if (_isSubscribing || _refreshQueued)
      return;

    _refreshQueued = true;
    CallDeferred(nameof(FlushRefresh));
  }

  /// <summary>执行排队中的回填（公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约）。</summary>
  public void FlushRefresh()
  {
    _refreshQueued = false;
    Refresh();
  }

  /// <summary>把设置值回填到控件（由设置绑定驱动；回填期间抑制控件回写）。</summary>
  public void Refresh()
  {
    // 程序化赋值会触发控件的 ValueChanged/ItemSelected/focus_exited，必须先立起护栏，
    // 否则回填会反向写回设置并触发自动保存
    _applyingModel = true;
    try
    {
      PortInput.Value = _settings.WebSocketPort.Value;

      var isClient = string.Equals(
        _settings.WebSocketMode.Value,
        "Client",
        StringComparison.OrdinalIgnoreCase
      );
      ModeSelect.Select(isClient ? 1 : 0);
      KoishiUrlRow.Visible = isClient;
      KoishiUrlInput.Text = _settings.KoishiWebSocketUrl.Value;

      FilterSelect.Select(
        _settings.MessageFilterMode.Value switch
        {
          "ai" => 1,
          "strict_then_ai" => 2,
          _ => 0,
        }
      );

      RefreshPluginPath();
    }
    finally
    {
      _applyingModel = false;
    }
  }

  /// <summary>回显 Koishi 插件的安装状态（此前只写设置、界面从不显示，重启后看不出装没装过）。</summary>
  private void RefreshPluginPath()
  {
    var path = _settings.KoishiPluginPath.Value.Trim();
    PluginPathLabel.Text =
      path.Length == 0
        ? $"插件状态: 未安装（点上方按钮选择 Koishi 应用根目录或它的 {KoishiPluginSpec.ExternalDirName} 目录）"
        : $"插件状态: 已安装到 {path}";
  }

  /// <summary>
  /// 用户改了端口：只记待提交值并启动去抖，不立即写回设置。
  /// </summary>
  /// <remarks>
  /// 每次 <c>value_changed</c> 都直接写模型会让 <see cref="AutoCMEX.UI.Main.MainWindow"/> 的端口绑定
  /// 每 tick 触发一次完整 WebSocket 重启（停/Dispose/重建/启）并重置 1500ms 自动保存防抖（S10）；
  /// 滚轮和连点箭头时这种写入尤其密集。
  /// </remarks>
  /// <param name="value">控件当前值。</param>
  private void OnPortChanged(double value)
  {
    if (_applyingModel)
      return;

    _pendingPort = (int)value;
    _pendingPortElapsed = 0;
    _hasPendingPort = true;
    SetProcess(true);
  }

  /// <summary>推进端口提交去抖计时，窗口结束后写回一次。</summary>
  /// <param name="delta">距上一帧的秒数。</param>
  public override void _Process(double delta)
  {
    if (!_hasPendingPort)
    {
      // 兜底：没有待提交改动就不该再被每帧回调
      SetProcess(false);
      return;
    }

    _pendingPortElapsed += delta;
    if (_pendingPortElapsed >= PortCommitDebounceSeconds)
      CommitPendingPort();
  }

  /// <summary>把去抖窗口结束后的端口写入设置；值没变则既不写模型也不触发保存。</summary>
  private void CommitPendingPort()
  {
    if (!_hasPendingPort)
      return;

    _hasPendingPort = false;
    _pendingPortElapsed = 0;
    SetProcess(false);

    var port = Math.Clamp(_pendingPort, PortMinValue, PortMaxValue);
    if (_settings.WebSocketPort.Value == port)
      return;

    // 写模型即通知 MainWindow 重启 WebSocket（它绑定了 WebSocketPort）；一次提交只写一次
    _settings.WebSocketPort.Value = port;
    _dm?.TriggerAutoSave();
  }

  private void OnModeChanged(long index)
  {
    if (_applyingModel)
      return;

    _settings.WebSocketMode.Value = index == 1 ? "Client" : "Server";
    KoishiUrlRow.Visible = index == 1;
    _dm?.TriggerAutoSave();
  }

  private void OnFilterChanged(long index)
  {
    if (_applyingModel)
      return;

    _settings.MessageFilterMode.Value = index switch
    {
      1 => "ai",
      2 => "strict_then_ai",
      _ => "strict",
    };
    _dm?.TriggerAutoSave();
  }

  /// <summary>把「Koishi 地址」输入框的内容回写到设置（失焦或回车时调用）。</summary>
  /// <param name="text">输入框当前文本。</param>
  private void CommitKoishiUrl(string text)
  {
    // 回填期间切换 KoishiUrlRow.Visible 会让正聚焦的输入框发出 focus_exited，
    // 若在这里回写就会把「只是对齐这一页」变成一次自动保存，故与其余两处写回共用护栏
    if (_applyingModel)
      return;

    var url = text.Trim();
    var changed = !string.Equals(url, _settings.KoishiWebSocketUrl.Value, StringComparison.Ordinal);

    if (changed)
    {
      _settings.KoishiWebSocketUrl.Value = url;

      // 走 TriggerAutoSave：MainWindow 订阅了 KoishiWebSocketUrl，会据此重建 WebSocket 实例
      _dm?.TriggerAutoSave();
    }

    // 把规范化后的值回填输入框，保证「输入框看到的」与「设置里存的」一致
    if (!string.Equals(url, KoishiUrlInput.Text, StringComparison.Ordinal))
    {
      KoishiUrlInput.Text = url;
    }
  }

  /// <summary>文件对话框标题：说明该选 Koishi 的哪一层目录（应用根目录或它的 external）。</summary>
  private static string SelectDirectoryHint =>
    $"选择 Koishi 应用根目录（含 {KoishiPluginSpec.AppManifestFileName}）"
    + $"或它的 {KoishiPluginSpec.ExternalDirName} 目录";

  private void OnInstallPlugin()
  {
    PluginFileDialog.Title = SelectDirectoryHint;
    FileDialogSizing.Popup(PluginFileDialog);
  }

  /// <summary>
  /// 一键安装：把插件复制到 Koishi 工作区的短名落点目录。
  /// </summary>
  /// <remarks>
  /// 落点必须是 <c>&lt;应用根&gt;/external/&lt;包短名&gt;</c>：external 下的目录才是工作区成员，
  /// 且短名与工作区链接（node_modules/&lt;包名&gt;）指向的目录同名，覆盖安装才会生效。
  /// </remarks>
  /// <param name="dir">用户在文件对话框里选择的目录。</param>
  private void OnPluginDirSelected(string dir)
  {
    if (!KoishiPluginSpec.TryResolve(dir, out var plan, out var error))
    {
      PluginOkDialog.DialogText = error;
      PluginOkDialog.PopupCentered();
      return;
    }

    PluginInstaller.CopyPluginDir(KoishiPluginSpec.SourceDir, plan.InstallDir);

    // 只改数据模型：状态标签由 KoishiPluginPath 的同一条绑定回填
    _settings.KoishiPluginPath.Value = plan.InstallDir;
    _dm?.TriggerAutoSave();

    PluginOkDialog.DialogText = KoishiPluginSpec.DescribeInstalled(plan);
    PluginOkDialog.PopupCentered();
  }
}
