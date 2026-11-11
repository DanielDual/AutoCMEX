namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 设置板块脚本 — 使用静态场景实例，不再动态构建 UI
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class SettingsPanel : Control, ISettingsPanel
{
  #region AutoConnect Nodes

  [Node("%SearchBar")]
  public ILineEdit SearchBar { get; set; } = default!;

  [Node("%CategoryList")]
  public IItemList CategoryList { get; set; } = default!;

  [Node("%ConfigArea")]
  public IControl ConfigArea { get; set; } = default!;

  /// <summary>设置板块内容分割条（类别 / 配置两栏），位置由 <c>ui_layout.json</c> 的 <c>settings.content</c> 记录还原。</summary>
  [Node("%ContentArea")]
  public ISplitContainer ContentArea { get; set; } = default!;

  [Node("%AiModelConfigPanel")]
  public IAiModelConfigPanel AiModelConfigPanel { get; set; } = default!;

  [Node("%ChatConfigPanel")]
  public IChatConfigPanel ChatConfigPanel { get; set; } = default!;

  [Node("%InfoConfigPanel")]
  public IInfoConfigPanel InfoConfigPanel { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  #endregion

  private AppSettings _settings = new();
  private DataManager? _dm;

  /// <summary>内容分割条的布局接线路，在 <see cref="OnResolved"/> 中按语义键建立并应用。</summary>
  private readonly List<SplitLayoutLink> _splitLinks = new();

  private readonly string[] _categories =
  {
    "AI模型",
    "群聊",
    "猜测",
    "整合",
    "信息",
    "帮助",
    "通用",
  };
  private int _currentCategory = -1;

  public override void _Notification(int what) => this.Notify(what);

  /// <summary>
  /// 退出前把内容分割条的当前布局写回模型；随后主窗口的 <c>DataManager.Dispose</c> 统一落盘。
  /// </summary>
  public override void _ExitTree()
  {
    foreach (var link in _splitLinks)
      link.Capture();
  }

  public void OnReady()
  {
    foreach (var cat in _categories)
      CategoryList.AddItem(cat);

    CategoryList.ItemSelected += OnCategorySelected;
    SearchBar.TextChanged += OnSearchChanged;

    // 默认隐藏所有配置面板（配置子面板脚本类均为 Control 子类）
    if (AiModelConfigPanel is Control aiModelPanel)
      aiModelPanel.Visible = false;
    if (ChatConfigPanel is Control chatPanel)
      chatPanel.Visible = false;
    if (InfoConfigPanel is Control infoPanel)
      infoPanel.Visible = false;
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm != null)
    {
      _settings = _dm.Settings;
      EnsureSplitLayoutLink();
    }
  }

  /// <summary>
  /// 建立并应用内容分割条的布局接线路（规则 6：依赖解析完成后接线；重复解析只重新应用，不重复订阅信号）。
  /// </summary>
  private void EnsureSplitLayoutLink()
  {
    if (_dm is null)
      return;

    if (_splitLinks.Count == 0)
      _splitLinks.Add(new SplitLayoutLink(ContentArea, UiLayoutKeys.SettingsContent, _dm));

    foreach (var link in _splitLinks)
      link.Apply();
  }

  private void OnCategorySelected(long index)
  {
    _currentCategory = (int)index;
    RefreshConfigArea();
  }

  private void OnSearchChanged(string newText)
  {
    for (int i = 0; i < _categories.Length; i++)
    {
      var visible =
        string.IsNullOrEmpty(newText)
        || _categories[i].Contains(newText, StringComparison.OrdinalIgnoreCase);
      CategoryList.SetItemDisabled(i, !visible);
    }
  }

  private void RefreshConfigArea()
  {
    // 隐藏所有面板
    if (AiModelConfigPanel is Control aiModelPanel)
      aiModelPanel.Visible = false;
    if (ChatConfigPanel is Control chatPanel)
      chatPanel.Visible = false;
    if (InfoConfigPanel is Control infoPanelHidden)
      infoPanelHidden.Visible = false;

    // 显示当前类别对应的面板（索引与 _categories 顺序一致）
    switch (_currentCategory)
    {
      case 0:
        if (AiModelConfigPanel is Control aiModelPanelVisible)
          aiModelPanelVisible.Visible = true;
        break;
      case 1:
        if (ChatConfigPanel is Control chatPanelVisible)
          chatPanelVisible.Visible = true;
        break;
      case 4:
        if (InfoConfigPanel is Control infoPanelVisible)
          infoPanelVisible.Visible = true;
        break;
      default:
        // 其他类别暂无独立场景，保持隐藏
        break;
    }
  }
}
