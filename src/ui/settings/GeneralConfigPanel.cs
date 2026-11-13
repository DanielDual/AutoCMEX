namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Godot;

/// <summary>
/// 设置面板「通用」类别页：界面主题下拉。把设置里的主题 ID 回填到下拉，
/// 把用户选择写回设置。
/// </summary>
/// <remarks>
/// <para>
/// 本页只写数据模型，不负责应用主题：主题切换由 <c>MainWindow</c> 订阅
/// <see cref="AppSettings.ThemeId"/> 后统一应用（与 WebSocket 重启范式一致）。
/// 面板若自己下发应用，会绕开主窗口在启动阶段的窗口几何还原顺序与退出护栏。
/// </para>
/// <para>
/// 下拉项恒来自 <see cref="ThemeCatalog.Options"/>：清单顺序就是界面契约，下标即回填与写回的共同索引。
/// 设置里的主题 ID 不在清单时（旧配置被外部改坏或来自其它分支）只选中「默认」并显示一行提示，
/// 但不静默改写设置——改写会让用户丢掉手工配置过的值。
/// </para>
/// </remarks>
[Meta(typeof(IAutoNode))]
public sealed partial class GeneralConfigPanel : VBoxContainer, IGeneralConfigPanel
{
  /// <summary>主题下拉（唯一名称 <c>%ThemeSelect</c>）。</summary>
  [Node("%ThemeSelect")]
  public IOptionButton ThemeSelect { get; set; } = default!;

  /// <summary>设置里的主题不可用时的提示行（唯一名称 <c>%ThemeStatusLabel</c>）。</summary>
  [Node("%ThemeStatusLabel")]
  public ILabel ThemeStatusLabel { get; set; } = default!;

  /// <summary>设置数据管理器（唯一数据源，主题设置值从这里读写）。</summary>
  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  private DataManager? _dm;
  private AppSettings _settings = new();

  /// <summary>主题设置值绑定，离树时统一释放（规则 20）。</summary>
  private readonly List<IDisposable> _bindings = new();

  /// <summary>本轮回填是否已排队：同一帧内多次设置变更只回填一次。</summary>
  private bool _refillQueued;

  /// <summary>是否正在订阅设置值绑定（订阅会立刻回放一次，抑制这类伪变更的排队）。</summary>
  private bool _isSubscribing;

  /// <summary>
  /// 正在把设置值回填到下拉：此期间下拉的 <c>item_selected</c> 是程序化 <c>Select</c> 的产物，
  /// 不代表用户选择，不得回写设置。
  /// </summary>
  /// <remarks>
  /// 没有这道护栏时，仅仅打开本页（回填即 <c>Select</c>）就会把主题写回去并触发一次自动保存。
  /// </remarks>
  private bool _applyingModel;

  /// <inheritdoc/>
  public override void _Notification(int what)
  {
    this.Notify(what);

    // 真实运行时离树会走 _ExitTree，两条路径都调同一个幂等释放；
    // 替身面板不入树，单测只能手工发这条通知（与 ChatConfigPanel 一致）。
    if (what == (int)NotificationExitTree)
      DisposeBindings();
  }

  /// <summary>释放全部设置值绑定并停止回填（规则 20）。</summary>
  public override void _ExitTree() => DisposeBindings();

  /// <summary>AutoInject 节点注入完成：按下拉填充主题清单并挂上选择回调。</summary>
  public void OnReady()
  {
    foreach (var option in ThemeCatalog.Options)
      ThemeSelect.AddItem(option.DisplayName);

    ThemeSelect.ItemSelected += OnThemeSelected;

    // 场景里初始已隐藏，这里代码兜底：提示行只在设置里的主题不可用时出现
    ThemeStatusLabel.Visible = false;
  }

  /// <summary>AutoInject 依赖解析完成：订阅主题设置值并立即回填一次下拉。</summary>
  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm is null)
      return;

    _settings = _dm.Settings;

    // UI 由设置值绑定驱动（规则 18）：订阅会立刻用当前值回放一次，
    // 订阅期间先抑制排队，随后同步回填一次，避免启动时就排一串重复回填。
    _isSubscribing = true;
    try
    {
      _bindings.Add(_settings.ThemeId.Bind().OnValue(_ => QueueRefill()));
    }
    finally
    {
      _isSubscribing = false;
    }

    Refill();
  }

  /// <summary>释放设置值绑定（幂等：离树通知与 <c>_ExitTree</c> 都会调用）。</summary>
  private void DisposeBindings()
  {
    foreach (var binding in _bindings)
      binding.Dispose();

    _bindings.Clear();
  }

  /// <summary>把同帧内多次回填请求合并成一次（绑定回调里只调度，规则 19）。</summary>
  private void QueueRefill()
  {
    if (_isSubscribing || _refillQueued)
      return;

    _refillQueued = true;
    CallDeferred(nameof(FlushRefill));
  }

  /// <summary>执行排队中的回填（公开以便 <c>CallDeferred</c> 按名调用，不属于对外契约）。</summary>
  public void FlushRefill()
  {
    _refillQueued = false;
    Refill();
  }

  /// <summary>把设置里的主题 ID 回填到下拉；不在清单时选中「默认」并显示提示。</summary>
  /// <remarks>
  /// 回填期间下拉会同步发 <c>item_selected</c>，必须先立起 <see cref="_applyingModel"/> 护栏，
  /// 否则回填会反向写回设置并触发自动保存。
  /// </remarks>
  public void Refill()
  {
    _applyingModel = true;
    try
    {
      var resolved = ThemeCatalog.TryResolve(_settings.ThemeId.Value, out var option, out _);

      var index = 0;
      if (resolved)
      {
        for (var i = 0; i < ThemeCatalog.Options.Count; i++)
        {
          if (!string.Equals(ThemeCatalog.Options[i].Id, option.Id, StringComparison.Ordinal))
            continue;

          index = i;
          break;
        }
      }

      ThemeSelect.Select(index);

      if (resolved)
      {
        ThemeStatusLabel.Visible = false;
      }
      else
      {
        ThemeStatusLabel.Text = "设置中的主题不可用，已回退到默认主题。";
        ThemeStatusLabel.Visible = true;
      }
    }
    finally
    {
      _applyingModel = false;
    }
  }

  /// <summary>用户从下拉里选了主题：只写设置，不在这里应用主题。</summary>
  /// <param name="index">下拉项下标，与 <see cref="ThemeCatalog.Options"/> 的顺序一致。</param>
  private void OnThemeSelected(long index)
  {
    // 回填期间的 item_selected 是程序化 Select 的产物，不是用户操作
    if (_applyingModel)
      return;

    var option = ThemeCatalog.Options[(int)index];

    // 选中项与设置里的当前值相同时不写模型：省掉一次无意义的自动保存与绑定回填
    if (string.Equals(option.Id, _settings.ThemeId.Value, StringComparison.Ordinal))
      return;

    _settings.ThemeId.Value = option.Id;
    _dm?.TriggerAutoSave();

    // 用户已选到清单内的主题，之前的「不可用」提示不再成立
    ThemeStatusLabel.Visible = false;
  }
}
