namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
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
      Id = new AutoValue<string>("test"),
      ApiFormat = new AutoValue<string>("OpenAI"),
      EndpointUrl = new AutoValue<string>("https://api.openai.com"),
      ModelId = new AutoValue<string>("gpt-4"),
      EncryptedApiKey = new AutoValue<string>("sk-test"),
    };

    var service = new OpenAiService(config);
    service.ShouldNotBeNull();
  }

  [Test]
  public void AnthropicService_CanBeConstructed()
  {
    var config = new AiModelConfig
    {
      Id = new AutoValue<string>("test"),
      ApiFormat = new AutoValue<string>("Anthropic"),
      EndpointUrl = new AutoValue<string>("https://api.anthropic.com"),
      ModelId = new AutoValue<string>("claude-3"),
      EncryptedApiKey = new AutoValue<string>("sk-test"),
    };

    var service = new AnthropicService(config);
    service.ShouldNotBeNull();
  }

  [Test]
  public void AiServiceFactory_CreateService_AppliesRequestTimeout()
  {
    ReadRequestTimeout(AiServiceFactory.CreateService(MakeConfig("OpenAI"), 42))
      .ShouldBe(TimeSpan.FromSeconds(42));
    ReadRequestTimeout(AiServiceFactory.CreateService(MakeConfig("Anthropic"), 42))
      .ShouldBe(TimeSpan.FromSeconds(42));
    // 不显式给超时时沿用默认值，防止默认值被无意改掉
    ReadRequestTimeout(AiServiceFactory.CreateService(MakeConfig("OpenAI")))
      .ShouldBe(TimeSpan.FromSeconds(100));
  }

  [Test]
  public void AiServiceFactory_GetActiveService_UsesConfiguredRequestTimeout()
  {
    // 「请求超时(秒)」此前根本没接上：设置成多少都一样，服务始终是默认的 100 秒
    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    try
    {
      var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir));
      var dm = new DataManager(tmpDir, encryptor);
      dm.Settings.AiModels.Add(MakeConfig("OpenAI"));
      dm.Settings.ActiveAiModelId.Value = "test";
      dm.Settings.AiTimeoutSeconds.Value = 7;

      var factory = new AiServiceFactory(dm);

      ReadRequestTimeout(factory.GetActiveService()).ShouldBe(TimeSpan.FromSeconds(7));
    }
    finally
    {
      if (Directory.Exists(tmpDir))
        Directory.Delete(tmpDir, true);
    }
  }

  /// <summary>构造字段齐全的模型配置，仅供工厂产出服务实例，不发起请求。</summary>
  private static AiModelConfig MakeConfig(string apiFormat) =>
    new()
    {
      Id = new AutoValue<string>("test"),
      ApiFormat = new AutoValue<string>(apiFormat),
      EndpointUrl = new AutoValue<string>("https://example.test"),
      ModelId = new AutoValue<string>("some-model"),
      EncryptedApiKey = new AutoValue<string>("sk-test"),
    };

  /// <summary>请求超时只在构造时写进 HttpClient、没有公开读取口，因此反射取私有字段。</summary>
  private static TimeSpan ReadRequestTimeout(IAiService service)
  {
    var field = service
      .GetType()
      .GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
    field.ShouldNotBeNull();
    // Godot 也有同名类型 HttpClient，这里必须写全名
    var httpClient = (System.Net.Http.HttpClient)field!.GetValue(service)!;
    return httpClient.Timeout;
  }

  [Test]
  public void AiFuzzifier_CanBeConstructed()
  {
    var config = new AiModelConfig
    {
      Id = new AutoValue<string>("test"),
      ApiFormat = new AutoValue<string>("OpenAI"),
      EndpointUrl = new AutoValue<string>("https://api.openai.com"),
      ModelId = new AutoValue<string>("gpt-4"),
      EncryptedApiKey = new AutoValue<string>("sk-test"),
    };

    var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{System.Guid.NewGuid():N}");
    Directory.CreateDirectory(tmpDir);
    try
    {
      var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir));
      var dm = new DataManager(tmpDir, encryptor);
      dm.Settings.AiModels.Add(config);
      dm.Settings.ActiveAiModelId.Value = "test";
      var factory = new AiServiceFactory(dm);

      var boss = new Boss
      {
        Name = "TestBoss",
        SpellCards = new AutoList<SpellCard>
        {
          new() { Name = new AutoValue<string>("Card1") },
          new() { Name = new AutoValue<string>("Card2") },
        },
      };

      var fuzzifier = new AiFuzzifier(factory, new List<CreatorAlias>(), boss);
      fuzzifier.ShouldNotBeNull();
    }
    finally
    {
      if (Directory.Exists(tmpDir))
        Directory.Delete(tmpDir, true);
    }
  }

  [Test]
  public void AiFuzzifier_Prompt_TeachesAliasConversionAndMarksCreatorsWithoutAliases()
  {
    var aliases = new List<CreatorAlias>
    {
      new() { MainName = "Alice" },
      new() { MainName = "Bob" },
    };
    aliases[0].Aliases.Add("Ally");

    var prompt = BuildPrompt(aliases);

    // 别名转换必须有可直接照做的例子（例子只允许出现占位符名）
    prompt.ShouldContain("输入：1Ally 2Alice 3Bob");
    prompt.ShouldContain("输出：1Alice 2Alice 3Bob");
    // 无别名的创作者行写「无别名」，不能是空括号（空括号既像字段缺失，又像空字符串别名）
    prompt.ShouldContain("- Bob（无别名）");
    prompt.ShouldNotContain("（别名：）");
    // 「表外名字转最匹配主名」是有意保留的规则，不能被顺手删掉
    prompt.ShouldContain("否则，请按照将其转换为最匹配的主名。");
  }

  [Test]
  public void AiFuzzifier_Prompt_OmitsAliasExampleWhenTheTableIsEmpty()
  {
    var prompt = BuildPrompt(new List<CreatorAlias>());

    prompt.ShouldNotContain("1Ally");
    // 表头（不是规则句里那处提及）不该出现
    prompt.ShouldNotContain("创作者别名表（请将别名转换为主名）：");
  }

  /// <summary>取 AiFuzzifier 的系统提示词（生成为私有方法，用反射取，避免为测试开生产接缝）。</summary>
  private static string BuildPrompt(IReadOnlyList<CreatorAlias> aliases)
  {
    var fuzzifier = new AiFuzzifier(null!, aliases, null!);
    var method = typeof(AiFuzzifier).GetMethod(
      "BuildSystemPrompt",
      BindingFlags.NonPublic | BindingFlags.Instance
    )!;
    return (string)method.Invoke(fuzzifier, null)!;
  }

  [Test]
  public void IAiService_Interface_IsImplementedByOpenAi()
  {
    var config = new AiModelConfig
    {
      Id = new AutoValue<string>("test"),
      EndpointUrl = new AutoValue<string>("https://api.openai.com"),
      ModelId = new AutoValue<string>("gpt-4"),
      EncryptedApiKey = new AutoValue<string>("sk-test"),
    };

    IAiService service = new OpenAiService(config);
    service.ShouldBeAssignableTo<IAiService>();
  }

  [Test]
  public void IAiService_Interface_IsImplementedByAnthropic()
  {
    var config = new AiModelConfig
    {
      Id = new AutoValue<string>("test"),
      ApiFormat = new AutoValue<string>("Anthropic"),
      EndpointUrl = new AutoValue<string>("https://api.anthropic.com"),
      ModelId = new AutoValue<string>("claude-3"),
      EncryptedApiKey = new AutoValue<string>("sk-test"),
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
      var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir));
      var dm = new DataManager(tmpDir, encryptor);

      var openAiConfig = new AiModelConfig
      {
        Id = new AutoValue<string>("openai-1"),
        ApiFormat = new AutoValue<string>("OpenAI"),
        EndpointUrl = new AutoValue<string>("https://api.openai.com"),
        ModelId = new AutoValue<string>("gpt-4"),
        EncryptedApiKey = new AutoValue<string>("sk-test"),
      };
      dm.Settings.AiModels.Add(openAiConfig);
      dm.Settings.ActiveAiModelId.Value = "openai-1";

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
      var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir));
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
      var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(tmpDir));
      var dm = new DataManager(tmpDir, encryptor);

      var incompleteConfig = new AiModelConfig
      {
        Id = new AutoValue<string>("bad-1"),
        ApiFormat = new AutoValue<string>("OpenAI"),
        EndpointUrl = new AutoValue<string>(""),
        ModelId = new AutoValue<string>(""),
        EncryptedApiKey = new AutoValue<string>(""),
      };
      dm.Settings.AiModels.Add(incompleteConfig);
      dm.Settings.ActiveAiModelId.Value = "bad-1";

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
      EndpointUrl = new AutoValue<string>("https://api.example.com"),
      ModelId = new AutoValue<string>("test-model"),
      EncryptedApiKey = new AutoValue<string>("key123"),
    };
    AiServiceFactory.IsModelValid(valid).ShouldBeTrue();

    var missingUrl = new AiModelConfig
    {
      EndpointUrl = new AutoValue<string>(""),
      ModelId = new AutoValue<string>("test-model"),
      EncryptedApiKey = new AutoValue<string>("key123"),
    };
    AiServiceFactory.IsModelValid(missingUrl).ShouldBeFalse();

    var missingKey = new AiModelConfig
    {
      EndpointUrl = new AutoValue<string>("https://api.example.com"),
      ModelId = new AutoValue<string>("test-model"),
      EncryptedApiKey = new AutoValue<string>(""),
    };
    AiServiceFactory.IsModelValid(missingKey).ShouldBeFalse();
  }
}
