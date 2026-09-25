namespace AutoCMEX.UI.Info;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 活动规则栏：纯文本的展示与编辑，并提供本栏发布入口。
/// </summary>
/// <remarks>
/// <para>
/// 规则文本落在 <see cref="InfoConfig.ActivityRule"/>，编辑后走 <see cref="DataManager"/>
/// 既有的 1500ms 防抖自动保存，不单独落盘。
/// </para>
/// <para>
/// <see cref="TextEdit.Text"/> 赋值同样会触发 <see cref="TextEdit.TextChanged"/>，因此用
/// <c>_isApplyingModel</c> 区分"用户输入"与"模型回灌"，避免回灌被当成用户输入而反复触发保存。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class ActivityRulePanel : VBoxContainer
{
  [Node("%RuleEdit")]
  public ITextEdit RuleEdit { get; set; } = default!;

  [Node("%PublishButton")]
  public IButton PublishButton { get; set; } = default!;

  private DataManager? _dataManager;
  private InfoConfig? _config;
  private ColumnPublishHandler? _publish;
  private AutoValue<string>.Binding? _ruleBinding;
  private bool _isApplyingModel;
  private bool _isPublishing;

  /// <inheritdoc/>
  public override void _Notification(int what) => this.Notify(what);

  /// <inheritdoc/>
  public override void _ExitTree()
  {
    _ruleBinding?.Dispose();
    _ruleBinding = null;
  }

  /// <summary>AutoInject 节点注入完成（依赖注入前，无需额外动作）。</summary>
  public void OnReady() { }

  /// <summary>AutoInject 依赖解析完成（本栏无依赖，无需额外动作）。</summary>
  public void OnResolved() { }

  /// <summary>
  /// 装配本栏；由信息面板在本节点加入场景树后调用。
  /// </summary>
  /// <param name="dataManager">数据管理器（规则文本的持久化入口）。</param>
  /// <param name="publish">本栏发布回调。</param>
  public void Setup(DataManager dataManager, ColumnPublishHandler publish)
  {
    _dataManager = dataManager;
    _config = dataManager.InfoConfig;
    _publish = publish;

    // 先落初值再挂绑定：初值赋值不应被当成用户输入
    RuleEdit.Text = _config.ActivityRule.Value ?? string.Empty;
    RuleEdit.TextChanged += OnRuleTextChanged;
    _ruleBinding = _config.ActivityRule.Bind().OnValue(OnRuleValueChanged);

    PublishButton.Pressed += OnPublishPressed;
    UpdatePublishButtonState();
  }

  private void OnRuleTextChanged()
  {
    if (_config is null || _isApplyingModel)
      return;

    if (string.Equals(_config.ActivityRule.Value, RuleEdit.Text, StringComparison.Ordinal))
      return;

    _config.ActivityRule.Value = RuleEdit.Text;
    _dataManager?.TriggerAutoSave();
    UpdatePublishButtonState();
  }

  private void OnRuleValueChanged(string? value)
  {
    var text = value ?? string.Empty;
    if (string.Equals(text, RuleEdit.Text, StringComparison.Ordinal))
      return;

    _isApplyingModel = true;
    try
    {
      RuleEdit.Text = text;
    }
    finally
    {
      _isApplyingModel = false;
    }

    UpdatePublishButtonState();
  }

  private void OnPublishPressed()
  {
    if (_publish is null || _isPublishing)
      return;

    // 规则文本是即时读模型发布的，发布期间只做忙碌态标记
    _ = PublishAsync();
  }

  private async System.Threading.Tasks.Task PublishAsync()
  {
    _isPublishing = true;
    PublishButton.Disabled = true;
    try
    {
      await _publish!();
    }
    finally
    {
      _isPublishing = false;
      if (IsInsideTree())
        UpdatePublishButtonState();
    }
  }

  private void UpdatePublishButtonState()
  {
    if (PublishButton is null)
      return;

    PublishButton.Disabled = _isPublishing || string.IsNullOrWhiteSpace(RuleEdit.Text);
  }
}
