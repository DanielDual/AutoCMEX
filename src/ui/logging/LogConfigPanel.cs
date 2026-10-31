namespace AutoCMEX.UI.Logging;

using System;
using AutoCMEX.Core.Logging;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 日志配置面板：独立管理日志文件数量限制和最低日志级别配置。
/// 从 <see cref="LogPanel"/> 中分离，遵循单一职责原则。
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class LogConfigPanel : VBoxContainer
{
  [Node("%MaxFileCountInput")]
  public ISpinBox MaxFileCountInput { get; set; } = default!;

  [Node("%MinLevelOption")]
  public IOptionButton MinLevelOption { get; set; } = default!;

  [Node("%ApplyConfigBtn")]
  public IButton ApplyConfigBtn { get; set; } = default!;

  [Node("%StatusLabel")]
  public IRichTextLabel StatusLabel { get; set; } = default!;

  [Dependency]
  public ILogService LogService => this.DependOn<ILogService>();

  private ILogService? _service;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    if (MaxFileCountInput != null)
    {
      MaxFileCountInput.MinValue = 1;
      MaxFileCountInput.MaxValue = 1000;
    }

    if (MinLevelOption != null)
    {
      MinLevelOption.Clear();
      MinLevelOption.AddItem("Info", 0);
      MinLevelOption.AddItem("Warn", 1);
      MinLevelOption.AddItem("Error", 2);
    }

    if (ApplyConfigBtn != null)
    {
      ApplyConfigBtn.Text = "Apply Config";
      ApplyConfigBtn.Pressed += OnApplyConfigPressed;
    }
  }

  public void OnResolved()
  {
    _service = LogService;
    if (_service == null)
      return;

    MaxFileCountInput.Value = _service.Config.MaxFileCount;
    MinLevelOption.Selected = (int)_service.Config.MinLevel;
  }

  private void OnApplyConfigPressed()
  {
    if (_service == null)
    {
      SetStatus("Log service not ready.");
      return;
    }

    _service.Config.MaxFileCount = (int)MaxFileCountInput.Value;
    var newMinLevel = (LogLevel)MinLevelOption.Selected;
    _service.Config.MinLevel = newMinLevel;

    if (_service.InMemoryWriter != null)
      _service.InMemoryWriter.MinLevel = newMinLevel;

    _service.RotateIfNeeded();
    SetStatus($"Applied: MaxFileCount={_service.Config.MaxFileCount}, MinLevel={newMinLevel}");
  }

  private void SetStatus(string message)
  {
    StatusLabel.Clear();
    StatusLabel.AppendText(message);
  }
}
