namespace AutoCMEX.UI.Guessing;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
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
  public GuessPipeline Pipeline =>
    this.DependOn<GuessPipeline>(() =>
      new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>())
    );

  #endregion

  private DataManager? _dm;
  private GuessPipeline? _pipeline;
  private Boss? _currentBoss;
  private bool _rebuildingAliasTree;

  public override void _Notification(int what) => this.Notify(what);

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
      _pipeline = Pipeline;
    }
    catch
    {
      _pipeline = null;
    }

    if (_dm != null)
    {
      _dm.LoadAll();
      UpdateFuzzifyButtonState();
      RefreshAll();
    }
  }

  private void UpdateFuzzifyButtonState()
  {
    var hasAi =
      _dm != null
      && _dm.Settings.AiModels.Count > 0
      && _dm.Settings.AiModels.Exists(m =>
        !string.IsNullOrEmpty(m.EndpointUrl)
        && !string.IsNullOrEmpty(m.ModelId)
        && !string.IsNullOrEmpty(m.EncryptedApiKey)
      );
    FuzzifyBtn.Disabled = !hasAi;
    FuzzifyBtn.TooltipText = hasAi ? "使用 AI 模糊化" : "请先配置 AI 模型";
  }

  public void RefreshAll()
  {
    RefreshBossSelect();
    RefreshSpellCardTree();
    RefreshAliasTree();
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
      BossSelect.Select(0);
      _currentBoss = _dm.Bosses[0];
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
      _currentBoss = _dm.Bosses[(int)index];
      RefreshSpellCardTree();
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

  private void OnProcessGuess()
  {
    if (_currentBoss == null)
    {
      ResponseDisplay.Text = "[color=red]请先选择 Boss[/color]";
      return;
    }
    var text = GuessInput.Text.Trim();
    if (string.IsNullOrEmpty(text))
    {
      ResponseDisplay.Text = "[color=red]请输入猜测文本[/color]";
      return;
    }
    var pipeline =
      _dm != null
        ? new GuessPipeline(new GuessResponseHandler(), _dm.Aliases)
        : _pipeline ?? Pipeline;
    var result = pipeline.Process(text, _currentBoss);
    if (!result.IsSuccess)
    {
      ResponseDisplay.Text = $"[color=red]{result.ErrorMessage}[/color]";
      return;
    }
    var display = "";
    if (!string.IsNullOrEmpty(result.Response))
      display += $"[b]{result.Response}[/b]\n\n";
    foreach (var d in result.Details)
      display += $"{d}\n";
    ResponseDisplay.Text = display;
  }

  private async void OnFuzzify()
  {
    if (_currentBoss == null)
    {
      ResponseDisplay.Text = "[color=red]请先选择 Boss[/color]";
      return;
    }
    var text = GuessInput.Text.Trim();
    if (string.IsNullOrEmpty(text))
    {
      ResponseDisplay.Text = "[color=red]请输入猜测文本[/color]";
      return;
    }
    if (_dm == null || _dm.Settings.AiModels.Count == 0)
    {
      ResponseDisplay.Text = "[color=yellow]请先配置 AI 模型[/color]";
      return;
    }
    var mc = _dm.Settings.AiModels.Find(m =>
      !string.IsNullOrEmpty(m.EndpointUrl)
      && !string.IsNullOrEmpty(m.ModelId)
      && !string.IsNullOrEmpty(m.EncryptedApiKey)
    );
    if (mc == null)
    {
      ResponseDisplay.Text = "[color=yellow]请完整配置 AI 模型[/color]";
      return;
    }
    FuzzifyBtn.Disabled = true;
    FuzzifyBtn.Text = "模糊化中...";
    ResponseDisplay.Text = "[color=gray]正在调用 AI...[/color]";
    try
    {
      IAiService ai =
        mc.ApiFormat == "Anthropic" ? new AnthropicService(mc) : new OpenAiService(mc);
      var fuzzifier = new AiFuzzifier(ai, _dm.Aliases, _dm.Bosses, _currentBoss);
      var result = await fuzzifier.FuzzifyAsync(text);
      GuessInput.Text = result;
      ResponseDisplay.Text = $"[color=green]完成[/color]\n\n{result}";
    }
    catch (Exception ex)
    {
      ResponseDisplay.Text = $"[color=red]失败: {ex.Message}[/color]";
    }
    finally
    {
      FuzzifyBtn.Disabled = false;
      FuzzifyBtn.Text = "模糊化";
    }
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
