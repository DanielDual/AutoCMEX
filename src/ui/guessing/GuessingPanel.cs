namespace AutoCMEX.UI.Guessing;

using Godot;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Core.Guessing;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;

/// <summary>
/// 猜测板块脚本
/// </summary>
public partial class GuessingPanel : Control
{
    // Boss 选择
    private OptionButton _bossSelect = default!;

    // 左上：符卡—创作者对应表
    private Tree _spellCardTree = default!;
    private Button _importCardBtn = default!;
    private Button _exportCardBtn = default!;
    private Button _addBossBtn = default!;
    private Button _deleteBtn = default!;

    // 左下：别名表
    private ItemList _aliasList = default!;
    private Button _importAliasBtn = default!;
    private Button _addAliasBtn = default!;
    private Button _deleteAliasBtn = default!;

    // 右侧
    private TextEdit _guessInput = default!;
    private Button _fuzzifyBtn = default!;
    private Button _processBtn = default!;
    private RichTextLabel _responseDisplay = default!;

    // 数据
    private DataManager? _dataManager;
    private GuessPipeline? _pipeline;
    private List<Boss> _bosses = new();
    private List<CreatorAlias> _aliases = new();
    private Boss? _currentBoss;

    public override void _Ready()
    {
        // 获取节点引用
        _bossSelect = GetNode<OptionButton>("%BossSelect");
        _spellCardTree = GetNode<Tree>("%SpellCardTree");
        _importCardBtn = GetNode<Button>("%ImportCardBtn");
        _exportCardBtn = GetNode<Button>("%ExportCardBtn");
        _addBossBtn = GetNode<Button>("%AddBossBtn");
        _deleteBtn = GetNode<Button>("%DeleteBtn");
        _aliasList = GetNode<ItemList>("%AliasList");
        _importAliasBtn = GetNode<Button>("%ImportAliasBtn");
        _addAliasBtn = GetNode<Button>("%AddAliasBtn");
        _deleteAliasBtn = GetNode<Button>("%DeleteAliasBtn");
        _guessInput = GetNode<TextEdit>("%GuessInput");
        _fuzzifyBtn = GetNode<Button>("%FuzzifyBtn");
        _processBtn = GetNode<Button>("%ProcessBtn");
        _responseDisplay = GetNode<RichTextLabel>("%ResponseDisplay");

        // 连接信号
        _bossSelect.ItemSelected += OnBossSelected;
        _importCardBtn.Pressed += OnImportCardTable;
        _importAliasBtn.Pressed += OnImportAliasTable;
        _addBossBtn.Pressed += OnAddBoss;
        _deleteBtn.Pressed += OnDeleteSelected;
        _addAliasBtn.Pressed += OnAddAlias;
        _deleteAliasBtn.Pressed += OnDeleteAlias;
        _processBtn.Pressed += OnProcessGuess;
        _fuzzifyBtn.Pressed += OnFuzzify;

        // 初始化管道
        _pipeline = new GuessPipeline(new GuessResponseHandler(), _aliases);

        // 禁用模糊化按钮（需配置 AI）
        _fuzzifyBtn.Disabled = true;
        _fuzzifyBtn.TooltipText = "请先配置 AI 模型";
    }

