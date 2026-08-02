namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class TestAiModelConfigPanel : TestClass
{
  private AiModelConfigPanel _panel = default!;
  private DataManager _dm = default!;
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

    var activeModelSelect = new OptionButton();
    _panel.AddChild(activeModelSelect);
    _panel.ActiveModelSelect = activeModelSelect;

    var timeoutInput = new SpinBox();
    _panel.AddChild(timeoutInput);
    _panel.TimeoutInput = timeoutInput;

    var modelList = new VBoxContainer();
    _panel.AddChild(modelList);
    _panel.ModelList = modelList;

    var addModelBtn = new Button();
    _panel.AddChild(addModelBtn);
    _panel.AddModelBtn = addModelBtn;

    _panel.FakeDependency<DataManager>(_dm);
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
    _panel.ActiveModelSelect.GetItemText(0).ShouldBe("(未选择)");
  }

  [Test]
  public void TimeoutInput_ReflectsSettings()
  {
    _panel.TimeoutInput.Value.ShouldBe(_dm.Settings.AiTimeoutSeconds.Value);
  }
}
