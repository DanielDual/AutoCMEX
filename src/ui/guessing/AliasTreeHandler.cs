namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using AutoCMEX;
using AutoCMEX.Core.Storage;
using AutoCMEX.Helpers;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 别名表 Tree 子节点脚本：管理别名表的展示、编辑和 CRUD
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class AliasTreeHandler : Control
{
  [Node("AliasTree")]
  public Tree AliasTree { get; set; } = default!;

  [Node("AliasButtons/ImportAliasBtn")]
  public Button ImportAliasBtn { get; set; } = default!;

  [Node("AliasButtons/ExportAliasBtn")]
  public Button ExportAliasBtn { get; set; } = default!;

  [Node("AliasButtons/AddAliasBtn")]
  public Button AddAliasBtn { get; set; } = default!;

  [Node("AliasButtons/AddAliasToCreatorBtn")]
  public Button AddAliasToCreatorBtn { get; set; } = default!;

  [Node("AliasButtons/DeleteAliasBtn")]
  public Button DeleteAliasBtn { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>(() => null!);

  private DataManager? _dm;
  private readonly HashSet<CreatorAlias> _subscribedCreators = new();
  private bool _rebuildingAliasTree;

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
    AliasTree.Columns = 1;
    AliasTree.SetColumnTitle(0, "创作者 / 别名");
    AliasTree.HideRoot = true;

    AliasTree.ItemEdited += OnAliasEdited;
    ImportAliasBtn.Pressed += OnImportAliasTable;
    ExportAliasBtn.Pressed += OnExportAliasTable;
    AddAliasBtn.Pressed += OnAddAlias;
    AddAliasToCreatorBtn.Pressed += OnAddAliasToCreator;
    DeleteAliasBtn.Pressed += OnDeleteAlias;
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm == null)
      return;
    _dm.Aliases.CollectionChanged += (_, _) => Refresh();
    Refresh();
  }

  public void Refresh()
  {
    RefreshAliasTree();
  }

  private void RefreshAliasTree()
  {
    UnsubscribeAllCreators();
    _rebuildingAliasTree = true;
    AliasTree.ItemEdited -= OnAliasEdited;
    AliasTree.Clear();

    if (_dm != null)
    {
      var root = AliasTree.CreateItem();
      for (int i = 0; i < _dm.Aliases.Count; i++)
      {
        var creator = _dm.Aliases[i];
        SubscribeToCreator(creator);
        var creatorItem = AliasTree.CreateItem(root);
        creatorItem.SetText(0, creator.MainName);
        creatorItem.SetEditable(0, true);
        creatorItem.SetMetadata(0, i);

        for (int j = 0; j < creator.Aliases.Count; j++)
        {
          var aliasItem = AliasTree.CreateItem(creatorItem);
          aliasItem.SetText(0, creator.Aliases[j]);
          aliasItem.SetEditable(0, true);
          aliasItem.SetMetadata(0, i);
        }
      }
    }

    AliasTree.ItemEdited += OnAliasEdited;
    _rebuildingAliasTree = false;
  }

  private void SubscribeToCreator(CreatorAlias creator)
  {
    if (_subscribedCreators.Add(creator))
      creator.Aliases.CollectionChanged += OnCreatorAliasesChanged;
  }

  private void OnCreatorAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
    RefreshAliasTree();

  private void UnsubscribeAllCreators()
  {
    foreach (var creator in _subscribedCreators)
      creator.Aliases.CollectionChanged -= OnCreatorAliasesChanged;
    _subscribedCreators.Clear();
  }

  private void OnAliasEdited()
  {
    if (_rebuildingAliasTree || _dm == null)
      return;
    var edited = AliasTree.GetEdited();
    if (edited == null)
      return;
    var column = AliasTree.GetEditedColumn();
    if (column != 0)
      return;

    var creatorIdx = edited.GetMetadata(0).AsInt32();
    if (creatorIdx < 0 || creatorIdx >= _dm.Aliases.Count)
      return;

    if (edited.GetChildCount() > 0)
    {
      _dm.Aliases[creatorIdx].MainName = edited.GetText(0);
    }
    else
    {
      var parent = edited.GetParent();
      if (parent == null)
        return;
      var aliasIdx = edited.GetIndex();
      if (aliasIdx >= 0 && aliasIdx < _dm.Aliases[creatorIdx].Aliases.Count)
        _dm.Aliases[creatorIdx].Aliases[aliasIdx] = edited.GetText(0);
    }

    _dm.TriggerAutoSave();
  }

  private void OnImportAliasTable()
  {
    var d = new FileDialog();
    d.FileMode = FileDialog.FileModeEnum.OpenFile;
    d.Access = FileDialog.AccessEnum.Filesystem;
    d.AddFilter("*.csv, *.xlsx", "*.csv, *.xlsx");
    d.AddFilter("*.csv", "*.csv");
    d.AddFilter("*.xlsx", "*.xlsx");
    d.FileSelected += OnAliasFileSelected;
    AddChild(d);
    d.PopupCentered();
  }

  private void OnExportAliasTable()
  {
    var d = new FileDialog();
    d.FileMode = FileDialog.FileModeEnum.SaveFile;
    d.Access = FileDialog.AccessEnum.Filesystem;
    d.AddFilter("*.csv", "*.csv");
    d.FileSelected += OnAliasExportFileSelected;
    AddChild(d);
    d.PopupCentered();
  }

  private void OnAliasExportFileSelected(string path)
  {
    if (_dm == null)
      return;
    int maxAliases = 0;
    foreach (var a in _dm.Aliases)
    {
      if (a.Aliases.Count > maxAliases)
        maxAliases = a.Aliases.Count;
    }
    var sb = new StringBuilder();
    var header = "主名";
    for (int i = 1; i <= maxAliases; i++)
      header += $",别名{i}";
    sb.AppendLine(header);
    foreach (var a in _dm.Aliases)
    {
      var line = StringEscapeHelper.EscapeCsv(a.MainName);
      for (int i = 0; i < maxAliases; i++)
      {
        line += ",";
        if (i < a.Aliases.Count)
          line += StringEscapeHelper.EscapeCsv(a.Aliases[i]);
      }
      sb.AppendLine(line);
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
  }

  private void OnDeleteAlias()
  {
    if (_dm == null)
      return;
    var selected = AliasTree.GetNextSelected(null);
    if (selected == null)
      return;
    var creatorIdx = selected.GetMetadata(0).AsInt32();
    if (creatorIdx < 0 || creatorIdx >= _dm.Aliases.Count)
      return;

    if (selected.GetChildCount() > 0)
      _dm.Aliases.RemoveAt(creatorIdx);
    else
    {
      var aliasIdx = selected.GetIndex();
      if (aliasIdx >= 0 && aliasIdx < _dm.Aliases[creatorIdx].Aliases.Count)
        _dm.Aliases[creatorIdx].Aliases.RemoveAt(aliasIdx);
    }
    _dm.TriggerAutoSave();
  }

  private void ShowError(string msg)
  {
    var d = new AcceptDialog();
    d.Title = "错误";
    d.DialogText = msg;
    AddChild(d);
    d.PopupCentered();
  }
}
