namespace AutoCMEX;

using AutoCMEX.Helpers;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// Tests for <see cref="StringEscapeHelper"/>.
/// </summary>
public class StringEscapeHelperTest : TestClass
{
  public StringEscapeHelperTest(Node testScene)
    : base(testScene) { }

  // ==================== EscapeCsv Tests ====================

  [Test]
  public void EscapeCsv_NullInput_ReturnsEmptyString() =>
    StringEscapeHelper.EscapeCsv(null!).ShouldBe(string.Empty);

  [Test]
  public void EscapeCsv_EmptyInput_ReturnsEmptyString() =>
    StringEscapeHelper.EscapeCsv(string.Empty).ShouldBe(string.Empty);

  [Test]
  public void EscapeCsv_SimpleText_ReturnsUnchanged() =>
    StringEscapeHelper.EscapeCsv("hello").ShouldBe("hello");

  [Test]
  public void EscapeCsv_ContainsComma_WrapsInQuotes() =>
    StringEscapeHelper.EscapeCsv("a,b").ShouldBe("\"a,b\"");

  [Test]
  public void EscapeCsv_ContainsDoubleQuote_EscapesAndWraps() =>
    StringEscapeHelper.EscapeCsv("say \"hi\"").ShouldBe("\"say \"\"hi\"\"\"");

  [Test]
  public void EscapeCsv_ContainsNewline_WrapsInQuotes() =>
    StringEscapeHelper.EscapeCsv("line1\nline2").ShouldBe("\"line1\nline2\"");

  // ==================== EscapeBbcode Tests ====================

  [Test]
  public void EscapeBbcode_NullInput_ReturnsEmptyString() =>
    StringEscapeHelper.EscapeBbcode(null!).ShouldBe(string.Empty);

  [Test]
  public void EscapeBbcode_EmptyInput_ReturnsEmptyString() =>
    StringEscapeHelper.EscapeBbcode(string.Empty).ShouldBe(string.Empty);

  [Test]
  public void EscapeBbcode_NoBrackets_ReturnsUnchanged() =>
    StringEscapeHelper.EscapeBbcode("plain text").ShouldBe("plain text");

  [Test]
  public void EscapeBbcode_ContainsBracket_Escapes() =>
    // Only '[' is replaced with '[lb]', ']' is left as-is
    StringEscapeHelper.EscapeBbcode("[b]bold[/b]").ShouldBe("[lb]b]bold[lb]/b]");

  [Test]
  public void EscapeBbcode_MultipleBrackets_AllEscaped() =>
    StringEscapeHelper.EscapeBbcode("[[[lb]]]").ShouldBe("[lb][lb][lb]lb]]]");
}
