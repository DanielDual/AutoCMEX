namespace AutoCMEX.UI.Info;

using System;
using System.Linq;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 符卡猜测情况表栏：展示与发布图同源的表格预览，并提供本栏发布入口。
/// </summary>
/// <remarks>
/// 预览与出图都消费 <see cref="TableModelBuilder.BuildGuessingTable"/> 的产物，因此不会出现
/// "界面看到的和发出去的不一致"。
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class GuessingTablePanel : VBoxContainer
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
  private Action? _onTableChanged;
  private bool _isPublishing;

  /// <summary>上一次刷新时的表内容指纹；首次刷新只记基线，不视为「变化」。</summary>
  private string? _lastSignature;

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
  /// <param name="onTableChanged">
  /// 表内容变化回调（可选）；仅在真正重建出不同的表内容时触发，供信息面板做自动推送。
  /// </param>
  public void Setup(
    DataManager dataManager,
    InfoDataService dataService,
    ColumnPublishHandler publish,
    Action? onTableChanged = null
  )
  {
    _dataService = dataService;
    _publish = publish;
    _onTableChanged = onTableChanged;

    PublishButton.Pressed += OnPublishPressed;
    _watcher = new BossDataWatcher(dataManager, () => CallDeferred(nameof(Refresh)));

    Refresh();
  }

  /// <summary>按当前数据重建表格预览。</summary>
  /// <remarks>
  /// 刷新由 <see cref="BossDataWatcher"/> 驱动，触发源里混着与这张表无关的变化（例如创作者改名），
  /// 因此这里比对内容指纹，只在表真的变了、且表不为空时回调 <c>onTableChanged</c>；
  /// 首次刷新只记基线，保证刚启动不会把当前表推一遍。
  /// </remarks>
  public void Refresh()
  {
    if (_dataService is null)
      return;

    var model = TableModelBuilder.BuildGuessingTable(_dataService.BuildSnapshot());
    TitleLabel.Text = model.Title;
    TableModelView.Fill(TableGrid, model);
    PublishButton.Disabled = _isPublishing || !model.HasContent;

    var signature = BuildContentSignature(model);
    var changed = _lastSignature is not null && signature != _lastSignature;
    _lastSignature = signature;

    if (changed && model.HasContent)
      _onTableChanged?.Invoke();
  }

  /// <summary>计算表内容指纹：标题 + 每行的高亮标记与单元格文本。</summary>
  /// <param name="model">表格模型。</param>
  /// <returns>可直接用 <c>!=</c> 比较的指纹字符串。</returns>
  private static string BuildContentSignature(TableModel model) =>
    string.Join(
      '\n',
      new[] { model.Title }.Concat(
        model.Rows.Select(row =>
          (row.IsHighlighted ? "1" : "0") + "\u001f" + string.Join('\u001f', row.Cells)
        )
      )
    );

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
