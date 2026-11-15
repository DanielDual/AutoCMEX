namespace AutoCMEX.Test.Drivers;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Help;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI;
using AutoCMEX.UI.Help;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Godot;
using Moq;

/// <summary>
/// <see cref="HelpPanel"/> 的测试驱动器：用 FakeNodeTree 注入全部控件替身，
/// 并把 <see cref="ITree"/> 的树项操作转发到真实 <see cref="Tree"/>（规则 29）。
/// </summary>
/// <remarks>
/// 真实树必须挂在测试场景下且 <c>Columns = 1</c>，与面板 <c>OnReady</c> 设置一致，
/// 否则 <c>TreeItem.SetEditable</c> / <c>Select</c> 会越界产生 ERROR 日志。
/// 搜索与目录的 <see cref="IItemList"/> 替身自行维护文本与元数据，供面板回读。
/// </remarks>
public sealed class HelpPanelDriver : IDisposable
{
  public HelpPanel Panel { get; }
  public Mock<IHBoxContainer> TopBar { get; }
  public Mock<IButton> BackBtn { get; }
  public Mock<ILabel> PageTitle { get; }
  public Mock<IButton> TocBtn { get; }
  public Mock<IButton> SearchBtn { get; }
  public Mock<ISplitContainer> Body { get; }
  public Mock<ITree> NavTree { get; }
  public Mock<IScrollContainer> ContentScroll { get; }
  public Mock<IRichTextLabel> ContentView { get; }
  public Mock<IHBoxContainer> BottomBar { get; }
  public Mock<IButton> PrevBtn { get; }
  public Mock<IButton> NextBtn { get; }
  public Mock<IPanelContainer> SearchOverlay { get; }
  public Mock<ILineEdit> SearchInput { get; }
  public Mock<ILabel> SearchHint { get; }
  public Mock<IItemList> SearchResults { get; }
  public Mock<IButton> SearchCloseBtn { get; }
  public Mock<IControl> SearchBackdrop { get; }
  public Mock<ITimer> SearchDebounce { get; }
  public Mock<IPopupPanel> TocPopup { get; }
  public Mock<IItemList> TocList { get; }

  /// <summary>驱动器的数据管理器（布局记忆）；测试可预置记录后重建驱动器验证应用。</summary>
  public DataManager DataManager { get; }

  private readonly Tree _tree;
  private readonly bool _ownsDataManager;
  private readonly List<string> _searchTexts = new();
  private readonly List<string> _searchIds = new();
  private readonly List<string> _tocTexts = new();

