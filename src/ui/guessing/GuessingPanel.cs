namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Helpers;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 猜测板块脚本 - 支持符卡表和别名表的内联编辑
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class GuessingPanel : Control
{
  #region AutoConnect Nodes

  [Node]
  public OptionButton BossSelect { get; set; } = default!;

  [Node]
  public Tree SpellCardTree { get; set; } = default!;

  [Node]
  public Button ImportCardBtn { get; set; } = default!;

  [Node]
  public Button ExportCardBtn { get; set; } = default!;

  [Node]
  public Button AddBossBtn { get; set; } = default!;

  [Node]
  public Button AddCardBtn { get; set; } = default!;

  [Node]
  public Button DeleteBtn { get; set; } = default!;

  [Node]
  public Tree AliasTree { get; set; } = default!;

  [Node]
  public Button ImportAliasBtn { get; set; } = default!;

  [Node]
  public Button ExportAliasBtn { get; set; } = default!;

  [Node]
  public Button AddAliasBtn { get; set; } = default!;

  [Node]
  public Button AddAliasToCreatorBtn { get; set; } = default!;

  [Node]
  public Button DeleteAliasBtn { get; set; } = default!;

  [Node]
  public TextEdit GuessInput { get; set; } = default!;

  [Node]
  public Button FuzzifyBtn { get; set; } = default!;

  [Node]
  public Button ProcessBtn { get; set; } = default!;

  [Node]
  public RichTextLabel ResponseDisplay { get; set; } = default!;

  #endregion

  #region Dropped UI Nodes

  [Node]
  public ItemList DroppedList { get; set; } = default!;

  [Node]
  public Button RetryDroppedBtn { get; set; } = default!;

  [Node]
  public Button ClearDroppedBtn { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager =>
    this.DependOn<DataManager>(() =>
    {
      // 尝试多个路径确保 fallback 不会抛出
      string[] dirs = { Path.Combine(Path.GetTempPath(), "AutoCMEX_Fallback"), Path.GetTempPath() };
      foreach (var dir in dirs)
      {
        try
        {
          Directory.CreateDirectory(dir);
          return new DataManager(dir, new AesEncryptor(Path.Combine(dir, "key.bin")));
        }
        catch (Exception ex)
        {
          GD.PrintErr($"[GuessingPanel] Fallback attempt {dir}: {ex.Message}");
        }
      }
      // 最终兜底：使用内存中的临时路径
      var tmpDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tmpDir);
      return new DataManager(tmpDir, new AesEncryptor(Path.Combine(tmpDir, "key.bin")));
    });

  [Dependency]
  public AiServiceFactory AiServiceFactory =>
    this.DependOn<AiServiceFactory>(() => new AiServiceFactory(DataManager));

  [Dependency]
  public IGuessProcessingService GuessProcessingService =>
    this.DependOn<IGuessProcessingService>(() =>
      new GuessProcessingService(DataManager, AiServiceFactory, new GuessResponseHandler())
    );

  #endregion

  private DataManager? _dm;
  private IGuessProcessingService? _guessProcessingService;
  private Boss? _currentBoss;
  private Boss? _subscribedBoss;
  private readonly HashSet<CreatorAlias> _subscribedCreators = new();
  private bool _rebuildingAliasTree;
  private ILog _log = AppLogs.GetOrCreate().GetLogger(nameof(GuessingPanel));

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

  private void SubscribeToCreator(CreatorAlias creator)
  {
    if (_subscribedCreators.Add(creator))
      creator.Aliases.CollectionChanged += OnCreatorAliasesChanged;
  }

  private void OnCreatorAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
    CallDeferred(nameof(RefreshAliasTree));

  private void UnsubscribeAllCreators()
  {
    foreach (var creator in _subscribedCreators)
      creator.Aliases.CollectionChanged -= OnCreatorAliasesChanged;
    _subscribedCreators.Clear();
  }

  public override void _Notification(int what)
  {
    if (what == NotificationVisibilityChanged && Visible)
    {
      UpdateFuzzifyButtonState();
      RefreshDroppedUI();
    }
    this.Notify(what);
  }

  public void OnReady()
  {
    BossSelect.ItemSelected += OnBossSelected;
    ImportCardBtn.Pressed += OnImportCardTable;
    ExportCardBtn.Pressed += OnExportCardTable;
    AddBossBtn.Pressed += OnAddBoss;
    AddCardBtn.Pressed += OnAddSpellCard;
    DeleteBtn.Pressed += OnDeleteSelected;
    SpellCardTree.ItemEdited += OnSpellCardEdited;

    ImportAliasBtn.Pressed += OnImportAliasTable;
    ExportAliasBtn.Pressed += OnExportAliasTable;
    AddAliasBtn.Pressed += OnAddAlias;
    AddAliasToCreatorBtn.Pressed += OnAddAliasToCreator;
    DeleteAliasBtn.Pressed += OnDeleteAlias;
    AliasTree.ItemEdited += OnAliasEdited;

    ProcessBtn.Pressed += OnProcessGuess;
    FuzzifyBtn.Pressed += OnFuzzify;

    SpellCardTree.Columns = 3;
    SpellCardTree.SetColumnTitle(0, "符卡名");
    SpellCardTree.SetColumnTitle(1, "创作者");
    SpellCardTree.SetColumnTitle(2, "已猜出");

    AliasTree.Columns = 1;
    AliasTree.SetColumnTitle(0, "创作者 / 别名");
    AliasTree.HideRoot = true;

    FuzzifyBtn.Disabled = true;
    FuzzifyBtn.TooltipText = "请先配置 AI 模型";

    // 丢包重试 UI 信号连接
    RetryDroppedBtn.Pressed += OnRetryAllDropped;
    ClearDroppedBtn.Pressed += OnClearDropped;
  }

  public void OnResolved()
  {
    try
    {
      _dm = DataManager;
    }
    catch (Exception ex)
    {
      GD.PrintErr($"[GuessingPanel] Resolve DataManager: {ex.Message}");
      _dm = null;
    }
    try
    {
      _guessProcessingService = GuessProcessingService;
    }
    catch
    {
      _guessProcessingService = null;
    }

    if (_dm != null)
    {
      _dm.LoadAll();
      // 订阅 ObservableCollection 的 CollectionChanged 事件实现自动 UI 更新
      _dm.Bosses.CollectionChanged += (_, _) => CallDeferred(nameof(RefreshSpellCardTree));
      _dm.Aliases.CollectionChanged += (_, _) => CallDeferred(nameof(RefreshAliasTree));
      _dm.DataChanged += () => CallDeferred(nameof(RefreshSpellCardTree));
      UpdateFuzzifyButtonState();
      RefreshAll();
    }

    RefreshDroppedUI();
  }

  private void UpdateFuzzifyButtonState()
  {
    var hasAi = false;
    if (_dm != null)
    {
      var activeId = _dm.Settings.ActiveAiModelId;
      if (!string.IsNullOrEmpty(activeId))
      {
        var activeModel = _dm.Settings.AiModels.Find(m => m.Id == activeId);
        hasAi = activeModel != null && AiServiceFactory.IsModelValid(activeModel);
      }
    }
    FuzzifyBtn.Disabled = !hasAi;
    FuzzifyBtn.TooltipText = hasAi ? "使用 AI 模糊化" : "请先在设置中选择一个有效的 AI 模型";
  }

  public void RefreshAll()
  {
    RefreshBossSelect();
    RefreshSpellCardTree();
    RefreshAliasTree();
  }

  /// <summary>
  /// 注入测试数据管理器并刷新 UI。仅供测试使用。
  /// </summary>
  public void InjectTestData(DataManager dm)
  {
    _dm = dm;
    UpdateFuzzifyButtonState();
    RefreshAll();
  }

  // ==================== 符卡表 ====================

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
    _log.Print("GuessingPanel: user requested import spellcard table.");
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
    _log.Print("GuessingPanel: user requested export spellcard table.");
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
    _log.Print("GuessingPanel: user added a new Boss.");
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
    _log.Print($"GuessingPanel: user added spellcard to boss '{_currentBoss.Name}'.");
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

  // ==================== 别名表 ====================

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

    // 有子行 → 创作者行；无子行 → 别名行
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
    _log.Print("GuessingPanel: user requested import alias table.");
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

    // 有子行 → 删除整个创作者；无子行 → 删除单个别名
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

  // ==================== 猜测处理 ====================

  private async void OnProcessGuess()
  {
    if (_currentBoss == null)
    {
      _log.Warn("OnProcessGuess: no current boss selected.");
      ResponseDisplay.Text = "[color=red]请先选择 Boss[/color]";
      return;
    }
    var text = GuessInput.Text.Trim();
    if (string.IsNullOrEmpty(text))
    {
      _log.Warn("OnProcessGuess: empty input.");
      ResponseDisplay.Text = "[color=red]请输入猜测文本[/color]";
      return;
    }
    _log.Print(
      $"OnProcessGuess: processing guess (len={text.Length}) for boss '{_currentBoss.Name}'."
    );
    var service =
      _guessProcessingService
      ?? (
        _dm != null
          ? new GuessProcessingService(_dm, AiServiceFactory, new GuessResponseHandler())
          : GuessProcessingService
      );

    var result = await service.ProcessAsync(text);
    RefreshDroppedUI();
    if (!result.IsGuess)
    {
      _log.Warn($"OnProcessGuess: processing returned failure: {result.FailureReason}");
      ResponseDisplay.Text = $"[color=red]{result.FailureReason}[/color]";
      return;
    }
    var display = "";
    if (!string.IsNullOrEmpty(result.ReplyText))
      display += $"[b]{result.ReplyText}[/b]\n\n";
    foreach (var d in result.Details)
      display += $"{d}\n";
    ResponseDisplay.Text = display;
  }

  private async void OnFuzzify()
  {
    if (_currentBoss == null)
    {
      _log.Warn("OnFuzzify: no current boss selected.");
      ResponseDisplay.Text = "[color=red]请先选择 Boss[/color]";
      return;
    }
    var text = GuessInput.Text.Trim();
    if (string.IsNullOrEmpty(text))
    {
      _log.Warn("OnFuzzify: empty input.");
      ResponseDisplay.Text = "[color=red]请输入猜测文本[/color]";
      return;
    }
    if (_dm == null)
    {
      _log.Warn("OnFuzzify: DataManager not available.");
      ResponseDisplay.Text = "[color=yellow]数据管理器未就绪[/color]";
      return;
    }

    AiModelConfig mc;
    try
    {
      mc = AiServiceFactory.GetActiveModelConfig();
    }
    catch (InvalidOperationException ex)
    {
      _log.Warn($"OnFuzzify: no valid active model - {ex.Message}");
      ResponseDisplay.Text = $"[color=yellow]{ex.Message}[/color]";
      return;
    }

    _log.Print(
      $"OnFuzzify: start, model={mc.ModelId}, format={mc.ApiFormat}, input_len={text.Length}"
    );
    FuzzifyBtn.Disabled = true;
    FuzzifyBtn.Text = "模糊化中...";
    ResponseDisplay.Text = "[color=gray]正在调用 AI...[/color]";
    try
    {
      var fuzzifier = new AiFuzzifier(AiServiceFactory, _dm.Aliases, _currentBoss);
      var result = await fuzzifier.FuzzifyAsync(text);
      if (AiFuzzifier.IsNotAGuessResult(result))
      {
        _log.Print("OnFuzzify: AI judged current input as not a guess.");
        ResponseDisplay.Text = "[color=yellow]AI 判定该输入不像猜测文本[/color]";
        return;
      }
      _log.Print($"OnFuzzify: succeeded, output_len={result?.Length ?? 0}");
      GuessInput.Text = result;
      ResponseDisplay.Text = $"[color=green]完成[/color]\n\n{result}";
    }
    catch (Exception ex)
    {
      _log.Err($"OnFuzzify failed: {ex.GetType().Name}: {ex.Message}");
      ResponseDisplay.Text = $"[color=red]失败: {ex.Message}[/color]";
    }
    finally
    {
      FuzzifyBtn.Disabled = false;
      FuzzifyBtn.Text = "模糊化";
      RefreshDroppedUI();
    }
  }

  // ==================== 丢包重试 ====================

  private void RefreshDroppedUI()
  {
    var service = _guessProcessingService;
    if (service == null)
      return;

    DroppedList.Clear();
    var dropped = service.GetDroppedGuesses();
    if (dropped.Count == 0)
    {
      RetryDroppedBtn.Disabled = true;
      ClearDroppedBtn.Disabled = true;
      return;
    }

    foreach (var d in dropped)
    {
      DroppedList.AddItem($"[{d.Id}] {d.RawText}");
    }

    RetryDroppedBtn.Disabled = false;
    ClearDroppedBtn.Disabled = false;
  }

  private async void OnRetryAllDropped()
  {
    var service = _guessProcessingService;
    if (service == null)
      return;

    var dropped = service.GetDroppedGuesses();
    if (dropped.Count == 0)
      return;

    _log.Print($"OnRetryAllDropped: retrying {dropped.Count} dropped guesses.");
    RetryDroppedBtn.Disabled = true;
    RetryDroppedBtn.Text = "重试中...";

    var successCount = 0;
    var failCount = 0;

    // 并行重试所有丢包
    var tasks = dropped.Select(async d =>
    {
      var result = await service.RetryDroppedGuessAsync(d.Id);
      if (result.IsGuess)
        Interlocked.Increment(ref successCount);
      else
        Interlocked.Increment(ref failCount);
    });

    await Task.WhenAll(tasks);

    _log.Print($"OnRetryAllDropped: done, success={successCount}, fail={failCount}");
    RetryDroppedBtn.Text = "重试全部丢包";
    RefreshDroppedUI();
  }

  private void OnClearDropped()
  {
    var service = _guessProcessingService;
    if (service == null)
      return;

    var dropped = service.GetDroppedGuesses();
    foreach (var d in dropped)
      service.RemoveDroppedGuess(d.Id);

    _log.Print($"OnClearDropped: cleared {dropped.Count} dropped guesses.");
    RefreshDroppedUI();
  }

  // ==================== 工具 ====================

  private void ShowError(string msg)
  {
    var d = new AcceptDialog();
    d.Title = "错误";
    d.DialogText = msg;
    AddChild(d);
    d.PopupCentered();
  }
}
