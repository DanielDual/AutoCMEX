namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class TestAiModelConfigPanel : TestClass
{
  private AiModelConfigPanel _panel = default!;
  private DataManager _dm = default!;
  private Mock<IOptionButton> _activeModelSelect = default!;
  private readonly List<Node> _toCleanup = new();

  public TestAiModelConfigPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new AiModelConfigPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    _activeModelSelect = new Mock<IOptionButton>();
    _activeModelSelect.SetupProperty(m => m.ItemCount, 0);
    _activeModelSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback(() => _activeModelSelect.Object.ItemCount++);
    var timeoutInput = new Mock<ISpinBox>();
    timeoutInput.SetupProperty(m => m.MinValue);
    timeoutInput.SetupProperty(m => m.MaxValue);
    timeoutInput.SetupProperty(m => m.Value);
    var modelList = new Mock<IVBoxContainer>();
    modelList
      .Setup(m => m.GetChildren(It.IsAny<bool>()))
      .Returns(new Godot.Collections.Array<Node>());
    var addModelBtn = new Mock<IButton>();

    _panel.FakeNodeTree(
      new()
      {
        ["%ActiveModelSelect"] = _activeModelSelect.Object,
        ["%TimeoutInput"] = timeoutInput.Object,
        ["%ModelList"] = modelList.Object,
        ["%AddModelBtn"] = addModelBtn.Object,
      }
    );

    _panel.FakeDependency<DataManager>(_dm);
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }
    _toCleanup.Clear();
  }

  [Test]
  public void Panel_IsNotNull()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void ActiveModelSelect_IsNotNull()
  {
    _panel.ActiveModelSelect.ShouldNotBeNull();
  }

  [Test]
  public void TimeoutInput_HasCorrectRange()
  {
    _panel.TimeoutInput.MinValue.ShouldBe(1);
    _panel.TimeoutInput.MaxValue.ShouldBe(600);
  }

  [Test]
  public void AddModelBtn_IsNotNull()
  {
    _panel.AddModelBtn.ShouldNotBeNull();
  }

  [Test]
  public void ActiveModelSelect_HasDefaultOption()
  {
    _panel.ActiveModelSelect.ItemCount.ShouldBe(1);
    _activeModelSelect.Verify(m => m.AddItem("(未选择)", It.IsAny<int>()), Times.Once);
  }

  [Test]
  public void TimeoutInput_ReflectsSettings()
  {
    _panel.TimeoutInput.Value.ShouldBe(_dm.Settings.AiTimeoutSeconds.Value);
  }

  [Test]
  public void ActiveModelSelect_SelectingItem_UpdatesActiveAiModelId()
  {
    var model = new AiModelConfig
    {
      Id = new Chickensoft.Sync.Primitives.AutoValue<string>("mid-1"),
      ModelId = new Chickensoft.Sync.Primitives.AutoValue<string>("m1"),
    };
    // 通过同一 DataManager 引用追加模型；面板 _settings 与 dm.Settings 同引用
    _dm.Settings.AiModels.Add(model);

    // 触发下拉选择：占位索引 0，模型从索引 1 开始
    _activeModelSelect.Raise(m => m.ItemSelected += null, 1L);

    _dm.Settings.ActiveAiModelId.Value.ShouldBe("mid-1");
  }

  [Test]
  public void ActiveModelSelect_SelectingPlaceholder_ClearsActiveAiModelId()
  {
    // 选中占位「(未选择)」索引 0 → modelIndex = -1 → 清空激活模型
    _activeModelSelect.Raise(m => m.ItemSelected += null, 0L);

    _dm.Settings.ActiveAiModelId.Value.ShouldBeNull();
  }
}
