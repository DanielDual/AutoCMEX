namespace AutoCMEX.UI.Info;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 符卡 GIF 集栏：集切换、导入（文件夹/压缩包）、卡片式纵向列表（懒加载播放）与本栏发布。
/// </summary>
/// <remarks>
/// <para>
/// 卡片列表由清单驱动：每条 = <c>序号. 符卡名</c> + GIF 动图。符卡名只取清单，不与猜测模块的
/// Boss 做任何对齐（集对应"导入的工程"，与 Boss 无关）。
/// </para>
/// <para>
/// 内存策略：GIF 单张可达 10MB 量级，因此只解码当前可见的 1-3 张（见
/// <see cref="GifPlaybackCoordinator"/>），滑出视野立即释放帧缓存；发布时从磁盘直接取原图。
/// </para>
/// <para>
/// 录制按钮本期占位禁用（场景中已置 <c>disabled</c>），后续版本提供录制产出同构清单后可直接复用本栏。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class GifSetPanel : VBoxContainer
{
  /// <summary>卡片播放区的占位高度（像素）；实际画面按比例缩放到该区域内。</summary>
  private const int CardPlayerMinHeight = 220;

  /// <summary>卡片最小宽度（像素），避免列过窄导致标题折行。</summary>
  private const int CardMinWidth = 260;

  [Node("%SetSelector")]
  public IOptionButton SetSelector { get; set; } = default!;

  [Node("%ImportFolderButton")]
  public IButton ImportFolderButton { get; set; } = default!;

  [Node("%ImportZipButton")]
  public IButton ImportZipButton { get; set; } = default!;

  [Node("%RecordButton")]
  public IButton RecordButton { get; set; } = default!;

  [Node("%SetInfoLabel")]
  public ILabel SetInfoLabel { get; set; } = default!;

  [Node("%CardScroll")]
  public IScrollContainer CardScroll { get; set; } = default!;

  [Node("%CardList")]
  public IVBoxContainer CardList { get; set; } = default!;

  [Node("%PublishButton")]
  public IButton PublishButton { get; set; } = default!;

  private AutoList<GifSetRecord>.Binding? _setsBinding;
  private AutoValue<string>.Binding? _activeSetIdBinding;
  private GifSetService? _gifSets;
  private DataManager? _dataManager;
  private InfoConfig? _config;
  private ColumnPublishHandler? _publish;

  private readonly GifPlaybackCoordinator _coordinator = new();
  private readonly List<CardRef> _cards = new();

  private FileDialog? _folderDialog;
  private FileDialog? _zipDialog;
  private AcceptDialog? _messageDialog;

  /// <summary>一次性状态提示（如导入成功）；由下一次 <see cref="RebuildCards"/> 渲染后清空。</summary>
  private string? _pendingStatus;

  private bool _isRebuilding;
  private bool _isPublishing;

  private readonly record struct CardRef(Control Card, GifFramePlayer Player, string GifPath);

  /// <inheritdoc/>
  public override void _Notification(int what) => this.Notify(what);

  /// <inheritdoc/>
  public override void _ExitTree()
  {
    _setsBinding?.Dispose();
    _setsBinding = null;

    _activeSetIdBinding?.Dispose();
    _activeSetIdBinding = null;

    ReleaseCards();
  }

  /// <summary>AutoInject 节点注入完成（依赖注入前，无需额外动作）。</summary>
  public void OnReady() { }

  /// <summary>AutoInject 依赖解析完成（本栏无依赖，无需额外动作）。</summary>
  public void OnResolved() { }

  /// <summary>
  /// 装配本栏；由信息面板在本节点加入场景树后调用。
  /// </summary>
  /// <param name="dataManager">数据管理器（自动保存入口）。</param>
  /// <param name="gifSets">GIF 集服务。</param>
  /// <param name="publish">本栏发布回调。</param>
  public void Setup(DataManager dataManager, GifSetService gifSets, ColumnPublishHandler publish)
  {
    _dataManager = dataManager;
    _config = dataManager.InfoConfig;
    _gifSets = gifSets;
    _publish = publish;

    // 录制本期不实现：以代码再确认一次禁用态，防止场景被误改后按钮"看似可用"
    RecordButton.Disabled = true;
    RecordButton.TooltipText = "本期不实现录制，后续版本提供";

    ImportFolderButton.Pressed += OnImportFolderPressed;
    ImportZipButton.Pressed += OnImportZipPressed;
    SetSelector.ItemSelected += OnSetSelected;
    PublishButton.Pressed += OnPublishPressed;
    CardScroll.GetVScrollBar().ValueChanged += OnCardScrollValueChanged;

    _setsBinding = _config.GifSets.Bind().OnModify(OnSetsChanged);
    _activeSetIdBinding = _config.ActiveGifSetId.Bind().OnValue(OnActiveSetIdChanged);

    RebuildCards();
  }

  /// <summary>按当前集重建卡片列表与集下拉（发布与预览同源，均为清单）。</summary>
  public void RebuildCards()
  {
    if (_gifSets is null || _config is null || _isRebuilding)
      return;

    _isRebuilding = true;
    try
    {
      ReleaseCards();
      SyncSetSelector();

      var set = _gifSets.GetActiveSet();
      if (set is null)
      {
        SetInfoLabel.Text = ComposeStatusText("(未导入任何集)");
        UpdatePublishButtonState();
        return;
      }

      var entries = set.Manifest.Entries ?? new List<GifSetEntry>();
      foreach (var entry in entries)
      {
        if (entry is null)
          continue;

        BuildCard(set, entry);
      }

      SetInfoLabel.Text = ComposeStatusText(BuildSetSummary(set, entries.Count));
      UpdatePublishButtonState();

      // 卡片尺寸要等一次布局才有效，延后一帧再决定哪些卡片驻留解码
      CallDeferred(nameof(RefreshPlayback));
    }
    finally
    {
      _isRebuilding = false;
    }
  }

  /// <summary>按视口范围刷新卡片的解码驻留状态（滚动与布局变化时调用）。</summary>
  public void RefreshPlayback()
  {
    if (_cards.Count == 0)
      return;

    var viewTop = CardScroll.ScrollVertical;
    var viewBottom = viewTop + CardScroll.Size.Y;

    var bindings = _cards
      .Select(card => new GifCardBinding
      {
        Player = card.Player,
        GifPath = card.GifPath,
        ContentRect = new Rect2(card.Card.Position, card.Card.Size),
      })
      .ToList();

    _coordinator.Update(bindings, viewTop, viewBottom);
  }

  private void BuildCard(GifSetRecord set, GifSetEntry entry)
  {
    var gifPath = _gifSets!.GetEntryPath(set, entry);

    var body = new VBoxContainer();
    body.AddThemeConstantOverride("separation", 4);

    var title = new Label
    {
      Text = $"{entry.Index}. {entry.SpellCardName}",
      AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };
    body.AddChild(title);

    var subTitle = new Label
    {
      Text = $"{entry.FileName}　{entry.Width}×{entry.Height}",
      AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };
    subTitle.AddThemeFontSizeOverride("font_size", 12);
    subTitle.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.68f));
    body.AddChild(subTitle);

    var player = new GifFramePlayer
    {
      CustomMinimumSize = new Vector2(CardMinWidth, CardPlayerMinHeight),
      ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
      StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
      // 播放区不拦截鼠标，滚轮手势继续交给外层 ScrollContainer
      MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    player.LoadFailed += reason => OnCardLoadFailed(entry, player, reason);
    body.AddChild(player);

    var card = new PanelContainer { CustomMinimumSize = new Vector2(CardMinWidth, 0) };
    card.AddChild(body);
    CardList.AddChild(card);

    _cards.Add(new CardRef(card, player, gifPath));
  }

  private static void OnCardLoadFailed(GifSetEntry entry, GifFramePlayer player, string reason)
  {
    var message = $"GIF 加载失败（{entry.Index}. {entry.SpellCardName}）：{reason}";
    GD.PushWarning($"GifSetPanel: {message}");

    if (!GodotObject.IsInstanceValid(player))
      return;

    // 失败原因挂到卡片上：界面悬停可见，日志里保留完整上下文，不静默丢错
    player.TooltipText = message;
    player.Texture = null;
  }

  private void ReleaseCards()
  {
    foreach (var card in _cards)
      card.Player?.Release();

    _cards.Clear();

    foreach (var child in CardList.GetChildren())
    {
      CardList.RemoveChild(child);
      child.QueueFree();
    }
  }

  private void SyncSetSelector()
  {
    var sets = _gifSets!.Sets;

    SetSelector.Clear();
    for (var i = 0; i < sets.Count; i++)
      SetSelector.AddItem(sets[i].SetName.Value, i);

    var activeId = _config!.ActiveGifSetId.Value;
    var activeIndex = -1;
    for (var i = 0; i < sets.Count; i++)
    {
      if (string.Equals(sets[i].Id.Value, activeId, StringComparison.Ordinal))
      {
        activeIndex = i;
        break;
      }
    }

    if (activeIndex < 0 && sets.Count > 0)
      activeIndex = 0;

    if (activeIndex >= 0)
      SetSelector.Select(activeIndex);

    SetSelector.Disabled = sets.Count == 0;
  }

  private string BuildSetSummary(GifSetRecord set, int entryCount) =>
    $"集：{set.SetName.Value}　|　{entryCount} 张　|　导入于 {set.ImportedAt.Value}\n{set.RootPath.Value}";

  /// <summary>
  /// 合成状态文本：把最近一次操作提示（如导入成功）叠加在集摘要之上。
  /// </summary>
  /// <remarks>
  /// 提示不在渲染时清空：集列表变化与当前集变化各自会触发一次重建，
  /// 若"渲染即消费"则第二次重建会把提示抹掉，用户看不到。改由下一次用户动作清除。
  /// </remarks>
  /// <param name="summary">集摘要（或"未导入任何集"占位）。</param>
  /// <returns>用于状态标签的完整文本。</returns>
  private string ComposeStatusText(string summary) =>
    string.IsNullOrEmpty(_pendingStatus) ? summary : $"{_pendingStatus}\n{summary}";

  private void OnSetsChanged() => CallDeferred(nameof(RebuildCards));

  private void OnActiveSetIdChanged(string? setId) => CallDeferred(nameof(RebuildCards));

  private void OnSetSelected(long index)
  {
    if (_isRebuilding || _gifSets is null)
      return;

    var sets = _gifSets.Sets;
    if (index < 0 || index >= sets.Count)
      return;

    var targetId = sets[(int)index].Id.Value;
    if (string.Equals(_config?.ActiveGifSetId.Value, targetId, StringComparison.Ordinal))
      return;

    // 用户已经切到别的集，上一次导入的成功提示到此为止
    _pendingStatus = null;
    _gifSets.SetActiveSet(targetId);
  }

  private void OnCardScrollValueChanged(double value) => RefreshPlayback();

  private void OnImportFolderPressed()
  {
    _folderDialog ??= CreateFileDialog(
      "选择 GIF 集文件夹（需含 manifest.json）",
      FileDialog.FileModeEnum.OpenDir,
      Array.Empty<string>()
    );
    _folderDialog.DirSelected += OnImportFolderSelected;
    _folderDialog.PopupCentered(new Vector2I(760, 520));
  }

  private void OnImportZipPressed()
  {
    _zipDialog ??= CreateFileDialog(
      "选择 GIF 集压缩包",
      FileDialog.FileModeEnum.OpenFile,
      new[] { "*.zip" }
    );
    _zipDialog.FileSelected += OnImportZipSelected;
    _zipDialog.PopupCentered(new Vector2I(760, 520));
  }

  private FileDialog CreateFileDialog(
    string title,
    FileDialog.FileModeEnum mode,
    IReadOnlyList<string> filters
  )
  {
    var dialog = new FileDialog
    {
      Title = title,
      FileMode = mode,
      Access = FileDialog.AccessEnum.Filesystem,
      Unresizable = false,
      UseNativeDialog = false,
    };

    foreach (var filter in filters)
      dialog.AddFilter(filter, "压缩包");

    AddChild(dialog);
    return dialog;
  }

  private void OnImportFolderSelected(string path) => Import(() => _gifSets!.ImportFolder(path));

  private void OnImportZipSelected(string path) => Import(() => _gifSets!.ImportZip(path));

  private void Import(Func<GifSetImportResult> import)
  {
    if (_gifSets is null)
      return;

    GifSetImportResult result;
    try
    {
      result = import();
    }
    catch (Exception ex)
    {
      // 服务层已分类报错；这里是兜底，避免解压期的 IO 异常直接冒到 Godot 主循环
      result = GifSetImportResult.Error($"导入失败：{ex.Message}", new[] { ex.ToString() });
    }

    if (result.IsSuccess && result.Set is not null)
    {
      _dataManager?.TriggerAutoSave();

      // 切集/集列表变化会触发延后一帧的 RebuildCards（它写集摘要）；把成功提示先记下来，
      // 由那次重建一并渲染，否则提示会被摘要直接覆盖、用户看不到
      _pendingStatus = $"导入成功：{result.Set.SetName.Value}（{result.Set.EntryCount.Value} 张）";
      SetInfoLabel.Text = _pendingStatus;

      _gifSets.SetActiveSet(result.Set.Id.Value);
      return;
    }

    // 失败结果覆盖上一次的成功提示，避免两条状态并排留下误导
    _pendingStatus = null;
    SetInfoLabel.Text = result.ToDisplayText();
    GD.PushWarning($"GifSetPanel: 导入失败：{result.ToDisplayText()}");
    ShowMessage("导入失败", result.ToDisplayText());
  }

  private void ShowMessage(string title, string text)
  {
    if (_messageDialog is null || !GodotObject.IsInstanceValid(_messageDialog))
    {
      _messageDialog = new AcceptDialog();
      AddChild(_messageDialog);
    }

    _messageDialog.Title = title;
    _messageDialog.DialogText = text;
    _messageDialog.PopupCentered(new Vector2I(640, 420));
  }

  private async void OnPublishPressed()
  {
    if (_publish is null || _isPublishing)
      return;

    _isPublishing = true;
    UpdatePublishButtonState();
    try
    {
      await _publish();
    }
    finally
    {
      _isPublishing = false;
      if (IsInsideTree())
        UpdatePublishButtonState();
    }
  }

  private void UpdatePublishButtonState()
  {
    if (PublishButton is null)
      return;

    var set = _gifSets?.GetActiveSet();
    var hasEntries = set?.Manifest?.Entries is { Count: > 0 };
    PublishButton.Disabled = _isPublishing || !hasEntries;
  }
}
