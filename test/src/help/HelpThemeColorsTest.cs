namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Core.Help;
using AutoCMEX.UI;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="HelpThemeColors"/> 单元测试：四项帮助语义色的主题读取与兜底、
/// 提示块底色的透明度按明暗底选择，以及占位符替换的输出格式与异常输入。
/// </summary>
/// <remarks>
/// 纯逻辑用例：只建真实 <see cref="Theme"/> 与 <see cref="Control"/>（必须入树，主题链才生效），
/// 不加载 <c>src/assets/themes</c> 下的主题资源，也不依赖运行时主题切换。
/// </remarks>
public class HelpThemeColorsTest : TestClass
{
  /// <summary>链接色：刻意与其他三项、与兜底色都不同，用来识别串色。</summary>
  private static readonly Color LinkColor = new("#abcdef");

  /// <summary>代码块底色。</summary>
  private static readonly Color CodeBackgroundColor = new("#112233");

  /// <summary>表头底色。</summary>
  private static readonly Color TableHeaderColor = new("#445566");

  /// <summary>表格线色。</summary>
  private static readonly Color TableBorderColor = new("#778899");

  private readonly List<Node> _toCleanup = new();

  public HelpThemeColorsTest(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
        node.Free();
    }

    _toCleanup.Clear();
  }

  /// <summary>建一个只定义给定语义色的主题。</summary>
  /// <param name="entries">颜色项名与颜色值的成对列表。</param>
  /// <returns>新主题。</returns>
  private static Theme CreateTheme(params (string Name, Color Color)[] entries)
  {
    var theme = new Theme();
    foreach (var (name, color) in entries)
      theme.SetColor(name, ThemeCatalog.AppColorsType, color);
    return theme;
  }

  /// <summary>建一个挂了指定主题的 Control 并接进测试场景（主题链只在树内生效）。</summary>
  /// <param name="theme">要挂的主题；<c>null</c> 表示不挂主题。</param>
  /// <returns>已入树的 Control。</returns>
  private Control CreateControl(Theme? theme)
  {
    var control = new Control();
    if (theme is not null)
      control.Theme = theme;

    TestScene.AddChild(control);
    _toCleanup.Add(control);
    return control;
  }

  // ==================== 四项语义色：主题读取与兜底 ====================

  /// <summary>主题定义了四项帮助语义色：各读取方法返回各自的颜色项，不串色。</summary>
  [Test]
  public void SemanticColors_AreReadFromTheme()
  {
    var control = CreateControl(
      CreateTheme(
        (HelpThemeColors.LinkColorName, LinkColor),
        (HelpThemeColors.CodeBackgroundColorName, CodeBackgroundColor),
        (HelpThemeColors.TableHeaderBackgroundColorName, TableHeaderColor),
        (HelpThemeColors.TableBorderColorName, TableBorderColor)
      )
    );

    AssertColor(HelpThemeColors.Link(control), LinkColor, "链接色");
    AssertColor(HelpThemeColors.CodeBackground(control), CodeBackgroundColor, "代码块底色");
    AssertColor(HelpThemeColors.TableHeaderBackground(control), TableHeaderColor, "表头底色");
    AssertColor(HelpThemeColors.TableBorder(control), TableBorderColor, "表格线色");
  }

  /// <summary>主题只定义其中一项：该项取主题色，其余三项分别回退到各自的暗夜兜底色。</summary>
  [Test]
  public void SemanticColors_MissingItemsFallBackToDarkDefaults()
  {
    var control = CreateControl(CreateTheme((HelpThemeColors.LinkColorName, LinkColor)));

    AssertColor(HelpThemeColors.Link(control), LinkColor, "链接色");
    AssertColor(
      HelpThemeColors.CodeBackground(control),
      HelpThemeColors.FallbackCodeBackground,
      "代码块底色"
    );
    AssertColor(
      HelpThemeColors.TableHeaderBackground(control),
      HelpThemeColors.FallbackTableHeaderBackground,
      "表头底色"
    );
    AssertColor(
      HelpThemeColors.TableBorder(control),
      HelpThemeColors.FallbackTableBorder,
      "表格线色"
    );
  }

  /// <summary>没有主题（默认主题）或节点为空：四项全部回退到各自的暗夜兜底色。</summary>
  [Test]
  public void SemanticColors_WithoutThemeFallBackToDarkDefaults()
  {
    var control = CreateControl(null);

    AssertColor(HelpThemeColors.Link(control), HelpThemeColors.FallbackLink, "链接色");
    AssertColor(HelpThemeColors.Link(null), HelpThemeColors.FallbackLink, "空节点的链接色");
    AssertColor(
      HelpThemeColors.CodeBackground(control),
      HelpThemeColors.FallbackCodeBackground,
      "代码块底色"
    );
    AssertColor(
      HelpThemeColors.TableHeaderBackground(control),
      HelpThemeColors.FallbackTableHeaderBackground,
      "表头底色"
    );
    AssertColor(
      HelpThemeColors.TableBorder(control),
      HelpThemeColors.FallbackTableBorder,
      "表格线色"
    );
  }

  // ==================== 提示块底色：状态色 + 明暗底透明度 ====================

  /// <summary>亮底（窗口底色为白）：底色取状态色 RGB，透明度为 <see cref="HelpThemeColors.LightBackgroundAlpha"/>。</summary>
  [Test]
  public void TipBackground_OnLightBackground_UsesLightAlpha()
  {
    var status = new Color("#3366cc");
    var control = CreateControl(
      CreateTheme((ThemeCatalog.WindowBackground, Colors.White), (ThemeCatalog.StatusError, status))
    );

    var tip = HelpThemeColors.TipBackground(ThemeCatalog.StatusError, control);

    tip.A.ShouldBe(HelpThemeColors.LightBackgroundAlpha);
    AssertRgb(tip, status, "亮底提示块");
  }

  /// <summary>暗底（窗口底色为黑）：底色取状态色 RGB，透明度为 <see cref="HelpThemeColors.DarkBackgroundAlpha"/>。</summary>
  [Test]
  public void TipBackground_OnDarkBackground_UsesDarkAlpha()
  {
    var status = new Color("#3366cc");
    var control = CreateControl(
      CreateTheme((ThemeCatalog.WindowBackground, Colors.Black), (ThemeCatalog.Accent, status))
    );

    var tip = HelpThemeColors.TipBackground(ThemeCatalog.Accent, control);

    tip.A.ShouldBe(HelpThemeColors.DarkBackgroundAlpha);
    AssertRgb(tip, status, "暗底提示块");
  }

  /// <summary>主题未定义状态色：RGB 回退到 <see cref="HelpThemeColors.FallbackStatus"/>，透明度仍按窗口底色决定。</summary>
  [Test]
  public void TipBackground_WithoutStatusColor_FallsBackToDarkStatusColor()
  {
    var control = CreateControl(CreateTheme((ThemeCatalog.WindowBackground, Colors.White)));

    var tip = HelpThemeColors.TipBackground(ThemeCatalog.StatusWarn, control);

    var expected = HelpThemeColors.FallbackStatus(ThemeCatalog.StatusWarn);
    tip.A.ShouldBe(HelpThemeColors.LightBackgroundAlpha);
    AssertRgb(tip, expected, "兜底状态色");
  }

  // ==================== 占位符替换 ====================

  /// <summary>占位符被替换成 <c>#RRGGBBAA</c>：9 字符、小写、带 alpha，前后正文原样保留。</summary>
  [Test]
  public void Resolve_ReplacesPlaceholderWithRgbaHex()
  {
    var control = CreateControl(CreateTheme((HelpThemeColors.LinkColorName, Colors.Red)));

    var resolved = HelpThemeColors.Resolve(
      "见 " + HelpPlaceholders.Wrap(HelpPlaceholders.Link) + " 链接",
      control
    );

    // 小写 'f' 与末尾 alpha 一起被这条等值断言固定住。
    resolved.ShouldBe("见 #ff0000ff 链接");
    resolved.ShouldNotContain(HelpPlaceholders.Delimiter.ToString());
  }

  /// <summary>非不透明颜色：保留 alpha 通道，而不是一律输出 <c>ff</c>。</summary>
  [Test]
  public void Resolve_KeepsAlphaChannelBelowOpaque()
  {
    var control = CreateControl(
      CreateTheme((HelpThemeColors.LinkColorName, new Color(1f, 0f, 0f, 0.75f)))
    );

    var resolved = HelpThemeColors.Resolve(HelpPlaceholders.Wrap(HelpPlaceholders.Link), control);

    // 0.75 × 255 = 191.25 → 0xbf（四舍五入与截断都是 191，不依赖取整策略）。
    resolved.ShouldBe("#ff0000bf");
  }

  /// <summary>正文不含占位符：原样返回，不做任何改写（包括不含分隔符的空串）。</summary>
  [Test]
  public void Resolve_WithoutPlaceholder_ReturnsInputAsIs()
  {
    var control = CreateControl(CreateTheme((HelpThemeColors.LinkColorName, Colors.Red)));

    const string Plain = "[b]粗体[/b] {花括号} [方括号]";
    HelpThemeColors.Resolve(Plain, control).ShouldBe(Plain);
    HelpThemeColors.Resolve(string.Empty, control).ShouldBe(string.Empty);
  }

  /// <summary>落单的分隔符不吞字：开头落单时正文全部保留，结尾落单时也原样留下。</summary>
  [Test]
  public void Resolve_UnpairedDelimiter_DoesNotSwallowText()
  {
    var control = CreateControl(CreateTheme((HelpThemeColors.LinkColorName, Colors.Red)));

    var leading = HelpPlaceholders.Delimiter + "孤立的分隔符";
    HelpThemeColors.Resolve(leading, control).ShouldBe(leading);

    var trailing =
      "A" + HelpPlaceholders.Wrap(HelpPlaceholders.Link) + "B" + HelpPlaceholders.Delimiter;
    HelpThemeColors.Resolve(trailing, control).ShouldBe("A#ff0000ffB" + HelpPlaceholders.Delimiter);
  }

  /// <summary>未登记的占位符名：回退正文色（<see cref="ThemeCatalog.LogText"/>），不抛异常也不留分隔符。</summary>
  [Test]
  public void Resolve_UnknownPlaceholderName_FallsBackToLogTextColor()
  {
    var logText = new Color("#123456");
    var control = CreateControl(CreateTheme((ThemeCatalog.LogText, logText)));

    var resolved = HelpThemeColors.Resolve(HelpPlaceholders.Wrap("no_such_color"), control);

    resolved.ShouldBe("#123456ff");
  }

  /// <summary>断言两色四通道一致，失败时给出可读差异。</summary>
  /// <param name="actual">实测颜色。</param>
  /// <param name="expected">期望颜色。</param>
  /// <param name="what">用例说明，用于失败信息。</param>
  private static void AssertColor(Color actual, Color expected, string what) =>
    actual.IsEqualApprox(expected).ShouldBeTrue($"{what}色值不符：{actual} != {expected}");

  /// <summary>只断言 RGB 三个通道（提示块底色带透明度，alpha 单独断言）。</summary>
  /// <param name="actual">实测颜色。</param>
  /// <param name="expected">期望颜色。</param>
  /// <param name="what">用例说明，用于失败信息。</param>
  private static void AssertRgb(Color actual, Color expected, string what)
  {
    AssertChannel(actual.R, expected.R, what + " R");
    AssertChannel(actual.G, expected.G, what + " G");
    AssertChannel(actual.B, expected.B, what + " B");
  }

  /// <summary>断言单个颜色通道近似相等（浮点通道不能用 ShouldBe 直接比较）。</summary>
  /// <param name="actual">实测通道值。</param>
  /// <param name="expected">期望通道值。</param>
  /// <param name="what">用例说明，用于失败信息。</param>
  private static void AssertChannel(float actual, float expected, string what) =>
    Mathf.IsEqualApprox(actual, expected).ShouldBeTrue($"{what} 不符：{actual} != {expected}");
}
