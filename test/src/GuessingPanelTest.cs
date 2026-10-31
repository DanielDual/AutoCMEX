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
  private Mock<IButton> _removeDroppedBtn = default!;
  private Mock<IButton> _fuzzifyBtn = default!;
  private Mock<IRichTextLabel> _responseDisplay = default!;
  private Mock<IDroppedGuessRetryService> _retryService = default!;
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
    _responseDisplay = new Mock<IRichTextLabel>();
    _responseDisplay.SetupProperty(m => m.Text);
    _droppedList = new Mock<IItemList>();
    _retryDroppedBtn = new Mock<IButton>();
    _retryDroppedBtn.SetupProperty(m => m.Disabled);
    _retryDroppedBtn.SetupProperty(m => m.Text);
    _clearDroppedBtn = new Mock<IButton>();
    _clearDroppedBtn.SetupProperty(m => m.Disabled);
    _removeDroppedBtn = new Mock<IButton>();
    _removeDroppedBtn.SetupProperty(m => m.Disabled);

    _panel.FakeNodeTree(
      new()
      {
        ["%GuessInput"] = guessInput.Object,
        ["%FuzzifyBtn"] = _fuzzifyBtn.Object,
        ["%ProcessBtn"] = processBtn.Object,
        ["%ResponseDisplay"] = _responseDisplay.Object,
        ["%DroppedList"] = _droppedList.Object,
        ["%RetryDroppedBtn"] = _retryDroppedBtn.Object,
        ["%ClearDroppedBtn"] = _clearDroppedBtn.Object,
        ["%RemoveDroppedBtn"] = _removeDroppedBtn.Object,
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

    // 重试链路的终点在协调器里（它负责回帖），面板只负责发起与展示结局
    _retryService = new Mock<IDroppedGuessRetryService>();
    _panel.FakeDependency<IDroppedGuessRetryService>(_retryService.Object);

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
  public void RemoveDroppedBtn_IsDisabledByDefault()
  {
    _panel.RemoveDroppedBtn.Disabled.ShouldBeTrue();
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
    _removeDroppedBtn.Object.Disabled.ShouldBeFalse();
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
    _removeDroppedBtn.Object.Disabled.ShouldBeTrue();
  }

  [Test]
  public async Task RemoveSelectedDropped_WithoutSelection_ShowsHintAndKeepsRecords()
  {
    _droppedRepo.Add(new DroppedGuess("text1", "error1"));
    _droppedRepo.Add(new DroppedGuess("text2", "error2"));
    await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    _panel.GetOnRemoveSelectedDropped()();

    _droppedRepo.GetAll().Count.ShouldBe(2);
    _responseDisplay.Object.Text.ShouldContain("选中");
  }

  [Test]
  public async Task RemoveSelectedDropped_RemovesOnlyTheSelectedRecord()
  {
    var first = new DroppedGuess("text1", "error1");
    var second = new DroppedGuess("text2", "error2");
    _droppedRepo.Add(first);
    _droppedRepo.Add(second);
    await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    // 选中下标 1 = 列表里第二条记录
    _droppedList.Setup(m => m.GetSelectedItems()).Returns(new[] { 1 });
    _panel.GetOnRemoveSelectedDropped()();

    _droppedRepo.GetAll().Count.ShouldBe(1);
    _droppedRepo.GetAll()[0].Id.ShouldBe(first.Id);
    _responseDisplay.Object.Text.ShouldContain("已删除 1 条");
  }

  [Test]
  public async Task RetryAllDropped_RoutesThroughTheCoordinatorAndShowsEveryOutcome()
  {
    _droppedRepo.Add(new DroppedGuess("1Alice 2Bob", "ai down", "req-1", "group-1", "strict"));
    await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    _retryService
      .Setup(s => s.RetryAllAsync())
      .ReturnsAsync(
        new List<DroppedRetryOutcome>
        {
          new(
            "abc12345",
            "1Alice 2Bob",
            DroppedRetryStatus.Replied,
            "已引用原消息回帖，记录已移除。",
            true
          ),
          new(
            "def67890",
            "1Alice 2Bob",
            DroppedRetryStatus.LinkInactive,
            "链路未运行或没有已连接的对端，结果未送达，记录保留。",
            false
          ),
        }
      );

    _panel.GetOnRetryAllDropped()();
    for (var i = 0; i < 5; i++)
      await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    // 面板不再自己调服务重放，而是把重放与回帖整个交给协调器，并把逐条结局显示出来
    _retryService.Verify(s => s.RetryAllAsync(), Times.Once);
    _responseDisplay.Object.Text.ShouldContain("丢包重试结果");
    _responseDisplay.Object.Text.ShouldContain("已引用原消息回帖");
    _responseDisplay.Object.Text.ShouldContain("结果未送达");
    _retryDroppedBtn.Object.Text.ShouldBe("重试全部丢包");
    _retryDroppedBtn.Object.Disabled.ShouldBeFalse();
  }

  [Test]
  public async Task RetryAllDropped_WithoutDroppedGuesses_DoesNotCallTheCoordinator()
  {
    _panel.GetOnRetryAllDropped()();
    for (var i = 0; i < 2; i++)
      await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    _retryService.Verify(s => s.RetryAllAsync(), Times.Never);
  }

  [Test]
  public async Task RetryAllDropped_CoordinatorThrows_ReportsItWithoutCrashingThePanel()
  {
    // async void 里的异常没人接：协调器抛出来时必须自己兜住、写进回应栏，并把按钮状态还原
    _droppedRepo.Add(new DroppedGuess("text1", "error1"));
    await _panel.ToSignal(TestScene.GetTree(), "process_frame");
    _retryService
      .Setup(s => s.RetryAllAsync())
      .ThrowsAsync(new InvalidOperationException("coordinator exploded"));

    _panel.GetOnRetryAllDropped()();
    for (var i = 0; i < 5; i++)
      await _panel.ToSignal(TestScene.GetTree(), "process_frame");

    _responseDisplay.Object.Text.ShouldContain("重试中断");
    _responseDisplay.Object.Text.ShouldContain("coordinator exploded");
    _retryDroppedBtn.Object.Text.ShouldBe("重试全部丢包");
    _retryDroppedBtn.Object.Disabled.ShouldBeFalse();
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
