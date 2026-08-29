namespace AutoCMEX.UI.Settings;

using System;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 模型条目面板 — 独立场景，展示和编辑单个 AI 模型配置
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class ModelEntryPanel : VBoxContainer
{
  [Node("%FormatOption")]
  public IOptionButton FormatOption { get; set; } = default!;

  [Node("%UrlInput")]
  public ILineEdit UrlInput { get; set; } = default!;

  [Node("%ModelIdInput")]
  public ILineEdit ModelIdInput { get; set; } = default!;

  [Node("%KeyInput")]
  public ILineEdit KeyInput { get; set; } = default!;

  [Node("%ToggleKeyBtn")]
  public IButton ToggleKeyBtn { get; set; } = default!;

  [Node("%TestBtn")]
  public IButton TestBtn { get; set; } = default!;

  [Node("%DeleteBtn")]
  public IButton DeleteBtn { get; set; } = default!;

  private AiModelConfig _model = default!;
  private DataManager? _dm;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    FormatOption.AddItem("OpenAI");
    FormatOption.AddItem("Anthropic");
    FormatOption.ItemSelected += OnFormatChanged;

    ToggleKeyBtn.Toggled += OnToggleKey;
  }

  public void OnResolved() { }

  /// <summary>
  /// 设置模型数据和 DataManager 引用
  /// </summary>
  public void Setup(AiModelConfig model, DataManager? dm)
  {
    _model = model;
    _dm = dm;

    FormatOption.Select(model.ApiFormat == "Anthropic" ? 1 : 0);
    UrlInput.Text = model.EndpointUrl;
    ModelIdInput.Text = model.ModelId;
    KeyInput.Text = model.EncryptedApiKey;

    UrlInput.TextChanged += (text) =>
    {
      _model.EndpointUrl = text;
      _dm?.TriggerAutoSave();
    };
    ModelIdInput.TextChanged += (text) =>
    {
      _model.ModelId = text;
      _dm?.TriggerAutoSave();
    };
    KeyInput.TextChanged += (text) =>
    {
      _model.EncryptedApiKey = text;
      _dm?.TriggerAutoSave();
    };
  }

  /// <summary>
  /// 设置测试按钮回调
  /// </summary>
  public void SetTestCallback(Func<Task<bool>> testFunc)
  {
    TestBtn.Pressed += async () =>
    {
      TestBtn.Disabled = true;
      TestBtn.Text = "测试中...";
      var success = await testFunc();
      TestBtn.Disabled = false;
      TestBtn.Text = success ? "连接成功" : "连接失败";
      await ToSignal(GetTree().CreateTimer(2), "timeout");
      TestBtn.Text = "测试连接";
    };
  }

  /// <summary>
  /// 设置删除按钮回调
  /// </summary>
  public void SetDeleteCallback(Action deleteAction)
  {
    DeleteBtn.Pressed += deleteAction;
  }

  private void OnFormatChanged(long index)
  {
    _model.ApiFormat = index == 1 ? "Anthropic" : "OpenAI";
    _dm?.TriggerAutoSave();
  }

  private void OnToggleKey(bool on)
  {
    KeyInput.Secret = !on;
    ToggleKeyBtn.Text = on ? "隐藏" : "显示";
  }
}
