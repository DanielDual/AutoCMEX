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

  private static DataManager CreateDataManager()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    return new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
  }

  private sealed class FakeAiServiceFactory : AiServiceFactory
  {
    private readonly string _response;

    public FakeAiServiceFactory(string response)
      : base(
        new DataManager(
          Path.GetTempPath(),
          new AesEncryptor(AesEncryptor.GetDefaultKeyPath(Path.GetTempPath()))
        )
      )
    {
      _response = response;
    }

    public override IAiService GetActiveService() => new FakeAiService(_response);

    public override AiModelConfig GetActiveModelConfig() =>
      new()
      {
        Id = "fake-model",
        EndpointUrl = "https://example.com",
        ModelId = "fake-model",
        EncryptedApiKey = "fake-key",
      };
  }

  private sealed class FakeAiService : IAiService
  {
    private readonly string _response;

    public FakeAiService(string response)
    {
      _response = response;
    }

    public Task<string> ChatAsync(string systemPrompt, string userMessage) =>
      Task.FromResult(_response);

    public Task<bool> TestConnectionAsync() => Task.FromResult(true);
  }
}