    /// <summary>
    /// 设置数据管理器引用
    /// </summary>
    public void SetDataManager(DataManager dataManager)
    {
        _dataManager = dataManager;
        _bosses = dataManager.Bosses;
        _aliases = dataManager.Aliases;
        _pipeline = new GuessPipeline(new GuessResponseHandler(), _aliases);
        RefreshAll();
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
        _bossSelect.Clear();
        foreach (var boss in _bosses)
        {
            _bossSelect.AddItem(boss.Name);
        }

        if (_bosses.Count > 0)
        {
            _bossSelect.Select(0);
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
        _spellCardTree.Clear();
        var root = _spellCardTree.CreateItem();
        _spellCardTree.HideRoot = true;

        if (_currentBoss == null)
            return;

        var bossItem = _spellCardTree.CreateItem(root);
        bossItem.SetText(0, _currentBoss.Name);
        bossItem.SetEditable(0, true);

        for (int i = 0; i < _currentBoss.SpellCards.Count; i++)
        {
            var card = _currentBoss.SpellCards[i];
            var cardItem = _spellCardTree.CreateItem(bossItem);
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
        _aliasList.Clear();
        foreach (var alias in _aliases)
        {
            var aliasesStr = string.Join(", ", alias.Aliases);
            _aliasList.AddItem($"{alias.MainName}: {aliasesStr}");
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
        if (_dataManager != null)
        {
            _dataManager.Bosses.Clear();
            _dataManager.Bosses.AddRange(_bosses);
            _dataManager.TriggerAutoSave();
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
        if (_dataManager != null)
        {
            _dataManager.Aliases.Clear();
            _dataManager.Aliases.AddRange(_aliases);
            _dataManager.TriggerAutoSave();
        }
        _pipeline = new GuessPipeline(new GuessResponseHandler(), _aliases);
        RefreshAliasList();
    }

    /// <summary>
    /// 添加 Boss
    /// </summary>
    private void OnAddBoss()
    {
        var boss = new Boss { Name = "新 Boss" };
        _bosses.Add(boss);
        if (_dataManager != null)
        {
            _dataManager.Bosses.Clear();
            _dataManager.Bosses.AddRange(_bosses);
            _dataManager.TriggerAutoSave();
        }
        RefreshAll();
    }

    /// <summary>
    /// 删除选中项
    /// </summary>
    private void OnDeleteSelected()
    {
        var selected = _spellCardTree.GetNextSelected(null);
        if (selected == null)
            return;

        var parent = selected.GetParent();
        if (parent == null || parent == _spellCardTree.GetRoot())
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

        if (_dataManager != null)
            _dataManager.TriggerAutoSave();

        RefreshAll();
    }

    /// <summary>
    /// 添加别名
    /// </summary>
    private void OnAddAlias()
    {
        var alias = new CreatorAlias { MainName = "新创作者" };
        _aliases.Add(alias);
        if (_dataManager != null)
        {
            _dataManager.Aliases.Clear();
            _dataManager.Aliases.AddRange(_aliases);
            _dataManager.TriggerAutoSave();
        }
        _pipeline = new GuessPipeline(new GuessResponseHandler(), _aliases);
        RefreshAliasList();
    }

    /// <summary>
    /// 删除别名
    /// </summary>
    private void OnDeleteAlias()
    {
        var selectedItems = _aliasList.GetSelectedItems();
        if (selectedItems.Length == 0)
            return;

        var index = selectedItems[0];
        if (index >= 0 && index < _aliases.Count)
        {
            _aliases.RemoveAt((int)index);
        }

        if (_dataManager != null)
            _dataManager.TriggerAutoSave();

        _pipeline = new GuessPipeline(new GuessResponseHandler(), _aliases);
        RefreshAliasList();
    }

    /// <summary>
    /// 处理猜测文本
    /// </summary>
    private void OnProcessGuess()
    {
        if (_currentBoss == null)
        {
            _responseDisplay.Text = "[color=red]请先选择 Boss[/color]";
            return;
        }

        var text = _guessInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            _responseDisplay.Text = "[color=red]请输入猜测文本[/color]";
            return;
        }

        var result = _pipeline!.Process(text, _currentBoss);

        if (!result.IsSuccess)
        {
            _responseDisplay.Text = $"[color=red]{result.ErrorMessage}[/color]";
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

        _responseDisplay.Text = displayText;
    }

    /// <summary>
    /// 模糊化处理
    /// </summary>
    private void OnFuzzify()
    {
        // 需要 AI 配置，暂时禁用
        _responseDisplay.Text = "[color=yellow]请先在设置中配置 AI 模型[/color]";
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
