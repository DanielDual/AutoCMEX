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
public partial class AiModelConfigPanel : VBoxContainer
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

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    TimeoutInput.MinValue = 1;
    TimeoutInput.MaxValue = 600;
    TimeoutInput.ValueChanged += OnTimeoutChanged;
    AddModelBtn.Pressed += OnAddModel;
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
      entry.Setup(model, _dm);
      entry.SetTestCallback(() => TestModelConnection(model));
      entry.SetDeleteCallback(() =>
      {
        _settings.AiModels.Remove(model);
        _dm?.TriggerAutoSave();
        Refresh();
      });
      ModelList.AddChild(entry);
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
    _settings.AiModels.Add(newModel);
    _dm?.TriggerAutoSave();
    Refresh();
  }
}
