namespace AutoCMEX.UI.Merge;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Merge;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 整合板块：四栏联动。左上创作者包信息、左下工程模板配置、右上对应表（可编辑）、右下导出功能。
/// UI 刷新由 AutoValue/AutoList 绑定驱动，事件只写数据模型（指示 24）。
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class MergePanel : Control, IMergePanel
{
  #region 左上 · 创作者包信息

  [Node("%CreatorTitle")]
  public ILabel CreatorTitle { get; set; } = default!;

  [Node("%PackageList")]
  public IItemList PackageList { get; set; } = default!;

  [Node("%SpellCardList")]
  public IItemList SpellCardList { get; set; } = default!;

  [Node("%ResourceList")]
  public IItemList ResourceList { get; set; } = default!;

  [Node("%ObjectList")]
  public IItemList ObjectList { get; set; } = default!;

  [Node("%ImportPackageBtn")]
  public IButton ImportPackageBtn { get; set; } = default!;

  [Node("%RemovePackageBtn")]
  public IButton RemovePackageBtn { get; set; } = default!;

  #endregion

  #region 左下 · 工程模板配置

  [Node("%TemplatePathEdit")]
  public ILineEdit TemplatePathEdit { get; set; } = default!;

  [Node("%SharpPathEdit")]
  public ILineEdit SharpPathEdit { get; set; } = default!;

  [Node("%PluginDllEdit")]
  public ILineEdit PluginDllEdit { get; set; } = default!;

  [Node("%InjectionStatusLabel")]
  public ILabel InjectionStatusLabel { get; set; } = default!;

  [Node("%ImportTemplateBtn")]
  public IButton ImportTemplateBtn { get; set; } = default!;

  #endregion

  #region 右上 · 对应表

  [Node("%MappingList")]
  public IItemList MappingList { get; set; } = default!;

  [Node("%MoveUpBtn")]
  public IButton MoveUpBtn { get; set; } = default!;

  [Node("%MoveDownBtn")]
  public IButton MoveDownBtn { get; set; } = default!;

  [Node("%ShuffleBtn")]
  public IButton ShuffleBtn { get; set; } = default!;

  [Node("%GroupOption")]
  public ICheckBox GroupOption { get; set; } = default!;

  #endregion

  #region 右下 · 导出功能

  [Node("%IncludeLstgesToggle")]
  public ICheckBox IncludeLstgesToggle { get; set; } = default!;

  [Node("%ObfuscateLuaToggle")]
  public ICheckBox ObfuscateLuaToggle { get; set; } = default!;

  [Node("%OutputDirEdit")]
  public ILineEdit OutputDirEdit { get; set; } = default!;

  [Node("%ConflictList")]
  public IItemList ConflictList { get; set; } = default!;

  [Node("%AutoRenameConflictsToggle")]
  public ICheckBox AutoRenameConflictsToggle { get; set; } = default!;

  [Node("%ForcePerformActionToggle")]
  public ICheckBox ForcePerformActionToggle { get; set; } = default!;

  [Node("%GroupByCreatorFoldersToggle")]
  public ICheckBox GroupByCreatorFoldersToggle { get; set; } = default!;

  [Node("%ExportFullPackageBtn")]
  public IButton ExportFullPackageBtn { get; set; } = default!;

  [Node("%ExportMappingBtn")]
  public IButton ExportMappingBtn { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  #endregion

  private DataManager? _dm;
  private readonly ILog _log = AppLogs.GetOrCreate().GetLogger(nameof(MergePanel));

  private AutoList<CreatorPackage>.Binding? _creatorPackagesBinding;
  private AutoList<SpellCardMappingEntry>.Binding? _mappingBinding;
  private AutoValue<int>.Binding? _selectedPackageIndexBinding;
  private int _selectedMapping = -1;

  public override void _Notification(int what) => this.Notify(what);

  public override void _ExitTree()
  {
    _creatorPackagesBinding?.Dispose();
    _mappingBinding?.Dispose();
    _selectedPackageIndexBinding?.Dispose();
  }

  public void OnReady()
  {
    ImportPackageBtn.Pressed += OnImportPackage;
    RemovePackageBtn.Pressed += OnRemovePackage;
    PackageList.ItemSelected += _ => OnPackageSelected();
    ImportTemplateBtn.Pressed += OnImportTemplate;
    MappingList.ItemSelected += _ =>
      _selectedMapping = MappingList.GetSelectedItems().FirstOrDefault();
    MoveUpBtn.Pressed += OnMoveMapping(-1);
    MoveDownBtn.Pressed += OnMoveMapping(1);
    ShuffleBtn.Pressed += OnShuffleMapping;
    ExportFullPackageBtn.Pressed += OnExportFullPackage;
    ExportMappingBtn.Pressed += OnExportMapping;
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm == null)
      return;

    _creatorPackagesBinding = _dm
      .CreatorPackages.Bind()
      .OnModify(() => CallDeferred(nameof(RefreshPackageList)));
    _mappingBinding = _dm
      .MergeConfig.Mapping.Bind()
      .OnModify(() => CallDeferred(nameof(RefreshMappingList)));

    // 选包变化（含初始值）驱动三栏清单刷新（指示 24：事件只写模型，UI 由绑定传播）。
    _selectedPackageIndexBinding = _dm
      .MergeConfig.SelectedPackageIndex.Bind()
      .OnValue(_ => CallDeferred(nameof(RefreshPackageDetail)));

    // 导出选项：事件只写数据模型 + 触发保存（指示 24）
    IncludeLstgesToggle.Toggled += _ => PersistConfig();
    ObfuscateLuaToggle.Toggled += _ => PersistConfig();
    AutoRenameConflictsToggle.Toggled += _ => PersistConfig();
    ForcePerformActionToggle.Toggled += _ => PersistConfig();
    GroupByCreatorFoldersToggle.Toggled += _ => PersistConfig();

    // 工程模板配置：路径/目录编辑框输入即写回模型 + 触发保存（避免只填不写、重启丢失）
    TemplatePathEdit.TextChanged += _ => SyncConfigToModel();
    SharpPathEdit.TextChanged += _ => SyncConfigToModel();
    PluginDllEdit.TextChanged += _ => SyncConfigToModel();
    OutputDirEdit.TextChanged += _ => SyncConfigToModel();

    // 初始态：把持久化模型值同步到导出控件（避免开关默认值与模型不一致导致首次导出失真）
    LoadConfigToControls();

    RefreshPackageList();
    RefreshMappingList();
  }

  #region 数据源

  /// <summary>把四栏控件当前值写回数据模型并触发自动保存（事件只写模型）。</summary>
  private void PersistConfig()
  {
    if (_dm == null)
      return;
    _dm.MergeConfig.IncludeLstges.Value = IncludeLstgesToggle.ButtonPressed;
    _dm.MergeConfig.ObfuscateLua.Value = ObfuscateLuaToggle.ButtonPressed;
    _dm.MergeConfig.AutoRenameConflicts.Value = AutoRenameConflictsToggle.ButtonPressed;
    _dm.MergeConfig.ForcePerformAction.Value = ForcePerformActionToggle.ButtonPressed;
    _dm.MergeConfig.GroupByCreatorFolders.Value = GroupByCreatorFoldersToggle.ButtonPressed;
    _dm.MergeConfig.OutputDir.Value = OutputDirEdit.Text.Trim();
    _dm.MergeConfig.OutputName.Value = string.IsNullOrWhiteSpace(_dm.MergeConfig.OutputName.Value)
      ? "mod"
      : _dm.MergeConfig.OutputName.Value;
    _dm.TriggerAutoSave();
  }

  /// <summary>
  /// 把工程模板配置的 4 个路径/目录编辑框当前值写回模型并触发自动保存（事件只写模型，指示 24）。
  /// 用于 TemplatePathEdit/SharpPathEdit/PluginDllEdit/OutputDirEdit 的 TextChanged，保证输入即时落盘。
  /// </summary>
  private void SyncConfigToModel()
  {
    if (_dm == null)
      return;
    _dm.MergeConfig.TemplatePath.Value = TemplatePathEdit.Text.Trim();
    _dm.MergeConfig.SharpEditorPath.Value = SharpPathEdit.Text.Trim();
    _dm.MergeConfig.PluginDll.Value = PluginDllEdit.Text.Trim();
    _dm.MergeConfig.OutputDir.Value = OutputDirEdit.Text.Trim();
    _dm.TriggerAutoSave();
  }

  /// <summary>把持久化模型值同步到导出控件（初始态驱动，保证首次导出与配置一致，重启后回显）。</summary>
  private void LoadConfigToControls()
  {
    if (_dm == null)
      return;
    TemplatePathEdit.Text = _dm.MergeConfig.TemplatePath.Value;
    SharpPathEdit.Text = _dm.MergeConfig.SharpEditorPath.Value;
    PluginDllEdit.Text = _dm.MergeConfig.PluginDll.Value;
    OutputDirEdit.Text = _dm.MergeConfig.OutputDir.Value;
    IncludeLstgesToggle.ButtonPressed = _dm.MergeConfig.IncludeLstges.Value;
    ObfuscateLuaToggle.ButtonPressed = _dm.MergeConfig.ObfuscateLua.Value;
    AutoRenameConflictsToggle.ButtonPressed = _dm.MergeConfig.AutoRenameConflicts.Value;
    ForcePerformActionToggle.ButtonPressed = _dm.MergeConfig.ForcePerformAction.Value;
    GroupByCreatorFoldersToggle.ButtonPressed = _dm.MergeConfig.GroupByCreatorFolders.Value;
  }

  #endregion

  #region 左上 · 创作者包

  private void RefreshPackageList()
  {
    PackageList.Clear();
    foreach (var pkg in _dm?.CreatorPackages ?? new())
    {
      if (pkg.IsDeleted.Value)
        continue;
      PackageList.AddItem($"{pkg.CreatorName.Value}（{pkg.PackageName}）");
    }
  }

  /// <summary>
  /// 由选中包索引绑定驱动：刷新三栏（符卡/资源/对象）清单。
  /// 数据源为选中包的缓存清单（导入时抽取），非符标注为「（非符）」。
  /// </summary>
  private void RefreshPackageDetail()
  {
    SpellCardList.Clear();
    ResourceList.Clear();
    ObjectList.Clear();

    if (_dm == null)
      return;

    var index = _dm.MergeConfig.SelectedPackageIndex.Value;
    var packages = _dm.CreatorPackages.Where(p => !p.IsDeleted.Value).ToList();
    if (index < 0 || index >= packages.Count)
      return;

    var pkg = packages[index];
    foreach (var card in pkg.SpellCards)
      SpellCardList.AddItem(card);
    foreach (var r in pkg.Resources)
      ResourceList.AddItem(r);
    foreach (var o in pkg.Objects)
      ObjectList.AddItem(o);
  }

  /// <summary>选中包：事件只写模型（索引写 SelectedPackageIndex），三栏由绑定驱动刷新。</summary>
  private void OnPackageSelected()
  {
    if (_dm == null)
      return;
    var selected = PackageList.GetSelectedItems();
    if (selected.Length == 0)
      return;
    _dm.MergeConfig.SelectedPackageIndex.Value = selected[0];
  }

  private void OnImportPackage()
  {
    if (_dm == null)
      return;

    var fileDialog = new FileDialog
    {
      FileMode = FileDialog.FileModeEnum.OpenFile,
      Filters = new[] { "*.zip ; 创作者包" },
      Access = FileDialog.AccessEnum.Filesystem,
    };
    fileDialog.FileSelected += path =>
    {
      var result = MergeImporter.ImportZip(path);
      if (!result.IsSuccess || result.Package == null)
      {
        _log.Warn($"OnImportPackage: failed to import {path}: {result.Error}");
        return;
      }
      _dm.CreatorPackages.Add(result.Package);
      foreach (var card in result.Cards)
        _dm.MergeConfig.Mapping.Add(card);
      _dm.TriggerAutoSave();
    };
    AddChild(fileDialog);
    fileDialog.PopupCentered();
  }

  private void OnRemovePackage()
  {
    if (_dm == null)
      return;
    var index = _dm.MergeConfig.SelectedPackageIndex.Value;
    var packages = _dm.CreatorPackages.Where(p => !p.IsDeleted.Value).ToList();
    if (index < 0 || index >= packages.Count)
      return;
    packages[index].IsDeleted.Value = true;
    // 同步移除该包在对应表中的映射条目，避免「删包后再导出」时映射引用已删包而失败（P1-1）。
    // 模型变更触发 _mappingBinding 自动刷新对应表（指示 24，事件只写模型）。
    var removed = _dm
      .MergeConfig.Mapping.Where(m => m.PackageName == packages[index].PackageName)
      .ToList();
    foreach (var m in removed)
      _dm.MergeConfig.Mapping.Remove(m);
    // 置回未选中：绑定驱动三栏清空（指示 24，事件只写模型）。
    _dm.MergeConfig.SelectedPackageIndex.Value = -1;
    // IsDeleted 是包内 AutoValue，AutoList 绑定不触发外层刷新，故此处手动刷新包列表（P2-1 保留）。
    _dm.TriggerAutoSave();
    RefreshPackageList();
  }

  #endregion

  #region 左下 · 导入工程模板

  private void OnImportTemplate()
  {
    if (_dm == null)
      return;
    var path = TemplatePathEdit.Text.Trim();
    if (string.IsNullOrEmpty(path))
    {
      InjectionStatusLabel.Text = "请填写模板路径";
      return;
    }

    var doc = LstgesParser.LoadFile(path, out var error);
    if (doc == null)
    {
      InjectionStatusLabel.Text = $"解析失败：{error}";
      return;
    }

    var points = new InjectionPointDetector().Detect(doc);
    var spell = points.Find(InjectionPointKind.SpellCards) != null;
    var res = points.Find(InjectionPointKind.Resources) != null;
    var obj = points.Find(InjectionPointKind.Objects) != null;
    InjectionStatusLabel.Text =
      $"注入点：符卡{(spell ? "✓" : "✗")} 资源{(res ? "✓" : "✗")} 对象{(obj ? "✓" : "✗")}";

    _dm.MergeConfig.TemplatePath.Value = path;
    _dm.TriggerAutoSave();
  }

  #endregion

  #region 右上 · 对应表

  private void RefreshMappingList()
  {
    MappingList.Clear();
    foreach (var entry in _dm?.MergeConfig.Mapping ?? new())
    {
      var label = entry.IsNonSpell.Value
        ? $"（非符）{entry.Creator.Value}"
        : $"{entry.Name} — {entry.Creator.Value}";
      MappingList.AddItem(label);
    }
  }

  private Action OnMoveMapping(int delta) =>
    () =>
    {
      if (_dm == null || _selectedMapping < 0)
        return;
      var mapping = _dm.MergeConfig.Mapping;
      var target = _selectedMapping + delta;
      if (target < 0 || target >= mapping.Count)
        return;
      (mapping[_selectedMapping], mapping[target]) = (mapping[target], mapping[_selectedMapping]);
      _selectedMapping = target;
      _dm.TriggerAutoSave();
    };

  private void OnShuffleMapping()
  {
    if (_dm == null || _dm.MergeConfig.Mapping.Count <= 1)
      return;
    var entries = _dm.MergeConfig.Mapping.ToList();
    var rng = new Random();
    for (int i = entries.Count - 1; i > 0; i--)
    {
      int j = rng.Next(i + 1);
      (entries[i], entries[j]) = (entries[j], entries[i]);
    }
    _dm.MergeConfig.Mapping.Clear();
    foreach (var e in entries)
      _dm.MergeConfig.Mapping.Add(e);
    _dm.TriggerAutoSave();
  }

  #endregion

  #region 右下 · 导出功能

  /// <summary>
  /// 把导出控件的当前值写回模型（输出目录/输出名），使导出落盘目录与 UI 一致（事件只写模型）。
  /// </summary>
  private void SyncOutputToModel()
  {
    if (_dm == null)
      return;
    _dm.MergeConfig.OutputDir.Value = OutputDirEdit.Text.Trim();
    _dm.MergeConfig.OutputName.Value = string.IsNullOrWhiteSpace(_dm.MergeConfig.OutputName.Value)
      ? "mod"
      : _dm.MergeConfig.OutputName.Value;
    _dm.MergeConfig.IncludeLstges.Value = IncludeLstgesToggle.ButtonPressed;
    _dm.MergeConfig.ObfuscateLua.Value = ObfuscateLuaToggle.ButtonPressed;
    _dm.MergeConfig.AutoRenameConflicts.Value = AutoRenameConflictsToggle.ButtonPressed;
    _dm.MergeConfig.ForcePerformAction.Value = ForcePerformActionToggle.ButtonPressed;
    _dm.MergeConfig.GroupByCreatorFolders.Value = GroupByCreatorFoldersToggle.ButtonPressed;
  }

  /// <summary>
  /// 调起 Sharp Cli 把当前目录下的 .lstgproj 编译打包为 mod zip；返回成功与否。
  /// </summary>
  private bool TryBuildModZip(MergeEngine engine, out string message)
  {
    message = string.Empty;
    if (_dm == null)
      return false;

    var sharpDir = _dm.MergeConfig.SharpEditorPath.Value.Trim();
    var plugin = _dm.MergeConfig.PluginDll.Value.Trim();
    if (string.IsNullOrEmpty(sharpDir) || string.IsNullOrEmpty(plugin))
    {
      message = "未配置 Sharp 路径/插件 dll，跳过 mod zip 打包（仅生成对应表与工程）";
      return false;
    }

    var invoker = new SharpCliInvoker();
    var ok = invoker.Run(
      sharpDir,
      engine.MergedProjectPath,
      _dm.MergeConfig.OutputDir.Value.Trim(),
      _dm.MergeConfig.OutputName.Value.Trim(),
      plugin,
      out var exitCode,
      out _
    );
    message = ok
      ? $"已打包 mod zip（输出名：{_dm.MergeConfig.OutputName.Value}）"
      : $"Sharp 打包失败（exit {exitCode}），请检查 Sharp 配置";
    return ok;
  }

  private void OnExportFullPackage()
  {
    // 导出完整项目包：合并 → 落地工程（依「包含 .lstges」开关）→（可选）Sharp Cli 编译打包 mod zip。
    // 需模板路径与输出目录；Sharp 路径缺失时提示但不阻塞合并产物生成。
    if (_dm == null)
      return;

    SyncOutputToModel();

    var template = _dm.MergeConfig.TemplatePath.Value;
    var outputDir = _dm.MergeConfig.OutputDir.Value;
    if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(outputDir))
    {
      ConflictList.AddItem("[错误] 请先设置模板路径与输出目录");
      return;
    }

    var engine = new MergeEngine(
      _dm,
      template,
      IncludeLstgesToggle.ButtonPressed,
      ObfuscateLuaToggle.ButtonPressed
    );
    var result = engine.BuildAndMerge();
    if (!result.IsSuccess)
    {
      ConflictList.AddItem($"[失败] {result.Error}");
      return;
    }

    ConflictList.Clear();
    foreach (var c in result.Conflicts)
      ConflictList.AddItem($"[{c.Kind}] {c.Name}（{c.Packages}）");

    // Sharp 打包（可选）：配置了 Sharp 则打包 mod zip，否则仅提示（不阻塞工程/对应表生成）
    TryBuildModZip(engine, out var buildMessage);
    ConflictList.AddItem($"[打包] {buildMessage}");

    var exportResult = engine.ExportMapping(_dm.MergeConfig.OutputDir.Value);
    if (!string.IsNullOrEmpty(exportResult))
      ConflictList.AddItem("[完成] 对应表已导出");
    _dm.TriggerAutoSave();
  }

  private void OnExportMapping()
  {
    if (_dm == null || _dm.MergeConfig.Mapping.Count == 0)
      return;
    SyncOutputToModel();
    var bossName = MergeEngine.ResolveCommonBossName(_dm);
    var rows = _dm
      .MergeConfig.Mapping.Select(e => new SpellCardMappingRow(
        e.Name,
        e.Creator.Value,
        e.IsNonSpell.Value
      ))
      .ToList();
    var outPath = Path.Combine(_dm.MergeConfig.OutputDir.Value, "spellcard_mapping.csv");
    new SpellCardMappingExporter().Export(bossName, rows, outPath);
    ConflictList.AddItem($"[完成] 对应表已导出：{outPath}");
  }

  #endregion
}