  public HelpPanelDriver(
    IHelpContent? content = null,
    Node? testScene = null,
    DataManager? dataManager = null
  )
  {
    Panel = new HelpPanel();
    (Panel as IAutoInit).IsTesting = true;

    _ownsDataManager = dataManager == null;
    DataManager =
      dataManager
      ?? new DataManager(
        System.IO.Path.Combine(
          System.IO.Path.GetTempPath(),
          "AutoCMEX_HelpDriver_" + Guid.NewGuid().ToString("N")
        ),
        new AesEncryptor("test-key")
      );
    if (_ownsDataManager)
      DataManager.LoadAll();

    _tree = new Tree { Columns = 1 };
    if (testScene != null)
    {
      testScene.AddChild(_tree);
    }

    TopBar = new Mock<IHBoxContainer>();
    BackBtn = new Mock<IButton>();
    PageTitle = new Mock<ILabel>();
    TocBtn = new Mock<IButton>();
    SearchBtn = new Mock<IButton>();
    Body = new Mock<ISplitContainer>();
    Body.SetupProperty(m => m.SplitOffset);
    Body.SetupProperty(m => m.Collapsed);
    Body.SetupProperty(m => m.MouseBehaviorRecursive);
    NavTree = new Mock<ITree>();
    ContentScroll = new Mock<IScrollContainer>();
    ContentView = new Mock<IRichTextLabel>();
    BottomBar = new Mock<IHBoxContainer>();
    PrevBtn = new Mock<IButton>();
    NextBtn = new Mock<IButton>();
    SearchOverlay = new Mock<IPanelContainer>();
    SearchInput = new Mock<ILineEdit>();
    SearchHint = new Mock<ILabel>();
    SearchResults = new Mock<IItemList>();
    SearchCloseBtn = new Mock<IButton>();
    SearchBackdrop = new Mock<IControl>();
    SearchDebounce = new Mock<ITimer>();
    TocPopup = new Mock<IPopupPanel>();
    TocList = new Mock<IItemList>();

    SetupTree();
    SetupButtons();
    SetupLabels();
    SetupSearchControls();
    SetupTocControls();

    Panel.FakeNodeTree(
      new()
      {
        ["%TopBar"] = TopBar.Object,
        ["%BackBtn"] = BackBtn.Object,
        ["%PageTitle"] = PageTitle.Object,
        ["%TocBtn"] = TocBtn.Object,
        ["%SearchBtn"] = SearchBtn.Object,
        ["%Body"] = Body.Object,
        ["%NavTree"] = NavTree.Object,
        ["%ContentScroll"] = ContentScroll.Object,
        ["%ContentView"] = ContentView.Object,
        ["%BottomBar"] = BottomBar.Object,
        ["%PrevBtn"] = PrevBtn.Object,
        ["%NextBtn"] = NextBtn.Object,
        ["%SearchOverlay"] = SearchOverlay.Object,
        ["%SearchInput"] = SearchInput.Object,
        ["%SearchHint"] = SearchHint.Object,
        ["%SearchResults"] = SearchResults.Object,
        ["%SearchCloseBtn"] = SearchCloseBtn.Object,
        ["%SearchBackdrop"] = SearchBackdrop.Object,
        ["%SearchDebounce"] = SearchDebounce.Object,
        ["%TocPopup"] = TocPopup.Object,
        ["%TocList"] = TocList.Object,
      }
    );

    Panel.FakeDependency<DataManager>(DataManager);

    if (content != null)
    {
      Panel.ContentOverride = content;
    }

    Panel._Notification((int)Node.NotificationEnterTree);
    Panel._Notification((int)Node.NotificationReady);
  }

  /// <summary>模拟用户点击导航树里的某一页（先选中真实行，再触发选择信号）。</summary>
  /// <param name="pageId">页面 id。</param>
  public void SelectPage(string pageId)
  {
    var item = FindNavItem(NavTree.Object.GetRoot(), pageId);
    if (item == null)
    {
      throw new InvalidOperationException($"导航树中找不到页面：{pageId}");
    }

    item.Select(0);
    NavTree.Raise(m => m.ItemSelected += null);
  }

  /// <summary>模拟主题切换：按真实信号路径发出 <see cref="Control.ThemeChanged"/>。</summary>
  public void EmitThemeChanged() => Panel.EmitSignal(Control.SignalName.ThemeChanged);

  /// <summary>搜索结果条目的纯文本（按添加顺序）。</summary>
  public IReadOnlyList<string> SearchResultTexts => _searchTexts;

