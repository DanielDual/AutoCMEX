namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// AI 服务单元测试
/// </summary>
public class AiServiceTest : TestClass
{
  public AiServiceTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void OpenAiService_CanBeConstructed()
  {
    var config = new AiModelConfig
    {
      Id = "test",
      ApiFormat = "OpenAI",
      EndpointUrl = "https://api.openai.com",
      ModelId = "gpt-4",
      EncryptedApiKey = "sk-test",
    };

    var service = new OpenAiService(config);
    service.ShouldNotBeNull();
  }

  [Test]
  public void AnthropicService_CanBeConstructed()
  {
    var config = new AiModelConfig
    {
      Id = "test",
      ApiFormat = "Anthropic",
      EndpointUrl = "https://api.anthropic.com",
      ModelId = "claude-3",
      EncryptedApiKey = "sk-test",
    };

    var service = new AnthropicService(config);
    service.ShouldNotBeNull();
  }

  [Test]
  public void AiFuzzifier_CanBeConstructed()
  {
    var config = new AiModelConfig
    {
      Id = "test",
      ApiFormat = "OpenAI",
      EndpointUrl = "https://api.openai.com",
      ModelId = "gpt-4",
      EncryptedApiKey = "sk-test",
    };

    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    try
    {
      var encryptor = new AesEncryptor(Path.Combine(tmpDir, "key.bin"));
      var dm = new DataManager(tmpDir, encryptor);
      dm.Settings.AiModels.Add(config);
      dm.Settings.ActiveAiModelId = "test";
      var factory = new AiServiceFactory(dm);

      var boss = new Boss
      {
        Name = "TestBoss",
        SpellCards = new List<SpellCard>
        {
          new() { Name = "Card1" },
          new() { Name = "Card2" },
        },
      };

      var fuzzifier = new AiFuzzifier(factory, new List<CreatorAlias>(), new List<Boss>(), boss);
      fuzzifier.ShouldNotBeNull();
    }
    finally
    {
      if (Directory.Exists(tmpDir))
        Directory.Delete(tmpDir, true);
    }
  }

  [Test]
  public void IAiService_Interface_IsImplementedByOpenAi()
  {
    var config = new AiModelConfig
    {
      Id = "test",
      EndpointUrl = "https://api.openai.com",
      ModelId = "gpt-4",
      EncryptedApiKey = "sk-test",
    };

    IAiService service = new OpenAiService(config);
    service.ShouldBeAssignableTo<IAiService>();
  }

  [Test]
  public void IAiService_Interface_IsImplementedByAnthropic()
  {
    var config = new AiModelConfig
    {
      Id = "test",
      ApiFormat = "Anthropic",
      EndpointUrl = "https://api.anthropic.com",
      ModelId = "claude-3",
      EncryptedApiKey = "sk-test",
    };

    IAiService service = new AnthropicService(config);
    service.ShouldBeAssignableTo<IAiService>();
  }

  [Test]
  public void AiServiceFactory_GetActiveService_ReturnsCorrectType()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    try
    {
      var encryptor = new AesEncryptor(Path.Combine(tmpDir, "key.bin"));
      var dm = new DataManager(tmpDir, encryptor);

      var openAiConfig = new AiModelConfig
      {
        Id = "openai-1",
        ApiFormat = "OpenAI",
        EndpointUrl = "https://api.openai.com",
        ModelId = "gpt-4",
        EncryptedApiKey = "sk-test",
      };
      dm.Settings.AiModels.Add(openAiConfig);
      dm.Settings.ActiveAiModelId = "openai-1";

      var factory = new AiServiceFactory(dm);
      var service = factory.GetActiveService();
      service.ShouldBeAssignableTo<IAiService>();
      service.ShouldBeOfType<OpenAiService>();
      (service as IDisposable)?.Dispose();
    }
    finally
    {
      if (Directory.Exists(tmpDir))
        Directory.Delete(tmpDir, true);
    }
  }

  [Test]
  public void AiServiceFactory_ThrowsWhenNoActiveModel()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    try
    {
      var encryptor = new AesEncryptor(Path.Combine(tmpDir, "key.bin"));
      var dm = new DataManager(tmpDir, encryptor);
      var factory = new AiServiceFactory(dm);

      Should.Throw<System.InvalidOperationException>(() => factory.GetActiveService());
    }
    finally
    {
      if (Directory.Exists(tmpDir))
        Directory.Delete(tmpDir, true);
    }
  }

  [Test]
  public void AiServiceFactory_ThrowsWhenActiveModelInvalid()
  {
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    try
    {
      var encryptor = new AesEncryptor(Path.Combine(tmpDir, "key.bin"));
      var dm = new DataManager(tmpDir, encryptor);

      var incompleteConfig = new AiModelConfig
      {
        Id = "bad-1",
        ApiFormat = "OpenAI",
        EndpointUrl = "",
        ModelId = "",
        EncryptedApiKey = "",
      };
      dm.Settings.AiModels.Add(incompleteConfig);
      dm.Settings.ActiveAiModelId = "bad-1";

      var factory = new AiServiceFactory(dm);
      Should.Throw<System.InvalidOperationException>(() => factory.GetActiveService());
    }
    finally
    {
      if (Directory.Exists(tmpDir))
        Directory.Delete(tmpDir, true);
    }
  }

  [Test]
  public void AiServiceFactory_IsModelValid_ReturnsCorrectly()
  {
    var valid = new AiModelConfig
    {
      EndpointUrl = "https://api.example.com",
      ModelId = "test-model",
      EncryptedApiKey = "key123",
    };
    AiServiceFactory.IsModelValid(valid).ShouldBeTrue();

    var missingUrl = new AiModelConfig
    {
      EndpointUrl = "",
      ModelId = "test-model",
      EncryptedApiKey = "key123",
    };
    AiServiceFactory.IsModelValid(missingUrl).ShouldBeFalse();

    var missingKey = new AiModelConfig
    {
      EndpointUrl = "https://api.example.com",
      ModelId = "test-model",
      EncryptedApiKey = "",
    };
    AiServiceFactory.IsModelValid(missingKey).ShouldBeFalse();
  }
}
