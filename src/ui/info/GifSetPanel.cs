namespace AutoCMEX.UI.Info;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Recording;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI;
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
/// 内存策略：GIF 单张可达 10MB 量级，因此只解码与视口有交集的卡片（哪怕只露出一部分，
/// 见 <see cref="GifPlaybackCoordinator"/>），滑出视野立即释放帧缓存；发布时从磁盘直接取原图。
/// </para>
/// <para>
/// 录制按钮为本栏的录制入口：前置检查通过后依次手选工程包与输出目录，自动逐张录制并在结束时
/// 自动导入为本栏的集（选中该集）；详见 <see cref="RecordingRunPanel"/>。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class GifSetPanel : VBoxContainer, IGifSetPanel
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

  /// <summary>导入文件夹选择器（场景内常驻对话框；模式/过滤/标题都写在场景里）。</summary>
  [Node("%FolderDialog")]
  public IFileDialog FolderDialog { get; set; } = default!;

  /// <summary>导入压缩包选择器（场景内常驻对话框）。</summary>
  [Node("%ZipDialog")]
  public IFileDialog ZipDialog { get; set; } = default!;

  /// <summary>导入/录制结果提示框（场景内常驻对话框，标题按用途在运行时改写）。</summary>
  [Node("%MessageDialog")]
  public IAcceptDialog MessageDialog { get; set; } = default!;

  /// <summary>录制入口的工程包选择器（场景内常驻对话框）。</summary>
  [Node("%RecordZipDialog")]
  public IFileDialog RecordZipDialog { get; set; } = default!;

  /// <summary>录制入口的输出目录选择器（场景内常驻对话框）。</summary>
  [Node("%RecordOutputDirDialog")]
  public IFileDialog RecordOutputDirDialog { get; set; } = default!;

  /// <summary>录制过程对话框（GifSetPanel.tscn 内的场景实例，Runner 由本栏注入）。</summary>
  [Node("%RecordingRunPanel")]
  public IRecordingRunPanel RecordingRun { get; set; } = default!;

  #region Dependencies

  /// <summary>数据管理器（由信息面板经 AutoInject 提供）。</summary>
  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  /// <summary>GIF 集服务（由信息面板经 AutoInject 提供，与发布编排共用同一个实例）。</summary>
  [Dependency]
  public GifSetService GifSets => this.DependOn<GifSetService>();

  #endregion

  /// <summary>本栏发布请求：由信息面板订阅后统一编排串行发布（本栏不再持有发布回调）。</summary>
  public event Action? PublishRequested;

  private AutoList<GifSetRecord>.Binding? _setsBinding;
  private AutoValue<string>.Binding? _activeSetIdBinding;

  /// <summary>当前活动集的元素级绑定（集名 / 导入时间 / 条目数 / 根目录）。</summary>
  /// <remarks>
  /// 集列表的 <c>OnModify</c> 只在集增删时触发；同一文件夹重复导入时 <c>GifSetService.Register</c>
  /// 会原地更新已有记录的字段（列表对象没有任何变化），只绑列表会让集摘要停在旧值。
  /// </remarks>
  private readonly List<IDisposable> _activeSetBindings = new();

  /// <summary>是否正在重挂活动集字段绑定（订阅时会立刻回放一次，抑制这类伪变更）。</summary>
  private bool _isRebindingActiveSetFields;

  private GifSetService? _gifSets;
  private DataManager? _dataManager;
  private InfoConfig? _config;

  private readonly GifPlaybackCoordinator _coordinator = new();
  private readonly List<CardRef> _cards = new();

  /// <summary>已手选、等待接着选输出目录的工程包；两步选择之间的中间态。</summary>
  private string? _pendingModPackZip;

  /// <summary>一次性状态提示（如导入成功）；不在 <see cref="RebuildCards"/> 时清空，由下一次用户动作（切集/起录/失败）清除。</summary>
  private string? _pendingStatus;

  private bool _isRebuilding;
  private bool _isPublishing;

  /// <summary>是否正在执行一次导入（重入护栏：同一时刻只做一次解压/拷贝）。</summary>
  private bool _importing;

  /// <summary>是否已排队一次「布局落定后的驻留刷新」（信号与 deferred 回调都在主线程，普通 bool 即可）。</summary>
  private bool _layoutRefreshQueued;

  /// <summary>是否已排队一次卡片重建（同一帧内多次数据变化只重建一次；回调都在主线程，普通 bool 即可）。</summary>
  private bool _rebuildQueued;

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

    DisposeActiveSetBindings();

    // 本栏退出后不该还有引擎在后台跑；取消语义会保留已归集的产物与报告。
    // 录制过程对话框是场景子节点，退订后由它随本栏一起释放。
    if (RecordingRun is not null)
    {
      RecordingRun.Finished -= OnRecordingFinished;
      RecordingRun.Cancel();
    }

    ReleaseCards();
  }

  /// <summary>AutoInject 节点注入完成：只挂信号与点亮录制入口，依赖待解析后再装配。</summary>
  /// <remarks>
  /// 信号连接放这里而不是 <see cref="OnResolved"/>：<c>OnReady</c> 每个节点实例只执行一次，
  /// 而依赖重新解析（节点重新入树）会再次触发 <see cref="OnResolved"/>，重复连接信号会报
  /// "already connected"。
  /// </remarks>
  public void OnReady()
  {
    // 录制入口已接通：场景里该按钮仍是占位态（disabled + 过时 tooltip），故在这里点亮并换成可执行提示
    RecordButton.Disabled = false;
    RecordButton.TooltipText = "选工程包与输出目录后自动逐张录制，录完自动导入为本栏的集";

    // 对话框都是场景内常驻节点：信号在 OnReady 连一次（按节点缓存后每次唤起都复用同一实例）
    FolderDialog.DirSelected += OnImportFolderSelected;
    ZipDialog.FileSelected += OnImportZipSelected;
    RecordZipDialog.FileSelected += OnRecordZipSelected;
    RecordOutputDirDialog.DirSelected += OnRecordOutputDirSelected;

    // 录制过程对话框也是场景实例：注入跑一轮的委托与结束回调
    RecordingRun.Runner = RunRecordingAsync;
    RecordingRun.Finished += OnRecordingFinished;

    ImportFolderButton.Pressed += OnImportFolderPressed;
    ImportZipButton.Pressed += OnImportZipPressed;
    SetSelector.ItemSelected += OnSetSelected;
    PublishButton.Pressed += OnPublishPressed;
    RecordButton.Pressed += OnRecordPressed;
    CardScroll.GetVScrollBar().ValueChanged += OnCardScrollValueChanged;

    // 可见区不只随滚动变化：窗口缩放/最大化、拖拽分栏会改变视口尺寸，卡片重排会改变位置，
    // 面板显隐也可能让布局在隐藏期间才落定。这些路径都必须重新评估驻留集合，
    // 否则新进入视口的卡会空白到下一次滚动（旧行为正是如此）。
    CardScroll.Resized += ScheduleLayoutRefresh;
    CardList.Resized += ScheduleLayoutRefresh;
    VisibilityChanged += ScheduleLayoutRefresh;
  }

  /// <summary>AutoInject 依赖解析完成：绑定数据源并首次重建卡片（装配全部来自依赖注入）。</summary>
  public void OnResolved()
  {
    _dataManager = DataManager;
    _gifSets = GifSets;
    _config = _dataManager.InfoConfig;

    // 重新解析会再次进入：先释放上一轮绑定，避免同一条数据被订阅多次
    _setsBinding?.Dispose();
    _activeSetIdBinding?.Dispose();

    _setsBinding = _config.GifSets.Bind().OnModify(OnSetsChanged);
    _activeSetIdBinding = _config.ActiveGifSetId.Bind().OnValue(OnActiveSetIdChanged);

    // 活动集字段绑定：重复导入同一文件夹时 Register 原地更新字段（集列表不增删），
    // 只绑列表会让摘要与卡片停在旧值；之后由 OnSetsChanged / OnActiveSetIdChanged 重挂。
    RebindActiveSetBindings();

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

      // 卡片尺寸要等一次布局才有效：走与布局事件同一条刷新通道
      ScheduleLayoutRefresh();
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

  /// <summary>排队一次「布局落定后的驻留刷新」：同一帧内多次布局事件只排一次。</summary>
  private void ScheduleLayoutRefresh()
  {
    if (_layoutRefreshQueued)
      return;

    _layoutRefreshQueued = true;
    CallDeferred(nameof(FlushLayoutRefresh));
  }

  /// <summary>布局事件的首轮回调：容器排序可能仍排在 deferred 队列更后面，故再兜一次。</summary>
  private void FlushLayoutRefresh()
  {
    _layoutRefreshQueued = false;
    RefreshPlayback();

    // CardList 子项位置由容器排序写入，与本次回调同属 deferred 消息队列、先后不保证；
    // 再排一次兜底刷新，按真正落定的几何复核。已加载或在途解码的同路径卡再 Play
    // 都是空操作，所以这里只多一次几何复核的开销。
    CallDeferred(nameof(RefreshPlayback));
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

  /// <summary>集列表增删：活动集可能换人，先重挂活动集字段绑定再排队重建。</summary>
  private void OnSetsChanged()
  {
    RebindActiveSetBindings();
    ScheduleRebuild();
  }

  /// <summary>当前选中集变化：按新活动集重挂字段绑定再排队重建。</summary>
  /// <param name="setId">新的活动集 Id（未消费，重建时统一从模型解析）。</param>
  private void OnActiveSetIdChanged(string? setId)
  {
    RebindActiveSetBindings();
    ScheduleRebuild();
  }

  /// <summary>
  /// 按当前活动集重挂元素字段绑定（集切换 / 列表变化 / 依赖重新解析时调用）。
  /// </summary>
  /// <remarks>
  /// 取活动集时不写回 Id（与 <see cref="GifSetService.GetActiveSet"/> 同口径但无副作用），
  /// 避免在重挂过程中触发选中项绑定造成重入；Id 的修正仍由重建时的 GetActiveSet 负责。
  /// </remarks>
  private void RebindActiveSetBindings()
  {
    DisposeActiveSetBindings();

    var set = ResolveActiveSet();
    if (set is null)
      return;

    // 重挂期间抑制回调：AutoValue 订阅会立刻用当前值回放一次，不抑制会排一批伪重建
    _isRebindingActiveSetFields = true;
    try
    {
      _activeSetBindings.Add(set.SetName.Bind().OnValue(_ => OnActiveSetFieldChanged()));
      _activeSetBindings.Add(set.ImportedAt.Bind().OnValue(_ => OnActiveSetFieldChanged()));
      _activeSetBindings.Add(set.EntryCount.Bind().OnValue(_ => OnActiveSetFieldChanged()));
      _activeSetBindings.Add(set.RootPath.Bind().OnValue(_ => OnActiveSetFieldChanged()));
    }
    finally
    {
      _isRebindingActiveSetFields = false;
    }
  }

  /// <summary>释放全部活动集字段绑定（集切换、本栏退出时）。</summary>
  private void DisposeActiveSetBindings()
  {
    foreach (var binding in _activeSetBindings)
      binding.Dispose();

    _activeSetBindings.Clear();
  }

  /// <summary>取当前活动集（不写回 Id）：与 <see cref="GifSetService.GetActiveSet"/> 同口径但无副作用。</summary>
  /// <returns>当前活动集；没有任何集时为 null。</returns>
  private GifSetRecord? ResolveActiveSet()
  {
    var sets = _gifSets?.Sets;
    if (sets is null || sets.Count == 0)
      return null;

    var activeId = _config?.ActiveGifSetId.Value ?? string.Empty;
    var active = sets.FirstOrDefault(set =>
      string.Equals(set.Id.Value, activeId, StringComparison.Ordinal)
    );
    return active ?? sets[0];
  }

  /// <summary>活动集字段变化（重复导入原地更新 / 集改名）：摘要与卡片都要重建。</summary>
  private void OnActiveSetFieldChanged()
  {
    if (_isRebindingActiveSetFields)
      return;

    ScheduleRebuild();
  }

  /// <summary>排队一次卡片重建：同一帧内多次数据变化（集增删 + 选中变化 + 字段变化）只重建一次。</summary>
  private void ScheduleRebuild()
  {
    if (_rebuildQueued)
      return;

    _rebuildQueued = true;
    CallDeferred(nameof(FlushRebuild));
  }

  /// <summary>释放重建排队标记并真正重建（供 <see cref="ScheduleRebuild"/> 的 deferred 回调使用）。</summary>
  private void FlushRebuild()
  {
    _rebuildQueued = false;
    RebuildCards();
  }

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

  /// <summary>导入文件夹：选择器是场景内的常驻节点，信号已在 <see cref="OnReady"/> 连过一次。</summary>
  private void OnImportFolderPressed() => FileDialogSizing.Popup(FolderDialog, _dataManager!);

  /// <summary>导入压缩包：选择器是场景内的常驻节点，信号已在 <see cref="OnReady"/> 连过一次。</summary>
  private void OnImportZipPressed() => FileDialogSizing.Popup(ZipDialog, _dataManager!);

  private void OnImportFolderSelected(string path) => Import(() => _gifSets!.ImportFolder(path));

  private void OnImportZipSelected(string path) => Import(() => _gifSets!.ImportZip(path));

  /// <summary>执行一次导入：成功则选中该集并留提示，失败则展示明细。</summary>
  /// <remarks>
  /// 带重入护栏：解压/拷贝是同步的长动作，期间同一帧内可能再触发一次导入
  /// （重复点击、录制结束的自动导入与手选叠加）。重入会让压缩包按新的 GUID 目录
  /// 再解一份、再注册一个集，因此重入时直接忽略（返回 <c>null</c>）。
  /// </remarks>
  /// <param name="import">导入动作。</param>
  /// <returns>导入结果；服务未就绪或正在导入时为 <c>null</c>。</returns>
  private GifSetImportResult? Import(Func<GifSetImportResult> import)
  {
    if (_gifSets is null || _importing)
      return null;

    _importing = true;
    try
    {
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
        _pendingStatus =
          $"导入成功：{result.Set.SetName.Value}（{result.Set.EntryCount.Value} 张）";
        SetInfoLabel.Text = _pendingStatus;

        _gifSets.SetActiveSet(result.Set.Id.Value);
        return result;
      }

      // 失败结果覆盖上一次的成功提示，避免两条状态并排留下误导
      _pendingStatus = null;
      SetInfoLabel.Text = result.ToDisplayText();
      GD.PushWarning($"GifSetPanel: 导入失败：{result.ToDisplayText()}");
      ShowMessage("导入失败", result.ToDisplayText());
      return result;
    }
    finally
    {
      _importing = false;
    }
  }

  /// <summary>录制入口：前置检查通过后依次手选工程包与输出目录。</summary>
  private void OnRecordPressed()
  {
    // 已有轮次在跑：把进度窗拉回前台即可，不起第二轮（同一引擎目录不能并发）
    if (RecordingRun is { IsRunning: true })
    {
      RecordingRun.BringToFront();
      return;
    }

    var blocker = DescribeRecordBlocker();
    if (blocker is not null)
    {
      ShowMessage("暂时无法录制", blocker);
      return;
    }

    _pendingModPackZip = null;
    FileDialogSizing.Popup(RecordZipDialog, _dataManager!);
  }

  /// <summary>选定工程包后接着选输出目录。</summary>
  /// <param name="path">工程包路径。</param>
  private void OnRecordZipSelected(string path)
  {
    _pendingModPackZip = path;
    FileDialogSizing.Popup(RecordOutputDirDialog, _dataManager!);
  }

  /// <summary>选定输出目录后起录。</summary>
  /// <param name="path">输出目录。</param>
  private void OnRecordOutputDirSelected(string path) => StartRecording(path);

  /// <summary>拼出不可起录的原因。</summary>
  /// <returns>原因（沿用各层原文，已含下一步指引）；可起录时为 <c>null</c>。</returns>
  private string? DescribeRecordBlocker()
  {
    if (_dataManager is null || _gifSets is null)
      return "信息面板尚未装配完成，请稍候再试。";

    var config = _dataManager.RecordingConfig;
    var engineDir = config.EngineDir.Value;
    if (!EngineLocator.TryValidate(engineDir, out var reason))
      return reason;

    // 与设置页同一套判据，区别在「清单缺失」：能装 ≠ 能录（引擎不会加载插件）
    if (!PluginDeployer.TryValidateForRecording(engineDir, out reason))
      return reason;

    return !RecordingSandbox.TryValidateRoot(config.SandboxRoot.Value, out _, out reason)
      ? reason
      : null;
  }

  /// <summary>起录一轮（输出目录由用户手选，不默认挟带整合导出产物）。</summary>
  /// <param name="outputDir">输出目录。</param>
  private void StartRecording(string outputDir)
  {
    if (_dataManager is null || _gifSets is null || string.IsNullOrEmpty(_pendingModPackZip))
      return;

    var config = _dataManager.RecordingConfig;
    var request = new RecordingRequest(
      config.EngineDir.Value,
      _pendingModPackZip!,
      outputDir,
      config
    );
    _pendingModPackZip = null;

    _pendingStatus = null;
    SetInfoLabel.Text = $"录制中：成功产物将写入 {outputDir}";
    RecordingRun.Start(request);
  }

  /// <summary>跑一轮录制的默认实现（UI 侧唯一入口）。</summary>
  /// <param name="request">本轮输入。</param>
  /// <param name="progress">进度回调。</param>
  /// <param name="cancellationToken">取消令牌。</param>
  /// <returns>本轮结果。</returns>
  /// <remarks>
  /// 非静态：日志按 <c>Recording</c> 模块名取自 <see cref="DataManager.GetLogger"/>（生产经 MainWindow
  /// 注入的工厂转给 ILogService；测试退回 NullLog），不再在 UI 层做静态日志定位。
  /// </remarks>
  private Task<RecordingRunResult> RunRecordingAsync(
    RecordingRequest request,
    IProgress<RecordingProgress>? progress,
    CancellationToken cancellationToken
  ) =>
    new RecordingOrchestrator(_dataManager!.GetLogger("Recording")).RunAsync(
      request,
      progress,
      cancellationToken
    );

  /// <summary>本轮结束：记下输出目录、自动导入为该集，并把汇总回填到进度窗。</summary>
  /// <param name="result">本轮结果。</param>
  private void OnRecordingFinished(RecordingRunResult result)
  {
    var summary = new List<string> { DescribeRun(result) };

    if (result.Error is null)
    {
      _dataManager!.RecordingConfig.LastOutputDir.Value = result.OutputDir;
      _dataManager.TriggerAutoSave();

      if (result.ManifestPath is null)
      {
        // 无成功产物时不写出清单（清单里没有条目会被集内校验判为空集），导入没有意义
        summary.Add("本轮没有成功落盘的产物，跳过导入。");
      }
      else
      {
        var imported = Import(() => _gifSets!.ImportFolder(result.OutputDir));
        summary.Add(
          imported is { IsSuccess: true }
            ? $"已自动导入为集：{imported.Set?.SetName.Value}"
            : "自动导入未完成，明细见导入提示框。"
        );
      }
    }

    if (!string.IsNullOrEmpty(result.EngineLogTail))
      summary.Add(result.EngineLogTail!);

    var text = string.Join("\n", summary);
    RecordingRun.AppendSummary(text);

    if (result.Error is not null)
    {
      _pendingStatus = null;
      SetInfoLabel.Text = result.Error;
      ShowMessage("录制失败", text);
    }
  }

  /// <summary>把本轮结果拼成多行汇总说明（首行计数，随后追加取消/告警说明）。</summary>
  /// <param name="result">本轮结果。</param>
  /// <returns>汇总文本。</returns>
  private static string DescribeRun(RecordingRunResult result)
  {
    var report = result.Report;
    var parts = new List<string>
    {
      $"本轮录制：成功 {report.Succeeded}　失败 {report.Failed}"
        + $"　未录制 {report.TotalCombat - report.Succeeded - report.Failed}　共 {report.TotalCombat} 张",
    };

    if (result.Cancelled)
      parts.Add("已取消（已归集的产物与报告保留）");

    if (!string.IsNullOrEmpty(result.Warning))
      parts.Add($"提示：{result.Warning}");

    return string.Join("\n", parts);
  }

  private void ShowMessage(string title, string text)
  {
    MessageDialog.Title = title;
    MessageDialog.DialogText = text;
    MessageDialog.PopupCentered(new Vector2I(640, 420));
  }

  /// <summary>发布按钮：触发 <see cref="PublishRequested"/>，由信息面板统一编排发布。</summary>
  /// <remarks>
  /// 忙碌态由信息面板在真正发送期间经 <see cref="SetPublishBusy"/> 驱动（覆盖整个在飞区间），
  /// 因此这里的守卫能在重复点击时直接丢弃、不会重入；发布结束后由信息面板复位。
  /// </remarks>
  private void OnPublishPressed()
  {
    if (_isPublishing)
      return;

    PublishRequested?.Invoke();
  }

  /// <summary>设置本栏「发布中」忙碌态：在飞期间发布按钮置灰（由信息面板统一驱动）。</summary>
  /// <param name="busy">是否正在发布。</param>
  public void SetPublishBusy(bool busy)
  {
    _isPublishing = busy;
    UpdatePublishButtonState();
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
