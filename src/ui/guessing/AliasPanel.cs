namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AutoCMEX;
using AutoCMEX.Core.Storage;
using AutoCMEX.Helpers;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 别名表面板 — 独立场景，管理创作者别名的展示、编辑和 CRUD
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class AliasPanel : VBoxContainer
{
  [Node("%AliasTree")]
  public Tree AliasTree { get; set; } = default!;

  [Node("%ImportAliasBtn")]
  public Button ImportAliasBtn { get; set; } = default!;

  [Node("%ExportAliasBtn")]
  public Button ExportAliasBtn { get; set; } = default!;

  [Node("%AddAliasBtn")]
  public Button AddAliasBtn { get; set; } = default!;

  [Node("%AddAliasToCreatorBtn")]
  public Button AddAliasToCreatorBtn { get; set; } = default!;

  [Node("%DeleteAliasBtn")]
  public Button DeleteAliasBtn { get; set; } = default!;

  [Node("%ImportFileDialog")]
  public FileDialog ImportFileDialog { get; set; } = default!;

  [Node("%ExportFileDialog")]
  public FileDialog ExportFileDialog { get; set; } = default!;

  [Node("%ErrorDialog")]
  public AcceptDialog ErrorDialog { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  private DataManager? _dm;
  private AutoList<CreatorAlias>.Binding? _aliasesBinding;

  /// <summary>
  /// 获取当前使用的 DataManager 实例（供测试使用）
  /// </summary>
  public DataManager? GetDataManager() => _dm;

  /// <summary>
  /// 测试用：获取 OnAddAlias 委托
  /// </summary>
  public Action GetOnAlias() => OnAddAlias;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    AliasTree.Columns = 2;
    AliasTree.SetColumnTitle(0, "主名");
    AliasTree.SetColumnTitle(1, "别名");

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

  public void Refresh()
  {
    AliasTree.Clear();
    if (_dm == null)
      return;
    var root = AliasTree.CreateItem();
    AliasTree.HideRoot = true;
    foreach (var alias in _dm.Aliases)
    {
      var item = AliasTree.CreateItem(root);
      item.SetText(0, alias.MainName);
      item.SetEditable(0, true);
      item.SetText(1, string.Join(", ", alias.Aliases));
      item.SetEditable(1, true);
    }
  }

  private void OnAliasEdited()
  {
    var edited = AliasTree.GetEdited();
    var column = AliasTree.GetEditedColumn();
    if (edited == null || _dm == null)
      return;
    var parent = edited.GetParent();
    if (parent != AliasTree.GetRoot())
      return;
    var mainName = edited.GetText(0);
    var index = parent.GetIndex();
    if (index < 0 || index >= _dm.Aliases.Count)
      return;
    var alias = _dm.Aliases[index];
    if (column == 0)
      alias.MainName = mainName;
    else if (column == 1)
    {
      alias.Aliases.Clear();
      foreach (
        var a in mainName.Split(
          ',',
          StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        )
      )
        alias.Aliases.Add(a);
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

  private void OnAliasExportFileSelected(string path)
  {
    if (_dm == null)
      return;
    var sb = new StringBuilder();
    sb.AppendLine("主名,别名");
    foreach (var alias in _dm.Aliases)
      sb.AppendLine(
        CultureInfo.InvariantCulture,
        $"{StringEscapeHelper.EscapeCsv(alias.MainName)},{StringEscapeHelper.EscapeCsv(string.Join(", ", alias.Aliases))}"
      );
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

  private void OnAddAliasToCreator()
  {
    if (_dm == null || _dm.Aliases.Count == 0)
      return;
    _dm.Aliases[0].Aliases.Add("新别名");
    _dm.TriggerAutoSave();
  }

  private void OnDeleteSelected()
  {
    if (_dm == null)
      return;
    var selected = AliasTree.GetNextSelected(null);
    if (selected == null)
      return;
    var parent = selected.GetParent();
    if (parent != AliasTree.GetRoot())
      return;
    var mainName = selected.GetText(0);
    var toRemove = _dm.Aliases.Where(a => a.MainName == mainName).ToList();
    foreach (var alias in toRemove)
      _dm.Aliases.Remove(alias);
    _dm.TriggerAutoSave();
  }

  private void ShowError(string msg)
  {
    ErrorDialog.DialogText = msg;
    ErrorDialog.PopupCentered();
  }
}
