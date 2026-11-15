namespace AutoCMEX.UI.Help;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Help;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 帮助面板：内置帮助文档中心（导航树 + BBCode 正文 + 搜索覆盖层 + 本页目录）。
/// </summary>
/// <remarks>
/// <para>
/// 节点结构全部在 <c>HelpPanel.tscn</c>，代码只创建非 Node 的 <see cref="TreeItem"/> 与
/// <see cref="ItemList"/> 条目（与 <c>AliasPanel</c> 同一范式，规则 7/8）。
/// </para>
/// <para>
/// 会话内状态（当前页、历史栈、每页滚动位置）只放实例字段，不落盘；面板被
/// <c>MainWindow</c> 隐藏再显示时实例复用，因此切板块再切回会保持当前页。
/// </para>
/// <para>
/// 本面板没有 <c>Chickensoft.Sync</c> 绑定；<see cref="_ExitTree"/> 只做两件事：
/// 把导航栏宽度写回布局记忆、释放分割条接线路（规则 6/20）。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class HelpPanel : Control, IHelpPanel
{
  #region AutoConnect Nodes

  /// <summary>顶栏容器。</summary>
  [Node("%TopBar")]
  public IHBoxContainer TopBar { get; set; } = default!;

  /// <summary>返回上一页按钮。</summary>
  [Node("%BackBtn")]
  public IButton BackBtn { get; set; } = default!;

  /// <summary>当前页标题。</summary>
  [Node("%PageTitle")]
  public ILabel PageTitle { get; set; } = default!;

  /// <summary>「本页目录」按钮（H2 较多时才显示）。</summary>
  [Node("%TocBtn")]
  public IButton TocBtn { get; set; } = default!;

  /// <summary>打开搜索覆盖层的按钮。</summary>
  [Node("%SearchBtn")]
  public IButton SearchBtn { get; set; } = default!;

  /// <summary>导航树与正文的可拖拽两栏容器。</summary>
  [Node("%Body")]
  public ISplitContainer Body { get; set; } = default!;

  /// <summary>左侧导航树。</summary>
  [Node("%NavTree")]
  public ITree NavTree { get; set; } = default!;

  /// <summary>正文滚动容器（负责每页滚动位置记忆）。</summary>
  [Node("%ContentScroll")]
  public IScrollContainer ContentScroll { get; set; } = default!;

  /// <summary>BBCode 正文。</summary>
  [Node("%ContentView")]
  public IRichTextLabel ContentView { get; set; } = default!;

  /// <summary>底栏容器。</summary>
  [Node("%BottomBar")]
  public IHBoxContainer BottomBar { get; set; } = default!;

  /// <summary>上一页（按导航全局顺序）。</summary>
  [Node("%PrevBtn")]
  public IButton PrevBtn { get; set; } = default!;

  /// <summary>下一页（按导航全局顺序）。</summary>
  [Node("%NextBtn")]
  public IButton NextBtn { get; set; } = default!;

  /// <summary>覆盖正文区域的搜索覆盖层。</summary>
  [Node("%SearchOverlay")]
  public IPanelContainer SearchOverlay { get; set; } = default!;

  /// <summary>搜索输入框。</summary>
  [Node("%SearchInput")]
  public ILineEdit SearchInput { get; set; } = default!;

  /// <summary>搜索空态 / 无结果提示。</summary>
  [Node("%SearchHint")]
  public ILabel SearchHint { get; set; } = default!;

  /// <summary>搜索结果列表（每项元数据存 pageId）。</summary>
  [Node("%SearchResults")]
  public IItemList SearchResults { get; set; } = default!;

  /// <summary>搜索覆盖层里的可见返回按钮（与 Esc 同效）。</summary>
  [Node("%SearchCloseBtn")]
  public IButton SearchCloseBtn { get; set; } = default!;

  /// <summary>输入防抖计时器（150 ms，一次性）。</summary>
  [Node("%SearchDebounce")]
  public ITimer SearchDebounce { get; set; } = default!;

  /// <summary>搜索覆盖层的半透明遮罩：点击空白处关闭搜索（也保证点击不外泄到正文）。</summary>
  [Node("%SearchBackdrop")]
  public IControl SearchBackdrop { get; set; } = default!;

  /// <summary>「本页目录」弹出层。</summary>
  [Node("%TocPopup")]
  public IPopupPanel TocPopup { get; set; } = default!;

  /// <summary>本页目录条目列表（v1 只展示，不跳转）。</summary>
  [Node("%TocList")]
  public IItemList TocList { get; set; } = default!;

  #endregion

  /// <summary>「本页目录」按钮显示阈值：当前页 H2/H3 标题不少于该数时显示。</summary>
  /// <remarks>
  /// 生成模型只保留 H2/H3 文本、不区分级别，故按标题总数近似 UI 设计的「H2 ≥ 4」。
  /// </remarks>
  public const int TocHeadingThreshold = 4;

  /// <summary>搜索空态提示文案。</summary>
  private const string SearchIdleHint = "输入关键词搜索帮助内容，Esc 关闭";

  /// <summary>搜索无结果提示文案。</summary>
  private const string SearchEmptyHint = "没有找到匹配内容，换个更短的关键词试试";

  /// <summary>内容不可用时的兜底提示。</summary>
  private const string ContentUnavailableHint = "[i]帮助内容不可用，请重新构建项目。[/i]";

  /// <summary>
  /// 测试替换用的帮助内容；为 null 时在 <see cref="OnResolved"/> 用生成常量构建默认目录。
  /// </summary>
  /// <remarks>
  /// 生产路径不设置该属性；测试在节点通知前注入替身（规则 25）。
  /// </remarks>
  public IHelpContent? ContentOverride { get; set; }

  /// <summary>
  /// 外链打开方式；默认 <see cref="OS.ShellOpen(string)"/>，测试可替换以验证 http(s) 分支。
  /// </summary>
  public Func<string, Error> ExternalLinkOpener { get; set; } = OS.ShellOpen;

  /// <summary>数据管理器（布局记忆的唯一来源）。</summary>
  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  /// <summary>已解析的帮助内容；<see cref="OnResolved"/> 之前为 null。</summary>
  private IHelpContent? _content;

  /// <summary>业务数据入口；<see cref="OnResolved"/> 之后可用。</summary>
  private DataManager? _dm;

  /// <summary>导航栏分割条的布局接线路（左导航 / 正文两栏）。</summary>
  private readonly List<SplitLayoutLink> _splitLinks = new();

  /// <summary>检索服务（随内容一起构建）。</summary>
  private HelpSearchService? _search;

  /// <summary>当前页 id；空串表示尚未打开任何页。</summary>
  private string _currentPageId = string.Empty;

  /// <summary>返回历史栈（只存页面 id，不含当前页）。</summary>
  private readonly List<string> _history = new();

  /// <summary>每页滚动位置记忆（会话内，不落盘）。</summary>
  private readonly Dictionary<string, int> _scrollMemory = new(StringComparer.Ordinal);

  /// <summary>导航树里 pageId → 行，用于高亮当前页。</summary>
  private readonly Dictionary<string, TreeItem> _navItems = new(StringComparer.Ordinal);

  /// <summary>本次开页要恢复的滚动位置，由帧末 <see cref="ApplyPendingScroll"/> 应用。</summary>
  private int _pendingScroll;

  /// <summary>滚动恢复重试计数：<c>fit_content</c> 布局可能晚一帧才完成，被夹到 0 时需重试。</summary>
  private int _scrollRestoreAttempts;

  /// <summary>高亮当前页时抑制导航选择回调，避免自触发开页。</summary>
  private bool _suppressSelection;

  public override void _Notification(int what) => this.Notify(what);

  /// <summary>退出场景树：把导航栏宽度写回布局记忆（最终由 DataManager 落盘）。</summary>
  public override void _ExitTree()
  {
    foreach (var link in _splitLinks)
      link.Capture();
  }

  /// <summary>接信号并初始化控件参数（不依赖帮助内容）。</summary>
  public void OnReady()
  {
    NavTree.Columns = 1;
    NavTree.HideRoot = true;

    SearchDebounce.WaitTime = 0.15;
    SearchDebounce.OneShot = true;

    TocBtn.Visible = false;
    SearchOverlay.Visible = false;
    TocPopup.Visible = false;
    SearchHint.Text = SearchIdleHint;

    BackBtn.Pressed += OnBackPressed;
    PrevBtn.Pressed += OnPreviousPressed;
    NextBtn.Pressed += OnNextPressed;
    SearchBtn.Pressed += ShowSearch;
    TocBtn.Pressed += OnTocPressed;
    NavTree.ItemSelected += OnNavItemSelected;
    SearchInput.TextChanged += OnSearchTextChanged;
    SearchDebounce.Timeout += OnSearchTimeout;
    SearchResults.ItemSelected += OnSearchResultSelected;
    SearchCloseBtn.Pressed += CloseSearch;
    SearchBackdrop.GuiInput += OnSearchBackdropGuiInput;
    ContentView.MetaClicked += OnMetaClicked;
    // 正文颜色写入时就固化成十六进制了，主题切换不会自己更新：订阅自身主题变化重新解析。
    ThemeChanged += OnThemeChanged;
  }

  /// <summary>装配帮助内容、填充导航并打开默认页。</summary>
  public void OnResolved()
  {
    _dm = DataManager;
    _content = ContentOverride ?? HelpContentCatalog.FromGenerated();
    if (_content.Pages.Count == 0)
    {
      ShowUnavailableContent();
      return;
    }

    _search = new HelpSearchService(_content);
    EnsureSplitLayoutLink();
    BuildNavigation();

    var first = _content.GetPage(HelpContentCatalog.DefaultPageId) ?? _content.Pages[0];
    RenderPage(first, pushHistory: false);
  }

  /// <summary>
  /// 建立并应用导航栏分割条的布局接线路（规则 6：依赖解析完成后接线；重复解析只重新应用）。
  /// </summary>
  private void EnsureSplitLayoutLink()
  {
    if (_dm is null)
      return;

    if (_splitLinks.Count == 0)
      _splitLinks.Add(new SplitLayoutLink(Body, UiLayoutKeys.HelpNav, _dm));

    foreach (var link in _splitLinks)
      link.Apply();
  }

  /// <summary>主题变化：按当前主题重新解析当前页正文的颜色。</summary>
  /// <remarks>
  /// <para>
  /// 正文的颜色在写入 <see cref="ContentView"/> 时就固化成十六进制了，切主题不会自动更新，故重解析一次。
  /// 只换颜色：当前页、返回栈与每页滚动位置都不动。
  /// </para>
  /// <para>
  /// 各主题解析出的颜色长度一致（<c>#RRGGBBAA</c>），正文长度不变，
  /// 因此不需要（也不应）主动写 <c>ScrollVertical</c>，滚动位置自然保持。
  /// </para>
  /// <para>
  /// 内容尚未解析（<see cref="_content"/> 为 null）或还没打开任何页时直接返回，
  /// 面板未进树时同样安全。
  /// </para>
  /// </remarks>
  private void OnThemeChanged()
  {
    if (_content == null || _currentPageId.Length == 0)
    {
      return;
    }

    ContentView.Text = HelpThemeColors.Resolve(_content.GetBbcode(_currentPageId), this);
  }

  /// <summary>
  /// 打开指定页面：更新正文、标题、导航高亮、上一页/下一页文案与历史栈。
  /// </summary>
  /// <param name="id">页面 id；不存在或与当前页相同时不动作。</param>
  public void OpenPage(string id)
  {
    if (_content == null)
    {
      return;
    }

    var page = _content.GetPage(id);
    if (page == null)
    {
      GD.PushWarning($"帮助：打开不存在的页面 id：{id}");
      return;
    }

    if (string.Equals(_currentPageId, id, StringComparison.Ordinal))
    {
      return;
    }

    RenderPage(page, pushHistory: true);
  }

  /// <summary>帧末恢复滚动位置；公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约。</summary>
  /// <remarks>
  /// 正文高度由 <c>fit_content</c> 决定，赋值当帧布局尚未完成时 <c>ScrollVertical</c> 会被夹到 0，
  /// 因此按帧重试（最多 3 次）直到设置生效。
  /// </remarks>
  public void ApplyPendingScroll()
  {
    ContentScroll.ScrollVertical = _pendingScroll;
    if (
      !IsInsideTree()
      || ContentScroll.ScrollVertical == _pendingScroll
      || _scrollRestoreAttempts >= 3
    )
    {
      _scrollRestoreAttempts = 0;
      return;
    }

    _scrollRestoreAttempts++;
    CallDeferred(nameof(ApplyPendingScroll));
  }

  /// <summary>填充左侧导航树：分区行为不可选父行，页面行元数据存 pageId。</summary>
  private void BuildNavigation()
  {
    NavTree.Clear();
    _navItems.Clear();

    var root = NavTree.CreateItem();
    string? currentSection = null;
    TreeItem? sectionItem = null;

    foreach (var page in _content!.Pages)
    {
      if (!string.Equals(currentSection, page.Section, StringComparison.Ordinal))
      {
        currentSection = page.Section;
        sectionItem = NavTree.CreateItem(root);
        sectionItem.SetText(0, page.Section);
        sectionItem.SetSelectable(0, false);
      }

      var item = NavTree.CreateItem(sectionItem);
      item.SetText(0, page.Title);
      item.SetMetadata(0, page.Id);
      _navItems[page.Id] = item;
    }
  }

  /// <summary>渲染一页并同步所有派生 UI。</summary>
  /// <param name="page">目标页。</param>
  /// <param name="pushHistory">是否把当前页压入返回栈（默认页与返回操作不压栈）。</param>
  private void RenderPage(HelpPage page, bool pushHistory)
  {
    if (
      _currentPageId.Length > 0
      && !string.Equals(_currentPageId, page.Id, StringComparison.Ordinal)
    )
    {
      _scrollMemory[_currentPageId] = ContentScroll.ScrollVertical;
      if (pushHistory)
      {
        _history.Add(_currentPageId);
      }
    }

    _currentPageId = page.Id;
    ContentView.Text = HelpThemeColors.Resolve(_content!.GetBbcode(page.Id), this);
    PageTitle.Text = page.Title;
    HighlightNavigation(page.Id);
    UpdatePager(page.Id);
    TocBtn.Visible = page.Headings.Count >= TocHeadingThreshold;
    BackBtn.Disabled = _history.Count == 0;

    _pendingScroll = _scrollMemory.TryGetValue(page.Id, out int remembered) ? remembered : 0;
    if (IsInsideTree())
    {
      CallDeferred(nameof(ApplyPendingScroll));
    }
    else
    {
      ApplyPendingScroll();
    }
  }

  /// <summary>把导航树选中行切到指定页。</summary>
  /// <param name="id">页面 id。</param>
  private void HighlightNavigation(string id)
  {
    if (!_navItems.TryGetValue(id, out var item))
    {
      return;
    }

    _suppressSelection = true;
    item.Select(0);
    _suppressSelection = false;
  }

  /// <summary>按导航全局顺序更新上一页/下一页按钮的文案与可用状态。</summary>
  /// <param name="id">当前页 id。</param>
  private void UpdatePager(string id)
  {
    int index = IndexOfPage(id);
    HelpPage? previous = index > 0 ? _content!.Pages[index - 1] : null;
    HelpPage? next =
      index >= 0 && index + 1 < _content!.Pages.Count ? _content.Pages[index + 1] : null;

    PrevBtn.Text = previous == null ? "上一页" : $"← {previous.Title}";
    PrevBtn.Disabled = previous == null;
    NextBtn.Text = next == null ? "下一页" : $"{next.Title} →";
    NextBtn.Disabled = next == null;
  }

  /// <summary>取页面在导航全局顺序里的下标。</summary>
  /// <param name="id">页面 id。</param>
  /// <returns>下标；不存在返回 -1。</returns>
  private int IndexOfPage(string id)
  {
    if (_content == null)
    {
      return -1;
    }

    for (int i = 0; i < _content.Pages.Count; i++)
    {
      if (string.Equals(_content.Pages[i].Id, id, StringComparison.Ordinal))
      {
        return i;
      }
    }
    return -1;
  }

  /// <summary>导航树选择变化：打开所选页面。</summary>
  private void OnNavItemSelected()
  {
    if (_suppressSelection)
    {
      return;
    }

    var selected = NavTree.GetNextSelected(null!);
    if (selected == null)
    {
      return;
    }

    string id = selected.GetMetadata(0).AsString();
    if (id.Length > 0)
    {
      OpenPage(id);
    }
  }

  /// <summary>返回上一页；历史栈为空时不动作。</summary>
  private void OnBackPressed()
  {
    if (_history.Count == 0)
    {
      return;
    }

    string id = _history[^1];
    _history.RemoveAt(_history.Count - 1);
    var page = _content?.GetPage(id);
    if (page == null)
    {
      return;
    }

    RenderPage(page, pushHistory: false);
  }

  /// <summary>按导航全局顺序打开上一页。</summary>
  private void OnPreviousPressed()
  {
    int index = IndexOfPage(_currentPageId);
    if (index > 0)
    {
      OpenPage(_content!.Pages[index - 1].Id);
    }
  }

  /// <summary>按导航全局顺序打开下一页。</summary>
  private void OnNextPressed()
  {
    int index = IndexOfPage(_currentPageId);
    if (index >= 0 && index + 1 < _content!.Pages.Count)
    {
      OpenPage(_content.Pages[index + 1].Id);
    }
  }

  /// <summary>显示搜索覆盖层并聚焦输入框；面板不可见时不响应。</summary>
  private void ShowSearch()
  {
    if (!Visible)
    {
      return;
    }

    SearchOverlay.Visible = true;
    // 覆盖层打开期间屏蔽正文子树的鼠标：输入拾取按树序而不是 z_index，用属性方案与树序无关。
    Body.MouseBehaviorRecursive = Control.MouseBehaviorRecursiveEnum.Disabled;
    if (string.IsNullOrWhiteSpace(SearchInput.Text))
    {
      SearchResults.Clear();
      SearchHint.Text = SearchIdleHint;
      SearchHint.Visible = true;
    }
    else
    {
      RunSearch(SearchInput.Text);
    }
    SearchInput.GrabFocus();
  }

  /// <summary>点击遮罩空白处：关闭搜索覆盖层。</summary>
  /// <param name="event">遮罩收到的 GUI 输入事件。</param>
  private void OnSearchBackdropGuiInput(InputEvent @event)
  {
    if (@event is InputEventMouseButton { Pressed: true })
      CloseSearch();
  }

  /// <summary>隐藏搜索覆盖层并停止防抖计时。</summary>
  private void CloseSearch()
  {
    SearchOverlay.Visible = false;
    Body.MouseBehaviorRecursive = Control.MouseBehaviorRecursiveEnum.Inherited;
    SearchDebounce.Stop();
  }

  /// <summary>输入变化：只重启 150 ms 防抖计时，超时才真正检索。</summary>
  /// <param name="text">输入框当前文本。</param>
  private void OnSearchTextChanged(string text)
  {
    SearchDebounce.Stop();
    if (string.IsNullOrWhiteSpace(text))
    {
      SearchResults.Clear();
      SearchHint.Text = SearchIdleHint;
      SearchHint.Visible = true;
      return;
    }

    SearchDebounce.Start();
  }

  /// <summary>防抖超时：执行检索并填充结果列表。</summary>
  private void OnSearchTimeout() => RunSearch(SearchInput.Text);

  /// <summary>检索并填充结果列表；空输入/无结果走提示文案。</summary>
  /// <param name="query">查询词。</param>
  private void RunSearch(string query)
  {
    SearchResults.Clear();
    if (string.IsNullOrWhiteSpace(query) || _search == null)
    {
      SearchHint.Text = SearchIdleHint;
      SearchHint.Visible = true;
      return;
    }

    var hits = _search.Search(query);
    if (hits.Count == 0)
    {
      SearchHint.Text = SearchEmptyHint;
      SearchHint.Visible = true;
      return;
    }

    SearchHint.Text = string.Empty;
    SearchHint.Visible = false;
    for (int i = 0; i < hits.Count; i++)
    {
      var hit = hits[i];
      string itemText = string.IsNullOrEmpty(hit.PlainSnippet)
        ? hit.Title
        : $"{hit.Title}  ·  {hit.PlainSnippet}";
      SearchResults.AddItem(itemText, null, true);
      SearchResults.SetItemMetadata(i, hit.PageId);
    }
  }

  /// <summary>点击搜索结果：开页并关闭覆盖层。</summary>
  /// <param name="index">结果下标。</param>
  private void OnSearchResultSelected(long index)
  {
    string id = SearchResults.GetItemMetadata((int)index).AsString();
    if (id.Length == 0)
    {
      return;
    }

    CloseSearch();
    OpenPage(id);
  }

  /// <summary>填充并弹出「本页目录」。</summary>
  private void OnTocPressed()
  {
    var page = _content?.GetPage(_currentPageId);
    if (page == null)
    {
      return;
    }

    TocList.Clear();
    foreach (var heading in page.Headings)
    {
      TocList.AddItem(heading, null, false);
    }
    TocPopup.PopupCentered();
    TocPopup.Visible = true;
  }

  /// <summary>
  /// 正文链接分派：<c>help://</c> 开页、<c>http(s)://</c> 交系统浏览器、其它协议记日志忽略。
  /// </summary>
  /// <param name="meta">RichTextLabel 传入的链接地址。</param>
  private void OnMetaClicked(Variant meta)
  {
    string target = meta.AsString();
    if (target.StartsWith("help://", StringComparison.Ordinal))
    {
      string id = target["help://".Length..];
      if (_content?.GetPage(id) == null)
      {
        GD.PushWarning($"帮助：站内链接目标不存在：{id}");
        return;
      }
      OpenPage(id);
      return;
    }

    if (
      target.StartsWith("http://", StringComparison.Ordinal)
      || target.StartsWith("https://", StringComparison.Ordinal)
    )
    {
      Error error = ExternalLinkOpener(target);
      if (error != Error.Ok)
      {
        GD.PushWarning($"帮助：外链打开失败（{error}）：{target}");
      }
      return;
    }

    GD.PushWarning($"帮助：忽略不支持的链接协议：{target}");
  }

  /// <summary>内容为空时的兜底界面（理论上不该发生）。</summary>
  private void ShowUnavailableContent()
  {
    PageTitle.Text = "帮助";
    ContentView.Text = ContentUnavailableHint;
    PrevBtn.Disabled = true;
    NextBtn.Disabled = true;
    BackBtn.Disabled = true;
    TocBtn.Visible = false;
    GD.PushWarning("帮助：生成内容为空，面板显示不可用提示");
  }

  /// <summary>全局快捷键：面板可见时 Ctrl+K 打开搜索、Esc 关闭搜索。</summary>
  /// <param name="event">输入事件。</param>
  public override void _UnhandledKeyInput(InputEvent @event)
  {
    if (!Visible || @event is not InputEventKey key || !key.Pressed || key.Echo)
    {
      return;
    }

    if (key.Keycode == Key.K && key.CtrlPressed)
    {
      ShowSearch();
      GetViewport()?.SetInputAsHandled();
    }
    else if (key.Keycode == Key.Escape && SearchOverlay.Visible)
    {
      CloseSearch();
      GetViewport()?.SetInputAsHandled();
    }
  }
}
