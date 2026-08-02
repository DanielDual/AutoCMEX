namespace AutoCMEX.UI.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using AutoCMEX.Core.Logging;
using AutoCMEX.Helpers;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 日志面板：实时显示 <see cref="InMemoryLogWriter"/> 中的日志条目。
/// </summary>
/// <remarks>
/// <para>提供日志级别过滤、模块筛选、自动滚动、清空、配置入口。</para>
/// <para>通过 [Dependency] 获取 ILogService。</para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public partial class LogPanel : Control
{
  #region AutoConnect Nodes

  [Node("%LogView")]
  public RichTextLabel LogView { get; set; } = default!;

  [Node("%LevelFilter")]
  public OptionButton LevelFilter { get; set; } = default!;

  [Node("%ModuleFilter")]
  public OptionButton ModuleFilter { get; set; } = default!;

  [Node("%PauseBtn")]
  public Button PauseBtn { get; set; } = default!;

  [Node("%ClearBtn")]
  public Button ClearBtn { get; set; } = default!;

  [Node("%MaxFileCountInput")]
  public SpinBox MaxFileCountInput { get; set; } = default!;

  [Node("%MinLevelOption")]
  public OptionButton MinLevelOption { get; set; } = default!;

  [Node("%ApplyConfigBtn")]
  public Button ApplyConfigBtn { get; set; } = default!;

  [Node("%StatusLabel")]
  public RichTextLabel StatusLabel { get; set; } = default!;

  #endregion

  [Dependency]
  public ILogService LogService => this.DependOn<ILogService>();

  private ILogService? _service;
  private InMemoryLogWriter? _writer;
  private bool _paused;
  private bool _suppressEvents;

  // 待处理日志队列(用于跨线程安全)
  private readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _pending = new();

  // 已知模块集合,用于筛选下拉框(动态扩充)
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

    // 配置项
    MaxFileCountInput.MinValue = 1;
    MaxFileCountInput.MaxValue = 1000;
    ApplyConfigBtn.Text = "应用配置";
    ApplyConfigBtn.Pressed += OnApplyConfigPressed;

    MinLevelOption.Clear();
    MinLevelOption.AddItem("Info", 0);
    MinLevelOption.AddItem("Warn", 1);
    MinLevelOption.AddItem("Error", 2);
    MinLevelOption.Selected = 0;

    // 点击日志目录路径时打开文件资源管理器
    StatusLabel.MetaClicked += OnStatusMetaClicked;

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

    // 初始化配置 UI
    _suppressEvents = true;
    MaxFileCountInput.Value = _service.Config.MaxFileCount;
    MinLevelOption.Selected = (int)_service.Config.MinLevel;
    _suppressEvents = false;

    // 初次显示历史条目
    RefreshFromBuffer();
    SetStatus(
      $"日志目录: [url=file://{_service.Config.LogDirectory}]{_service.Config.LogDirectory}[/url]  ·  缓冲 {_service.Config.InMemoryBufferSize} 条"
    );
  }

  public override void _ExitTree()
  {
    if (_writer != null)
      _writer.OnNewLogEntry -= OnNewLogEntry;
  }

  private void OnNewLogEntry(LogEntry entry)
  {
    if (_paused || _writer == null)
      return;
    EnsureModuleRegistered(entry.Module);
    if (entry.Level < CurrentMinLevel)
      return;
    if (!string.IsNullOrEmpty(CurrentModule) && entry.Module != CurrentModule)
      return;
    _pending.Enqueue(entry);
  }

  public override void _Process(double delta)
  {
    if (_paused)
      return;
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
    var color = entry.Level switch
    {
      LogLevel.Warn => "#d4a017",
      LogLevel.Error => "#d04040",
      _ => "#dddddd",
    };
    var line =
      $"[color=#888888]{entry.Timestamp.ToLocalTime():HH:mm:ss.fff}[/color] "
      + $"[b][color={color}]{entry.Level}[/color][/b] "
      + $"[color=#5dade2]{StringEscapeHelper.EscapeBbcode(entry.Module)}[/color]  "
      + $"{StringEscapeHelper.EscapeBbcode(entry.Message)}\n";
    LogView.AppendText(line);
  }

  /// <summary>
  /// 从内存缓冲区重新加载条目到视图(用于筛选切换时)。
  /// </summary>
  public void RefreshFromBuffer()
  {
    if (_writer == null || LogView == null)
      return;
    LogView.Clear();
    foreach (var entry in _writer.GetEntries())
    {
      EnsureModuleRegistered(entry.Module);
      if (entry.Level < CurrentMinLevel)
        continue;
      if (!string.IsNullOrEmpty(CurrentModule) && entry.Module != CurrentModule)
        continue;
      AppendEntryToView(entry);
    }
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
    SetStatus("已清空显示(文件不受影响)");
  }

  private void OnApplyConfigPressed()
  {
    if (_service == null)
    {
      SetStatus("日志服务未就绪");
      return;
    }
    _service.Config.MaxFileCount = (int)MaxFileCountInput.Value;
    var newMinLevel = (LogLevel)MinLevelOption.Selected;
    _service.Config.MinLevel = newMinLevel;
    if (_writer != null)
      _writer.MinLevel = newMinLevel;
    _service.RotateIfNeeded();
    SetStatus($"已应用: MaxFileCount={_service.Config.MaxFileCount}, MinLevel={newMinLevel}");
  }

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

  private void SetStatus(string message)
  {
    if (StatusLabel == null)
      return;
    StatusLabel.Clear();
    StatusLabel.AppendText(message);
  }

  private void OnStatusMetaClicked(Variant meta)
  {
    var url = meta.AsString();
    if (string.IsNullOrEmpty(url))
      return;
    // 去掉 file:// 前缀
    var path = url.StartsWith("file://", StringComparison.Ordinal) ? url[7..] : url;
    path = path.Replace('/', '\\');
    if (Directory.Exists(path) || File.Exists(path))
      OS.ShellOpen(ProjectSettings.GlobalizePath(path));
    else
      OS.ShellOpen(ProjectSettings.GlobalizePath(Path.GetDirectoryName(path) ?? path));
  }
}
