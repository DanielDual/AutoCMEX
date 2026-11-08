namespace AutoCMEX.UI.Info;

using System;
using AutoCMEX.Core.Info;
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
/// 数据变化由信息面板注入的共享 <see cref="BossDataWatcher"/> 通知（两栏共用一个观察者），
/// 本栏只订阅 <see cref="BossDataWatcher.Changed"/> 并把自己的重建合并到每帧一次，
/// 因此一次猜测连改 K 张符卡也只重建、重绘一次。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class CreatorRemainingPanel : VBoxContainer
{
  [Node("%TitleLabel")]
  public ILabel TitleLabel { get; set; } = default!;

  [Node("%TableGrid")]
  public IGridContainer TableGrid { get; set; } = default!;

  [Node("%PublishButton")]
  public IButton PublishButton { get; set; } = default!;

  private InfoDataService? _dataService;
  private ColumnPublishHandler? _publish;
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

  /// <summary>当前订阅的数据观察者（供面板级测试断言两栏共享同一个实例）。</summary>
  internal BossDataWatcher? WatcherForTests => _watcher;

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

  /// <summary>AutoInject 节点注入完成（依赖注入前，无需额外动作）。</summary>
  public void OnReady() { }

  /// <summary>AutoInject 依赖解析完成（本栏无依赖，无需额外动作）。</summary>
  public void OnResolved() { }

  /// <summary>
  /// 装配本栏；由信息面板在本节点加入场景树后调用。
  /// </summary>
  /// <param name="watcher">两栏共享的数据观察者（由信息面板创建并负责释放）。</param>
  /// <param name="dataService">信息数据服务。</param>
  /// <param name="publish">本栏发布回调。</param>
  public void Setup(
    BossDataWatcher watcher,
    InfoDataService dataService,
    ColumnPublishHandler publish
  )
  {
    _dataService = dataService;
    _publish = publish;

    PublishButton.Pressed += OnPublishPressed;

    _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
    _watcher.Changed += OnWatcherChanged;

    _previewExport = new TablePreviewExport(
      this,
      TableGrid,
      model => TableImageRenderer.RenderToImageAsync(this, model),
      new WindowsTableImageExportBackend()
    );

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

  private async void OnPublishPressed()
  {
    if (_publish is null || _isPublishing)
      return;

    _isPublishing = true;
    PublishButton.Disabled = true;
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
}
