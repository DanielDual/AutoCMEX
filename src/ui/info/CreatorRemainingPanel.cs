namespace AutoCMEX.UI.Info;

using System;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 创作者剩余未被猜测符卡数表栏：展示与发布图同源的表格预览，并提供本栏发布入口。
/// </summary>
/// <remarks>
/// <para>
/// 预览与出图都消费 <see cref="TableModelBuilder.BuildCreatorRemainingTable"/> 的产物，因此两者
/// 的列、计数与整行染色完全一致。
/// </para>
/// <para>
/// 本栏是 <c>InfoPanel.tscn</c> 里的场景内实例：依赖（数据管理器 / 信息数据服务 / 共享观察者）
/// 全部由信息面板经 AutoInject 提供，发布改为抛事件由信息面板统一编排，
/// 不再有 <c>Setup(...)</c> 手动注入。
/// </para>
/// <para>
/// 数据变化由共享 <see cref="BossDataWatcher"/> 通知（两栏共用一个观察者），本栏只订阅
/// <see cref="BossDataWatcher.Changed"/> 并把自己的重建合并到每帧一次，
/// 因此一次猜测连改 K 张符卡也只重建、重绘一次。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class CreatorRemainingPanel : VBoxContainer, ICreatorRemainingPanel
{
  [Node("%TitleLabel")]
  public ILabel TitleLabel { get; set; } = default!;

  [Node("%TableGrid")]
  public IGridContainer TableGrid { get; set; } = default!;

  [Node("%PublishButton")]
  public IButton PublishButton { get; set; } = default!;

  /// <summary>右键「复制」菜单（栏场景节点，交给 <see cref="TablePreviewExport"/> 填充与弹出）。</summary>
  [Node("%TableImageMenu")]
  public IPopupMenu TableImageMenu { get; set; } = default!;

  /// <summary>导出失败提示框（栏场景节点；非独占，避免与其它子窗口冲突）。</summary>
  [Node("%TableImageMessage")]
  public IAcceptDialog TableImageMessage { get; set; } = default!;

  #region Dependencies

  /// <summary>数据管理器（由信息面板经 AutoInject 提供，与发布编排同源）。</summary>
  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  /// <summary>信息数据服务（由信息面板经 AutoInject 提供；快照由它按当前数据构建）。</summary>
  [Dependency]
  public InfoDataService DataService => this.DependOn<InfoDataService>();

  /// <summary>两表栏共享的数据观察者（由信息面板经 AutoInject 提供；两栏同一实例）。</summary>
  [Dependency]
  public BossDataWatcher Watcher => this.DependOn<BossDataWatcher>();

  #endregion

  /// <summary>本栏发布请求：由信息面板订阅后统一编排串行发布（本栏不再持有发布回调）。</summary>
  public event Action? PublishRequested;

  private InfoDataService? _dataService;
  private BossDataWatcher? _watcher;
  private bool _isPublishing;
  private TablePreviewExport? _previewExport;

  /// <summary>当前表是否有内容：发布按钮可用态的唯一依据，发布收尾因此无需全量重建。</summary>
  private bool _hasContent;

  /// <summary>本帧是否已排过重建：同一帧内的多次数据通知合并成一次重建。</summary>
  /// <remarks>
  /// 通知可能来自数据线程（WebSocket 线程池写符卡属性），因此用 <c>volatile</c> 保证读写可见性；
  /// 竞态本身可接受——最坏只是同一帧多排一次重建，重建本身是幂等的。
  /// </remarks>
  private volatile bool _refreshQueued;

  /// <summary>表格预览的拖拽/复制交互（供面板级测试断言接线）。</summary>
  internal TablePreviewExport? PreviewExport => _previewExport;

  /// <summary>已执行的重建次数（供面板级测试断言一次猜测只重建一次）。</summary>
  internal int RefreshCountForTests { get; private set; }

  /// <inheritdoc/>
  public override void _Notification(int what) => this.Notify(what);

  /// <inheritdoc/>
  public override void _ExitTree()
  {
    // 观察者由信息面板创建并负责 Dispose：这里只退订。
    // 子节点先于父节点退出，因此退订一定发生在共享观察者被释放之前。
    if (_watcher is not null)
      _watcher.Changed -= OnWatcherChanged;

    _watcher = null;

    _previewExport?.Dispose();
    _previewExport = null;
  }

  /// <summary>AutoInject 节点注入完成：只挂信号与预览交互，依赖待解析后再绑定数据。</summary>
  /// <remarks>
  /// 信号连接放这里而不是 <see cref="OnResolved"/>：<c>OnReady</c> 每个节点实例只执行一次，
  /// 而依赖重新解析（节点重新入树）会再次触发 <see cref="OnResolved"/>，重复连接信号会报
  /// "already connected"。
  /// </remarks>
  public void OnReady()
  {
    PublishButton.Pressed += OnPublishPressed;
  }

  /// <summary>AutoInject 依赖解析完成：绑定共享观察者并首次重建表。</summary>
  /// <remarks>
  /// 可重复进入（节点重新入树）：订阅前先退订，避免同一路通知排队多次；
  /// 预览交互对象按需补建，上一轮已在退出时释放。
  /// </remarks>
  public void OnResolved()
  {
    _dataService = DataService;
    _watcher = Watcher;

    _watcher.Changed -= OnWatcherChanged;
    _watcher.Changed += OnWatcherChanged;

    EnsurePreviewExport();
    Refresh();
  }

  /// <summary>按当前数据重建表格预览。</summary>
  /// <remarks>
  /// 刷新由共享 <see cref="BossDataWatcher"/> 驱动（经 <see cref="FlushRefresh"/> 每帧合并一次）。
  /// 预览图不在这里出：<see cref="TablePreviewExport.Update"/> 只记内容，真正出图等到交互时。
  /// </remarks>
  public void Refresh()
  {
    if (_dataService is null)
      return;

    RefreshCountForTests++;

    var model = TableModelBuilder.BuildCreatorRemainingTable(_dataService.BuildSnapshot());
    TitleLabel.Text = model.Title;
    TableModelView.Fill(TableGrid, model);
    _previewExport?.Update(model);
    _hasContent = model.HasContent;
    UpdatePublishButtonState();
  }

  /// <summary>
  /// 共享观察者的数据变化通知（可能来自数据线程）：只置脏并排一次主线程重建，
  /// 本帧后续通知一律丢弃，因此一次猜测连改 K 张符卡也只重建一次。
  /// </summary>
  private void OnWatcherChanged()
  {
    if (_refreshQueued)
      return;

    _refreshQueued = true;
    CallDeferred(nameof(FlushRefresh));
  }

  /// <summary>执行本帧合并后的重建；公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约。</summary>
  public void FlushRefresh()
  {
    _refreshQueued = false;

    if (!IsInsideTree())
      return;

    Refresh();
  }

  /// <summary>只刷新发布按钮可用态，不重建表（数据未变时无需重算整张表与预览）。</summary>
  private void UpdatePublishButtonState()
  {
    PublishButton.Disabled = _isPublishing || !_hasContent;
  }

  /// <summary>按需补建预览交互对象（拖拽/右键复制）；依赖节点注入，故在依赖解析后调用。</summary>
  private void EnsurePreviewExport()
  {
    if (_previewExport is not null)
      return;

    _previewExport = new TablePreviewExport(
      this,
      TableGrid,
      TableImageMenu,
      TableImageMessage,
      model => TableImageRenderer.RenderToImageAsync(this, model),
      new WindowsTableImageExportBackend()
    );
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
}
