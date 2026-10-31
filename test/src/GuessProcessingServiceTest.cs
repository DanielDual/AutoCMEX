namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Shouldly;

/// <summary>
/// 统一猜测处理服务测试
/// </summary>
public class GuessProcessingServiceTest : TestClass
{
  public GuessProcessingServiceTest(Node testScene)
    : base(testScene) { }

  [Test]
  public async Task ProcessAsync_StrictGuess_SucceedsWithoutAi()
  {
    var dataManager = CreateDataManager();
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = new AutoValue<string>("Card1"), Creator = new AutoValue<string>("Alice") },
        new() { Name = new AutoValue<string>("Card2"), Creator = new AutoValue<string>("Bob") },
      },
    };

    dataManager.Bosses.Add(boss);
    dataManager.Settings.SelectedBossIndex.Value = 0;
    dataManager.Settings.MessageFilterMode.Value = "strict";
    var service = new GuessProcessingService(
      dataManager,
      new FakeAiServiceFactory("unused"),
      new GuessResponseHandler(),
      new DroppedGuessRepository()
    );

    var result = await service.ProcessAsync("1Alice 2Bob");

    result.Status.ShouldBe(GuessProcessingStatus.Success);
    result.ReplyText.ShouldBe("✔️");
    result.ShouldReply.ShouldBeTrue();
  }

  [Test]
  public async Task ProcessAsync_AiReturnsNotGuess_ReturnsNotGuess()
  {
    var dataManager = CreateDataManager();
    dataManager.Bosses.Add(
      new Boss
      {
        Name = "TestBoss",
        SpellCards = new AutoList<SpellCard>
        {
          new() { Name = new AutoValue<string>("Card1"), Creator = new AutoValue<string>("Alice") },
        },
      }
    );
    dataManager.Settings.SelectedBossIndex.Value = 0;
    dataManager.Settings.MessageFilterMode.Value = "ai";
    dataManager.Settings.ActiveAiModelId.Value = "fake-model";

    var service = new GuessProcessingService(
      dataManager,
      new FakeAiServiceFactory(AiFuzzifier.NotAGuessToken),
      new GuessResponseHandler(),
      new DroppedGuessRepository()
    );

    var result = await service.ProcessAsync("这看起来不像猜测");

    result.Status.ShouldBe(GuessProcessingStatus.NotGuess);
    result.ShouldReply.ShouldBeFalse();
  }

  [Test]
  public async Task ProcessAsync_StrictFailureAiSuccess_ReturnsNormalizedSuccess()
  {
    var dataManager = CreateDataManager();
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
    dataManager.Settings.MessageFilterMode.Value = "strict_then_ai";
    dataManager.Settings.ActiveAiModelId.Value = "fake-model";

    var service = new GuessProcessingService(
      dataManager,
      new FakeAiServiceFactory("1Alice 2Bob"),
      new GuessResponseHandler(),
      new DroppedGuessRepository()
    );

    var result = await service.ProcessAsync("阿 乙");

    result.Status.ShouldBe(GuessProcessingStatus.Success);
    result.NormalizedGuess.ShouldBe("1Alice 2Bob");
    result.ReplyText.ShouldBe("✔️");
  }

  [Test]
  public async Task ProcessAsync_WithContext_KeepsTheReplyContextOnDroppedGuesses()
  {
    var dataManager = CreateDataManager();
    dataManager.Bosses.Add(CreateBoss("TestBoss"));
    dataManager.Settings.SelectedBossIndex.Value = 0;
    dataManager.Settings.MessageFilterMode.Value = "ai";
    dataManager.Settings.ActiveAiModelId.Value = "fake-model";
    var repository = new DroppedGuessRepository();
    var service = new GuessProcessingService(
      dataManager,
      new FakeAiServiceFactory("unused", new InvalidOperationException("ai down")),
      new GuessResponseHandler(),
      repository
    );

    var result = await service.ProcessAsync("1Alice 2Bob", "req-9", "group-9");

    // 落包时要连「回给谁、按什么口径重放」一起留档，否则重试无从回帖
    result.Status.ShouldBe(GuessProcessingStatus.NotGuess);
    repository.GetAll().Count.ShouldBe(1);
    var dropped = repository.GetAll()[0];
    dropped.RawText.ShouldBe("1Alice 2Bob");
    dropped.RequestId.ShouldBe("req-9");
    dropped.Sender.ShouldBe("group-9");
    dropped.FilterMode.ShouldBe("ai");
  }

  [Test]
  public async Task ProcessAsync_WithoutContext_LeavesTheReplyContextEmpty()
  {
    // 本地手输没有来源请求标识，行为与改动前一致
    var dataManager = CreateDataManager();
    dataManager.Bosses.Add(CreateBoss("TestBoss"));
    dataManager.Settings.SelectedBossIndex.Value = 0;
    dataManager.Settings.MessageFilterMode.Value = "ai";
    dataManager.Settings.ActiveAiModelId.Value = "fake-model";
    var repository = new DroppedGuessRepository();
    var service = new GuessProcessingService(
      dataManager,
      new FakeAiServiceFactory("unused", new InvalidOperationException("ai down")),
      new GuessResponseHandler(),
      repository
    );

    await service.ProcessAsync("1Alice 2Bob");

    repository.GetAll().Count.ShouldBe(1);
    repository.GetAll()[0].RequestId.ShouldBeEmpty();
    repository.GetAll()[0].Sender.ShouldBeEmpty();
  }

  [Test]
  public async Task RetryDroppedGuessAsync_FailingAgain_KeepsTheSameRecord()
  {
    var dataManager = CreateDataManager();
    dataManager.Bosses.Add(CreateBoss("TestBoss"));
    dataManager.Settings.SelectedBossIndex.Value = 0;
    dataManager.Settings.MessageFilterMode.Value = "ai";
    dataManager.Settings.ActiveAiModelId.Value = "fake-model";
    var repository = new DroppedGuessRepository();
    var original = new DroppedGuess("1Alice 2Bob", "ai down", "req-1", "group-1", "ai");
    repository.Add(original);
    var service = new GuessProcessingService(
      dataManager,
      new FakeAiServiceFactory("unused", new InvalidOperationException("ai still down")),
      new GuessResponseHandler(),
      repository
    );

    var result = await service.RetryDroppedGuessAsync(original.Id);

    // 重放失败必须原地保留这条记录：既不能删掉它，也不能换个新 Id 再落一条
    //（旧写法正是「删旧 + 落新」，用户看到的「丢包被重新解析了一遍」就是它）
    result.Status.ShouldBe(GuessProcessingStatus.NotGuess);
    repository.GetAll().Count.ShouldBe(1);
    repository.GetAll()[0].Id.ShouldBe(original.Id);
  }

  [Test]
  public async Task RetryDroppedGuessAsync_ReplaysWithTheRecordedFilterMode()
  {
    // 丢包时是 strict，之后设置切成 ai 且 AI 已不可用；重放必须按记录里的 strict 走，
    // 否则这条记录永远救不回来（按当前设置重放就会再吃一次 AI 异常）
    var dataManager = CreateDataManager();
    dataManager.Bosses.Add(CreateBoss("TestBoss"));
    dataManager.Settings.SelectedBossIndex.Value = 0;
    dataManager.Settings.MessageFilterMode.Value = "ai";
    dataManager.Settings.ActiveAiModelId.Value = "fake-model";
    var repository = new DroppedGuessRepository();
    var original = new DroppedGuess("1Alice 2Bob", "ai down", "req-2", "group-2", "strict");
    repository.Add(original);
    var service = new GuessProcessingService(
      dataManager,
      new FakeAiServiceFactory("unused", new InvalidOperationException("ai still down")),
      new GuessResponseHandler(),
      repository
    );

    var result = await service.RetryDroppedGuessAsync(original.Id);

    result.Status.ShouldBe(GuessProcessingStatus.Success);
    result.ReplyText.ShouldBe("✔️");
    // 重放不负责删记录：去留由 DroppedGuessRetryService 确认送达后再裁决
    repository.GetAll().Count.ShouldBe(1);
    repository.GetAll()[0].Id.ShouldBe(original.Id);
  }

  [Test]
  public async Task RetryDroppedGuessAsync_UnknownId_ReturnsError()
  {
    var service = new GuessProcessingService(
      CreateDataManager(),
      new FakeAiServiceFactory("unused"),
      new GuessResponseHandler(),
      new DroppedGuessRepository()
    );

    var result = await service.RetryDroppedGuessAsync("missing");

    result.Status.ShouldBe(GuessProcessingStatus.Error);
    service.FindDroppedGuess("missing").ShouldBeNull();
  }

  [Test]
  public void FindDroppedGuess_ReturnsTheRecordById()
  {
    var repository = new DroppedGuessRepository();
    var original = new DroppedGuess("1Alice 2Bob", "ai down", "req-3", "group-3", "ai");
    repository.Add(original);
    var service = new GuessProcessingService(
      CreateDataManager(),
      new FakeAiServiceFactory("unused"),
      new GuessResponseHandler(),
      repository
    );

    var found = service.FindDroppedGuess(original.Id);

    found.ShouldNotBeNull();
    found.RequestId.ShouldBe("req-3");
  }

  /// <summary>构造带两张符卡的 Boss，供严格管道判为有效猜测。</summary>
  private static Boss CreateBoss(string name) =>
    new()
    {
      Name = name,
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = new AutoValue<string>("Card1"), Creator = new AutoValue<string>("Alice") },
        new() { Name = new AutoValue<string>("Card2"), Creator = new AutoValue<string>("Bob") },
      },
    };

  private static DataManager CreateDataManager()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    return new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
  }
}
