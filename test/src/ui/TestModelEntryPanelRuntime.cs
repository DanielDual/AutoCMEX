namespace AutoCMEX;

using System;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 复现测试：验证「运行时动态实例化 ModelEntryPanel.tscn 并加入场景树」后，
/// Chickensoft.AutoInject 是否会自动解析其 [Node] 子节点。
/// 对应问题：设置页 AI 模型分类下，下拉框能列出模型，但模型列表区域空白。
/// </summary>
public class TestModelEntryPanelRuntime : TestClass
{
  private Node _host = default!;

  public TestModelEntryPanelRuntime(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();
  }

  [Test]
  public void DynamicInstance_ResolvesNodeFields_AfterAddChild()
  {
    _host = new Node();
    TestScene.AddChild(_host);

    // 与 AiModelConfigPanel.RefreshModelList 相同的动态加载/实例化路径
    var entry = GD.Load<PackedScene>("res://src/ui/settings/ModelEntryPanel.tscn")
      .Instantiate<ModelEntryPanel>();
    (entry as IAutoInit).IsTesting = true;

    _host.AddChild(entry);

    // 若 AutoConnect 在进入场景树时正常解析 [Node]，则不应为 null
    entry.FormatOption.ShouldNotBeNull();
    entry.UrlInput.ShouldNotBeNull();
    entry.ModelIdInput.ShouldNotBeNull();
    entry.KeyInput.ShouldNotBeNull();
    entry.ToggleKeyBtn.ShouldNotBeNull();
    entry.TestBtn.ShouldNotBeNull();
    entry.DeleteBtn.ShouldNotBeNull();
  }

  [Test]
  public void DynamicInstance_Setup_PopulatesFields()
  {
    _host = new Node();
    TestScene.AddChild(_host);

    var entry = GD.Load<PackedScene>("res://src/ui/settings/ModelEntryPanel.tscn")
      .Instantiate<ModelEntryPanel>();
    (entry as IAutoInit).IsTesting = true;
    _host.AddChild(entry);

    var model = new AiModelConfig
    {
      ApiFormat = new Chickensoft.Sync.Primitives.AutoValue<string>("OpenAI"),
      EndpointUrl = new Chickensoft.Sync.Primitives.AutoValue<string>("https://api.example.com/v1"),
      ModelId = new Chickensoft.Sync.Primitives.AutoValue<string>("model-x"),
    };

    entry.Setup(model, dm: null);

    entry.UrlInput.Text.ShouldBe("https://api.example.com/v1");
    entry.ModelIdInput.Text.ShouldBe("model-x");
  }

  [Test]
  public void FullPanel_Refresh_AddsEntriesToRealModelList()
  {
    _host = new Node();
    TestScene.AddChild(_host);

    // 预置 3 个已配置模型
    var dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    dm.LoadAll();
    for (int i = 0; i < 3; i++)
    {
      dm.Settings.AiModels.Add(
        new AiModelConfig
        {
          Id = new Chickensoft.Sync.Primitives.AutoValue<string>($"id-{i}"),
          ModelId = new Chickensoft.Sync.Primitives.AutoValue<string>($"model-{i}"),
        }
      );
    }

    // 真实实例化整面板场景（与 MainWindow/SettingsPanel 嵌入方式一致）
    var panel = GD.Load<PackedScene>("res://src/ui/settings/AiModelConfigPanel.tscn")
      .Instantiate<AiModelConfigPanel>();
    (panel as IAutoInit).IsTesting = true;
    panel.FakeDependency<DataManager>(dm);

    // 只加入场景树，由真实 AutoInject 自动触发 ConnectNodes → OnResolved → Refresh
    // （不手动 _Notification，避免重复触发 OnReady/信号重复连接）
    _host.AddChild(panel);

    // AutoConnect 后 [Node] 应指向真实节点
    panel.ModelList.ShouldNotBeNull();

    // 区分 Refresh 哪一段失败：
    // 下拉含 1(未选择)+3 =4 说明 RefreshModelSelect 成功（Refresh 已执行）；
    // ModelList 子节点 =3 说明 RefreshModelList 也成功；若下拉正常但列表为 0，
    // 则根因收敛到 RefreshModelList 本身。
    panel.ActiveModelSelect.ItemCount.ShouldBe(4);
    panel.ModelList.GetChildren().Count.ShouldBe(3);
  }

  [Test]
  public async Task AutoListBinding_AddModel_AutoRebuildsList()
  {
    _host = new Node();
    TestScene.AddChild(_host);

    var dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    dm.LoadAll();
    for (int i = 0; i < 3; i++)
    {
      dm.Settings.AiModels.Add(
        new AiModelConfig
        {
          Id = new Chickensoft.Sync.Primitives.AutoValue<string>($"id-{i}"),
          ModelId = new Chickensoft.Sync.Primitives.AutoValue<string>($"model-{i}"),
        }
      );
    }

    var panel = GD.Load<PackedScene>("res://src/ui/settings/AiModelConfigPanel.tscn")
      .Instantiate<AiModelConfigPanel>();
    (panel as IAutoInit).IsTesting = true;
    panel.FakeDependency<DataManager>(dm);
    _host.AddChild(panel);

    panel.ModelList.GetChildren().Count.ShouldBe(3); // 初始渲染

    // 只写数据模型（不手动 Refresh），AutoList 绑定应驱动列表自动重建
    dm.Settings.AiModels.Add(
      new AiModelConfig
      {
        Id = new Chickensoft.Sync.Primitives.AutoValue<string>("id-new"),
        ModelId = new Chickensoft.Sync.Primitives.AutoValue<string>("model-new"),
      }
    );
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await TestScene.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);

    panel.ModelList.GetChildren().Count.ShouldBe(4);
  }
}
