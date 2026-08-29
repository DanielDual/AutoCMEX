namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class GuessingPanelTest : TestClass
{
  private GuessingPanel _panel = default!;
  private DataManager _dm = default!;
  private readonly List<Node> _toCleanup = new();

  public GuessingPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new GuessingPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    var guessInput = new Mock<ITextEdit>();
    var fuzzifyBtn = new Mock<IButton>();
    fuzzifyBtn.SetupProperty(m => m.Disabled);
    var processBtn = new Mock<IButton>();
    var responseDisplay = new Mock<IRichTextLabel>();
    var droppedList = new Mock<IItemList>();
    var retryDroppedBtn = new Mock<IButton>();
    retryDroppedBtn.SetupProperty(m => m.Disabled);
    var clearDroppedBtn = new Mock<IButton>();
    clearDroppedBtn.SetupProperty(m => m.Disabled);

    _panel.FakeNodeTree(
      new()
      {
        ["%GuessInput"] = guessInput.Object,
        ["%FuzzifyBtn"] = fuzzifyBtn.Object,
        ["%ProcessBtn"] = processBtn.Object,
        ["%ResponseDisplay"] = responseDisplay.Object,
        ["%DroppedList"] = droppedList.Object,
        ["%RetryDroppedBtn"] = retryDroppedBtn.Object,
        ["%ClearDroppedBtn"] = clearDroppedBtn.Object,
      }
    );

    _panel.FakeDependency<DataManager>(_dm);
    _panel.FakeDependency<AiServiceFactory>(new AiServiceFactory(_dm));
    _panel.FakeDependency<IGuessProcessingService>(
      new GuessProcessingService(
        _dm,
        new AiServiceFactory(_dm),
        new GuessResponseHandler(),
        new DroppedGuessRepository()
      )
    );

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
  public void GuessInput_IsNotNull()
  {
    _panel.GuessInput.ShouldNotBeNull();
  }

  [Test]
  public void FuzzifyBtn_IsDisabledByDefault()
  {
    _panel.FuzzifyBtn.Disabled.ShouldBeTrue();
  }

  [Test]
  public void ProcessBtn_IsNotNull()
  {
    _panel.ProcessBtn.ShouldNotBeNull();
  }

  [Test]
  public void ResponseDisplay_IsNotNull()
  {
    _panel.ResponseDisplay.ShouldNotBeNull();
  }

  [Test]
  public void DroppedList_IsNotNull()
  {
    _panel.DroppedList.ShouldNotBeNull();
  }

  [Test]
  public void RetryDroppedBtn_IsDisabledByDefault()
  {
    _panel.RetryDroppedBtn.Disabled.ShouldBeTrue();
  }

  [Test]
  public void ClearDroppedBtn_IsDisabledByDefault()
  {
    _panel.ClearDroppedBtn.Disabled.ShouldBeTrue();
  }
}
