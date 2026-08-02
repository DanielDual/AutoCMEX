namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Shouldly;

/// <summary>
/// 猜测引擎单元测试
/// </summary>
public class GuessEngineTest : TestClass
{
  public GuessEngineTest(Node testScene)
    : base(testScene) { }

  // ==================== GuessParser Tests ====================

  [Test]
  public void GuessParser_NormalInput_ParsesCorrectly()
  {
    var result = GuessParser.Parse("1Alice 2Bob 3Charlie", 5);

    result.IsSuccess.ShouldBeTrue();
    result.Pairs.Count.ShouldBe(3);
    result.Pairs[0].Index.ShouldBe(1);
    result.Pairs[0].Creator.ShouldBe("Alice");
    result.Pairs[1].Index.ShouldBe(2);
    result.Pairs[1].Creator.ShouldBe("Bob");
    result.Pairs[2].Index.ShouldBe(3);
    result.Pairs[2].Creator.ShouldBe("Charlie");
  }

  [Test]
  public void GuessParser_SinglePair_ParsesCorrectly()
  {
    var result = GuessParser.Parse("1Alice", 5);

    result.IsSuccess.ShouldBeTrue();
    result.Pairs.Count.ShouldBe(1);
    result.Pairs[0].Index.ShouldBe(1);
    result.Pairs[0].Creator.ShouldBe("Alice");
  }

  [Test]
  public void GuessParser_EmptyInput_ReturnsError()
  {
    var result = GuessParser.Parse("", 5);
    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("为空");
  }

  [Test]
  public void GuessParser_WhitespaceInput_ReturnsError()
  {
    var result = GuessParser.Parse("   ", 5);
    result.IsSuccess.ShouldBeFalse();
  }

  [Test]
  public void GuessParser_InvalidFormat_ReturnsError()
  {
    var result = GuessParser.Parse("1 Alice 2Bob", 5);
    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("格式错误");
  }

  [Test]
  public void GuessParser_IndexOutOfBounds_ReturnsError()
  {
    var result = GuessParser.Parse("99Alice", 5);
    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("越界");
  }

  [Test]
  public void GuessParser_IndexZero_ReturnsError()
  {
    var result = GuessParser.Parse("0Alice", 5);
    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("越界");
  }

  [Test]
  public void GuessParser_MaxBoundaryIndex_Succeeds()
  {
    var result = GuessParser.Parse("5Alice", 5);
    result.IsSuccess.ShouldBeTrue();
    result.Pairs[0].Index.ShouldBe(5);
  }

  // ==================== GuessResponseHandler Tests ====================

  private static List<string> EmptyNames() => new();

