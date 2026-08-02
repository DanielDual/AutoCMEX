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
/// 符卡表面板 — 独立场景，管理符卡表的展示、编辑和 CRUD
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class SpellCardPanel : VBoxContainer
{
  [Node("%SpellCardTree")]
  public Tree SpellCardTree { get; set; } = default!;

  [Node("%ImportCardBtn")]
  public Button ImportCardBtn { get; set; } = default!;

  [Node("%ExportCardBtn")]
  public Button ExportCardBtn { get; set; } = default!;

  [Node("%AddBossBtn")]
  public Button AddBossBtn { get; set; } = default!;

  [Node("%AddCardBtn")]
  public Button AddCardBtn { get; set; } = default!;

  [Node("%DeleteBtn")]
  public Button DeleteBtn { get; set; } = default!;

  [Node("%ImportFileDialog")]
  public FileDialog ImportFileDialog { get; set; } = default!;

  [Node("%ExportFileDialog")]
  public FileDialog ExportFileDialog { get; set; } = default!;

  [Node("%ErrorDialog")]
  public AcceptDialog ErrorDialog { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  private DataManager? _dm;
  private Boss? _currentBoss;
  private AutoList<Boss>.Binding? _bossesBinding;
  private AutoList<SpellCard>.Binding? _spellCardsBinding;

  /// <summary>
  /// 获取当前使用的 DataManager 实例（供测试使用）
  /// </summary>
  public DataManager? GetDataManager() => _dm;

  /// <summary>
  /// 测试用：获取 OnAddSpellCard 委托
  /// </summary>
  public Action GetOnAddSpellCard() => OnAddSpellCard;

  /// <summary>
  /// 测试用：获取 OnAddBoss 委托
  /// </summary>
  public Action GetOnAddBoss() => OnAddBoss;

  /// <summary>
  /// 测试用：获取 SelectBoss 委托
  /// </summary>
  public Action<Boss?> GetSelectBoss() => SelectBoss;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    SpellCardTree.Columns = 3;
    SpellCardTree.SetColumnTitle(0, "符卡名");
    SpellCardTree.SetColumnTitle(1, "创作者");
    SpellCardTree.SetColumnTitle(2, "已猜出");

    SpellCardTree.ItemEdited += OnSpellCardEdited;
    ImportCardBtn.Pressed += OnImportCardTable;
    ExportCardBtn.Pressed += OnExportCardTable;
    AddBossBtn.Pressed += OnAddBoss;
    AddCardBtn.Pressed += OnAddSpellCard;
    DeleteBtn.Pressed += OnDeleteSelected;

    // 配置预置对话框
    ImportFileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
    ImportFileDialog.Access = FileDialog.AccessEnum.Filesystem;
    ImportFileDialog.AddFilter("*.csv, *.xlsx", "*.csv, *.xlsx");
    ImportFileDialog.AddFilter("*.csv", "*.csv");
    ImportFileDialog.AddFilter("*.xlsx", "*.xlsx");
    ImportFileDialog.FileSelected += OnCardFileSelected;

    ExportFileDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
    ExportFileDialog.Access = FileDialog.AccessEnum.Filesystem;
    ExportFileDialog.AddFilter("*.csv", "*.csv");
    ExportFileDialog.FileSelected += OnCardExportFileSelected;

    ErrorDialog.Title = "错误";
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm == null)
      return;

    _bossesBinding = _dm.Bosses.Bind().OnModify(() => CallDeferred(nameof(Refresh)));

    Refresh();
  }

  public void Refresh()
  {
    RefreshBossSelect();
    RefreshSpellCardTree();
  }

  private void RefreshBossSelect()
  {
    if (_dm == null)
      return;
  }

  private void RefreshSpellCardTree()
  {
    SpellCardTree.Clear();
    if (_currentBoss == null)
      return;
    var root = SpellCardTree.CreateItem();
    SpellCardTree.HideRoot = true;
    var bossItem = SpellCardTree.CreateItem(root);
    bossItem.SetText(0, _currentBoss.Name);
    bossItem.SetEditable(0, true);
    bossItem.SetMetadata(0, -1);
    for (int i = 0; i < _currentBoss.SpellCards.Count; i++)
    {
      var card = _currentBoss.SpellCards[i];
      var cardItem = SpellCardTree.CreateItem(bossItem);
      cardItem.SetText(0, card.Name);
      cardItem.SetText(1, string.IsNullOrEmpty(card.Creator) ? "(未揭晓)" : card.Creator);
      cardItem.SetCellMode(2, TreeItem.TreeCellMode.Check);
      cardItem.SetChecked(2, card.IsGuessedOut);
      cardItem.SetEditable(0, true);
      cardItem.SetEditable(1, true);
      cardItem.SetEditable(2, true);
      cardItem.SetMetadata(0, i);
    }
  }

  private void SelectBoss(Boss? boss)
  {
    _spellCardsBinding?.Dispose();
    _spellCardsBinding = null;
    _currentBoss = boss;
    if (boss != null)
    {
      _spellCardsBinding = boss
        .SpellCards.Bind()
        .OnModify(() => CallDeferred(nameof(RefreshSpellCardTree)));
    }
    RefreshSpellCardTree();
  }

  private void OnSpellCardEdited()
  {
    var edited = SpellCardTree.GetEdited();
    var column = SpellCardTree.GetEditedColumn();
    if (edited == null || _currentBoss == null)
      return;
    var parent = edited.GetParent();
    var metaIndex = edited.GetMetadata(0).AsInt32();
    if (parent == SpellCardTree.GetRoot() || metaIndex == -1)
    {
      _currentBoss.Name = edited.GetText(0);
    }
    else if (metaIndex >= 0 && metaIndex < _currentBoss.SpellCards.Count)
    {
      var card = _currentBoss.SpellCards[metaIndex];
      if (column == 0)
        card.Name = edited.GetText(0);
      else if (column == 1)
      {
        var t = edited.GetText(1);
        card.Creator = t == "(未揭晓)" ? "" : t;
      }
      else if (column == 2)
      {
        card.IsGuessedOut = edited.IsChecked(2);
      }
    }
    _dm?.TriggerAutoSave();
  }

  private void OnImportCardTable()
  {
    ImportFileDialog.PopupCentered();
  }

  private void OnExportCardTable()
  {
    ExportFileDialog.PopupCentered();
  }

  private void OnCardExportFileSelected(string path)
  {
    if (_dm == null)
      return;
    var sb = new StringBuilder();
    sb.AppendLine("Boss,符卡名,创作者");
    foreach (var boss in _dm.Bosses)
    foreach (var card in boss.SpellCards)
      sb.AppendLine(
        string.Format(
          "{0},{1},{2}",
          StringEscapeHelper.EscapeCsv(boss.Name),
          StringEscapeHelper.EscapeCsv(card.Name),
          StringEscapeHelper.EscapeCsv(card.Creator)
        )
      );
    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private void OnCardFileSelected(string path)
  {
    if (_dm == null)
      return;
    var importer = ImporterFactory.Create(path);
    ImportResult<List<Boss>> result = importer.ImportSpellCardTable(path);
    if (!result.IsSuccess)
    {
      ShowError(result.ErrorMessage);
      return;
    }
    _dm.Bosses.Clear();
    foreach (var boss in result.Data!)
      _dm.Bosses.Add(boss);
    _dm.TriggerAutoSave();
  }

  private void OnAddBoss()
  {
    if (_dm == null)
      return;
    _dm.Bosses.Add(new Boss { Name = "新 Boss" });
    _dm.TriggerAutoSave();
  }

  private void OnAddSpellCard()
  {
    if (_currentBoss == null)
    {
      ShowError("请先选择 Boss");
      return;
    }
    _currentBoss.SpellCards.Add(new SpellCard { Name = "新符卡" });
    _dm?.TriggerAutoSave();
  }

  private void OnDeleteSelected()
  {
    if (_dm == null)
      return;
    var selected = SpellCardTree.GetNextSelected(null);
    if (selected == null)
      return;
    var parent = selected.GetParent();
    if (parent == null || parent == SpellCardTree.GetRoot())
    {
      var toRemove = _dm.Bosses.Where(b => b.Name == selected.GetText(0)).ToList();
      foreach (var boss in toRemove)
        _dm.Bosses.Remove(boss);
    }
    else
    {
      var index = selected.GetMetadata(0).AsInt32();
      if (_currentBoss != null && index >= 0 && index < _currentBoss.SpellCards.Count)
        _currentBoss.SpellCards.RemoveAt(index);
    }
    _dm.TriggerAutoSave();
  }

  private void ShowError(string msg)
  {
    ErrorDialog.DialogText = msg;
    ErrorDialog.PopupCentered();
  }
}
