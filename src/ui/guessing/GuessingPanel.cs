namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Generic;
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

  #region Programmatic UI

  private Label? _droppedLabel;
  private Button? _retryDroppedBtn;
  private Button? _clearDroppedBtn;

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
  private bool _rebuildingAliasTree;
  private ILog _log = AppLogs.GetOrCreate().GetLogger(nameof(GuessingPanel));

  public override void _Notification(int what)
  {
    if (what == NotificationVisibilityChanged && Visible)
      UpdateFuzzifyButtonState();
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

    AliasTree.Columns = 1;
    AliasTree.SetColumnTitle(0, "创作者 / 别名");
    AliasTree.HideRoot = true;

    FuzzifyBtn.Disabled = true;
    FuzzifyBtn.TooltipText = "请先配置 AI 模型";

    // 丢包重试 UI
    var droppedSection = new VBoxContainer();
    droppedSection.AddChild(new HSeparator());

    _droppedLabel = new Label { Text = "丢包列表：无" };
    droppedSection.AddChild(_droppedLabel);

    var btnRow = new HBoxContainer();
    _retryDroppedBtn = new Button { Text = "重试全部丢包", Disabled = true };
    _retryDroppedBtn.Pressed += OnRetryAllDropped;
    btnRow.AddChild(_retryDroppedBtn);

    _clearDroppedBtn = new Button { Text = "清空丢包", Disabled = true };
    _clearDroppedBtn.Pressed += OnClearDropped;
    btnRow.AddChild(_clearDroppedBtn);

    droppedSection.AddChild(btnRow);

    var parent = ResponseDisplay.GetParent();
    if (parent != null)
      parent.AddChild(droppedSection);
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
      _currentBoss = _dm.Bosses[selectedIndex];
    }
    else
      _currentBoss = null;
    RefreshSpellCardTree();
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
      cardItem.SetEditable(0, true);
      cardItem.SetEditable(1, true);
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
    }
    _dm?.TriggerAutoSave();
  }

  private void OnBossSelected(long index)
  {
    if (_dm != null && index >= 0 && index < _dm.Bosses.Count)
    {
      _dm.Settings.SelectedBossIndex = (int)index;
      _currentBoss = _dm.Bosses[(int)index];
      _dm.TriggerAutoSave();
      RefreshSpellCardTree();
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
        $"{EscapeCsv(boss.Name)},{EscapeCsv(card.Name)},{EscapeCsv(card.Creator)}"
      );
    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private void OnCardFileSelected(string path)
  {
    if (_dm == null)
      return;
    ImportResult<List<Boss>> result;
    if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
      result = ExcelImporter.ImportSpellCardTable(path);
    else
      result = CsvImporter.ImportSpellCardTable(path);
    if (!result.IsSuccess)
    {
      ShowError(result.ErrorMessage);
      return;
    }
    _dm.Bosses.Clear();
    _dm.Bosses.AddRange(result.Data!);
    _dm.TriggerAutoSave();
    RefreshAll();
  }

  private void OnAddBoss()
  {
    if (_dm == null)
      return;
    _log.Print("GuessingPanel: user added a new Boss.");
    _dm.Bosses.Add(new Boss { Name = "新 Boss" });
    _dm.TriggerAutoSave();
    RefreshAll();
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
    RefreshSpellCardTree();
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
      _dm.Bosses.RemoveAll(b => b.Name == selected.GetText(0));
    else
    {
      var index = selected.GetMetadata(0).AsInt32();
      if (_currentBoss != null && index >= 0 && index < _currentBoss.SpellCards.Count)
        _currentBoss.SpellCards.RemoveAt(index);
    }
    _dm.TriggerAutoSave();
    RefreshAll();
  }

  // ==================== 别名表 ====================

  private void RefreshAliasTree()
  {
    _rebuildingAliasTree = true;
    AliasTree.ItemEdited -= OnAliasEdited;
    AliasTree.Clear();

    if (_dm != null)
    {
      var root = AliasTree.CreateItem();
      for (int i = 0; i < _dm.Aliases.Count; i++)
      {
        var creator = _dm.Aliases[i];
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
      var line = EscapeCsv(a.MainName);
      for (int i = 0; i < maxAliases; i++)
      {
        line += ",";
        if (i < a.Aliases.Count)
          line += EscapeCsv(a.Aliases[i]);
      }
      sb.AppendLine(line);
    }
    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private void OnAliasFileSelected(string path)
  {
    if (_dm == null)
      return;
    ImportResult<List<CreatorAlias>> result;
    if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
      result = ExcelImporter.ImportAliasTable(path);
    else
      result = CsvImporter.ImportAliasTable(path);
    if (!result.IsSuccess)
    {
      ShowError(result.ErrorMessage);
      return;
    }
    _dm.Aliases.Clear();
    _dm.Aliases.AddRange(result.Data!);
    _dm.TriggerAutoSave();
    RefreshAliasTree();
  }

  private void OnAddAlias()
  {
    if (_dm == null)
      return;
    _dm.Aliases.Add(new CreatorAlias { MainName = "新创作者" });
    _dm.TriggerAutoSave();
    RefreshAliasTree();
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
    RefreshAliasTree();
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
    RefreshAliasTree();
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

    var result = await service.ProcessManualAsync(text, _currentBoss);
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
      var fuzzifier = new AiFuzzifier(AiServiceFactory, _dm.Aliases, _dm.Bosses, _currentBoss);
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
    }
  }

  // ==================== 丢包重试 ====================

  private void RefreshDroppedUI()
  {
    if (_droppedLabel == null || _retryDroppedBtn == null || _clearDroppedBtn == null)
      return;

    var service = _guessProcessingService;
    if (service == null)
    {
      _droppedLabel.Text = "丢包列表：服务未就绪";
      return;
    }

    var dropped = service.GetDroppedGuesses();
    if (dropped.Count == 0)
    {
      _droppedLabel.Text = "丢包列表：无";
      _retryDroppedBtn.Disabled = true;
      _clearDroppedBtn.Disabled = true;
      return;
    }

    _droppedLabel.Text = $"丢包列表：{dropped.Count} 条";
    _retryDroppedBtn.Disabled = false;
    _clearDroppedBtn.Disabled = false;
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
    _retryDroppedBtn!.Disabled = true;
    _retryDroppedBtn.Text = "重试中...";

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
    _retryDroppedBtn.Text = "重试全部丢包";
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

  private static string EscapeCsv(string f)
  {
    if (string.IsNullOrEmpty(f))
      return "";
    if (f.Contains(',') || f.Contains('"') || f.Contains('\n'))
      return $"\"{f.Replace("\"", "\"\"")}\"";
    return f;
  }
}