  /// <summary>本页目录条目的纯文本（按添加顺序）。</summary>
  public IReadOnlyList<string> TocTexts => _tocTexts;

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
    {
      Panel.QueueFree();
    }
    if (!_tree.IsQueuedForDeletion())
    {
      _tree.QueueFree();
    }
    if (_ownsDataManager)
    {
      DataManager.Dispose();
    }
  }

  /// <summary>递归查找元数据等于 pageId 的导航行。</summary>
  /// <param name="parent">起始父行。</param>
  /// <param name="pageId">页面 id。</param>
  /// <returns>行或 null。</returns>
  private static TreeItem? FindNavItem(TreeItem? parent, string pageId)
  {
    if (parent == null)
    {
      return null;
    }

    for (int i = 0; i < parent.GetChildCount(); i++)
    {
      var child = parent.GetChild(i);
      if (string.Equals(child.GetMetadata(0).AsString(), pageId, StringComparison.Ordinal))
      {
        return child;
      }

      var nested = FindNavItem(child, pageId);
      if (nested != null)
      {
        return nested;
      }
    }
    return null;
  }

  /// <summary>把导航树的树项操作转发到真实树。</summary>
  private void SetupTree()
  {
    NavTree
      .Setup(m => m.CreateItem(It.IsAny<TreeItem>(), It.IsAny<int>()))
      .Returns((TreeItem parent, int index) => _tree.CreateItem(parent, index));
    NavTree.Setup(m => m.GetRoot()).Returns(() => _tree.GetRoot());
    NavTree.Setup(m => m.Clear()).Callback(() => _tree.Clear());
    NavTree
      .SetupSet(m => m.Columns = It.IsAny<int>())
      .Callback((int columns) => _tree.Columns = columns);
    NavTree
      .SetupSet(m => m.HideRoot = It.IsAny<bool>())
      .Callback((bool hide) => _tree.HideRoot = hide);
    NavTree
      .Setup(m => m.GetNextSelected(It.IsAny<TreeItem>()))
      .Returns((TreeItem from) => _tree.GetNextSelected(from));
  }

  /// <summary>按钮：记录文案与可用状态。</summary>
  private void SetupButtons()
  {
    SetupButton(BackBtn);
    SetupButton(TocBtn);
    SetupButton(SearchBtn);
    SetupButton(PrevBtn);
    SetupButton(NextBtn);
  }

  /// <summary>单个按钮的公共属性。</summary>
  /// <param name="button">按钮替身。</param>
  private static void SetupButton(Mock<IButton> button)
  {
    button.SetupProperty(m => m.Text);
    button.SetupProperty(m => m.Disabled);
    button.SetupProperty(m => m.Visible);
  }

  /// <summary>标签：记录文案与可见性。</summary>
  private void SetupLabels()
  {
    PageTitle.SetupProperty(m => m.Text);
    SearchHint.SetupProperty(m => m.Text);
    SearchHint.SetupProperty(m => m.Visible);
  }

  /// <summary>搜索相关控件：覆盖层、输入框、结果列表与防抖计时器。</summary>
  private void SetupSearchControls()
  {
    SearchOverlay.SetupProperty(m => m.Visible);
    SearchInput.SetupProperty(m => m.Text);
    SearchInput.SetupProperty(m => m.PlaceholderText);
    ContentScroll.SetupProperty(m => m.ScrollVertical);
    ContentView.SetupProperty(m => m.Text);

    SearchDebounce.SetupProperty(m => m.WaitTime);
    SearchDebounce.SetupProperty(m => m.OneShot);

    SetupItemList(SearchResults, _searchTexts, _searchIds);
  }

  /// <summary>本页目录相关控件。</summary>
  private void SetupTocControls()
  {
    TocPopup.SetupProperty(m => m.Visible);
    SetupItemList(TocList, _tocTexts, null);
  }

  /// <summary>让 <see cref="IItemList"/> 替身维护文本与元数据，供面板回读。</summary>
  /// <param name="list">列表替身。</param>
  /// <param name="texts">文本存储。</param>
  /// <param name="ids">元数据存储；为 null 表示该列表不使用元数据。</param>
  private static void SetupItemList(Mock<IItemList> list, List<string> texts, List<string>? ids)
  {
    list.Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<Texture2D>(), It.IsAny<bool>()))
      .Callback((string text, Texture2D icon, bool selectable) => texts.Add(text));
    list.Setup(m => m.Clear())
      .Callback(() =>
      {
        texts.Clear();
        ids?.Clear();
      });
    list.SetupGet(m => m.ItemCount).Returns(() => texts.Count);

    if (ids == null)
    {
      return;
    }

    list.Setup(m => m.SetItemMetadata(It.IsAny<int>(), It.IsAny<Variant>()))
      .Callback(
        (int index, Variant value) =>
        {
          while (ids.Count <= index)
          {
            ids.Add(string.Empty);
          }
          ids[index] = value.AsString();
        }
      );
    list.Setup(m => m.GetItemMetadata(It.IsAny<int>()))
      .Returns((int index) => index >= 0 && index < ids.Count ? Variant.From(ids[index]) : default);
  }
}
