namespace AutoCMEX.UI.Logging;

using System;
using AutoCMEX.Core.Logging;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 日志配置面板：独立管理日志文件数量限制和最低日志级别配置。
/// 从 <see cref="LogPanel"/> 中分离，遵循单一职责原则。
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class LogConfigPanel : Control
{
  private ILogService? _service;

  private SpinBox? _maxFileCountInput;
  private OptionButton? _minLevelOption;
  private Button? _applyConfigBtn;
  private RichTextLabel? _statusLabel;

  /// <summary>
  /// 绑定到指定日志服务。
  /// </summary>
  public void BindToService(ILogService service)
  {
    ArgumentNullException.ThrowIfNull(service);
    _service = service;

    if (_maxFileCountInput != null)
      _maxFileCountInput.Value = service.Config.MaxFileCount;
    if (_minLevelOption != null)
      _minLevelOption.Selected = (int)service.Config.MinLevel;
  }

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    // 查找子节点（需要在 .tscn 中定义或使用程序化创建）
    _maxFileCountInput = GetNode<SpinBox>("MaxFileCountInput");
    _minLevelOption = GetNode<OptionButton>("MinLevelOption");
    _applyConfigBtn = GetNode<Button>("ApplyConfigBtn");
    _statusLabel = GetNode<RichTextLabel>("StatusLabel");

    if (_maxFileCountInput != null)
    {
      _maxFileCountInput.MinValue = 1;
      _maxFileCountInput.MaxValue = 1000;
    }

    if (_minLevelOption != null)
    {
      _minLevelOption.Clear();
      _minLevelOption.AddItem("Info", 0);
      _minLevelOption.AddItem("Warn", 1);
      _minLevelOption.AddItem("Error", 2);
    }

    if (_applyConfigBtn != null)
    {
      _applyConfigBtn.Text = "Apply Config";
      _applyConfigBtn.Pressed += OnApplyConfigPressed;
    }
  }

  private void OnApplyConfigPressed()
  {
    if (_service == null)
    {
      SetStatus("Log service not ready.");
      return;
    }

    _service.Config.MaxFileCount = (int)(_maxFileCountInput?.Value ?? 10);
    var newMinLevel = (LogLevel)(_minLevelOption?.Selected ?? 0);
    _service.Config.MinLevel = newMinLevel;

    if (_service.InMemoryWriter != null)
      _service.InMemoryWriter.MinLevel = newMinLevel;

    _service.RotateIfNeeded();
    SetStatus($"Applied: MaxFileCount={_service.Config.MaxFileCount}, MinLevel={newMinLevel}");
  }

  private void SetStatus(string message)
  {
    if (_statusLabel == null)
      return;
    _statusLabel.Clear();
    _statusLabel.AppendText(message);
  }
}
