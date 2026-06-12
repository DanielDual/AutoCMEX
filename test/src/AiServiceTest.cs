namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Core.Ai;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// AI 服务单元测试
/// </summary>
public class AiServiceTest : TestClass
{
    public AiServiceTest(Node testScene) : base(testScene) { }

    [Test]
    public void OpenAiService_CanBeConstructed()
    {
        var config = new AiModelConfig
        {
            Id = "test",
            ApiFormat = "OpenAI",
            EndpointUrl = "https://api.openai.com",
            ModelId = "gpt-4",
            EncryptedApiKey = "sk-test"
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
            EncryptedApiKey = "sk-test"
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
            EncryptedApiKey = "sk-test"
        };

        var aiService = new OpenAiService(config);
        var boss = new Boss
        {
            Name = "TestBoss",
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1" },
                new() { Name = "Card2" }
            }
        };

        var fuzzifier = new AiFuzzifier(aiService, new List<CreatorAlias>(), new List<Boss>(), boss);
        fuzzifier.ShouldNotBeNull();
    }

    [Test]
    public void IAiService_Interface_IsImplementedByOpenAi()
    {
        var config = new AiModelConfig
        {
            Id = "test",
            EndpointUrl = "https://api.openai.com",
            ModelId = "gpt-4",
            EncryptedApiKey = "sk-test"
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
            EncryptedApiKey = "sk-test"
        };

        IAiService service = new AnthropicService(config);
        service.ShouldBeAssignableTo<IAiService>();
    }
}
