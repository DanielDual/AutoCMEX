namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
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

    var guessInput = new TextEdit();
    _panel.AddChild(guessInput);
    _panel.GuessInput = guessInput;

    var fuzzifyBtn = new Button();
    _panel.AddChild(fuzzifyBtn);
    _panel.FuzzifyBtn = fuzzifyBtn;

    var processBtn = new Button();
    _panel.AddChild(processBtn);
    _panel.ProcessBtn = processBtn;

    var responseDisplay = new RichTextLabel();
    _panel.AddChild(responseDisplay);
    _panel.ResponseDisplay = responseDisplay;

    var droppedList = new ItemList();
    _panel.AddChild(droppedList);
    _panel.DroppedList = droppedList;

    var retryDroppedBtn = new Button();
    _panel.AddChild(retryDroppedBtn);
    _panel.RetryDroppedBtn = retryDroppedBtn;

    var clearDroppedBtn = new Button();
    _panel.AddChild(clearDroppedBtn);
    _panel.ClearDroppedBtn = clearDroppedBtn;

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
