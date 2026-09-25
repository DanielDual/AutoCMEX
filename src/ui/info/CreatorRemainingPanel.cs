namespace AutoCMEX.UI.Info;

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
/// 预览与出图都消费 <see cref="TableModelBuilder.BuildCreatorRemainingTable"/> 的产物，因此两者
/// 的列、计数与整行染色完全一致。
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

  /// <inheritdoc/>
  public override void _Notification(int what) => this.Notify(what);

  /// <inheritdoc/>
  public override void _ExitTree()
  {
    _watcher?.Dispose();
    _watcher = null;
  }

  /// <summary>AutoInject 节点注入完成（依赖注入前，无需额外动作）。</summary>
  public void OnReady() { }

  /// <summary>AutoInject 依赖解析完成（本栏无依赖，无需额外动作）。</summary>
  public void OnResolved() { }

  /// <summary>
  /// 装配本栏；由信息面板在本节点加入场景树后调用。
  /// </summary>
  /// <param name="dataManager">数据管理器（监听两表数据变化用）。</param>
  /// <param name="dataService">信息数据服务。</param>
  /// <param name="publish">本栏发布回调。</param>
  public void Setup(
    DataManager dataManager,
    InfoDataService dataService,
    ColumnPublishHandler publish
  )
  {
    _dataService = dataService;
    _publish = publish;

    PublishButton.Pressed += OnPublishPressed;
    _watcher = new BossDataWatcher(dataManager, () => CallDeferred(nameof(Refresh)));

    Refresh();
  }

  /// <summary>按当前数据重建表格预览。</summary>
  public void Refresh()
  {
    if (_dataService is null)
      return;

    var model = TableModelBuilder.BuildCreatorRemainingTable(_dataService.BuildSnapshot());
    TitleLabel.Text = model.Title;
    TableModelView.Fill(TableGrid, model);
    PublishButton.Disabled = _isPublishing || !model.HasContent;
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
        Refresh();
    }
  }
}
