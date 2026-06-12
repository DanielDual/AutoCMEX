namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 猜测引擎单元测试
/// </summary>
public class GuessEngineTest : TestClass
{
    public GuessEngineTest(Node testScene) : base(testScene) { }

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

    [Test]
    public void ResponseHandler_ThreeOrMore_AllCorrect()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(3, 3, new List<string>());
        result.ShouldBe("猜对 3/3 张");
    }

    [Test]
    public void ResponseHandler_ThreeOrMore_PartialCorrect()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(3, 1, new List<string>());
        result.ShouldBe("猜对 1/3 张");
    }

    [Test]
    public void ResponseHandler_ThreeOrMore_NoneCorrect()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(4, 0, new List<string>());
        result.ShouldBe("猜对 0/4 张");
    }

    [Test]
    public void ResponseHandler_TwoCards_BothCorrect()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(2, 2, new List<string>());
        result.ShouldBe("对");
    }

    [Test]
    public void ResponseHandler_TwoCards_OneCorrect()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(2, 1, new List<string>());
        result.ShouldBe("错");
    }

    [Test]
    public void ResponseHandler_TwoCards_NoneCorrect()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(2, 0, new List<string>());
        result.ShouldBe("错");
    }

    [Test]
    public void ResponseHandler_OneCard_NoResponse()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(1, 1, new List<string>());
        result.ShouldBe(string.Empty);
    }

    [Test]
    public void ResponseHandler_ZeroCards_NoResponse()
    {
        var handler = new GuessResponseHandler();
        var result = handler.Handle(0, 0, new List<string>());
        result.ShouldBe(string.Empty);
    }

    // ==================== GuessPipeline Tests ====================

    [Test]
    public void Pipeline_ValidInput_ProcessesCorrectly()
    {
        var boss = new Boss
        {
            Name = "TestBoss",
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1", Creator = "Alice" },
                new() { Name = "Card2", Creator = "Bob" },
                new() { Name = "Card3", Creator = "Charlie" }
            }
        };

        var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
        var result = pipeline.Process("1Alice 2Bob 3Charlie", boss);

        result.IsSuccess.ShouldBeTrue();
        result.Response.ShouldBe("猜对 3/3 张");
    }

    [Test]
    public void Pipeline_InvalidFormat_ReturnsError()
    {
        var boss = new Boss
        {
            Name = "TestBoss",
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1" },
                new() { Name = "Card2" }
            }
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
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1" }
            }
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
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1", Creator = "Alice" },
                new() { Name = "Card2", Creator = "Bob" }
            }
        };

        var aliases = new List<CreatorAlias>
        {
            new() { MainName = "Alice", Aliases = new List<string> { "Ali" } },
            new() { MainName = "Bob", Aliases = new List<string> { "B" } }
        };

        var pipeline = new GuessPipeline(new GuessResponseHandler(), aliases);
        var result = pipeline.Process("1Ali 2B", boss);

        result.IsSuccess.ShouldBeTrue();
        result.Response.ShouldBe("对");
    }

    [Test]
    public void Pipeline_RevealedCard_Skipped()
    {
        var boss = new Boss
        {
            Name = "TestBoss",
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1", Creator = "Alice", IsRevealed = true },
                new() { Name = "Card2", Creator = "Bob" },
                new() { Name = "Card3", Creator = "Charlie" }
            }
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
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1", Creator = "Alice" },
                new() { Name = "Card2", Creator = "Bob" }
            }
        };

        var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
        var result = pipeline.Process("1Alice 2Bob", boss);

        result.IsSuccess.ShouldBeTrue();
        result.Response.ShouldBe("对");
    }

    [Test]
    public void Pipeline_OneCard_NoResponse()
    {
        var boss = new Boss
        {
            Name = "TestBoss",
            SpellCards = new List<SpellCard>
            {
                new() { Name = "Card1", Creator = "Alice" }
            }
        };

        var pipeline = new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>());
        var result = pipeline.Process("1Alice", boss);

        result.IsSuccess.ShouldBeTrue();
        result.Response.ShouldBe(string.Empty);
    }
}
