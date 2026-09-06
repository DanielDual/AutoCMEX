namespace AutoCMEX.UI.Settings;

using System;
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

  public override void _Notification(int what)
  {
    this.Notify(what);
    if (what == (int)NotificationExitTree)
    {
      _aiModelsBinding?.Dispose();
      _aiModelsBinding = null;
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

  public void Refresh()
  {
    RefreshModelSelect();
    RefreshTimeout();
    RefreshModelList();
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

  private static async Task<bool> TestModelConnection(AiModelConfig model)
  {
    try
    {
      var service = AiServiceFactory.CreateService(model);
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
