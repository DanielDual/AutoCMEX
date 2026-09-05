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
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 符卡表面板 — 独立场景，管理符卡表的展示、编辑和 CRUD。
/// UI 由 Sync 绑定驱动（AutoList/AutoValue 的 Bind() 自动推送变更），
/// 事件处理器只写数据模型，不做手动刷新。
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class SpellCardPanel : VBoxContainer
{
  [Node("%SpellCardTree")]
  public ITree SpellCardTree { get; set; } = default!;

  [Node("%BossSelect")]
  public IOptionButton BossSelect { get; set; } = default!;

  [Node("%ImportCardBtn")]
  public IButton ImportCardBtn { get; set; } = default!;

  [Node("%ExportCardBtn")]
  public IButton ExportCardBtn { get; set; } = default!;

  [Node("%AddBossBtn")]
  public IButton AddBossBtn { get; set; } = default!;

  [Node("%AddCardBtn")]
  public IButton AddCardBtn { get; set; } = default!;

  [Node("%DeleteBtn")]
  public IButton DeleteBtn { get; set; } = default!;

  [Node("%ImportFileDialog")]
  public IFileDialog ImportFileDialog { get; set; } = default!;

  [Node("%ExportFileDialog")]
  public IFileDialog ExportFileDialog { get; set; } = default!;

  [Node("%ErrorDialog")]
  public IAcceptDialog ErrorDialog { get; set; } = default!;

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  private DataManager? _dm;

  // 当前绑定的 Boss（用于切换其符卡列表的绑定）
  private Boss? _currentBoss;

  private AutoList<Boss>.Binding? _bossesBinding;

  // 选中下标绑定（单一数据源：AppSettings.SelectedBossIndex，与猜测流程共享）
  private AutoValue<int>.Binding? _selectedIndexBinding;

  private AutoList<SpellCard>.Binding? _spellCardsBinding;

  /// <summary>
  /// 获取当前使用的 DataManager 实例（供测试使用）
  /// </summary>
  public DataManager? GetDataManager() => _dm;

  /// <summary>
  /// 获取当前选中的 Boss（依据 AppSettings.SelectedBossIndex）
  /// </summary>
  public Boss? GetCurrentBoss()
  {
    if (_dm == null)
      return null;
    var index = _dm.Settings.SelectedBossIndex.Value;
    if (index < 0 || index >= _dm.Bosses.Count)
      return null;
    return _dm.Bosses[index];
  }

  /// <summary>
  /// 测试用：获取 OnAddSpellCard 委托
  /// </summary>
  public Action GetOnAddSpellCard() => OnAddSpellCard;

  /// <summary>
  /// 测试用：获取 OnAddBoss 委托
  /// </summary>
  public Action GetOnAddBoss() => OnAddBoss;

  /// <summary>
  /// 通过 Sync 模型选择 Boss。
  /// 仅写数据模型，UI 由 Sync 绑定或调用方显式 Refresh() 驱动（不在此处推 UI）。
  /// </summary>
  public void SelectBoss(int index)
  {
    if (_dm == null)
      return;
    _dm.Settings.SelectedBossIndex.Value = index;
  }

  /// <summary>
  /// 依据模型同步渲染 UI。运行时由 Sync 绑定自动触发，无需手动调用。
  /// </summary>
  public void Refresh()
  {
    ReconcileBosses();
    ReconcileSelection();
  }

  public override void _Notification(int what) => this.Notify(what);

  public override void _ExitTree()
  {
    _bossesBinding?.Dispose();
    _selectedIndexBinding?.Dispose();
    _spellCardsBinding?.Dispose();
  }

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

    // UI 由 Sync 绑定驱动：Boss 列表/选中下标/当前 Boss 符卡列表变化自动重渲染
    _bossesBinding = _dm.Bosses.Bind().OnModify(() => CallDeferred(nameof(Refresh)));

    _selectedIndexBinding = _dm
      .Settings.SelectedBossIndex.Bind()
      .OnValue(_ => CallDeferred(nameof(Refresh)));

    Refresh();
  }

  // ==================== Sync 驱动渲染 ====================

  /// <summary>
  /// Boss 列表变化时：重填下拉，并把选中下标规范到合法区间。
  /// </summary>
  private void ReconcileBosses()
  {
    if (_dm == null)
      return;

    RenderBossSelect();

    var count = _dm.Bosses.Count;
    var index = _dm.Settings.SelectedBossIndex.Value;
    if (count == 0)
    {
      if (index != -1)
        _dm.Settings.SelectedBossIndex.Value = -1;
      return;
    }
    // 越界（含负值）统一回落到首个 Boss 下标 0
    if (index < 0 || index >= count)
      _dm.Settings.SelectedBossIndex.Value = 0;
  }

  private void RenderBossSelect()
  {
    if (_dm == null)
      return;
    BossSelect.Clear();
    for (int i = 0; i < _dm.Bosses.Count; i++)
      BossSelect.AddItem(_dm.Bosses[i].Name);
  }

  /// <summary>
  /// 选中下标变化时：同步下拉高亮、切换当前 Boss 的符卡绑定并渲染树。
  /// </summary>
  private void ReconcileSelection()
  {
    if (_dm == null)
      return;

    var count = _dm.Bosses.Count;
    var index = _dm.Settings.SelectedBossIndex.Value;
    var currentBoss = index >= 0 && index < count ? _dm.Bosses[index] : null;

    if (index >= 0 && BossSelect.Selected != index)
      BossSelect.Select(index);

    // 切换 Boss 时才重建其符卡列表绑定
    if (currentBoss != _currentBoss)
    {
      _spellCardsBinding?.Dispose();
      _spellCardsBinding = null;
      _currentBoss = currentBoss;
      if (currentBoss != null)
      {
        _spellCardsBinding = currentBoss
          .SpellCards.Bind()
          .OnModify(() => CallDeferred(nameof(ReconcileSelection)));
      }
    }

    RefreshSpellCardTree();
  }

  private void RefreshSpellCardTree()
  {
    if (_dm == null)
      return;
    SpellCardTree.Clear();
    var currentBoss = GetCurrentBoss();
    if (currentBoss == null)
      return;
    var root = SpellCardTree.CreateItem();
    SpellCardTree.HideRoot = true;
    var bossItem = SpellCardTree.CreateItem(root);
    bossItem.SetText(0, currentBoss.Name);
    bossItem.SetEditable(0, true);
    bossItem.SetMetadata(0, -1);
    for (int i = 0; i < currentBoss.SpellCards.Count; i++)
    {
      var card = currentBoss.SpellCards[i];
      var cardItem = SpellCardTree.CreateItem(bossItem);
      cardItem.SetText(0, card.Name.Value);
      cardItem.SetText(
        1,
        string.IsNullOrEmpty(card.Creator.Value) ? "(未揭晓)" : card.Creator.Value
      );
      cardItem.SetCellMode(2, TreeItem.TreeCellMode.Check);
      cardItem.SetChecked(2, card.IsGuessedOut.Value);
      cardItem.SetEditable(0, true);
      cardItem.SetEditable(1, true);
      cardItem.SetEditable(2, true);
      cardItem.SetMetadata(0, i);
    }
  }

  // ==================== 事件处理器（只写数据模型） ====================

  private void OnBossSelected(long index)
  {
    if (_dm == null)
      return;
    if (index < 0 || index >= _dm.Bosses.Count)
      return;
    // 值未变化则跳过，避免程序化 Select 等触发多余写入/自动保存
    if (_dm.Settings.SelectedBossIndex.Value == (int)index)
      return;
    _dm.Settings.SelectedBossIndex.Value = (int)index;
    _dm.TriggerAutoSave();
  }

  private void OnSpellCardEdited()
  {
    var edited = SpellCardTree.GetEdited();
    var column = SpellCardTree.GetEditedColumn();
    var currentBoss = GetCurrentBoss();
    if (edited == null || currentBoss == null)
      return;
    var parent = edited.GetParent();
    var metaIndex = edited.GetMetadata(0).AsInt32();
    if (parent == SpellCardTree.GetRoot() || metaIndex == -1)
    {
      currentBoss.Name = edited.GetText(0);
      var idx = _dm?.Bosses.IndexOf(currentBoss) ?? -1;
      if (idx >= 0)
        BossSelect.SetItemText(idx, currentBoss.Name);
    }
    else if (metaIndex >= 0 && metaIndex < currentBoss.SpellCards.Count)
    {
      var card = currentBoss.SpellCards[metaIndex];
      if (column == 0)
        card.Name.Value = edited.GetText(0);
      else if (column == 1)
      {
        var t = edited.GetText(1);
        card.Creator.Value = t == "(未揭晓)" ? "" : t;
      }
      else if (column == 2)
      {
        card.IsGuessedOut.Value = edited.IsChecked(2);
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
          StringEscapeHelper.EscapeCsv(card.Name.Value),
          StringEscapeHelper.EscapeCsv(card.Creator.Value)
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
    if (_dm == null)
      return;
    var currentBoss = GetCurrentBoss();
    if (currentBoss == null)
    {
      ShowError("请先选择 Boss");
      return;
    }
    currentBoss.SpellCards.Add(new SpellCard { Name = new AutoValue<string>("新符卡") });
    _dm.TriggerAutoSave();
  }

  private void OnDeleteSelected()
  {
    if (_dm == null)
      return;
    var currentBoss = GetCurrentBoss();
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
    else if (currentBoss != null)
    {
      var index = selected.GetMetadata(0).AsInt32();
      if (index >= 0 && index < currentBoss.SpellCards.Count)
        currentBoss.SpellCards.RemoveAt(index);
    }
    _dm.TriggerAutoSave();
  }

  private void ShowError(string msg)
  {
    ErrorDialog.DialogText = msg;
    ErrorDialog.PopupCentered();
  }
}
