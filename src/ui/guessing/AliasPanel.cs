namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AutoCMEX;
using AutoCMEX.Core.Storage;
using AutoCMEX.Helpers;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 别名表面板 — 独立场景，管理创作者别名的展示、编辑和 CRUD。
/// </summary>
/// <remarks>
/// 树是<b>嵌套结构</b>：创作者是父行、每条别名是它的子行，只有一列（「创作者 / 别名」）。
/// <para>
/// 创作者下标一律走单元格元数据（<c>SetMetadata(0, 创作者下标)</c>）：隐藏根的 <c>GetIndex()</c>
/// 恒为 0，父行不能靠它定位。别名子行的下标则取它在自己父节点下的次序号（<c>GetIndex()</c>，
/// 父节点是真实行，次序有效）。父子行的区分必须用「父节点是不是树根」，不能用「有没有子节点」——
/// 没有别名的创作者行同为叶子，会被误判成别名行。
/// </para>
/// <para>
/// 别名列表是嵌套的 <c>AutoList</c>，其增删不会冒泡到 <c>DataManager.Aliases</c> 的订阅，
/// 故改动别名的操作需要自己重建树（<see cref="Refresh"/>）；创作者行的增删清空仍由订阅驱动。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class AliasPanel : VBoxContainer
{
  [Node("%AliasTree")]
  public ITree AliasTree { get; set; } = default!;

  [Node("%ImportAliasBtn")]
  public IButton ImportAliasBtn { get; set; } = default!;

  [Node("%ExportAliasBtn")]
  public IButton ExportAliasBtn { get; set; } = default!;

  [Node("%AddAliasBtn")]
  public IButton AddAliasBtn { get; set; } = default!;

  [Node("%AddAliasToCreatorBtn")]
  public IButton AddAliasToCreatorBtn { get; set; } = default!;

  [Node("%DeleteAliasBtn")]
  public IButton DeleteAliasBtn { get; set; } = default!;

  [Node("%ImportFileDialog")]
  public IFileDialog ImportFileDialog { get; set; } = default!;

  [Node("%ExportFileDialog")]
  public IFileDialog ExportFileDialog { get; set; } = default!;

  [Node("%ErrorDialog")]
  public IAcceptDialog ErrorDialog { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  /// <summary>数据源；在 <see cref="OnResolved"/> 之后可用，未注入时为 null，各回调需自行判空。</summary>
  private DataManager? _dm;

  /// <summary>创作者列表顶层增删清空的订阅句柄，重建树由它触发（<see cref="_ExitTree"/> 释放）。</summary>
  private AutoList<CreatorAlias>.Binding? _aliasesBinding;

  /// <summary>
  /// 获取当前使用的 DataManager 实例（供测试使用）
  /// </summary>
  public DataManager? GetDataManager() => _dm;

  /// <summary>
  /// 测试用：获取 OnAddAlias 委托
  /// </summary>
  public Action GetOnAlias() => OnAddAlias;

  /// <summary>
  /// 测试用：获取 OnAliasEdited 委托
  /// </summary>
  public Action GetOnAliasEdited() => OnAliasEdited;

  /// <summary>
  /// 测试用：获取 OnAddAliasToCreator 委托
  /// </summary>
  public Action GetOnAddAliasToCreator() => OnAddAliasToCreator;

  /// <summary>
  /// 测试用：获取 OnDeleteSelected 委托
  /// </summary>
  public Action GetOnDeleteSelected() => OnDeleteSelected;

  /// <summary>
  /// 测试用：获取 OnAliasExportFileSelected 委托
  /// </summary>
  public Action<string> GetOnAliasExportFileSelected() => OnAliasExportFileSelected;

  public override void _Notification(int what) => this.Notify(what);

  public override void _ExitTree()
  {
    _aliasesBinding?.Dispose();
  }

  public void OnReady()
  {
    AliasTree.Columns = 1;
    AliasTree.SetColumnTitle(0, "创作者 / 别名");

    AliasTree.ItemEdited += OnAliasEdited;
    ImportAliasBtn.Pressed += OnImportAliasTable;
    ExportAliasBtn.Pressed += OnExportAliasTable;
    AddAliasBtn.Pressed += OnAddAlias;
    AddAliasToCreatorBtn.Pressed += OnAddAliasToCreator;
    DeleteAliasBtn.Pressed += OnDeleteSelected;

    // 配置预置对话框
    ImportFileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
    ImportFileDialog.Access = FileDialog.AccessEnum.Filesystem;
    ImportFileDialog.AddFilter("*.csv, *.xlsx", "*.csv, *.xlsx");
    ImportFileDialog.AddFilter("*.csv", "*.csv");
    ImportFileDialog.AddFilter("*.xlsx", "*.xlsx");
    ImportFileDialog.FileSelected += OnAliasFileSelected;

    ExportFileDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
    ExportFileDialog.Access = FileDialog.AccessEnum.Filesystem;
    ExportFileDialog.AddFilter("*.csv", "*.csv");
    ExportFileDialog.FileSelected += OnAliasExportFileSelected;

    ErrorDialog.Title = "错误";
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm == null)
      return;

    _aliasesBinding = _dm.Aliases.Bind().OnModify(() => CallDeferred(nameof(Refresh)));

    Refresh();
  }

  /// <summary>重建别名表：创作者为父行、每条别名为子行；父子行都把创作者下标写入元数据供回调定位。</summary>
  public void Refresh()
  {
    AliasTree.Clear();
    if (_dm == null)
      return;
    var root = AliasTree.CreateItem();
    AliasTree.HideRoot = true;
    for (int i = 0; i < _dm.Aliases.Count; i++)
    {
      var alias = _dm.Aliases[i];
      var creatorItem = AliasTree.CreateItem(root);
      creatorItem.SetText(0, alias.MainName);
      creatorItem.SetEditable(0, true);
      creatorItem.SetMetadata(0, i);
      for (int j = 0; j < alias.Aliases.Count; j++)
      {
        var aliasItem = AliasTree.CreateItem(creatorItem);
        aliasItem.SetText(0, alias.Aliases[j]);
        aliasItem.SetEditable(0, true);
        aliasItem.SetMetadata(0, i);
      }
    }
  }

  /// <summary>提交一次单元格编辑：创作者行改主名，别名子行只改自己那一条别名。</summary>
  /// <remarks>
  /// 创作者下标只能来自 <c>GetMetadata(0)</c>（隐藏根的 <c>GetIndex()</c> 恒为 0）；
  /// 别名下标取子行在父节点下的 <c>GetIndex()</c>。区分父子行用「父节点是不是树根」，
  /// 不能用「有没有子节点」——没有别名的创作者行也同为叶子。
  /// </remarks>
  private void OnAliasEdited()
  {
    var edited = AliasTree.GetEdited();
    if (edited == null || _dm == null)
      return;
    var creatorIdx = edited.GetMetadata(0).AsInt32();
    if (creatorIdx < 0 || creatorIdx >= _dm.Aliases.Count)
      return;
    if (edited.GetParent() == AliasTree.GetRoot())
      _dm.Aliases[creatorIdx].MainName = edited.GetText(0);
    else
    {
      var aliasIdx = edited.GetIndex();
      if (aliasIdx < 0 || aliasIdx >= _dm.Aliases[creatorIdx].Aliases.Count)
        return;
      _dm.Aliases[creatorIdx].Aliases[aliasIdx] = edited.GetText(0);
    }
    _dm.TriggerAutoSave();
  }

  private void OnImportAliasTable()
  {
    ImportFileDialog.PopupCentered();
  }

  private void OnExportAliasTable()
  {
    ExportFileDialog.PopupCentered();
  }

  /// <summary>导出别名表：第一列主名，之后每列一条别名，列数按最长的一行补齐。</summary>
  /// <remarks>
  /// 必须「每列一条别名」而不是把一行的别名逗号拼接进单列：导入侧（CSV / Excel）只按列读别名，
  /// 拼接格式导出后再导入会把整串当成一条别名。
  /// </remarks>
  private void OnAliasExportFileSelected(string path)
  {
    if (_dm == null)
      return;
    var maxAliases = 0;
    foreach (var alias in _dm.Aliases)
    {
      if (alias.Aliases.Count > maxAliases)
        maxAliases = alias.Aliases.Count;
    }
    var sb = new StringBuilder();
    var header = new List<string> { "主名" };
    for (int i = 1; i <= maxAliases; i++)
      header.Add("别名" + i);
    sb.AppendLine(string.Join(",", header));
    foreach (var alias in _dm.Aliases)
    {
      var fields = new List<string> { StringEscapeHelper.EscapeCsv(alias.MainName) };
      for (int i = 0; i < maxAliases; i++)
      {
        fields.Add(
          i < alias.Aliases.Count ? StringEscapeHelper.EscapeCsv(alias.Aliases[i]) : string.Empty
        );
      }
      sb.AppendLine(string.Join(",", fields));
    }
    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private void OnAliasFileSelected(string path)
  {
    if (_dm == null)
      return;
    var importer = ImporterFactory.Create(path);
    ImportResult<List<CreatorAlias>> result = importer.ImportAliasTable(path);
    if (!result.IsSuccess)
    {
      ShowError(result.ErrorMessage);
      return;
    }
    _dm.Aliases.Clear();
    foreach (var alias in result.Data!)
      _dm.Aliases.Add(alias);
    _dm.TriggerAutoSave();
  }

  private void OnAddAlias()
  {
    if (_dm == null)
      return;
    _dm.Aliases.Add(new CreatorAlias { MainName = "新创作者" });
    _dm.TriggerAutoSave();
  }

  /// <summary>给选中的创作者追加一条占位别名「新别名」，随后重建树以便立刻看到它。</summary>
  /// <remarks>
  /// 未选中任何行时弹错误框并中止，而不是退回第一行：加到第一行会让用户误以为「点错了行/没反应」，
  /// 正是别名错配的成因。选中别名子行时同样作用于它所属的创作者（元数据里存的就是创作者下标）。
  /// 别名是嵌套列表，其变动不会冒泡到 <c>DataManager.Aliases</c> 的订阅，
  /// 因此这里必须自行 <see cref="Refresh"/>。
  /// </remarks>
  private void OnAddAliasToCreator()
  {
    if (_dm == null)
      return;
    var selected = AliasTree.GetNextSelected(null);
    if (selected == null)
    {
      ShowError("请先在别名表中选择一个创作者");
      return;
    }
    var creatorIdx = selected.GetMetadata(0).AsInt32();
    if (creatorIdx < 0 || creatorIdx >= _dm.Aliases.Count)
      return;
    _dm.Aliases[creatorIdx].Aliases.Add("新别名");
    _dm.TriggerAutoSave();
    Refresh();
  }

  /// <summary>删除选中行：创作者行删整个创作者，别名子行只删那一条别名。</summary>
  /// <remarks>
  /// 定位一律用元数据与父节点，而不是主名匹配——主名允许重复，按主名删会连带删掉同名行。
  /// 删别名后必须自行 <see cref="Refresh"/>（嵌套 <c>AutoList</c> 的变动不会冒泡到顶层订阅）。
  /// </remarks>
  private void OnDeleteSelected()
  {
    if (_dm == null)
      return;
    var selected = AliasTree.GetNextSelected(null);
    if (selected == null)
      return;
    var creatorIdx = selected.GetMetadata(0).AsInt32();
    if (creatorIdx < 0 || creatorIdx >= _dm.Aliases.Count)
      return;
    if (selected.GetParent() == AliasTree.GetRoot())
    {
      _dm.Aliases.RemoveAt(creatorIdx);
      _dm.TriggerAutoSave();
      return;
    }
    var aliasIdx = selected.GetIndex();
    if (aliasIdx < 0 || aliasIdx >= _dm.Aliases[creatorIdx].Aliases.Count)
      return;
    _dm.Aliases[creatorIdx].Aliases.RemoveAt(aliasIdx);
    _dm.TriggerAutoSave();
    Refresh();
  }

  private void ShowError(string msg)
  {
    ErrorDialog.DialogText = msg;
    ErrorDialog.PopupCentered();
  }
}
