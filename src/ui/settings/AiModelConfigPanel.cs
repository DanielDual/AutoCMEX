namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// AI 模型配置面板 — 独立场景，管理 AI 模型的选择、编辑和测试
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class AiModelConfigPanel : VBoxContainer, IAiModelConfigPanel
{
  [Node("%ActiveModelSelect")]
  public IOptionButton ActiveModelSelect { get; set; } = default!;

  [Node("%TimeoutInput")]
  public ISpinBox TimeoutInput { get; set; } = default!;

  [Node("%ModelList")]
  public IVBoxContainer ModelList { get; set; } = default!;

  [Node("%AddModelBtn")]
  public IButton AddModelBtn { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  private DataManager? _dm;
  private AppSettings _settings = new();
  private AutoList<AiModelConfig>.Binding? _aiModelsBinding;

  /// <summary>每个模型元素内部字段的绑定（模型 Id / 格式 / 端点 / 密钥）。</summary>
  /// <remarks>
  /// 列表级 <c>OnModify</c> 只在模型增删时触发，改元素字段列表本身没有任何变化；
  /// 只绑列表会让下拉项的可用态与标签停在上一次的状态（新模型填完 Key 仍是灰的）。
  /// 重挂范式对齐 <see cref="AutoCMEX.UI.Info.BossDataWatcher"/>。
  /// </remarks>
  private readonly List<IDisposable> _modelFieldBindings = new();

  /// <summary>是否正在重挂元素级绑定（Subscribe 时会立刻回放一次，抑制这类伪变更）。</summary>
  private bool _isRebindingModelFields;

  public override void _Notification(int what)
  {
    this.Notify(what);
    if (what == (int)NotificationExitTree)
    {
      _aiModelsBinding?.Dispose();
      _aiModelsBinding = null;

      DisposeModelFieldBindings();
    }
  }

  public void OnReady()
  {
    TimeoutInput.MinValue = 1;
    TimeoutInput.MaxValue = 600;
    TimeoutInput.ValueChanged += OnTimeoutChanged;
    AddModelBtn.Pressed += OnAddModel;
    ActiveModelSelect.ItemSelected += OnActiveModelSelected;
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm != null)
    {
      _settings = _dm.Settings;

      // UI 由 AutoList 绑定驱动：模型列表增删时自动重建刷新，
      // 事件处理器只写数据模型，不手动推 UI（符合重构核心原则）。
      _aiModelsBinding = _settings.AiModels.Bind().OnModify(() => CallDeferred(nameof(Refresh)));
      Refresh();
    }
  }

  /// <summary>
  /// 用户在下拉中选择模型：写入数据模型（激活模型 ID），由数据消费方读取。
  /// </summary>
  private void OnActiveModelSelected(long index)
  {
    // 下拉第 0 项为占位「(未选择)」，模型从下标 1 开始
    var modelIndex = (int)index - 1;
    if (_settings.AiModels.Count == 0 || modelIndex < 0 || modelIndex >= _settings.AiModels.Count)
    {
      _settings.ActiveAiModelId.Value = null;
      _dm?.TriggerAutoSave();
      return;
    }
    _settings.ActiveAiModelId.Value = _settings.AiModels[modelIndex].Id.Value;
    _dm?.TriggerAutoSave();
  }

  /// <summary>
  /// 全量刷新：先重挂模型元素级绑定（模型增删后旧绑定要释放），再重画下拉、超时与条目列表。
  /// </summary>
  public void Refresh()
  {
    RebindModelFieldBindings();

    RefreshModelSelect();
    RefreshTimeout();
    RefreshModelList();
  }

  /// <summary>
  /// 为每个模型的 <see cref="AiModelConfig.ModelId"/> / <c>ApiFormat</c> /
  /// <c>EndpointUrl</c> / <c>EncryptedApiKey</c> 挂值绑定，字段变化时只重画下拉。
  /// </summary>
  /// <remarks>
  /// 重挂期间抑制回调：<see cref="AutoValue{T}"/> 订阅会立刻用当前值回放一次，
  /// 不抑制的话每次模型增删都要按「模型数 × 4」排一串刷新（与 BossDataWatcher 同理）。
  /// 抑制窗口内的真实变化不会丢：<see cref="Refresh"/> 紧接着就会整体重画一次。
  /// </remarks>
  private void RebindModelFieldBindings()
  {
    DisposeModelFieldBindings();

    if (_dm is null)
      return;

    _isRebindingModelFields = true;
    try
    {
      foreach (var model in _settings.AiModels)
      {
        // 四个字段共同决定下拉项：ModelId/ApiFormat 是标签，EndpointUrl/Key 参与可用性判定
        _modelFieldBindings.Add(model.ModelId.Bind().OnValue(_ => OnModelFieldChanged()));
        _modelFieldBindings.Add(model.ApiFormat.Bind().OnValue(_ => OnModelFieldChanged()));
        _modelFieldBindings.Add(model.EndpointUrl.Bind().OnValue(_ => OnModelFieldChanged()));
        _modelFieldBindings.Add(model.EncryptedApiKey.Bind().OnValue(_ => OnModelFieldChanged()));
      }
    }
    finally
    {
      _isRebindingModelFields = false;
    }
  }

  /// <summary>释放全部模型元素级绑定（模型被删除或本面板退出时）。</summary>
  private void DisposeModelFieldBindings()
  {
    foreach (var binding in _modelFieldBindings)
      binding.Dispose();

    _modelFieldBindings.Clear();
  }

  /// <summary>模型字段变化：只重画下拉（不重建条目列表，避免正在输入的条目被销毁）。</summary>
  private void OnModelFieldChanged()
  {
    if (_isRebindingModelFields)
      return;

    // 写方可能在数据层线程（改模型、导入配置），UI 必须调度到主线程
    CallDeferred(nameof(RefreshModelSelect));
  }

  private void RefreshModelSelect()
  {
    ActiveModelSelect.Clear();
    ActiveModelSelect.AddItem("(未选择)");
    ActiveModelSelect.SetItemDisabled(0, true);

    int selectedIdx = 0;
    for (int i = 0; i < _settings.AiModels.Count; i++)
    {
      var model = _settings.AiModels[i];
      var label = string.IsNullOrEmpty(model.ModelId.Value)
        ? $"(未命名) ({model.ApiFormat.Value})"
        : $"{model.ModelId.Value} ({model.ApiFormat.Value})";
      ActiveModelSelect.AddItem(label);
      var itemIdx = i + 1;

      if (!AiServiceFactory.IsModelValid(model))
        ActiveModelSelect.SetItemDisabled(itemIdx, true);

      if (model.Id.Value == _settings.ActiveAiModelId.Value)
        selectedIdx = itemIdx;
    }
    ActiveModelSelect.Select(selectedIdx);
  }

  private void RefreshTimeout()
  {
    TimeoutInput.Value = _settings.AiTimeoutSeconds.Value;
  }

  private void RefreshModelList()
  {
    // Clear existing entries
    foreach (var child in ModelList.GetChildren())
      child.QueueFree();

    foreach (var model in _settings.AiModels)
    {
      var entry = GD.Load<PackedScene>("res://src/ui/settings/ModelEntryPanel.tscn")
        .Instantiate<ModelEntryPanel>();

      // 先加入节点树，触发 AutoConnect 连接其 [Node] 子节点，再执行 Setup，
      // 避免在 Setup 访问 FormatOption 等节点时它们尚未解析（此前抛 NullReferenceException）。
      ModelList.AddChild(entry);
      entry.Setup(model, _dm);
      entry.SetTestCallback(() => TestModelConnection(model));
      entry.SetDeleteCallback(() =>
      {
        // 只改数据模型；列表 UI 由 AutoList 绑定自动重建
        _settings.AiModels.Remove(model);
        _dm?.TriggerAutoSave();
      });
    }
  }

  /// <summary>
  /// 「测试连接」允许的最长等待（秒）。请求超时可设到 600 秒，但连接性验证只关心能否连上，
  /// 不该让用户为一次测试干等十分钟，因此这里给测试连接单独封顶。
  /// </summary>
  internal const int TestConnectionTimeoutCapSeconds = 30;

  /// <summary>
  /// 计算「测试连接」使用的超时：取请求超时设置值与上限中的较小值，且不小于 1 秒。
  /// </summary>
  internal static int ResolveTestConnectionTimeout(int configuredSeconds) =>
    Math.Clamp(configuredSeconds, 1, TestConnectionTimeoutCapSeconds);

  private async Task<bool> TestModelConnection(AiModelConfig model)
  {
    try
    {
      var service = AiServiceFactory.CreateService(
        model,
        ResolveTestConnectionTimeout(_settings.AiTimeoutSeconds.Value)
      );
      return await service.TestConnectionAsync();
    }
    catch
    {
      return false;
    }
  }

  private void OnTimeoutChanged(double value)
  {
    _settings.AiTimeoutSeconds.Value = (int)value;
    _dm?.TriggerAutoSave();
  }

  private void OnAddModel()
  {
    var newModel = new AiModelConfig
    {
      Id = new AutoValue<string>(Guid.NewGuid().ToString("N")[..8]),
      ApiFormat = new AutoValue<string>("OpenAI"),
    };
    // 只改数据模型；列表 UI 由 AutoList 绑定自动重建
    _settings.AiModels.Add(newModel);
    _dm?.TriggerAutoSave();
  }
}
