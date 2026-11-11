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
using AutoCMEX.UI;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;
using Godot;

/// <summary>
/// 整合板块：四栏联动。左上创作者包信息、左下工程模板配置、右上对应表（可编辑）、右下导出功能。
/// UI 刷新由 AutoValue/AutoList 绑定驱动，事件只写数据模型。
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

  /// <summary>右上「对应表」·「打乱模式」下拉（持久化为 <see cref="Models.MergeConfig.ShuffleMode"/>）：随机 / 交替 / 按创作者分组。</summary>
  [Node("%MappingModeOption")]
  public IOptionButton MappingModeOption { get; set; } = default!;

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

  [Node("%MergeAlgorithmOption")]
  public IOptionButton MergeAlgorithmOption { get; set; } = default!;

  [Node("%ExportFullPackageBtn")]
  public IButton ExportFullPackageBtn { get; set; } = default!;

  [Node("%ExportMappingBtn")]
  public IButton ExportMappingBtn { get; set; } = default!;

  #endregion

  #region 场景内对话框

  /// <summary>导入创作者包的文件选择框（场景节点；复用同一实例才能沿用上次尺寸）。</summary>
  [Node("%ImportPackageDialog")]
  public IFileDialog ImportPackageDialog { get; set; } = default!;

  #endregion

  #region 分割条布局

  /// <summary>整合板块根分割条（左 / 右两栏），位置由 <c>ui_layout.json</c> 的 <c>merge.root</c> 记录还原。</summary>
  [Node("%RootSplit")]
  public ISplitContainer RootSplit { get; set; } = default!;

  /// <summary>整合板块左栏分割条（左上 / 左下），对应语义键 <c>merge.left</c>。</summary>
  [Node("%LeftSplit")]
  public ISplitContainer LeftSplit { get; set; } = default!;

  /// <summary>整合板块右栏分割条（右上 / 右下），对应语义键 <c>merge.right</c>。</summary>
  [Node("%RightSplit")]
  public ISplitContainer RightSplit { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  #endregion

  private DataManager? _dm;

  /// <summary>本面板日志：依赖解析时按模块名取自 <see cref="DataManager.GetLogger"/>（生产为 ILogService 模块 logger，测试为 NullLog）。</summary>
  /// <remarks>解析前退回 NullLog 兜底；唯一使用点在导入失败的用户交互路径上。</remarks>
  private ILog _log = NullLog.Instance;

  private AutoList<CreatorPackage>.Binding? _creatorPackagesBinding;
  private AutoList<SpellCardMappingEntry>.Binding? _mappingBinding;
  private AutoValue<int>.Binding? _selectedPackageIndexBinding;
  private int _selectedMapping = -1;

  /// <summary>三条分割条的布局接线路（根 / 左 / 右），在 <see cref="OnResolved"/> 中按语义键建立并应用。</summary>
  private readonly List<SplitLayoutLink> _splitLinks = new();

  /// <summary>每个创作者包 <see cref="CreatorPackage.IsDeleted"/> 的元素级绑定（包增删时重挂）。</summary>
  /// <remarks>
  /// 列表级 <c>OnModify</c> 只在包增删时触发；删包改的是包内 <c>IsDeleted</c> AutoValue，
  /// 列表对象本身没有任何变化，只绑列表会让已删包继续留在列表中、并可被选中或误用于导出。
  /// </remarks>
  private readonly List<IDisposable> _packageDeletedBindings = new();

  /// <summary>是否正在重挂包级绑定（订阅时会立刻回放一次，抑制这类伪变更）。</summary>
  private bool _isRebindingPackageDeletedBindings;

  /// <summary>当前选中包的引用（用于过滤列表变化后按身份重定位选中下标）。</summary>
  /// <remarks>
  /// <see cref="Models.MergeConfig.SelectedPackageIndex"/> 是「过滤后可见列表」的显示序号：
  /// 删除靠前的包会让后面的包整体前移，沿用旧序号会静默选中别的包，因此另存包引用作为身份。
  /// </remarks>
  private CreatorPackage? _selectedPackage;

  public override void _Notification(int what) => this.Notify(what);

  public override void _ExitTree()
  {
    _creatorPackagesBinding?.Dispose();
    _mappingBinding?.Dispose();
    _selectedPackageIndexBinding?.Dispose();
    DisposePackageDeletedBindings();

    // 退出前把三条分割条的当前布局写回模型；随后主窗口的 DataManager.Dispose 统一落盘
    foreach (var link in _splitLinks)
      link.Capture();
  }

  public void OnReady()
  {
    ImportPackageBtn.Pressed += OnImportPackage;
    ImportPackageDialog.FileSelected += OnImportPackageFileSelected;
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

    // 合并算法选择：提取式（默认）/ 按顶层文件夹整搬
    // 仅添加选项；ItemSelected 的接线放在 OnResolved 回填之后（见该方法内的时序说明）。
    MergeAlgorithmOption.AddItem("提取式（按类型重放）");
    MergeAlgorithmOption.AddItem("按顶层文件夹整搬");

    // 对应表「打乱模式」下拉：随机（默认）/ 交替 / 按创作者分组。
    // 顺序必须与 MappingShuffleMode 枚举值一致；同样只添加选项，ItemSelected 接线见 OnResolved。
    MappingModeOption.AddItem("随机");
    MappingModeOption.AddItem("交替（非符/符卡）");
    MappingModeOption.AddItem("按创作者分组");
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm == null)
      return;

    _log = _dm.GetLogger(nameof(MergePanel));

    _creatorPackagesBinding = _dm.CreatorPackages.Bind().OnModify(OnCreatorPackagesChanged);
    _mappingBinding = _dm
      .MergeConfig.Mapping.Bind()
      .OnModify(() => CallDeferred(nameof(RefreshMappingList)));

    // 包级 IsDeleted 元素绑定：已有包先挂上，之后由 OnCreatorPackagesChanged 随列表增删重挂。
    RebindPackageDeletedBindings();

    // 选包变化（含初始值）驱动三栏清单刷新（事件只写模型，UI 由绑定传播）。
    _selectedPackageIndexBinding = _dm
      .MergeConfig.SelectedPackageIndex.Bind()
      .OnValue(_ => CallDeferred(nameof(RefreshPackageDetail)));

    // 初始态：把持久化模型值同步到导出控件（避免开关默认值与模型不一致导致首次导出失真）。
    // 必须早于下方事件注册：程序化回填 ButtonPressed 会按 Godot 语义触发 Toggled，
    // 若此时监听者已就绪，回填信号会被误判为用户输入，导致全量写回 + 落盘，
    // 把尚未回填的项用控件默认值覆盖（加载即被清空）。回填阶段无监听者，
    // 才能保证「只有真实用户交互才写回模型」。
    LoadConfigToControls();

    // 事件注册（回填完成之后）：开关/算法/路径编辑框一律只写数据模型 + 触发保存。
    IncludeLstgesToggle.Toggled += _ => PersistConfig();
    ObfuscateLuaToggle.Toggled += _ => PersistConfig();
    AutoRenameConflictsToggle.Toggled += _ => PersistConfig();
    ForcePerformActionToggle.Toggled += _ => PersistConfig();
    GroupByCreatorFoldersToggle.Toggled += _ => PersistConfig();
    MergeAlgorithmOption.ItemSelected += _ => PersistConfig();

    // 右上「对应表」·打乱模式下拉：只属于对应表本身，与导出配置无关，故单独写回。
    MappingModeOption.ItemSelected += _ => SyncMappingModeToModel();

    // 工程模板配置：路径/目录编辑框输入即写回模型 + 触发保存（避免只填不写、重启丢失）
    TemplatePathEdit.TextChanged += _ => SyncConfigToModel();
    SharpPathEdit.TextChanged += _ => SyncConfigToModel();
    PluginDllEdit.TextChanged += _ => SyncConfigToModel();
    OutputDirEdit.TextChanged += _ => SyncConfigToModel();

    EnsureSplitLayoutLinks();

    RefreshPackageList();
    RefreshMappingList();
  }

  /// <summary>
  /// 建立并应用三条分割条的布局接线路（规则 6：依赖解析完成后接线；重复解析只重新应用，不重复订阅信号）。
  /// </summary>
  private void EnsureSplitLayoutLinks()
  {
    if (_dm is null)
      return;

    if (_splitLinks.Count == 0)
    {
      _splitLinks.Add(new SplitLayoutLink(RootSplit, UiLayoutKeys.MergeRoot, _dm));
      _splitLinks.Add(new SplitLayoutLink(LeftSplit, UiLayoutKeys.MergeLeft, _dm));
      _splitLinks.Add(new SplitLayoutLink(RightSplit, UiLayoutKeys.MergeRight, _dm));
    }

    foreach (var link in _splitLinks)
      link.Apply();
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
    _dm.MergeConfig.Algorithm.Value = (MergeAlgorithm)MergeAlgorithmOption.Selected;
    _dm.MergeConfig.OutputDir.Value = OutputDirEdit.Text.Trim();
    _dm.MergeConfig.OutputName.Value = string.IsNullOrWhiteSpace(_dm.MergeConfig.OutputName.Value)
      ? "mod"
      : _dm.MergeConfig.OutputName.Value;
    _dm.TriggerAutoSave();
  }

  /// <summary>
  /// 把工程模板配置的 4 个路径/目录编辑框当前值写回模型并触发自动保存（事件只写模型）。
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

  /// <summary>
  /// 把右上「对应表」的「打乱模式」下拉写回模型并触发自动保存（事件只写模型）。
  /// 该下拉与导出/整合控件无关，故不并入 <see cref="PersistConfig"/>。
  /// </summary>
  private void SyncMappingModeToModel()
  {
    if (_dm == null)
      return;
    _dm.MergeConfig.ShuffleMode.Value = (MappingShuffleMode)MappingModeOption.Selected;
    _dm.TriggerAutoSave();
  }

  /// <summary>把持久化模型值同步到导出控件（初始态驱动，保证首次导出与配置一致，重启后回显）。</summary>
  private void LoadConfigToControls()
  {
    if (_dm == null)
      return;
    MappingModeOption.Select((int)_dm.MergeConfig.ShuffleMode.Value);
    TemplatePathEdit.Text = _dm.MergeConfig.TemplatePath.Value;
    SharpPathEdit.Text = _dm.MergeConfig.SharpEditorPath.Value;
    PluginDllEdit.Text = _dm.MergeConfig.PluginDll.Value;
    OutputDirEdit.Text = _dm.MergeConfig.OutputDir.Value;
    IncludeLstgesToggle.ButtonPressed = _dm.MergeConfig.IncludeLstges.Value;
    ObfuscateLuaToggle.ButtonPressed = _dm.MergeConfig.ObfuscateLua.Value;
    AutoRenameConflictsToggle.ButtonPressed = _dm.MergeConfig.AutoRenameConflicts.Value;
    ForcePerformActionToggle.ButtonPressed = _dm.MergeConfig.ForcePerformAction.Value;
    GroupByCreatorFoldersToggle.ButtonPressed = _dm.MergeConfig.GroupByCreatorFolders.Value;
    MergeAlgorithmOption.Select((int)_dm.MergeConfig.Algorithm.Value);
  }

  #endregion

  #region 左上 · 创作者包

  /// <summary>
  /// 创作者包列表增删：先按包身份重定位选中下标、重挂每包的
  /// <see cref="CreatorPackage.IsDeleted"/> 元素级绑定，再排队重建包列表。
  /// </summary>
  private void OnCreatorPackagesChanged()
  {
    RelocateSelectedPackageIndex();
    RebindPackageDeletedBindings();
    CallDeferred(nameof(RefreshPackageList));
  }

  /// <summary>
  /// 为每个创作者包挂 <see cref="CreatorPackage.IsDeleted"/> 绑定：删包时驱动包列表重建。
  /// </summary>
  /// <remarks>
  /// 重挂期间抑制回调：<see cref="AutoValue{T}"/> 订阅会立刻用当前值回放一次，不抑制的话每次
  /// 包增删都要按「包数」排一串重建（与 <c>BossDataWatcher</c> 同理）。抑制窗口内的真实变化不会丢：
  /// 紧随其后的 <see cref="RefreshPackageList"/> 会整体重建一次。
  /// </remarks>
  private void RebindPackageDeletedBindings()
  {
    DisposePackageDeletedBindings();

    if (_dm is null)
      return;

    _isRebindingPackageDeletedBindings = true;
    try
    {
      // 先取快照再遍历（规则 22）：绑定期间即使列表被并发增删也不会在枚举中改集合。
      foreach (var pkg in _dm.CreatorPackages.ToList())
        _packageDeletedBindings.Add(pkg.IsDeleted.Bind().OnValue(_ => OnPackageDeletedChanged()));
    }
    finally
    {
      _isRebindingPackageDeletedBindings = false;
    }
  }

  /// <summary>释放全部包级 <see cref="CreatorPackage.IsDeleted"/> 绑定（列表重建或本面板退出时）。</summary>
  private void DisposePackageDeletedBindings()
  {
    foreach (var binding in _packageDeletedBindings)
      binding.Dispose();

    _packageDeletedBindings.Clear();
  }

  /// <summary>
  /// 删包（软删除）：先按包身份重定位选中下标（删除靠前的包会让显示序号整体前移），再排队重建包列表。
  /// </summary>
  /// <remarks>
  /// 被删的正是当前选中包时重定位结果为 -1（选中清空）；被删的是非选中包时选中项保持原包，
  /// 不会因过滤掉靠前的包而静默选中别的包。
  /// </remarks>
  private void OnPackageDeletedChanged()
  {
    if (_isRebindingPackageDeletedBindings)
      return;

    RelocateSelectedPackageIndex();
    CallDeferred(nameof(RefreshPackageList));
  }

  /// <summary>
  /// 按包身份重定位 <see cref="Models.MergeConfig.SelectedPackageIndex"/>：把「当前选中包」在过滤后
  /// 可见列表里的新序号写回；当前选中包已被删除/移除时写 -1。
  /// </summary>
  /// <remarks>
  /// 只在已有选中包身份（<see cref="_selectedPackage"/>）时重定位：没有身份时无从判断「原本选的是谁」，
  /// 保持现状（未选中为 -1）比猜一个下标更安全。
  /// </remarks>
  private void RelocateSelectedPackageIndex()
  {
    var selected = _selectedPackage;
    if (_dm is null || selected is null)
      return;

    var packages = _dm.CreatorPackages.Where(pkg => !pkg.IsDeleted.Value).ToList();
    // List.IndexOf 对 CreatorPackage 走引用相等（模型未重写 Equals）：正是「按包定位」所需的语义
    _dm.MergeConfig.SelectedPackageIndex.Value = packages.IndexOf(selected);
  }

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
    {
      // 未选中/选中项失效：清掉身份，后续集合变化不按旧包重定位
      _selectedPackage = null;
      return;
    }

    var pkg = packages[index];
    _selectedPackage = pkg;
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

    // 双检：对话框是场景节点，但面板退出/场景重建后可能已被释放；
    // 先取底层 Godot 对象确认仍有效，避免对失效节点弹窗（与其它面板的 IsInstanceValid 双检一致）。
    var dialog = ImportPackageDialog?.GetTargetObj<GodotObject>();
    if (dialog is null || !GodotObject.IsInstanceValid(dialog))
      return;

    FileDialogSizing.Popup(ImportPackageDialog, _dm);
  }

  /// <summary>选定创作者包：解包成功后追加包与对应表映射，并触发自动保存；失败只记日志。</summary>
  /// <param name="path">用户选定的创作包 zip 路径。</param>
  private void OnImportPackageFileSelected(string path)
  {
    var result = MergeImporter.ImportZip(path);
    if (!result.IsSuccess || result.Package == null)
    {
      _log.Warn($"OnImportPackage: failed to import {path}: {result.Error}");
      return;
    }

    if (_dm == null)
      return;
    _dm.CreatorPackages.Add(result.Package);
    foreach (var card in result.Cards)
      _dm.MergeConfig.Mapping.Add(card);
    _dm.TriggerAutoSave();
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
    // 同步移除该包在对应表中的映射条目，避免「删包后再导出」时映射引用已删包而失败。
    // 模型变更触发 _mappingBinding 自动刷新对应表（事件只写模型）。
    var removed = _dm
      .MergeConfig.Mapping.Where(m => m.PackageName == packages[index].PackageName)
      .ToList();
    foreach (var m in removed)
      _dm.MergeConfig.Mapping.Remove(m);
    // 置回未选中：绑定驱动三栏清空（事件只写模型）。
    _dm.MergeConfig.SelectedPackageIndex.Value = -1;
    // IsDeleted 是包内 AutoValue，由元素级绑定驱动包列表重建（见 OnPackageDeletedChanged），不再手动刷新。
    _dm.TriggerAutoSave();
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

  /// <summary>
  /// 按右上「对应表」所选「打乱模式」重排 <see cref="Models.MergeConfig.Mapping"/>
  /// （顺序即注入顺序，同时决定对应表导出顺序）。
  /// <para>
  /// 三种模式在非退化输入下通常产生不同排列（单元素/每组仅 1 项时结果不变）：随机 = 整体 Fisher-Yates；
  /// 交替 = 形态由规则（非符/符卡块长）决定、两类内容各自随机；
  /// 按创作者分组 = 分组与组序确定、组内随机。
  /// </para>
  /// </summary>
  private void OnShuffleMapping()
  {
    if (_dm == null || _dm.MergeConfig.Mapping.Count <= 1)
      return;

    var rng = Random.Shared;
    List<SpellCardMappingEntry> entries;
    switch (_dm.MergeConfig.ShuffleMode.Value)
    {
      case MappingShuffleMode.Interleave:
        entries = MappingInterleave.Reinterleave(_dm.MergeConfig.Mapping, rng);
        break;
      case MappingShuffleMode.GroupByCreator:
        entries = MappingGroupByCreator.RegroupByCreator(_dm.MergeConfig.Mapping, rng);
        break;
      default:
        entries = _dm.MergeConfig.Mapping.ToList();
        ListShuffler.Shuffle(entries, rng);
        break;
    }

    _dm.MergeConfig.Mapping.Clear();
    foreach (var e in entries)
      _dm.MergeConfig.Mapping.Add(e);
    _dm.TriggerAutoSave();
  }

  #endregion

  #region 右下 · 导出功能

  /// <summary>
  /// 把导出区全部控件值（输出目录/输出名 + 5 个开关 + 合并算法）写回模型，使导出与 UI 一致（事件只写模型）。
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
    _dm.MergeConfig.Algorithm.Value = (MergeAlgorithm)MergeAlgorithmOption.Selected;
  }

  /// <summary>
  /// 调起 Sharp Cli 把合并产生的工作工程（临时目录下的 .lstgproj）编译打包为 mod zip；返回成功与否。
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

    var invoker = new SharpCliInvoker(_log);
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
      ObfuscateLuaToggle.ButtonPressed,
      _log
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

    // Sharp 打包（可选）：配置了 Sharp 路径与插件 dll 才打包 mod zip，否则仅提示（不阻塞工程/对应表生成）
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
    new SpellCardMappingExporter(_log).Export(bossName, rows, outPath);
    ConflictList.AddItem($"[完成] 对应表已导出：{outPath}");
  }

  #endregion
}
