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
/// <remarks>
/// <para>
/// 本面板由 <see cref="AiModelConfigPanel"/> 按模型逐条实例化，因此「展示哪个模型」是行级入参：
/// 用 <see cref="Model"/> 属性在入树前赋值，而不是构造注入或依赖注入。
/// </para>
/// <para>
/// 数据管理器是<b>环境依赖</b>（自动保存入口），由最近的祖先 provider 经 AutoInject 提供
/// （运行期由 <see cref="AiModelConfigPanel"/> 逐行提供，面板级测试用 <c>FakeDependency</c>）；
/// <see cref="Model"/> 非注入。
/// </para>
/// </remarks>
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

  /// <summary>数据管理器（自动保存入口，由祖先 provider 经 AutoInject 提供）。</summary>
  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  /// <summary>本行展示/编辑的模型；由创建方在加入场景树前赋值（per-row 入参，非注入）。</summary>
  public AiModelConfig Model { get; set; } = default!;

  public override void _Notification(int what) => this.Notify(what);

  /// <summary>AutoInject 节点注入完成：只挂信号与初始化下拉项，模型回填待依赖解析后再做。</summary>
  /// <remarks>
  /// 信号连接放这里而不是 <see cref="OnResolved"/>：<c>OnReady</c> 每个节点实例只执行一次，
  /// 而依赖重新解析（节点重新入树）会再次触发 <see cref="OnResolved"/>，重复连接信号会报
  /// "already connected"。
  /// </remarks>
  public void OnReady()
  {
    FormatOption.AddItem("OpenAI");
    FormatOption.AddItem("Anthropic");
    FormatOption.ItemSelected += OnFormatChanged;

    ToggleKeyBtn.Toggled += OnToggleKey;

    UrlInput.TextChanged += OnUrlTextChanged;
    ModelIdInput.TextChanged += OnModelIdTextChanged;
    KeyInput.TextChanged += OnApiKeyTextChanged;
  }

  /// <summary>AutoInject 依赖解析完成：把 <see cref="Model"/> 回填到控件。</summary>
  /// <remarks>
  /// 可重复进入（节点重新入树）；控件赋值（<c>LineEdit.Text</c> / <c>OptionButton.Select</c>）
  /// 不会回发变更信号，因此重复回填不会写成"用户输入"。
  /// </remarks>
  public void OnResolved()
  {
    if (Model is null)
      return;

    FormatOption.Select(Model.ApiFormat.Value == "Anthropic" ? 1 : 0);
    UrlInput.Text = Model.EndpointUrl.Value;
    ModelIdInput.Text = Model.ModelId.Value;
    KeyInput.Text = Model.EncryptedApiKey.Value;
  }

  /// <summary>
  /// 设置测试按钮回调
  /// </summary>
  /// <remarks>
  /// 「测试连接」是 <c>async void</c>，等待网络期间模型条目可能已被重建或整页被销毁
  /// （删/加模型会重建列表，关设置页会 QueueFree）：两个 <c>await</c> 之后都必须确认
  /// 节点还有效且仍在树内，不能对已释放节点赋值。
  /// </remarks>
  public void SetTestCallback(Func<Task<bool>> testFunc)
  {
    TestBtn.Pressed += async () =>
    {
      TestBtn.Disabled = true;
      TestBtn.Text = "测试中...";
      var success = await testFunc();

      if (!IsInsideTreeSafe())
        return;

      TestBtn.Disabled = false;
      TestBtn.Text = success ? "连接成功" : "连接失败";
      await ToSignal(GetTree().CreateTimer(2), "timeout");

      if (!IsInsideTreeSafe())
        return;

      TestBtn.Text = "测试连接";
    };
  }

  /// <summary>节点是否还有效且仍在场景树内（<c>async void</c> 续体里碰控件前的双检）。</summary>
  /// <returns>可以安全操作节点时为 <c>true</c>。</returns>
  private bool IsInsideTreeSafe() => GodotObject.IsInstanceValid(this) && IsInsideTree();

  /// <summary>
  /// 设置删除按钮回调
  /// </summary>
  public void SetDeleteCallback(Action deleteAction)
  {
    DeleteBtn.Pressed += deleteAction;
  }

  /// <summary>端点地址输入变化：写回模型并触发防抖自动保存。</summary>
  /// <param name="text">输入框当前文本。</param>
  private void OnUrlTextChanged(string text)
  {
    if (Model is null)
      return;

    Model.EndpointUrl.Value = text;
    DataManager.TriggerAutoSave();
  }

  /// <summary>模型 Id 输入变化：写回模型并触发防抖自动保存。</summary>
  /// <param name="text">输入框当前文本。</param>
  private void OnModelIdTextChanged(string text)
  {
    if (Model is null)
      return;

    Model.ModelId.Value = text;
    DataManager.TriggerAutoSave();
  }

  /// <summary>API Key 输入变化：写回模型（加密字段）并触发防抖自动保存。</summary>
  /// <param name="text">输入框当前文本。</param>
  private void OnApiKeyTextChanged(string text)
  {
    if (Model is null)
      return;

    Model.EncryptedApiKey.Value = text;
    DataManager.TriggerAutoSave();
  }

  private void OnFormatChanged(long index)
  {
    if (Model is null)
      return;

    Model.ApiFormat.Value = index == 1 ? "Anthropic" : "OpenAI";
    DataManager.TriggerAutoSave();
  }

  private void OnToggleKey(bool on)
  {
    KeyInput.Secret = !on;
    ToggleKeyBtn.Text = on ? "隐藏" : "显示";
  }
}
