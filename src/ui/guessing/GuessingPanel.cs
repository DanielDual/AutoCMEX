namespace AutoCMEX.UI.Guessing;

using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 猜测板块脚本
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
  public Button DeleteBtn { get; set; } = default!;

  [Node]
  public ItemList AliasList { get; set; } = default!;

  [Node]
  public Button ImportAliasBtn { get; set; } = default!;

  [Node]
  public Button AddAliasBtn { get; set; } = default!;

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
  public DataManager DataManager => this.DependOn<DataManager>(() => null!);

  [Dependency]
  public GuessPipeline Pipeline =>
    this.DependOn<GuessPipeline>(() =>
      new GuessPipeline(new GuessResponseHandler(), new List<CreatorAlias>())
    );

  #endregion

  // 数据
  private List<Boss> _bosses = new();
  private List<CreatorAlias> _aliases = new();
  private Boss? _currentBoss;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    // 连接信号 (节点已由 IAutoConnect 绑定)
    BossSelect.ItemSelected += OnBossSelected;
    ImportCardBtn.Pressed += OnImportCardTable;
    ImportAliasBtn.Pressed += OnImportAliasTable;
    AddBossBtn.Pressed += OnAddBoss;
    DeleteBtn.Pressed += OnDeleteSelected;
    AddAliasBtn.Pressed += OnAddAlias;
    DeleteAliasBtn.Pressed += OnDeleteAlias;
    ProcessBtn.Pressed += OnProcessGuess;
    FuzzifyBtn.Pressed += OnFuzzify;

    // 禁用模糊化按钮（需配置 AI）
    FuzzifyBtn.Disabled = true;
    FuzzifyBtn.TooltipText = "请先配置 AI 模型";
  }

  public void OnResolved()
  {
    // 依赖已解析，初始化数据
    if (DataManager != null)
    {
      _bosses = DataManager.Bosses;
      _aliases = DataManager.Aliases;
      UpdateFuzzifyButtonState();
      RefreshAll();
    }
  }

  /// <summary>
  /// 根据 AI 模型配置状态更新模糊化按钮
  /// </summary>
  private void UpdateFuzzifyButtonState()
  {
    var hasAiModel =
      DataManager != null
      && DataManager.Settings.AiModels.Count > 0
      && DataManager.Settings.AiModels.Exists(m =>
        !string.IsNullOrEmpty(m.EndpointUrl)
        && !string.IsNullOrEmpty(m.ModelId)
        && !string.IsNullOrEmpty(m.EncryptedApiKey)
      );

    FuzzifyBtn.Disabled = !hasAiModel;
    FuzzifyBtn.TooltipText = hasAiModel
      ? "使用 AI 将非严格格式文本转为严格格式"
      : "请先配置 AI 模型";
  }

  /// <summary>
  /// 刷新所有显示
  /// </summary>
  public void RefreshAll()
  {
    RefreshBossSelect();
    RefreshSpellCardTree();
    RefreshAliasList();
  }

  /// <summary>
  /// 刷新 Boss 下拉框
  /// </summary>
  private void RefreshBossSelect()
  {
    BossSelect.Clear();
    foreach (var boss in _bosses)
    {
      BossSelect.AddItem(boss.Name);
    }

    if (_bosses.Count > 0)
    {
      BossSelect.Select(0);
      _currentBoss = _bosses[0];
    }
    else
    {
      _currentBoss = null;
    }

    RefreshSpellCardTree();
  }

  /// <summary>
  /// 刷新符卡—创作者对应表
  /// </summary>
  private void RefreshSpellCardTree()
  {
    SpellCardTree.Clear();
    var root = SpellCardTree.CreateItem();
    SpellCardTree.HideRoot = true;

    if (_currentBoss == null)
      return;

    var bossItem = SpellCardTree.CreateItem(root);
    bossItem.SetText(0, _currentBoss.Name);
    bossItem.SetEditable(0, true);

    for (int i = 0; i < _currentBoss.SpellCards.Count; i++)
    {
      var card = _currentBoss.SpellCards[i];
      var cardItem = SpellCardTree.CreateItem(bossItem);
      cardItem.SetText(0, $"{i + 1}. {card.Name}");
      cardItem.SetText(1, string.IsNullOrEmpty(card.Creator) ? "(未揭晓)" : card.Creator);
      cardItem.SetEditable(0, true);
      cardItem.SetMetadata(0, i);
    }
  }

  /// <summary>
  /// 刷新别名表
  /// </summary>
  private void RefreshAliasList()
  {
    AliasList.Clear();
    foreach (var alias in _aliases)
    {
      var aliasesStr = string.Join(", ", alias.Aliases);
      AliasList.AddItem($"{alias.MainName}: {aliasesStr}");
    }
  }

  /// <summary>
  /// Boss 选择变更
  /// </summary>
  private void OnBossSelected(long index)
  {
    if (index >= 0 && index < _bosses.Count)
    {
      _currentBoss = _bosses[(int)index];
      RefreshSpellCardTree();
    }
  }

  /// <summary>
  /// 导入符卡—创作者对应表
  /// </summary>
  private void OnImportCardTable()
  {
    var dialog = new FileDialog();
    dialog.FileMode = FileDialog.FileModeEnum.OpenFile;
    dialog.Access = FileDialog.AccessEnum.Filesystem;
    dialog.AddFilter("*.csv, *.xlsx", "*.csv, *.xlsx");
    dialog.AddFilter("*.csv", "*.csv");
    dialog.AddFilter("*.xlsx", "*.xlsx");
    dialog.FileSelected += OnCardFileSelected;
    AddChild(dialog);
    dialog.PopupCentered();
  }

  private void OnCardFileSelected(string path)
  {
    ImportResult<List<Boss>> result;
    if (path.EndsWith(".xlsx"))
      result = ExcelImporter.ImportSpellCardTable(path);
    else
      result = CsvImporter.ImportSpellCardTable(path);

    if (!result.IsSuccess)
    {
      ShowError(result.ErrorMessage);
      return;
    }

    _bosses = result.Data!;
    if (DataManager != null)
    {
      DataManager.Bosses.Clear();
      DataManager.Bosses.AddRange(_bosses);
      DataManager.TriggerAutoSave();
    }
    RefreshAll();
  }

  /// <summary>
  /// 导入别名表
  /// </summary>
  private void OnImportAliasTable()
  {
    var dialog = new FileDialog();
    dialog.FileMode = FileDialog.FileModeEnum.OpenFile;
    dialog.Access = FileDialog.AccessEnum.Filesystem;
    dialog.AddFilter("*.csv, *.xlsx", "*.csv, *.xlsx");
    dialog.AddFilter("*.csv", "*.csv");
    dialog.AddFilter("*.xlsx", "*.xlsx");
    dialog.FileSelected += OnAliasFileSelected;
    AddChild(dialog);
    dialog.PopupCentered();
  }

  private void OnAliasFileSelected(string path)
  {
    ImportResult<List<CreatorAlias>> result;
    if (path.EndsWith(".xlsx"))
      result = ExcelImporter.ImportAliasTable(path);
    else
      result = CsvImporter.ImportAliasTable(path);

    if (!result.IsSuccess)
    {
      ShowError(result.ErrorMessage);
      return;
    }

    _aliases = result.Data!;
    if (DataManager != null)
    {
      DataManager.Aliases.Clear();
      DataManager.Aliases.AddRange(_aliases);
      DataManager.TriggerAutoSave();
    }
    RefreshAliasList();
  }

  /// <summary>
  /// 添加 Boss
  /// </summary>
  private void OnAddBoss()
  {
    var boss = new Boss { Name = "新 Boss" };
    _bosses.Add(boss);
    if (DataManager != null)
    {
      DataManager.Bosses.Clear();
      DataManager.Bosses.AddRange(_bosses);
      DataManager.TriggerAutoSave();
    }
    RefreshAll();
  }

  /// <summary>
  /// 删除选中项
  /// </summary>
  private void OnDeleteSelected()
  {
    var selected = SpellCardTree.GetNextSelected(null);
    if (selected == null)
      return;

    var parent = selected.GetParent();
    if (parent == null || parent == SpellCardTree.GetRoot())
    {
      // 删除 Boss
      var bossName = selected.GetText(0);
      _bosses.RemoveAll(b => b.Name == bossName);
    }
    else
    {
      // 删除符卡
      var index = selected.GetMetadata(0).AsInt32();
      if (_currentBoss != null && index >= 0 && index < _currentBoss.SpellCards.Count)
      {
        _currentBoss.SpellCards.RemoveAt(index);
      }
    }

    if (DataManager != null)
      DataManager.TriggerAutoSave();

    RefreshAll();
  }

  /// <summary>
  /// 添加别名
  /// </summary>
  private void OnAddAlias()
  {
    var alias = new CreatorAlias { MainName = "新创作者" };
    _aliases.Add(alias);
    if (DataManager != null)
    {
      DataManager.Aliases.Clear();
      DataManager.Aliases.AddRange(_aliases);
      DataManager.TriggerAutoSave();
    }
    RefreshAliasList();
  }

  /// <summary>
  /// 删除别名
  /// </summary>
  private void OnDeleteAlias()
  {
    var selectedItems = AliasList.GetSelectedItems();
    if (selectedItems.Length == 0)
      return;

    var index = selectedItems[0];
    if (index >= 0 && index < _aliases.Count)
    {
      _aliases.RemoveAt((int)index);
    }

    if (DataManager != null)
      DataManager.TriggerAutoSave();

    RefreshAliasList();
  }

  /// <summary>
  /// 处理猜测文本
  /// </summary>
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

    var result = Pipeline.Process(text, _currentBoss);

    if (!result.IsSuccess)
    {
      ResponseDisplay.Text = $"[color=red]{result.ErrorMessage}[/color]";
      return;
    }

    var displayText = string.Empty;
    if (!string.IsNullOrEmpty(result.Response))
    {
      displayText += $"[b]{result.Response}[/b]\n\n";
    }

    foreach (var detail in result.Details)
    {
      displayText += $"{detail}\n";
    }

    ResponseDisplay.Text = displayText;
  }

  /// <summary>
  /// 模糊化处理
  /// </summary>
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

    if (DataManager == null || DataManager.Settings.AiModels.Count == 0)
    {
      ResponseDisplay.Text = "[color=yellow]请先在设置中配置 AI 模型[/color]";
      return;
    }

    // 使用第一个已完整配置的 AI 模型
    var modelConfig = DataManager.Settings.AiModels.Find(m =>
      !string.IsNullOrEmpty(m.EndpointUrl)
      && !string.IsNullOrEmpty(m.ModelId)
      && !string.IsNullOrEmpty(m.EncryptedApiKey)
    );

    if (modelConfig == null)
    {
      ResponseDisplay.Text = "[color=yellow]请先在设置中完整配置 AI 模型[/color]";
      return;
    }

    FuzzifyBtn.Disabled = true;
    FuzzifyBtn.Text = "模糊化中...";
    ResponseDisplay.Text = "[color=gray]正在调用 AI 进行模糊化处理...[/color]";

    try
    {
      IAiService aiService =
        modelConfig.ApiFormat == "Anthropic"
          ? new AnthropicService(modelConfig)
          : new OpenAiService(modelConfig);

      var fuzzifier = new AiFuzzifier(aiService, _aliases, _bosses, _currentBoss);
      var result = await fuzzifier.FuzzifyAsync(text);

      GuessInput.Text = result;
      ResponseDisplay.Text = $"[color=green]模糊化完成[/color]\n\n{result}";
    }
    catch (System.Exception ex)
    {
      ResponseDisplay.Text = $"[color=red]模糊化失败: {ex.Message}[/color]";
    }
    finally
    {
      FuzzifyBtn.Disabled = false;
      FuzzifyBtn.Text = "模糊化";
    }
  }

  /// <summary>
  /// 显示错误弹窗
  /// </summary>
  private void ShowError(string message)
  {
    var dialog = new AcceptDialog();
    dialog.Title = "错误";
    dialog.DialogText = message;
    AddChild(dialog);
    dialog.PopupCentered();
  }
}
