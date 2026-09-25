namespace AutoCMEX.UI.Settings;

using System;
using AutoCMEX;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.Services;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 群聊配置面板 — 独立场景，管理 WebSocket 和消息筛选配置
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

  private DataManager? _dm;
  private AppSettings _settings = new();

  /// <summary>正在把设置刷进控件：此期间控件的变更事件不代表用户操作，不得回写与保存。</summary>
  private bool _isRefreshing;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    PortInput.MinValue = 1;
    PortInput.MaxValue = 65535;
    PortInput.ValueChanged += OnPortChanged;

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

    // 每次切回本页都重新对齐设置（设置也可能被别处改动）
    VisibilityChanged += OnVisibilityChanged;

    // 配置预置对话框
    PluginFileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
    PluginFileDialog.Access = FileDialog.AccessEnum.Filesystem;
    PluginFileDialog.Title = "选择 Koishi plugins 目录";
    PluginFileDialog.DirSelected += OnPluginDirSelected;

    PluginOkDialog.Title = "安装完成";
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm != null)
    {
      _settings = _dm.Settings;
      Refresh();
    }
  }

  public void Refresh()
  {
    // 赋值会触发控件的 ValueChanged/ItemSelected，必须先立起护栏，否则刷新会反向写回设置并触发自动保存
    _isRefreshing = true;
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
      _isRefreshing = false;
    }
  }

  /// <summary>回显 Koishi 插件的安装状态（此前只写设置、界面从不显示，重启后看不出装没装过）。</summary>
  private void RefreshPluginPath()
  {
    var path = _settings.KoishiPluginPath.Value.Trim();
    PluginPathLabel.Text =
      path.Length == 0
        ? "插件状态: 未安装（点上方按钮选择 Koishi plugins 目录）"
        : $"插件状态: 已安装到 {path}";
  }

  private void OnVisibilityChanged()
  {
    if (Visible)
    {
      Refresh();
    }
  }

  private void OnPortChanged(double value)
  {
    if (_isRefreshing)
      return;

    _settings.WebSocketPort.Value = (int)value;
    _dm?.TriggerAutoSave();
  }

  private void OnModeChanged(long index)
  {
    if (_isRefreshing)
      return;

    _settings.WebSocketMode.Value = index == 1 ? "Client" : "Server";
    KoishiUrlRow.Visible = index == 1;
    _dm?.TriggerAutoSave();
  }

  private void OnFilterChanged(long index)
  {
    if (_isRefreshing)
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
    // 刷新期间切换 KoishiUrlRow.Visible 会让正聚焦的输入框发出 focus_exited，
    // 若在这里回写就会把「只是翻到这一页」变成一次自动保存，故与其余三处共用护栏
    if (_isRefreshing)
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

  private void OnInstallPlugin()
  {
    PluginFileDialog.PopupCentered();
  }

  private void OnPluginDirSelected(string dir)
  {
    var sourceDir = "res://src/plugin/koishi/";
    var destDir = System.IO.Path.Combine(dir, "auto-cmex");
    PluginInstaller.CopyPluginDir(sourceDir, destDir);
    _settings.KoishiPluginPath.Value = destDir;
    _dm?.TriggerAutoSave();

    RefreshPluginPath();

    PluginOkDialog.DialogText = $"插件已安装到 {destDir}";
    PluginOkDialog.PopupCentered();
  }
}
