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
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// AI 模型配置面板 — 独立场景，管理 AI 模型的选择、编辑和测试
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class AiModelConfigPanel : VBoxContainer
{
  [Node("%ActiveModelSelect")]
  public OptionButton ActiveModelSelect { get; set; } = default!;

  [Node("%TimeoutInput")]
  public SpinBox TimeoutInput { get; set; } = default!;

  [Node("%ModelList")]
  public VBoxContainer ModelList { get; set; } = default!;

  [Node("%AddModelBtn")]
  public Button AddModelBtn { get; set; } = default!;

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
      var label = string.IsNullOrEmpty(model.ModelId)
        ? $"(未命名) ({model.ApiFormat})"
        : $"{model.ModelId} ({model.ApiFormat})";
      ActiveModelSelect.AddItem(label);
      var itemIdx = i + 1;

      if (!AiServiceFactory.IsModelValid(model))
        ActiveModelSelect.SetItemDisabled(itemIdx, true);

      if (model.Id == _settings.ActiveAiModelId)
        selectedIdx = itemIdx;
    }
    ActiveModelSelect.Select(selectedIdx);
  }

  private void RefreshTimeout()
  {
    TimeoutInput.Value = _settings.AiTimeoutSeconds;
  }

  private void RefreshModelList()
  {
    // Clear existing entries
    foreach (var child in ModelList.GetChildren())
      child.QueueFree();

    foreach (var model in _settings.AiModels)
    {
      var entry = CreateModelEntry(model);
      ModelList.AddChild(entry);
    }
  }

  private Control CreateModelEntry(AiModelConfig model)
  {
    var entry = new VBoxContainer();

    // API Format
    var formatRow = new HBoxContainer();
    var formatLabel = new Label { Text = "API 格式:", CustomMinimumSize = new Vector2(100, 0) };
    formatRow.AddChild(formatLabel);
    var formatOption = new OptionButton();
    formatOption.AddItem("OpenAI");
    formatOption.AddItem("Anthropic");
    formatOption.Select(model.ApiFormat == "Anthropic" ? 1 : 0);
    formatOption.ItemSelected += (idx) =>
    {
      model.ApiFormat = idx == 1 ? "Anthropic" : "OpenAI";
      _dm?.TriggerAutoSave();
    };
    formatRow.AddChild(formatOption);
    entry.AddChild(formatRow);

    // Endpoint URL
    var urlRow = new HBoxContainer();
    var urlLabel = new Label { Text = "请求地址:", CustomMinimumSize = new Vector2(100, 0) };
    urlRow.AddChild(urlLabel);
    var urlInput = new LineEdit
    {
      Text = model.EndpointUrl,
      SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill,
    };
    urlInput.TextChanged += (text) =>
    {
      model.EndpointUrl = text;
      _dm?.TriggerAutoSave();
    };
    urlRow.AddChild(urlInput);
    entry.AddChild(urlRow);

    // Model ID
    var modelIdRow = new HBoxContainer();
    var modelIdLabel = new Label { Text = "模型 ID:", CustomMinimumSize = new Vector2(100, 0) };
    modelIdRow.AddChild(modelIdLabel);
    var modelIdInput = new LineEdit
    {
      Text = model.ModelId,
      SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill,
    };
    modelIdInput.TextChanged += (text) =>
    {
      model.ModelId = text;
      _dm?.TriggerAutoSave();
    };
    modelIdRow.AddChild(modelIdInput);
    entry.AddChild(modelIdRow);

    // API Key
    var keyRow = new HBoxContainer();
    var keyLabel = new Label { Text = "API 密钥:", CustomMinimumSize = new Vector2(100, 0) };
    keyRow.AddChild(keyLabel);
    var keyInput = new LineEdit
    {
      Secret = true,
      PlaceholderText = "****",
      SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill,
    };
    keyInput.TextChanged += (text) =>
    {
      model.EncryptedApiKey = text;
      _dm?.TriggerAutoSave();
    };
    keyRow.AddChild(keyInput);
    var toggleBtn = new Button { Text = "显示" };
    toggleBtn.Toggled += (on) =>
    {
      keyInput.Secret = !on;
      toggleBtn.Text = on ? "隐藏" : "显示";
    };
    keyRow.AddChild(toggleBtn);
    entry.AddChild(keyRow);

    // Action buttons
    var actionRow = new HBoxContainer();
    var testBtn = new Button { Text = "测试连接" };
    testBtn.Pressed += async () =>
    {
      testBtn.Disabled = true;
      testBtn.Text = "测试中...";
      var success = await TestModelConnection(model);
      testBtn.Disabled = false;
      testBtn.Text = success ? "连接成功" : "连接失败";
      await ToSignal(GetTree().CreateTimer(2), "timeout");
      testBtn.Text = "测试连接";
    };
    actionRow.AddChild(testBtn);
    var deleteBtn = new Button { Text = "删除" };
    deleteBtn.Pressed += () =>
    {
      _settings.AiModels.Remove(model);
      _dm?.TriggerAutoSave();
      _dm?.NotifyDataChanged();
      Refresh();
    };
    actionRow.AddChild(deleteBtn);
    entry.AddChild(actionRow);
    entry.AddChild(new HSeparator());

    return entry;
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
    _settings.AiTimeoutSeconds = (int)value;
    _dm?.TriggerAutoSave();
  }

  private void OnAddModel()
  {
    var newModel = new AiModelConfig
    {
      Id = Guid.NewGuid().ToString("N")[..8],
      ApiFormat = "OpenAI",
    };
    _settings.AiModels.Add(newModel);
    _dm?.TriggerAutoSave();
    _dm?.NotifyDataChanged();
    Refresh();
  }
}
