namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Shouldly;

/// <summary>
/// 数据模型单元测试
/// </summary>
public class ModelsTest : TestClass
{
  public ModelsTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void Boss_DefaultValues()
  {
    var boss = new Boss();
    boss.Name.ShouldBe(string.Empty);
    boss.SpellCards.ShouldNotBeNull();
    boss.SpellCards.ShouldBeEmpty();
  }

  [Test]
  public void Boss_CanAddSpellCards()
  {
    var boss = new Boss { Name = "TestBoss" };
    boss.SpellCards.Add(
      new SpellCard
      {
        Name = new AutoValue<string>("Card1"),
        Creator = new AutoValue<string>("Alice"),
      }
    );
    boss.SpellCards.Add(
      new SpellCard
      {
        Name = new AutoValue<string>("Card2"),
        Creator = new AutoValue<string>("Bob"),
      }
    );

    boss.SpellCards.Count.ShouldBe(2);
    boss.SpellCards[0].Name.Value.ShouldBe("Card1");
    boss.SpellCards[1].Creator.Value.ShouldBe("Bob");
  }

  [Test]
  public void SpellCard_DefaultValues()
  {
    var card = new SpellCard();
    card.Name.Value.ShouldBe(string.Empty);
    card.Creator.Value.ShouldBe(string.Empty);
  }

  [Test]
  public void SpellCard_CreatorCanBeEmpty()
  {
    var card = new SpellCard { Name = new AutoValue<string>("Card1") };
    card.Creator.Value.ShouldBe(string.Empty);
  }

  [Test]
  public void CreatorAlias_DefaultValues()
  {
    var alias = new CreatorAlias();
    alias.MainName.ShouldBe(string.Empty);
    alias.Aliases.ShouldNotBeNull();
    alias.Aliases.ShouldBeEmpty();
  }

  [Test]
  public void CreatorAlias_CanAddAliases()
  {
    var alias = new CreatorAlias { MainName = "Alice" };
    alias.Aliases.Add("Ali");
    alias.Aliases.Add("A");

    alias.Aliases.Count.ShouldBe(2);
    alias.Aliases.ShouldContain("Ali");
    alias.Aliases.ShouldContain("A");
  }

  [Test]
  public void AiModelConfig_DefaultValues()
  {
    var config = new AiModelConfig();
    config.Id.ShouldBe(string.Empty);
    config.ApiFormat.ShouldBe("OpenAI");
    config.EndpointUrl.ShouldBe(string.Empty);
    config.ModelId.ShouldBe(string.Empty);
    config.EncryptedApiKey.ShouldBe(string.Empty);
  }

  [Test]
  public void AiModelConfig_CanSetAnthropicFormat()
  {
    var config = new AiModelConfig
    {
      Id = "test-1",
      ApiFormat = "Anthropic",
      EndpointUrl = "https://api.anthropic.com",
      ModelId = "claude-3",
    };

    config.ApiFormat.ShouldBe("Anthropic");
    config.EndpointUrl.ShouldBe("https://api.anthropic.com");
  }

  [Test]
  public void AppSettings_DefaultValues()
  {
    var settings = new AppSettings();
    settings.AiModels.ShouldNotBeNull();
    settings.AiModels.ShouldBeEmpty();
    settings.WebSocketPort.Value.ShouldBe(5140);
    settings.MessageFilterMode.Value.ShouldBe("strict");
    settings.KoishiPluginPath.Value.ShouldBe(string.Empty);
  }

  [Test]
  public void AppSettings_CanAddAiModels()
  {
    var settings = new AppSettings();
    settings.AiModels.Add(new AiModelConfig { Id = "model-1" });
    settings.AiModels.Add(new AiModelConfig { Id = "model-2" });

    settings.AiModels.Count.ShouldBe(2);
  }

  // ==================== Edge Case Tests ====================

  [Test]
  public void SpellCard_IsGuessedOut_DefaultsToFalse()
  {
    var card = new SpellCard();
    card.IsGuessedOut.Value.ShouldBeFalse();
  }

  [Test]
  public void Boss_SpellCards_CannotBeNull()
  {
    var boss = new Boss();
    boss.SpellCards.ShouldNotBeNull();
  }

  [Test]
  public void CreatorAlias_Aliases_CannotBeNull()
  {
    var alias = new CreatorAlias();
    alias.Aliases.ShouldNotBeNull();
  }

  [Test]
  public void AiModelConfig_DefaultApiFormat_IsOpenAI()
  {
    var config = new AiModelConfig();
    config.ApiFormat.ShouldBe("OpenAI");
  }

  [Test]
  public void AppSettings_DefaultWebSocketPort_Is5140()
  {
    var settings = new AppSettings();
    settings.WebSocketPort.Value.ShouldBe(5140);
  }

  [Test]
  public void AppSettings_DefaultMessageFilterMode_IsStrict()
  {
    var settings = new AppSettings();
    settings.MessageFilterMode.Value.ShouldBe("strict");
  }
}
