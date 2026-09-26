namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 丢包重试协调器测试：重放 → 引用原消息回帖 → 决定记录去留
/// </summary>
public class DroppedGuessRetryServiceTest : TestClass
{
  public DroppedGuessRetryServiceTest(Node testScene)
    : base(testScene) { }

  [Test]
  public async Task RetryAsync_ReplaySucceeds_RepliesToTheOriginalMessageAndRemovesTheRecord()
  {
    var harness = CreateHarness();
    var dropped = harness.AddDropped("1Alice 2Bob", "strict", "req-1", "group-1");
    harness.ActivateLink();

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.Replied);
    outcome.Removed.ShouldBeTrue();
    harness.Repository.GetAll().ShouldBeEmpty();

    // 重试的终点是群聊：必须推一条与「消息到达」口径相同的 guess_result，并带上原请求标识
    harness.Broadcast.Count.ShouldBe(1);
    harness.Broadcast[0].Type.ShouldBe("event");
    var payload = harness.Broadcast[0].Payload;
    payload.GetProperty("event").GetString().ShouldBe("guess_result");
    payload.GetProperty("data").GetProperty("requestId").GetString().ShouldBe("req-1");
    payload.GetProperty("data").GetProperty("replyText").GetString().ShouldBe("✔️");
  }

  [Test]
  public async Task RetryAsync_SuccessWithoutRequestId_RemovesTheRecordWithoutBroadcasting()
  {
    // 本地手输产生的丢包没有来源可回：结果算出来即处理完毕，不该在列表里留成「删不掉的记录」
    var harness = CreateHarness();
    var dropped = harness.AddDropped("1Alice 2Bob", "strict");
    harness.ActivateLink();

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.NoReplyTarget);
    outcome.Removed.ShouldBeTrue();
    harness.Repository.GetAll().ShouldBeEmpty();
    harness.Broadcast.ShouldBeEmpty();
  }

  [Test]
  public async Task RetryAsync_NoLinkInstance_KeepsTheRecordAndReportsInactiveLink()
  {
    var harness = CreateHarness();
    var dropped = harness.AddDropped("1Alice 2Bob", "strict", "req-2", "group-2");

    // 从未 Create 过实例 → Current 为 null
    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.LinkInactive);
    outcome.Removed.ShouldBeFalse();
    harness.Repository.GetAll().Count.ShouldBe(1);
    harness.Broadcast.ShouldBeEmpty();
  }

  [Test]
  public async Task RetryAsync_LinkNotRunning_KeepsTheRecord()
  {
    // 结果算出来了但没人在听：不能据此删记录，否则用户白丢一条
    var harness = CreateHarness();
    var dropped = harness.AddDropped("1Alice 2Bob", "strict", "req-3", "group-3");
    harness.ActivateLink(running: false, connections: 0);

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.LinkInactive);
    outcome.Removed.ShouldBeFalse();
    harness.Repository.GetAll().Count.ShouldBe(1);
    harness.Broadcast.ShouldBeEmpty();
  }

  [Test]
  public async Task RetryAsync_BroadcastThrows_KeepsTheRecord()
  {
    var harness = CreateHarness();
    var dropped = harness.AddDropped("1Alice 2Bob", "strict", "req-4", "group-4");
    harness.ActivateLink();
    harness
      .Server.Setup(server => server.BroadcastAsync(It.IsAny<WebSocketMessage>()))
      .ThrowsAsync(new IOException("socket aborted"));

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.Failed);
    outcome.Removed.ShouldBeFalse();
    harness.Repository.GetAll().Count.ShouldBe(1);
  }

  [Test]
  public async Task RetryAsync_NothingDelivered_KeepsTheRecord()
  {
    // 链路像是活的，但一条都没送出去（刚好断开 / 对端不可达）：
    // BroadcastAsync 只记日志不抛异常，所以判据必须是它的送达数
    var harness = CreateHarness();
    var dropped = harness.AddDropped("1Alice 2Bob", "strict", "req-10", "group-10");
    harness.ActivateLink();
    harness
      .Server.Setup(server => server.BroadcastAsync(It.IsAny<WebSocketMessage>()))
      .ReturnsAsync(0);

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.LinkInactive);
    outcome.Message.ShouldContain("没能送达");
    outcome.Removed.ShouldBeFalse();
    harness.Repository.GetAll().Count.ShouldBe(1);
  }

  [Test]
  public async Task RetryAsync_SuccessWithoutReplyText_RemovesTheRecordAsNothingToSay()
  {
    // 重放成功但按回应策略没有回帖文案（有来源、事件却为空）：这件事已经处理完，
    // 记录不该永远卡在列表里等一次永远不会有的回帖
    var dropped = new DroppedGuess("1Alice 2Bob", "ai down", "req-11", "group-11", "strict");
    var processing = new Mock<IGuessProcessingService>();
    processing.Setup(s => s.FindDroppedGuess(dropped.Id)).Returns(dropped);
    processing
      .Setup(s => s.RetryDroppedGuessAsync(dropped.Id))
      .ReturnsAsync(GuessProcessingResult.Success("1Alice 2Bob", string.Empty, new List<string>()));
    var harness = CreateHarness(processingOverride: processing.Object);
    harness.ActivateLink();

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.NoReplyNeeded);
    outcome.Succeeded.ShouldBeTrue();
    outcome.Removed.ShouldBeTrue();
    processing.Verify(s => s.RemoveDroppedGuess(dropped.Id), Times.Once);
    harness.Broadcast.ShouldBeEmpty();
  }

  [Test]
  public async Task RetryAsync_AiStillUnavailable_KeepsTheSameRecord()
  {
    // AI 兜底再次抛异常时，服务层按「非猜测」返回：记录必须原地保留（Id 不变、不新增），
    // 用户稍后还能再试一次
    var harness = CreateHarness(
      mode: "ai",
      aiError: new InvalidOperationException("ai still down")
    );
    var dropped = harness.AddDropped("1Alice 2Bob", "ai", "req-5", "group-5");

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.NotGuess);
    outcome.Removed.ShouldBeFalse();
    harness.Repository.GetAll().Count.ShouldBe(1);
    harness.Repository.GetAll()[0].Id.ShouldBe(dropped.Id);
    harness.Broadcast.ShouldBeEmpty();
  }

  [Test]
  public async Task RetryAsync_StrictFormatMismatch_KeepsTheRecord()
  {
    var harness = CreateHarness();
    var dropped = harness.AddDropped("just chatting", "strict", "req-6", "group-6");

    var outcome = await harness.Service.RetryAsync(dropped.Id);

    outcome.Status.ShouldBe(DroppedRetryStatus.Failed);
    outcome.Message.ShouldContain("重放失败");
    outcome.Removed.ShouldBeFalse();
    harness.Repository.GetAll().Count.ShouldBe(1);
    harness.Broadcast.ShouldBeEmpty();
  }

  [Test]
  public async Task RetryAsync_UnknownId_ReportsFailureWithoutRemovingAnything()
  {
    var harness = CreateHarness();

    var outcome = await harness.Service.RetryAsync("missing");

    outcome.Status.ShouldBe(DroppedRetryStatus.Failed);
    outcome.Removed.ShouldBeFalse();
    outcome.Message.ShouldContain("不存在");
  }

  [Test]
  public async Task RetryAllAsync_ReportsOneOutcomePerRecordAndKeepsOnlyTheUnsent()
  {
    var harness = CreateHarness();
    var replayed = harness.AddDropped("1Alice 2Bob", "strict", "req-7", "group-7");
    var notGuess = harness.AddDropped("just chatting", "strict", "req-8", "group-8");
    harness.ActivateLink();

    var outcomes = await harness.Service.RetryAllAsync();

    outcomes.Count.ShouldBe(2);
    outcomes.Count(o => o.Status == DroppedRetryStatus.Replied).ShouldBe(1);
    outcomes.Count(o => o.Status == DroppedRetryStatus.Failed).ShouldBe(1);
    harness.Broadcast.Count.ShouldBe(1);

    // 只留下没送出去的那条
    harness.Repository.GetAll().Count.ShouldBe(1);
    harness.Repository.GetAll()[0].Id.ShouldBe(notGuess.Id);
    harness.Repository.FindById(replayed.Id).ShouldBeNull();
  }

  [Test]
  public async Task RetryAllAsync_WithoutDroppedGuesses_DoesNothing()
  {
    var harness = CreateHarness();
    harness.ActivateLink();

    var outcomes = await harness.Service.RetryAllAsync();

    outcomes.ShouldBeEmpty();
    harness.Broadcast.ShouldBeEmpty();
  }

  /// <summary>
  /// 用例夹具：真实的数据管理器 / 丢包仓储 / 猜测服务 + 假的 WebSocket 端点
  /// </summary>
  private sealed class Harness
  {
    public DataManager DataManager = default!;
    public DroppedGuessRepository Repository = default!;
    public Mock<IWebSocketServer> Server = default!;
    public WebSocketLifecycle Lifecycle = default!;
    public DroppedGuessRetryService Service = default!;

    /// <summary>已推送的出站消息（供断言用）。</summary>
    public List<WebSocketMessage> Broadcast { get; } = new();

    /// <summary>创建并记录当前实例，模拟链路在工作。</summary>
    /// <param name="running">实例是否运行中。</param>
    /// <param name="connections">已连接的对端数。</param>
    public void ActivateLink(bool running = true, int connections = 1)
    {
      Server.SetupGet(server => server.IsRunning).Returns(running);
      Server.SetupGet(server => server.ConnectionCount).Returns(connections);
      Lifecycle.Create(DataManager.Settings);
    }

    /// <summary>往丢包列表里塞一条记录。</summary>
    /// <param name="rawText">原始猜测文本。</param>
    /// <param name="filterMode">丢包当时的筛选模式。</param>
    /// <param name="requestId">来源请求标识；留空表示无源可回。</param>
    /// <param name="sender">来源发送者。</param>
    public DroppedGuess AddDropped(
      string rawText,
      string filterMode,
      string requestId = "",
      string sender = ""
    )
    {
      var dropped = new DroppedGuess(rawText, "ai down", requestId, sender, filterMode);
      Repository.Add(dropped);
      return dropped;
    }
  }

  /// <summary>
  /// 建夹具：默认用真实的猜测处理服务（走完整管道），需要制造「服务层给出某种结果」时用替身覆盖
  /// </summary>
  /// <param name="mode">消息筛选模式。</param>
  /// <param name="aiError">非空时 AI 调用抛该异常（制造丢包）。</param>
  /// <param name="processingOverride">猜测处理服务替身；为空则用真实实现。</param>
  private static Harness CreateHarness(
    string mode = "strict",
    Exception? aiError = null,
    IGuessProcessingService? processingOverride = null
  )
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);

    var dataManager = new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
    dataManager.Bosses.Add(
      new Boss
      {
        Name = "TestBoss",
        SpellCards = new AutoList<SpellCard>
        {
          new() { Name = new AutoValue<string>("Card1"), Creator = new AutoValue<string>("Alice") },
          new() { Name = new AutoValue<string>("Card2"), Creator = new AutoValue<string>("Bob") },
        },
      }
    );
    dataManager.Settings.SelectedBossIndex.Value = 0;
    dataManager.Settings.MessageFilterMode.Value = mode;
    dataManager.Settings.ActiveAiModelId.Value = "fake-model";

    var harness = new Harness
    {
      DataManager = dataManager,
      Repository = new DroppedGuessRepository(),
    };

    var processing =
      processingOverride
      ?? new GuessProcessingService(
        dataManager,
        new FakeAiServiceFactory("unused", aiError),
        new GuessResponseHandler(),
        harness.Repository
      );

    harness.Server = new Mock<IWebSocketServer>();
    harness
      .Server.Setup(server => server.BroadcastAsync(It.IsAny<WebSocketMessage>()))
      .Callback((WebSocketMessage message) => harness.Broadcast.Add(message))
      .ReturnsAsync(1);

    var log = new Mock<ILog>().Object;
    harness.Lifecycle = new WebSocketLifecycle(_ => harness.Server.Object, log);
    harness.Service = new DroppedGuessRetryService(processing, harness.Lifecycle, log);

    return harness;
  }
}
