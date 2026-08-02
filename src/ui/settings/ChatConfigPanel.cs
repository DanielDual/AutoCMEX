namespace AutoCMEX.UI.Settings;

using System;
using AutoCMEX;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.Services;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 群聊配置面板 — 独立场景，管理 WebSocket 和消息筛选配置
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class ChatConfigPanel : VBoxContainer
{
  [Node("%PortInput")]
  public SpinBox PortInput { get; set; } = default!;

  [Node("%ModeSelect")]
  public OptionButton ModeSelect { get; set; } = default!;

  [Node("%KoishiUrlInput")]
  public LineEdit KoishiUrlInput { get; set; } = default!;

  [Node("%KoishiUrlRow")]
  public HBoxContainer KoishiUrlRow { get; set; } = default!;

  [Node("%FilterSelect")]
  public OptionButton FilterSelect { get; set; } = default!;

  [Node("%InstallBtn")]
  public Button InstallBtn { get; set; } = default!;

  [Node("%PluginFileDialog")]
  public FileDialog PluginFileDialog { get; set; } = default!;

  [Node("%PluginOkDialog")]
  public AcceptDialog PluginOkDialog { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  private DataManager? _dm;
  private AppSettings _settings = new();

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    PortInput.MinValue = 1;
    PortInput.MaxValue = 65535;
    PortInput.ValueChanged += OnPortChanged;

    ModeSelect.AddItem("Server（等待连接）");
    ModeSelect.AddItem("Client（主动连接）");
    ModeSelect.ItemSelected += OnModeChanged;

    FilterSelect.AddItem("仅严格格式匹配");
    FilterSelect.AddItem("仅 AI 智能匹配");
    FilterSelect.AddItem("先严格再 AI");
    FilterSelect.ItemSelected += OnFilterChanged;

    InstallBtn.Pressed += OnInstallPlugin;

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
  }

  private void OnPortChanged(double value)
  {
    _settings.WebSocketPort.Value = (int)value;
    _dm?.TriggerAutoSave();
  }

  private void OnModeChanged(long index)
  {
    _settings.WebSocketMode.Value = index == 1 ? "Client" : "Server";
    KoishiUrlRow.Visible = index == 1;
    _dm?.TriggerAutoSave();
  }

  private void OnFilterChanged(long index)
  {
    _settings.MessageFilterMode.Value = index switch
    {
      1 => "ai",
      2 => "strict_then_ai",
      _ => "strict",
    };
    _dm?.TriggerAutoSave();
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

    PluginOkDialog.DialogText = $"插件已安装到 {destDir}";
    PluginOkDialog.PopupCentered();
  }
}
