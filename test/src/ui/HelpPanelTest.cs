namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using AutoCMEX.Core.Help;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.Test.Drivers;
using AutoCMEX.UI;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// <see cref="HelpPanel"/> 单元测试：导航、开页、历史栈、搜索、本页目录与链接分派。
/// </summary>
/// <remarks>
/// 全部用例走 <c>FakeNodeTree + IsTesting</c> 单元测试模式（规则 25/26），不实例化 .tscn；
/// 导航树替身转发真实 <see cref="Tree"/>（规则 29）。
/// </remarks>
public class HelpPanelTest : TestClass
{
  private readonly List<HelpPanelDriver> _drivers = new();
  private FakeContent _content = default!;
  private HelpPanelDriver _driver = default!;

  public HelpPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _content = new FakeContent(
      Page("start/index", "帮助中心", "开始", 1, "甲", "乙", "丙", "丁"),
      Page("merge/overview", "整合概览", "整合", 1, "一", "二"),
      Page("run/faq", "常见问题", "运行与排障", 1)
    );
    _driver = NewDriver(_content);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var driver in _drivers)
    {
      driver.Dispose();
    }
    _drivers.Clear();
  }

  // ==================== 导航 ====================

  /// <summary>真实生成内容应渲染出 7 个分区、28 个页面行，且顺序与目录一致。</summary>
  [Test]
  public void Navigation_HasSevenSectionsAndTwentyEightPagesInOrder()
  {
    var catalog = HelpContentCatalog.FromGenerated();
    var driver = NewDriver(catalog);

    var root = driver.NavTree.Object.GetRoot();
    root.ShouldNotBeNull();
    root!.GetChildCount().ShouldBe(7, "导航分区数应与 7 个分区常量一致");

    var titles = new List<string>();
    int pageCount = 0;
    for (int sectionIndex = 0; sectionIndex < root.GetChildCount(); sectionIndex++)
    {
      var section = root.GetChild(sectionIndex);
      section.IsSelectable(0).ShouldBeFalse("分区行只作分组，不可选中");

      for (int pageIndex = 0; pageIndex < section.GetChildCount(); pageIndex++)
      {
        var item = section.GetChild(pageIndex);
        item.GetMetadata(0).AsString().ShouldBe(catalog.Pages[pageCount].Id);
        item.GetText(0).ShouldBe(catalog.Pages[pageCount].Title);
        titles.Add(item.GetText(0));
        pageCount++;
      }
    }

    pageCount.ShouldBe(28);
    titles.Count.ShouldBe(28);
  }

  /// <summary>点击导航项打开页面：写解析后的正文 BBCode 并更新顶栏标题。</summary>
  [Test]
  public void SelectingPage_WritesBbcodeAndTitle()
  {
    _driver.SelectPage("merge/overview");

    _driver.ContentView.Object.Text.ShouldBe(
      HelpThemeColors.Resolve(_content.GetBbcode("merge/overview"), _driver.Panel)
    );
    _driver.PageTitle.Object.Text.ShouldBe("整合概览");
  }

  /// <summary>同一页重复点击不重复入栈：返回一次应回到默认页而不是停留在原页。</summary>
  [Test]
  public void SelectingSamePageTwice_DoesNotPushHistoryTwice()
  {
    _driver.SelectPage("merge/overview");
    _driver.SelectPage("merge/overview");

    _driver.BackBtn.Raise(m => m.Pressed += null);

    _driver.PageTitle.Object.Text.ShouldBe("帮助中心");
  }

  /// <summary>返回栈：开两页后按返回应逐页回退，栈空后停住。</summary>
  [Test]
  public void Back_ReturnsToPreviousPageUntilStackEmpty()
  {
    _driver.SelectPage("merge/overview");
    _driver.SelectPage("run/faq");

    _driver.BackBtn.Raise(m => m.Pressed += null);
    _driver.PageTitle.Object.Text.ShouldBe("整合概览");

    _driver.BackBtn.Raise(m => m.Pressed += null);
    _driver.PageTitle.Object.Text.ShouldBe("帮助中心");

    _driver.BackBtn.Raise(m => m.Pressed += null);
    _driver.PageTitle.Object.Text.ShouldBe("帮助中心");
  }

  /// <summary>上一页/下一页按钮按导航全局顺序给出真实页名，首末页置灰。</summary>
  [Test]
  public void PrevNextButtons_ShowNeighborTitlesAndDisableAtEnds()
  {
    _driver.PrevBtn.Object.Disabled.ShouldBeTrue("首页没有上一页");
    _driver.NextBtn.Object.Text.ShouldBe("整合概览 →");

    _driver.SelectPage("merge/overview");
    _driver.PrevBtn.Object.Text.ShouldBe("← 帮助中心");
    _driver.PrevBtn.Object.Disabled.ShouldBeFalse();
    _driver.NextBtn.Object.Text.ShouldBe("常见问题 →");

    _driver.SelectPage("run/faq");
    _driver.NextBtn.Object.Disabled.ShouldBeTrue("末页没有下一页");
  }

  /// <summary>切板块（面板隐藏再显示）后仍停留在原页并保留正文。</summary>
  [Test]
  public void HidingAndShowingPanel_KeepsCurrentPage()
  {
    _driver.SelectPage("merge/overview");

    _driver.Panel.Visible = false;
    _driver.Panel.Visible = true;

    _driver.PageTitle.Object.Text.ShouldBe("整合概览");
    _driver.ContentView.Object.Text.ShouldBe(
      HelpThemeColors.Resolve(_content.GetBbcode("merge/overview"), _driver.Panel)
    );
  }

  /// <summary>会话内记住每页滚动位置：切走再切回恢复，首次打开的页从顶部开始。</summary>
  [Test]
  public void ScrollPosition_IsRememberedPerPage()
  {
    _driver.SelectPage("merge/overview");
    _driver.ContentScroll.Object.ScrollVertical = 120;

    _driver.SelectPage("run/faq");
    _driver.ContentScroll.Object.ScrollVertical.ShouldBe(0, "首次打开的页从顶部开始");

    _driver.SelectPage("merge/overview");
    _driver.ContentScroll.Object.ScrollVertical.ShouldBe(120, "回到旧页应恢复上次滚动位置");
  }

  /// <summary>搜索打开时屏蔽正文子树的鼠标（MouseBehaviorRecursive），关闭后恢复。</summary>
  [Test]
  public void Search_BlocksDocumentMouseRecursively()
  {
    _driver.Body.Object.MouseBehaviorRecursive.ShouldBe(
      Control.MouseBehaviorRecursiveEnum.Inherited
    );

    _driver.Panel._UnhandledKeyInput(KeyEvent(Key.K, ctrl: true));
    _driver.SearchOverlay.Object.Visible.ShouldBeTrue();
    _driver.Body.Object.MouseBehaviorRecursive.ShouldBe(
      Control.MouseBehaviorRecursiveEnum.Disabled
    );

    _driver.SearchCloseBtn.Raise(m => m.Pressed += null);
    _driver.SearchOverlay.Object.Visible.ShouldBeFalse();
    _driver.Body.Object.MouseBehaviorRecursive.ShouldBe(
      Control.MouseBehaviorRecursiveEnum.Inherited
    );
  }

  /// <summary>点击搜索遮罩空白处关闭搜索（同时证明遮罩吃掉鼠标、不外泄到正文）。</summary>
  [Test]
  public void SearchBackdropClick_ClosesOverlay()
  {
    _driver.Panel._UnhandledKeyInput(KeyEvent(Key.K, ctrl: true));
    _driver.SearchOverlay.Object.Visible.ShouldBeTrue();

    _driver.SearchBackdrop.Raise(
      m => m.GuiInput += null,
      new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }
    );

    _driver.SearchOverlay.Object.Visible.ShouldBeFalse("点遮罩应关闭搜索");
  }

  // ==================== 主题色 ====================

  /// <summary>正文里的语义颜色占位符在开页时解析成当前主题色，不再出现占位符分隔符。</summary>
  [Test]
  public void OpeningPage_ResolvesThemePlaceholders()
  {
    _driver.SelectPage("merge/overview");

    string raw = _content.GetBbcode("merge/overview");
    raw.Contains(HelpPlaceholders.Wrap(HelpPlaceholders.Link))
      .ShouldBeTrue("用例前提：正文含语义颜色占位符");

    string text = _driver.ContentView.Object.Text;
    text.ShouldBe(HelpThemeColors.Resolve(raw, _driver.Panel));
    text.Contains(HelpThemeColors.Link(_driver.Panel).ToHtml(true))
      .ShouldBeTrue("占位符应替换成当前主题的链接色");
    text.Contains(HelpPlaceholders.Delimiter).ShouldBeFalse("正文不得残留占位符分隔符");
  }

  /// <summary>主题切换：只重新解析当前页正文颜色，页面、返回栈与滚动位置都保持不变。</summary>
  [Test]
  public void ThemeChanged_ReresolvesCurrentPageOnly()
  {
    _driver.SelectPage("merge/overview");
    _driver.ContentScroll.Object.ScrollVertical = 240;
    string before = _driver.ContentView.Object.Text;

    string raw = $"[color={HelpPlaceholders.Wrap(HelpPlaceholders.TipWarn)}]注意[/color] 换了主题";
    _content.SetBbcode("merge/overview", raw);

    _driver.EmitThemeChanged();

    string text = _driver.ContentView.Object.Text;
    text.ShouldBe(HelpThemeColors.Resolve(raw, _driver.Panel), "主题变化后应按新主题重解析正文");
    text.ShouldNotBe(before);
    text.Contains(HelpPlaceholders.Delimiter).ShouldBeFalse("正文不得残留占位符分隔符");
    _driver.ContentScroll.Object.ScrollVertical.ShouldBe(240, "重渲染不得重置滚动位置");
    _driver.PageTitle.Object.Text.ShouldBe("整合概览", "重渲染不得换页");
    _driver.BackBtn.Object.Disabled.ShouldBeFalse("重渲染不得重置返回栈");
  }

  /// <summary>内容为空（还没打开任何页）时主题变化安全返回，不覆盖兜底文案。</summary>
  [Test]
  public void ThemeChanged_WithNoCurrentPage_IsIgnored()
  {
    var driver = NewDriver(new FakeContent());
    string fallback = driver.ContentView.Object.Text;
    fallback.ShouldContain("帮助内容不可用");

    driver.EmitThemeChanged();

    driver.ContentView.Object.Text.ShouldBe(fallback);
  }

  /// <summary>搜索覆盖层有可见的返回按钮，与 Esc 同效。</summary>
  [Test]
  public void SearchCloseButton_HidesOverlay()
  {
    _driver.Panel._UnhandledKeyInput(KeyEvent(Key.K, ctrl: true));
    _driver.SearchOverlay.Object.Visible.ShouldBeTrue();

    _driver.SearchCloseBtn.Raise(m => m.Pressed += null);

    _driver.SearchOverlay.Object.Visible.ShouldBeFalse("可见返回按钮应与 Esc 同效");
  }

  /// <summary>导航栏宽度从布局记忆恢复（ui_layout.json 的 help.nav）。</summary>
  [Test]
  public void NavSplitWidth_AppliesRememberedLayout()
  {
    var dm = new DataManager(
      Path.Combine(Path.GetTempPath(), "AutoCMEX_HelpSplit_" + Guid.NewGuid().ToString("N")),
      new AesEncryptor("test-key")
    );
    dm.LoadAll();
    dm.UiLayout.Splits.Add(
      new SplitLayoutRecord { Key = new(UiLayoutKeys.HelpNav), Offset = new(340) }
    );

    var driver = NewDriver(_content, dm);

    driver.Body.Object.SplitOffset.ShouldBe(340, "打开帮助时应恢复记忆里的导航栏宽度");
    dm.Dispose();
  }

  /// <summary>默认打开「帮助中心」，正文来自生成常量。</summary>
  [Test]
  public void DefaultPage_IsHelpCenter()
  {
    var catalog = HelpContentCatalog.FromGenerated();
    var driver = NewDriver(catalog);

    driver.PageTitle.Object.Text.ShouldBe("帮助中心");
    driver.ContentView.Object.Text.ShouldBe(
      HelpThemeColors.Resolve(catalog.GetBbcode(HelpContentCatalog.DefaultPageId), driver.Panel)
    );
  }

  /// <summary>生成内容为空时显示「帮助内容不可用」兜底，不崩溃。</summary>
  [Test]
  public void EmptyContent_ShowsUnavailableFallback()
  {
    var driver = NewDriver(new FakeContent());

    driver.ContentView.Object.Text.ShouldContain("帮助内容不可用");
    driver.TocBtn.Object.Visible.ShouldBeFalse();
  }

  // ==================== 搜索 ====================

  /// <summary>Ctrl+K 打开搜索覆盖层，Esc 关闭；面板不可见时不响应。</summary>
  [Test]
  public void CtrlK_OpensSearchOverlay_AndEscCloses()
  {
    _driver.Panel._UnhandledKeyInput(KeyEvent(Key.K, ctrl: true));
    _driver.SearchOverlay.Object.Visible.ShouldBeTrue();

    _driver.Panel._UnhandledKeyInput(KeyEvent(Key.Escape));
    _driver.SearchOverlay.Object.Visible.ShouldBeFalse();

    _driver.Panel.Visible = false;
    _driver.Panel._UnhandledKeyInput(KeyEvent(Key.K, ctrl: true));
    _driver.SearchOverlay.Object.Visible.ShouldBeFalse("面板不可见时不应抢 Ctrl+K");
  }

  /// <summary>输入变化只重启防抖计时，不在每次按键时检索。</summary>
  [Test]
  public void SearchTextChanged_OnlyRestartsDebounce()
  {
    _driver.SearchInput.Raise(m => m.TextChanged += null, "整合");

    _driver.SearchDebounce.Verify(m => m.Start(It.IsAny<double>()), Times.Once);
    _driver.SearchResultTexts.Count.ShouldBe(0, "超时前不应检索");
  }

  /// <summary>防抖超时入口：填结果、写标题与片段、每项 metadata 存 pageId。</summary>
  [Test]
  public void SearchTimeout_FillsResultsWithPageMetadata()
  {
    _driver.SearchInput.Object.Text = "整合";
    _driver.SearchDebounce.Raise(m => m.Timeout += null);

    _driver.SearchResultTexts.Count.ShouldBeGreaterThan(0);
    _driver.SearchResultTexts[0].ShouldContain("整合概览");
    _driver.SearchHint.Object.Text.ShouldBe(string.Empty);
    _driver.SearchResults.Verify(
      m => m.SetItemMetadata(0, It.Is<Variant>(v => v.AsString() == "merge/overview")),
      Times.Once
    );
  }

  /// <summary>空输入走空态提示，无结果走「换更短关键词」提示。</summary>
  [Test]
  public void Search_EmptyAndNoResult_UseHint()
  {
    _driver.SearchInput.Object.Text = "   ";
    _driver.SearchDebounce.Raise(m => m.Timeout += null);
    _driver.SearchHint.Object.Text.ShouldContain("输入关键词");
    _driver.SearchResultTexts.Count.ShouldBe(0);

    _driver.SearchInput.Object.Text = "一定不存在的词";
    _driver.SearchDebounce.Raise(m => m.Timeout += null);
    _driver.SearchHint.Object.Text.ShouldContain("没有找到");
    _driver.SearchResultTexts.Count.ShouldBe(0);
  }

  /// <summary>点击搜索结果：打开对应页并关闭覆盖层。</summary>
  [Test]
  public void SearchResultClick_OpensPageAndClosesOverlay()
  {
    _driver.SearchInput.Object.Text = "常见问题";
    _driver.SearchDebounce.Raise(m => m.Timeout += null);

    _driver.SearchResults.Raise(m => m.ItemSelected += null, 0L);

    _driver.PageTitle.Object.Text.ShouldBe("常见问题");
    _driver.SearchOverlay.Object.Visible.ShouldBeFalse();
  }

  // ==================== 本页目录 ====================

  /// <summary>标题数 ≥ 阈值时目录按钮可见，弹出列表内容为当前页标题；少于阈值时隐藏。</summary>
  [Test]
  public void TocButton_VisibilityAndListContent()
  {
    _driver.TocBtn.Object.Visible.ShouldBeTrue();

    _driver.TocBtn.Raise(m => m.Pressed += null);

    _driver.TocTexts.ShouldBe(new List<string> { "甲", "乙", "丙", "丁" });
    _driver.TocPopup.Object.Visible.ShouldBeTrue();

    _driver.SelectPage("merge/overview");
    _driver.TocBtn.Object.Visible.ShouldBeFalse();
  }

  /// <summary>目录条目只展示标题、不携带跳转目标，点击不会换页。</summary>
  [Test]
  public void TocListClick_DoesNotNavigate()
  {
    bool raised = false;
    _driver.TocList.Object.ItemSelected += _ => raised = true;

    _driver.TocBtn.Raise(m => m.Pressed += null);
    string body = _driver.ContentView.Object.Text;

    _driver.TocList.Raise(m => m.ItemSelected += null, 1L);

    raised.ShouldBeTrue();
    _driver.PageTitle.Object.Text.ShouldBe("帮助中心");
    _driver.ContentView.Object.Text.ShouldBe(body);
    _driver.TocList.Verify(
      m => m.SetItemMetadata(It.IsAny<int>(), It.IsAny<Variant>()),
      Times.Never,
      "目录条目不存 pageId，因此无法触发跳转"
    );
  }

  // ==================== 正文链接 ====================

  /// <summary>help:// 链接打开目标页。</summary>
  [Test]
  public void MetaClicked_HelpScheme_OpensTargetPage()
  {
    _driver.ContentView.Raise(m => m.MetaClicked += null, Variant.From("help://run/faq"));

    _driver.PageTitle.Object.Text.ShouldBe("常见问题");
  }

  /// <summary>http(s):// 链接交给系统浏览器，不改变当前页。</summary>
  [Test]
  public void MetaClicked_HttpScheme_OpensSystemBrowser()
  {
    string? opened = null;
    _driver.Panel.ExternalLinkOpener = url =>
    {
      opened = url;
      return Error.Ok;
    };

    _driver.ContentView.Raise(m => m.MetaClicked += null, Variant.From("https://example.com/help"));

    opened.ShouldBe("https://example.com/help");
    _driver.PageTitle.Object.Text.ShouldBe("帮助中心");
  }

  /// <summary>未知协议与不存在的站内页都只记日志，不跳转、不崩溃。</summary>
  [Test]
  public void MetaClicked_UnknownSchemeAndMissingPage_AreIgnored()
  {
    _driver.ContentView.Raise(m => m.MetaClicked += null, Variant.From("ftp://example.com"));
    _driver.ContentView.Raise(m => m.MetaClicked += null, Variant.From("help://missing/page"));

    _driver.PageTitle.Object.Text.ShouldBe("帮助中心");
  }

  // ==================== 辅助 ====================

  /// <summary>创建一个受清理管理的驱动器。</summary>
  /// <param name="content">注入的帮助内容。</param>
  /// <returns>驱动器。</returns>
  private HelpPanelDriver NewDriver(IHelpContent content, DataManager? dataManager = null)
  {
    var driver = new HelpPanelDriver(content, TestScene, dataManager);
    _drivers.Add(driver);
    return driver;
  }

  /// <summary>构造测试页面。</summary>
  /// <param name="id">页面 id。</param>
  /// <param name="title">标题。</param>
  /// <param name="section">分区。</param>
  /// <param name="order">分区内序号。</param>
  /// <param name="headings">H2/H3 标题文本。</param>
  /// <returns>页面模型。</returns>
  private static HelpPage Page(
    string id,
    string title,
    string section,
    int order,
    params string[] headings
  ) => new(id, title, section, order, $"{title}摘要", Array.Empty<string>(), headings);

  /// <summary>构造一个按下状态的键盘事件。</summary>
  /// <param name="keycode">按键。</param>
  /// <param name="ctrl">是否按住 Ctrl。</param>
  /// <returns>输入事件。</returns>
  private static InputEventKey KeyEvent(Key keycode, bool ctrl = false) =>
    new()
    {
      Keycode = keycode,
      CtrlPressed = ctrl,
      Pressed = true,
    };

  /// <summary>可配置的帮助内容替身（测试注入用）。</summary>
  private sealed class FakeContent : IHelpContent
  {
    private readonly List<HelpPage> _pages;
    private readonly Dictionary<string, string> _bbcode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _plain = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hash = new(StringComparer.Ordinal);

    /// <summary>构造替身内容。</summary>
    /// <param name="pages">页面集合（顺序即导航顺序）。</param>
    public FakeContent(params HelpPage[] pages)
    {
      _pages = new List<HelpPage>(pages);
      foreach (var page in pages)
      {
        _bbcode[page.Id] =
          $"[b]{page.Title}[/b] [color={HelpPlaceholders.Wrap(HelpPlaceholders.Link)}]链接[/color] 正文 {page.Id}";
        _plain[page.Id] = $"{page.Title} {page.Summary} 正文 {page.Id}";
        _hash[page.Id] = "hash-" + page.Id;
      }
    }

    /// <inheritdoc />
    public IReadOnlyList<HelpPage> Pages => _pages;

    /// <summary>替换某页的 BBCode（用于验证重解析读的是当前内容）。</summary>
    /// <param name="id">页面 id。</param>
    /// <param name="bbcode">新的 BBCode。</param>
    public void SetBbcode(string id, string bbcode) => _bbcode[id] = bbcode;

    /// <inheritdoc />
    public HelpPage? GetPage(string id) =>
      _pages.Find(page => string.Equals(page.Id, id, StringComparison.Ordinal));

    /// <inheritdoc />
    public string GetBbcode(string id) =>
      _bbcode.TryGetValue(id, out string? value) ? value : string.Empty;

    /// <inheritdoc />
    public string GetPlainText(string id) =>
      _plain.TryGetValue(id, out string? value) ? value : string.Empty;

    /// <inheritdoc />
    public string GetHash(string id) =>
      _hash.TryGetValue(id, out string? value) ? value : string.Empty;
  }
}
