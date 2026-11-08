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
/// <para>
/// 本栏是 <c>InfoPanel.tscn</c> 里的场景内实例：依赖由信息面板经 AutoInject 提供，发布改为抛
/// <see cref="PublishRequested"/> 事件，不再有 <c>Setup(...)</c> 手动注入。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class ActivityRulePanel : VBoxContainer, IActivityRulePanel
{
  [Node("%RuleEdit")]
  public ITextEdit RuleEdit { get; set; } = default!;

  [Node("%PublishButton")]
  public IButton PublishButton { get; set; } = default!;

  /// <summary>数据管理器（由信息面板经 AutoInject 提供，与发布编排同源）。</summary>
  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  /// <summary>本栏发布请求：由信息面板订阅后统一编排串行发布（本栏不再持有发布回调）。</summary>
  public event Action? PublishRequested;

  private InfoConfig? _config;
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

  /// <summary>AutoInject 节点注入完成：只挂信号，依赖待解析后再绑定数据。</summary>
  /// <remarks>
  /// 信号连接放这里而不是 <see cref="OnResolved"/>：<c>OnReady</c> 每个节点实例只执行一次，
  /// 而依赖重新解析（节点重新入树）会再次触发 <see cref="OnResolved"/>，重复连接信号会报
  /// "already connected"。
  /// </remarks>
  public void OnReady()
  {
    RuleEdit.TextChanged += OnRuleTextChanged;
    PublishButton.Pressed += OnPublishPressed;
    UpdatePublishButtonState();
  }

  /// <summary>AutoInject 依赖解析完成：回填规则文本并挂模型绑定。</summary>
  /// <remarks>
  /// 可重复进入（节点重新入树）：绑定先释放再重挂，避免同一路变化排队多次；
  /// 初值在挂绑定前落盘，回放时文本已相等故不会写成"用户输入"。
  /// </remarks>
  public void OnResolved()
  {
    _config = DataManager.InfoConfig;

    ApplyRuleText(_config.ActivityRule.Value);

    _ruleBinding?.Dispose();
    _ruleBinding = _config.ActivityRule.Bind().OnValue(OnRuleValueChanged);

    UpdatePublishButtonState();
  }

  /// <summary>把模型值回灌到输入框；回灌期间屏蔽"用户输入"分支。</summary>
  /// <param name="value">规则文本。</param>
  private void ApplyRuleText(string? value)
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
  }

  private void OnRuleTextChanged()
  {
    if (_config is null || _isApplyingModel)
      return;

    if (string.Equals(_config.ActivityRule.Value, RuleEdit.Text, StringComparison.Ordinal))
      return;

    _config.ActivityRule.Value = RuleEdit.Text;
    DataManager.TriggerAutoSave();
    UpdatePublishButtonState();
  }

  private void OnRuleValueChanged(string? value)
  {
    ApplyRuleText(value);
    UpdatePublishButtonState();
  }

  /// <summary>发布按钮：触发 <see cref="PublishRequested"/>，由信息面板统一编排发布。</summary>
  /// <remarks>
  /// 事件同步派发，因此这里的忙碌态只覆盖派发段；真正的串行发送、失败即停、重试与
  /// 「同一栏重复触发只发一次」的护栏由信息面板的发布编排负责（单栏与「一键转发」共用同一条链路）。
  /// </remarks>
  private void OnPublishPressed()
  {
    if (_isPublishing)
      return;

    _isPublishing = true;
    UpdatePublishButtonState();
    try
    {
      PublishRequested?.Invoke();
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
    if (PublishButton is null || RuleEdit is null)
      return;

    PublishButton.Disabled = _isPublishing || string.IsNullOrWhiteSpace(RuleEdit.Text);
  }
}
