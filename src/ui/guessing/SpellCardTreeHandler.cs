namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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
using Godot;

/// <summary>
/// 符卡表 Tree 子节点脚本：管理符卡表的展示、编辑和 CRUD
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class SpellCardTreeHandler : Control
{
  [Node("SpellCardTree")]
  public Tree SpellCardTree { get; set; } = default!;

  [Node("../../../BossSelect")]
  public OptionButton BossSelect { get; set; } = default!;

  [Node("SpellCardButtons/ImportCardBtn")]
  public Button ImportCardBtn { get; set; } = default!;

  [Node("SpellCardButtons/ExportCardBtn")]
  public Button ExportCardBtn { get; set; } = default!;

  [Node("SpellCardButtons/AddBossBtn")]
  public Button AddBossBtn { get; set; } = default!;

  [Node("SpellCardButtons/AddCardBtn")]
  public Button AddCardBtn { get; set; } = default!;

  [Node("SpellCardButtons/DeleteBtn")]
  public Button DeleteBtn { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>(() => null!);

  private DataManager? _dm;
  private Boss? _currentBoss;
  private Boss? _subscribedBoss;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    SpellCardTree.Columns = 3;
    SpellCardTree.SetColumnTitle(0, "符卡名");
    SpellCardTree.SetColumnTitle(1, "创作者");
    SpellCardTree.SetColumnTitle(2, "已猜出");

    SpellCardTree.ItemEdited += OnSpellCardEdited;
    BossSelect.ItemSelected += OnBossSelected;
    ImportCardBtn.Pressed += OnImportCardTable;
    ExportCardBtn.Pressed += OnExportCardTable;
    AddBossBtn.Pressed += OnAddBoss;
    AddCardBtn.Pressed += OnAddSpellCard;
    DeleteBtn.Pressed += OnDeleteSelected;
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm != null)
    {
      _dm.Bosses.CollectionChanged += (_, _) => CallDeferred(nameof(Refresh));
      Refresh();
    }
  }

  public void Refresh()
  {
    RefreshBossSelect();
    RefreshSpellCardTree();
  }

  private void RefreshBossSelect()
  {
    BossSelect.Clear();
    if (_dm == null)
      return;
    foreach (var boss in _dm.Bosses)
      BossSelect.AddItem(boss.Name);
    if (_dm.Bosses.Count > 0)
    {
      var selectedIndex = _dm.Settings.SelectedBossIndex;
      if (selectedIndex < 0 || selectedIndex >= _dm.Bosses.Count)
      {
        selectedIndex = 0;
        _dm.Settings.SelectedBossIndex = 0;
      }
      BossSelect.Select(selectedIndex);
      SubscribeToCurrentBoss(_dm.Bosses[selectedIndex]);
    }
    else
      SubscribeToCurrentBoss(null);
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

  private void SubscribeToCurrentBoss(Boss? boss)
  {
    if (_subscribedBoss == boss)
      return;

    if (_subscribedBoss != null)
    {
      _subscribedBoss.SpellCards.CollectionChanged -= OnSpellCardsChanged;
      foreach (var card in _subscribedBoss.SpellCards)
        card.PropertyChanged -= OnSpellCardPropertyChanged;
    }

    _subscribedBoss = boss;
    _currentBoss = boss;

    if (_subscribedBoss != null)
    {
      _subscribedBoss.SpellCards.CollectionChanged += OnSpellCardsChanged;
      foreach (var card in _subscribedBoss.SpellCards)
        card.PropertyChanged += OnSpellCardPropertyChanged;
    }
  }

  private void OnSpellCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
  {
    if (e.Action == NotifyCollectionChangedAction.Add && _subscribedBoss != null)
    {
      foreach (SpellCard card in e.NewItems!)
        card.PropertyChanged += OnSpellCardPropertyChanged;
    }
    else if (e.Action == NotifyCollectionChangedAction.Remove && _subscribedBoss != null)
    {
      foreach (SpellCard card in e.OldItems!)
        card.PropertyChanged -= OnSpellCardPropertyChanged;
    }
    CallDeferred(nameof(RefreshSpellCardTree));
  }

  private void OnSpellCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (sender is not SpellCard card || _currentBoss == null)
      return;

    var index = _currentBoss.SpellCards.IndexOf(card);
    if (index < 0)
      return;

    var root = SpellCardTree.GetRoot();
    var bossItem = root?.GetChild(0);
    var cardItem = bossItem?.GetChild(index);
    if (cardItem == null)
      return;

    switch (e.PropertyName)
    {
      case nameof(SpellCard.Name):
        cardItem.SetText(0, card.Name);
        break;
      case nameof(SpellCard.Creator):
        cardItem.SetText(1, string.IsNullOrEmpty(card.Creator) ? "(未揭晓)" : card.Creator);
        break;
      case nameof(SpellCard.IsGuessedOut):
        cardItem.SetChecked(2, card.IsGuessedOut);
        break;
    }
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
      var idx = _dm?.Bosses.IndexOf(_currentBoss) ?? -1;
      if (idx >= 0)
        BossSelect.SetItemText(idx, _currentBoss.Name);
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

  private void OnBossSelected(long index)
  {
    if (_dm != null && index >= 0 && index < _dm.Bosses.Count)
    {
      _dm.Settings.SelectedBossIndex = (int)index;
      SubscribeToCurrentBoss(_dm.Bosses[(int)index]);
      _dm.TriggerAutoSave();
    }
  }

  private void OnImportCardTable()
  {
    var d = new FileDialog();
    d.FileMode = FileDialog.FileModeEnum.OpenFile;
    d.Access = FileDialog.AccessEnum.Filesystem;
    d.AddFilter("*.csv, *.xlsx", "*.csv, *.xlsx");
    d.AddFilter("*.csv", "*.csv");
    d.AddFilter("*.xlsx", "*.xlsx");
    d.FileSelected += OnCardFileSelected;
    AddChild(d);
    d.PopupCentered();
  }

  private void OnExportCardTable()
  {
    var d = new FileDialog();
    d.FileMode = FileDialog.FileModeEnum.SaveFile;
    d.Access = FileDialog.AccessEnum.Filesystem;
    d.AddFilter("*.csv", "*.csv");
    d.FileSelected += OnCardExportFileSelected;
    AddChild(d);
    d.PopupCentered();
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
        CultureInfo.InvariantCulture,
        $"{StringEscapeHelper.EscapeCsv(boss.Name)},{StringEscapeHelper.EscapeCsv(card.Name)},{StringEscapeHelper.EscapeCsv(card.Creator)}"
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
    var d = new AcceptDialog();
    d.Title = "错误";
    d.DialogText = msg;
    AddChild(d);
    d.PopupCentered();
  }
}
