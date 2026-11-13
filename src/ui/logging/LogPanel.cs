namespace AutoCMEX.UI.Logging;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Logging;
using AutoCMEX.Helpers;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 日志面板：实时显示 <see cref="InMemoryLogWriter"/> 中的日志条目。
/// </summary>
/// <remarks>
/// <para>提供日志级别过滤、模块筛选、自动滚动、清空功能。</para>
/// <para>通过 [Dependency] 获取 ILogService。</para>
/// <para>日志配置由独立的 LogConfigPanel 子场景处理。</para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class LogPanel : Control, ILogPanel
{
  #region AutoConnect Nodes

  [Node("%LogView")]
  public IRichTextLabel LogView { get; set; } = default!;

  [Node("%LevelFilter")]
  public IOptionButton LevelFilter { get; set; } = default!;

  [Node("%ModuleFilter")]
  public IOptionButton ModuleFilter { get; set; } = default!;

  [Node("%PauseBtn")]
  public IButton PauseBtn { get; set; } = default!;

  [Node("%ClearBtn")]
  public IButton ClearBtn { get; set; } = default!;

  [Node("%LogDirLabel")]
  public ILabel LogDirLabel { get; set; } = default!;

  #endregion

  [Dependency]
  public ILogService LogService => this.DependOn<ILogService>();

  // 下面五个是「默认主题」的兜底色：与接入主题前写死的那套颜色完全一致，
  // 主题未提供对应语义色（默认主题）时原样沿用，保证观感零变化。

  /// <summary>时间戳（弱化文字）兜底色。</summary>
  private static readonly Color MutedTextFallback = new("#888888");

  /// <summary>日志正文兜底色。</summary>
  private static readonly Color LogTextFallback = new("#dddddd");

  /// <summary>Warn 级别兜底色。</summary>
  private static readonly Color WarnFallback = new("#d4a017");

  /// <summary>Error 级别兜底色。</summary>
  private static readonly Color ErrorFallback = new("#d04040");

  /// <summary>模块名（强调色）兜底色。</summary>
  private static readonly Color AccentFallback = new("#5dade2");

  private ILogService? _service;
  private InMemoryLogWriter? _writer;
  private bool _paused;
  private bool _suppressEvents;

  /// <summary>本帧是否已排过主题重画：合并同一帧里父子控件连发的主题通知。</summary>
  private bool _themeRefreshQueued;

  /// <summary>是否已离场：用于挡掉离场后排到帧末的主题重画。</summary>
  /// <remarks>
  /// 本面板是主窗口常驻子节点，离场后不会重新入树：AutoInject 需要显式 <c>RequestReady</c>
  /// 才会重新解析依赖，本类也不做信号重连。若要支持重新入树，需自行补上这两件事；
  /// 这里只保证离场后不再驱动重画。
  /// </remarks>
  private bool _exiting;

  // 待处理日志队列(用于跨线程安全)
  private readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _pending = new();

  // 待注册模块名队列:OnNewLogEntry 可能在写日志线程被调用,只能入队,注册留到主线程 _Process
  private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingModules = new();

  // 已知模块集合,用于筛选下拉框(动态扩充)。只在主线程访问(_Process / RefreshFromBuffer)
  private readonly HashSet<string> _knownModules = new(StringComparer.Ordinal);

  /// <summary>当前过滤级别。</summary>
  public LogLevel CurrentMinLevel { get; private set; } = LogLevel.Info;

  /// <summary>当前模块筛选。空字符串表示全部。</summary>
  public string CurrentModule { get; private set; } = string.Empty;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    _suppressEvents = true;

    // 日志级别过滤:All=Info(含所有), Info/Warn/Error
    LevelFilter.Clear();
    LevelFilter.AddItem("All", 0);
    LevelFilter.AddItem("Info", 1);
    LevelFilter.AddItem("Warn", 2);
    LevelFilter.AddItem("Error", 3);
    LevelFilter.Selected = 0;
    LevelFilter.ItemSelected += OnLevelFilterChanged;

    // 模块筛选:All + 已知模块
    ModuleFilter.Clear();
    ModuleFilter.AddItem("All", 0);
    ModuleFilter.ItemSelected += OnModuleFilterChanged;

    PauseBtn.Text = "暂停";
    PauseBtn.ToggleMode = true;
    PauseBtn.Toggled += OnPauseToggled;

    ClearBtn.Text = "清空";
    ClearBtn.Pressed += OnClearPressed;

    // 已渲染条目里的 BBCode 颜色在写入时就固化了，主题切换不会自己更新：
    // 订阅自身主题变化后整段重建（浅色主题需要另一套语义色）。重建排到帧末，见 OnThemeChanged。
    ThemeChanged += OnThemeChanged;

    _suppressEvents = false;
  }

  public void OnResolved()
  {
    _service = LogService;
    if (_service == null)
      return;

    _writer = _service.InMemoryWriter;
    _writer.OnNewLogEntry -= OnNewLogEntry;
    _writer.OnNewLogEntry += OnNewLogEntry;

    // 初次显示历史条目
    RefreshFromBuffer();
    LogDirLabel.Text =
      $"日志目录: {_service.Config.LogDirectory}  ·  缓冲 {_service.Config.InMemoryBufferSize} 条";
  }

  public override void _ExitTree()
  {
    ThemeChanged -= OnThemeChanged;

    if (_writer != null)
      _writer.OnNewLogEntry -= OnNewLogEntry;

    // 离场后不再需要排队标记：本帧已经不会有重画要合并了
    _themeRefreshQueued = false;

    // 已离场的面板不再重画：排到帧末的 deferred 可能还没跑，由 FlushThemeRefresh 的 _exiting 挡掉。
    // 不置空 _writer：AutoInject 不会自行重新解析依赖（需显式 RequestReady），置空后若真重新入树会永久空白且无报错。
    _exiting = true;
  }

  /// <summary>主题变化：把整段重建排到帧末执行（同一帧的多次通知只排一次）。</summary>
  /// <remarks>
  /// 不能在通知回调里直接重建：Godot 的主题通知是父→子，父控件自己的 <c>theme_changed</c> 触发时，
  /// 它（以及还没收到通知的子控件）解析主题项拿到的仍是上一套值，重建出的 BBCode 颜色会把旧主题色固化下来
  /// （浅色 accent 落到深底上就是近黑）。排到帧末时新主题已应用完，读到的才是新值。
  /// </remarks>
  private void OnThemeChanged() => QueueThemeRefresh();

  /// <summary>排一次帧末的主题重画；本帧已排过就直接返回（合并多次通知）。</summary>
  private void QueueThemeRefresh()
  {
    if (_themeRefreshQueued)
      return;

    _themeRefreshQueued = true;
    CallDeferred(nameof(FlushThemeRefresh));
  }

  /// <summary>执行帧末合并后的主题重画；公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约。</summary>
  public void FlushThemeRefresh()
  {
    // 离场的面板不再重画（deferred 可能排在离场之后才执行）
    if (_exiting)
    {
      _themeRefreshQueued = false;
      return;
    }

    _themeRefreshQueued = false;
    RefreshFromBuffer();
  }

  /// <summary>
  /// 新日志回调。<b>可能在写日志线程被调用</b>：这里只做队列操作，
  /// 模块注册（改筛选下拉与 <see cref="_knownModules"/>）必须留到主线程 <see cref="_Process"/>。
  /// </summary>
  /// <param name="entry">新日志条目。</param>
  private void OnNewLogEntry(LogEntry entry)
  {
    if (_paused || _writer == null)
      return;

    EnqueuePendingModule(entry.Module);

    if (entry.Level < CurrentMinLevel)
      return;
    if (!string.IsNullOrEmpty(CurrentModule) && entry.Module != CurrentModule)
      return;
    _pending.Enqueue(entry);
  }

  /// <summary>把待注册模块名入队(写日志线程安全；去重在主线程的 <see cref="EnsureModuleRegistered"/> 里做)。</summary>
  /// <param name="module">模块名。</param>
  private void EnqueuePendingModule(string? module)
  {
    // 不能在这里查 _knownModules:那是主线程独占的 HashSet
    if (string.IsNullOrEmpty(module))
      return;

    _pendingModules.Enqueue(module);
  }

  public override void _Process(double delta)
  {
    if (_paused)
      return;

    // 先在本帧把新模块名注册进筛选下拉(写日志线程只入队),再消费日志条目
    DrainPendingModules();

    if (_pending.IsEmpty)
      return;
    // 节流:每个 _Process 最多处理 200 条,避免积压造成卡顿
    int processed = 0;
    while (processed < 200 && _pending.TryDequeue(out var entry) && LogView != null)
    {
      AppendEntryToView(entry);
      processed++;
    }
  }

  private void AppendEntryToView(LogEntry entry)
  {
    if (LogView == null)
      return;

    // 语义色统一经 AppPalette 取（浅色主题下深底颜色会看不清），取不到就退回默认主题那套颜色。
    // LogView 是 [Node] 注入的接口适配器，取其包装的真实控件来解析主题链。
    var view = (LogView as INodeAdapter)?.TargetObj;
    var levelColor = entry.Level switch
    {
      LogLevel.Warn => AppPalette.Get(view, ThemeCatalog.StatusWarn, WarnFallback),
      LogLevel.Error => AppPalette.Get(view, ThemeCatalog.StatusError, ErrorFallback),
      _ => AppPalette.Get(view, ThemeCatalog.LogText, LogTextFallback),
    };
    var mutedColor = AppPalette.Get(view, ThemeCatalog.MutedText, MutedTextFallback);
    var accentColor = AppPalette.Get(view, ThemeCatalog.Accent, AccentFallback);

    // BBCode 只认十六进制色号：ToHtml(false) 给出不带 # / alpha 的 RGB 串（默认 true 会多出 alpha 两位）
    var line =
      $"[color=#{mutedColor.ToHtml(false)}]{entry.Timestamp.ToLocalTime():HH:mm:ss.fff}[/color] "
      + $"[b][color=#{levelColor.ToHtml(false)}]{entry.Level}[/color][/b] "
      + $"[color=#{accentColor.ToHtml(false)}]{StringEscapeHelper.EscapeBbcode(entry.Module)}[/color]  "
      + $"{StringEscapeHelper.EscapeBbcode(entry.Message)}\n";
    LogView.AppendText(line);
  }

  /// <summary>
  /// 从内存缓冲区重新加载条目到视图(用于筛选切换、主题变化与依赖重新解析时)。
  /// </summary>
  /// <remarks>
  /// 缓冲区快照与待处理队列必须一起去重，否则同一批条目会被画两遍（表现为同一毫秒的重复行）：
  /// <see cref="InMemoryLogWriter.AddEntry"/> 先入缓冲再抛事件，故待处理条目通常也已在快照里；
  /// 但快照之后才入队（或缓冲区满被挤出）的条目只存在于队列里。因此
  /// 「只清空队列不排空」会让下一帧 <see cref="_Process"/> 把同一批再画一遍，
  /// 「先排空队列再取快照」也只是把重复挪到快照里；这里取快照 → 排空队列 →
  /// 先画快照、再补画**不在快照里**的队列条目（按引用判同一条目）。
  /// </remarks>
  public void RefreshFromBuffer()
  {
    if (_writer == null || LogView == null)
      return;

    var buffered = _writer.GetEntries();
    var bufferedSet = new HashSet<LogEntry>(buffered, ReferenceEqualityComparer.Instance);

    // 只在主线程排空：排空后重画，下一帧 _Process 就不会再把这一批画一遍
    var drained = new List<LogEntry>();
    while (_pending.TryDequeue(out var queued))
      drained.Add(queued);

    LogView.Clear();

    foreach (var entry in buffered)
      RenderEntry(entry);

    // 快照之后才入队/被挤出缓冲区的条目只能从队列里补回来，否则会丢行
    foreach (var entry in drained)
    {
      if (!bufferedSet.Contains(entry))
        RenderEntry(entry);
    }
  }

  /// <summary>渲染单条条目：先注册模块名，再按当前级别下限与模块筛选决定是否画进视图。</summary>
  /// <param name="entry">待渲染的日志条目。</param>
  private void RenderEntry(LogEntry entry)
  {
    EnsureModuleRegistered(entry.Module);
    if (entry.Level < CurrentMinLevel)
      return;
    if (!string.IsNullOrEmpty(CurrentModule) && entry.Module != CurrentModule)
      return;
    AppendEntryToView(entry);
  }

  private void OnLevelFilterChanged(long index)
  {
    if (_suppressEvents)
      return;
    CurrentMinLevel = index switch
    {
      2 => LogLevel.Warn,
      3 => LogLevel.Error,
      _ => LogLevel.Info,
    };
    RefreshFromBuffer();
  }

  private void OnModuleFilterChanged(long index)
  {
    if (_suppressEvents)
      return;
    var text = ModuleFilter.GetItemText((int)index);
    CurrentModule = text == "All" ? string.Empty : text;
    RefreshFromBuffer();
  }

  private void OnPauseToggled(bool toggledOn)
  {
    _paused = toggledOn;
    PauseBtn.Text = toggledOn ? "继续" : "暂停";
  }

  private void OnClearPressed()
  {
    LogView?.Clear();
    _writer?.Clear();
  }

  /// <summary>主线程排空待注册模块名队列。</summary>
  private void DrainPendingModules()
  {
    while (_pendingModules.TryDequeue(out var module))
      EnsureModuleRegistered(module);
  }

  /// <summary>
  /// 把模块名注册进筛选下拉(仅主线程调用:会改控件与 <see cref="_knownModules"/>)。
  /// </summary>
  /// <param name="module">模块名。</param>
  private void EnsureModuleRegistered(string module)
  {
    if (string.IsNullOrEmpty(module) || _knownModules.Contains(module))
      return;
    _knownModules.Add(module);
    if (ModuleFilter == null)
      return;
    // 查找是否已存在
    for (int i = 0; i < ModuleFilter.ItemCount; i++)
    {
      if (ModuleFilter.GetItemText(i) == module)
        return;
    }
    ModuleFilter.AddItem(module);
  }
}