  [Test]
  public void ResponseHandler_ThreeOrMore_AllCorrect()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(3, 3, new List<string>(), 0, EmptyNames());
    result.ShouldBe("3/3");
  }

  [Test]
  public void ResponseHandler_ThreeOrMore_PartialCorrect()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(3, 1, new List<string>(), 0, EmptyNames());
    result.ShouldBe("1/3");
  }

  [Test]
  public void ResponseHandler_ThreeOrMore_NoneCorrect()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(4, 0, new List<string>(), 0, EmptyNames());
    result.ShouldBe("0/4");
  }

  [Test]
  public void ResponseHandler_TwoCards_BothCorrect()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(2, 2, new List<string>(), 0, EmptyNames());
    result.ShouldBe("✔️");
  }

  [Test]
  public void ResponseHandler_TwoCards_OneCorrect()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(2, 1, new List<string>(), 0, EmptyNames());
    result.ShouldBe("❌️");
  }

  [Test]
  public void ResponseHandler_TwoCards_NoneCorrect()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(2, 0, new List<string>(), 0, EmptyNames());
    result.ShouldBe("❌️");
  }

  [Test]
  public void ResponseHandler_OneCard_NoResponse()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(1, 1, new List<string>(), 0, EmptyNames());
    result.ShouldBe("必须猜两个以上");
  }

  [Test]
  public void ResponseHandler_ZeroCards_NoResponse()
  {
    var handler = new GuessResponseHandler();
    var result = handler.Handle(0, 0, new List<string>(), 0, EmptyNames());
    result.ShouldBe("必须猜两个以上");
  }

  [Test]
  public void ResponseHandler_WithGuessedOutCards()
  {
    var handler = new GuessResponseHandler();
    var guessedOutNames = new List<string> { "符卡1（Card1）", "符卡3（Card3）" };
    var result = handler.Handle(2, 2, new List<string>(), 2, guessedOutNames);
    result.ShouldBe("✔️（符卡1（Card1）、符卡3（Card3） 已被猜出，已跳过）");
  }

  [Test]
  public void ResponseHandler_AllGuessedOut()
  {
    var handler = new GuessResponseHandler();
    var guessedOutNames = new List<string> { "符卡1（Card1）" };
    var result = handler.Handle(0, 0, new List<string>(), 1, guessedOutNames);
    result.ShouldBe("所有猜测的符卡均已被猜出");
  }

  // ==================== GuessPipeline Tests ====================

  [Test]
  public void Pipeline_ValidInput_ProcessesCorrectly()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1", Creator = "Alice" },
        new() { Name = "Card2", Creator = "Bob" },
        new() { Name = "Card3", Creator = "Charlie" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Bob 3Charlie", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Response.ShouldBe("3/3");
  }

  [Test]
  public void Pipeline_InvalidFormat_ReturnsError()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1" },
        new() { Name = "Card2" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1 Alice 2Bob", boss);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("格式错误");
  }

  [Test]
  public void Pipeline_OutOfBounds_ReturnsError()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard> { new() { Name = "Card1" } },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("5Alice", boss);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("越界");
  }

  [Test]
  public void Pipeline_AliasConversion_ConvertsCorrectly()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1", Creator = "Alice" },
        new() { Name = "Card2", Creator = "Bob" },
      },
    };

    var aliases = new List<CreatorAlias>
    {
      new()
      {
        MainName = "Alice",
        Aliases = new AutoList<string> { "Ali" },
      },
      new()
      {
        MainName = "Bob",
        Aliases = new AutoList<string> { "B" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), aliases);
    var result = pipeline.Process("1Ali 2B", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Response.ShouldBe("✔️");
  }

  [Test]
  public void Pipeline_RevealedCard_Skipped()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new()
        {
          Name = "Card1",
          Creator = "Alice",
          IsRevealed = true,
        },
        new() { Name = "Card2", Creator = "Bob" },
        new() { Name = "Card3", Creator = "Charlie" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Bob 3Charlie", boss);

    result.IsSuccess.ShouldBeTrue();
    // Card1 is revealed, so only 2 unrevealed cards are guessed
    result.Details.ShouldContain(d => d.Contains("已揭晓"));
  }

  [Test]
  public void Pipeline_TwoCardsBothCorrect_ReturnsDui()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1", Creator = "Alice" },
        new() { Name = "Card2", Creator = "Bob" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Bob", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Response.ShouldBe("✔️");
  }

  [Test]
  public void Pipeline_OneCard_NoResponse()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1", Creator = "Alice" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Response.ShouldBe("必须猜两个以上");
    // 单张猜测不标记为已猜出
    boss.SpellCards[0].IsGuessedOut.ShouldBeFalse();
  }

  [Test]
  public void Pipeline_GuessedOutCard_Skipped()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new()
        {
          Name = "Card1",
          Creator = "Alice",
          IsGuessedOut = true,
        },
        new() { Name = "Card2", Creator = "Bob" },
        new() { Name = "Card3", Creator = "Charlie" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Bob 3Charlie", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Details.ShouldContain(d => d.Contains("已被猜出"));
    // Card1 is guessed out, so only 2 cards count
    result.Response.ShouldBe("✔️（1 已被猜出，已跳过）");
  }

  [Test]
  public void Pipeline_AllCorrect_MarksAsGuessedOut()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1", Creator = "Alice" },
        new() { Name = "Card2", Creator = "Bob" },
        new() { Name = "Card3", Creator = "Charlie" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Bob 3Charlie", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Response.ShouldBe("3/3");

    // All three should now be marked as guessed out
    boss.SpellCards[0].IsGuessedOut.ShouldBeTrue();
    boss.SpellCards[1].IsGuessedOut.ShouldBeTrue();
    boss.SpellCards[2].IsGuessedOut.ShouldBeTrue();
  }

  [Test]
  public void Pipeline_PartialCorrect_DoesNotMarkGuessedOut()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1", Creator = "Alice" },
        new() { Name = "Card2", Creator = "Bob" },
        new() { Name = "Card3", Creator = "Charlie" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Wrong 3Charlie", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Response.ShouldBe("2/3");

    // None should be marked as guessed out (only 2/3 correct)
    boss.SpellCards[0].IsGuessedOut.ShouldBeFalse();
    boss.SpellCards[1].IsGuessedOut.ShouldBeFalse();
    boss.SpellCards[2].IsGuessedOut.ShouldBeFalse();
  }

  [Test]
  public void Pipeline_AllGuessedOut_ReturnsSkipMessage()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new()
        {
          Name = "Card1",
          Creator = "Alice",
          IsGuessedOut = true,
        },
        new()
        {
          Name = "Card2",
          Creator = "Bob",
          IsGuessedOut = true,
        },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Bob", boss);

    result.IsSuccess.ShouldBeTrue();
    result.Response.ShouldBe("所有猜测的符卡均已被猜出");
  }

  [Test]
  public void Pipeline_DuplicatePairs_Deduplicated()
  {
    var boss = new Boss
    {
      Name = "TestBoss",
      SpellCards = new AutoList<SpellCard>
      {
        new() { Name = "Card1", Creator = "Alice" },
        new() { Name = "Card2", Creator = "Bob" },
        new() { Name = "Card3", Creator = "Charlie" },
      },
    };

    var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
    var result = pipeline.Process("1Alice 2Bob 2Bob 2Bob 3Charlie", boss);

    result.IsSuccess.ShouldBeTrue();
    // 2Bob appears 3 times but only counts once → 3 unique cards
    result.Response.ShouldBe("3/3");
    result.Details.ShouldContain(d => d.Contains("重复猜测"));
  }
}
