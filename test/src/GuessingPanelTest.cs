namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Moq;
using Shouldly;

public class GuessingPanelTest : TestClass
{
  private GuessingPanel _panel = default!;
  private DataManager _dm = default!;
  private DroppedGuessRepository _droppedRepo = default!;
  private Mock<IItemList> _droppedList = default!;
  private Mock<IButton> _retryDroppedBtn = default!;
  private Mock<IButton> _clearDroppedBtn = default!;
  private Mock<IButton> _fuzzifyBtn = default!;
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
    _fuzzifyBtn = new Mock<IButton>();
    _fuzzifyBtn.SetupProperty(m => m.Disabled);
    var processBtn = new Mock<IButton>();
    var responseDisplay = new Mock<IRichTextLabel>();
    _droppedList = new Mock<IItemList>();
    _retryDroppedBtn = new Mock<IButton>();
    _retryDroppedBtn.SetupProperty(m => m.Disabled);
    _clearDroppedBtn = new Mock<IButton>();
    _clearDroppedBtn.SetupProperty(m => m.Disabled);

    _panel.FakeNodeTree(
      new()
      {
        ["%GuessInput"] = guessInput.Object,
        ["%FuzzifyBtn"] = _fuzzifyBtn.Object,
        ["%ProcessBtn"] = processBtn.Object,
        ["%ResponseDisplay"] = responseDisplay.Object,
        ["%DroppedList"] = _droppedList.Object,
        ["%RetryDroppedBtn"] = _retryDroppedBtn.Object,
        ["%ClearDroppedBtn"] = _clearDroppedBtn.Object,
      }
    );

    _panel.FakeDependency<DataManager>(_dm);
    _panel.FakeDependency<AiServiceFactory>(new AiServiceFactory(_dm));
    _droppedRepo = new DroppedGuessRepository();
    _panel.FakeDependency<IGuessProcessingService>(
      new GuessProcessingService(
        _dm,
        new AiServiceFactory(_dm),
        new GuessResponseHandler(),
        _droppedRepo
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

  [Test]
  public async Task DroppedList_UpdatesWhenDroppedGuessesChange()
  {
    _droppedRepo.Add(new DroppedGuess("text", "error"));
    await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    _droppedList.Verify(
      m => m.AddItem(It.IsAny<string>(), It.IsAny<Texture2D>(), It.IsAny<bool>()),
      Times.Once
    );
    _retryDroppedBtn.Object.Disabled.ShouldBeFalse();
    _clearDroppedBtn.Object.Disabled.ShouldBeFalse();
  }

  [Test]
  public async Task ClearDropped_RemovesAllAndRefreshes()
  {
    _droppedRepo.Add(new DroppedGuess("text1", "error1"));
    _droppedRepo.Add(new DroppedGuess("text2", "error2"));
    await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    _panel.GetOnClearDropped()();
    await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    _droppedRepo.GetAll().Count.ShouldBe(0);
    _droppedList.Verify(m => m.Clear(), Times.AtLeastOnce);
    _retryDroppedBtn.Object.Disabled.ShouldBeTrue();
    _clearDroppedBtn.Object.Disabled.ShouldBeTrue();
  }

  [Test]
  public void FuzzifyBtn_EnablesWhenActiveModelBecomesValid()
  {
    _panel.FuzzifyBtn.Disabled.ShouldBeTrue();

    var model = new AiModelConfig
    {
      Id = new AutoValue<string>("m1"),
      EndpointUrl = new AutoValue<string>("https://api.example.com"),
      ModelId = new AutoValue<string>("gpt-4"),
      EncryptedApiKey = new AutoValue<string>("key"),
    };
    _dm.Settings.AiModels.Add(model);
    _dm.Settings.ActiveAiModelId.Value = "m1";

    _panel.FuzzifyBtn.Disabled.ShouldBeFalse();
  }

  [Test]
  public void FuzzifyBtn_DisablesWhenActiveModelRemoved()
  {
    var model = new AiModelConfig
    {
      Id = new AutoValue<string>("m1"),
      EndpointUrl = new AutoValue<string>("https://api.example.com"),
      ModelId = new AutoValue<string>("gpt-4"),
      EncryptedApiKey = new AutoValue<string>("key"),
    };
    _dm.Settings.AiModels.Add(model);
    _dm.Settings.ActiveAiModelId.Value = "m1";
    _panel.FuzzifyBtn.Disabled.ShouldBeFalse();

    _dm.Settings.AiModels.Remove(model);

    _panel.FuzzifyBtn.Disabled.ShouldBeTrue();
  }

  [Test]
  public void ExitTree_StopsBindingUpdates()
  {
    _panel._ExitTree();

    _droppedRepo.Add(new DroppedGuess("text", "error"));

    _droppedList.Verify(
      m => m.AddItem(It.IsAny<string>(), It.IsAny<Texture2D>(), It.IsAny<bool>()),
      Times.Never
    );
  }
}
