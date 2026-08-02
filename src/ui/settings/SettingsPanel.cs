namespace AutoCMEX.UI.Settings;

using System;
using System.Linq;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 设置板块脚本 — 使用静态场景实例，不再动态构建 UI
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class SettingsPanel : Control
{
  #region AutoConnect Nodes

  [Node("%SearchBar")]
  public LineEdit SearchBar { get; set; } = default!;

  [Node("%CategoryList")]
  public ItemList CategoryList { get; set; } = default!;

  [Node("%ConfigArea")]
  public Control ConfigArea { get; set; } = default!;

  [Node("%AiModelConfigPanel")]
  public Control AiModelConfigPanel { get; set; } = default!;

  [Node("%ChatConfigPanel")]
  public Control ChatConfigPanel { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>();

  #endregion

  private AppSettings _settings = new();
  private DataManager? _dm;

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

  public void OnReady()
  {
    foreach (var cat in _categories)
      CategoryList.AddItem(cat);

    CategoryList.ItemSelected += OnCategorySelected;
    SearchBar.TextChanged += OnSearchChanged;

    // 默认隐藏所有配置面板
    AiModelConfigPanel.Visible = false;
    ChatConfigPanel.Visible = false;
  }

  public void OnResolved()
  {
    _dm = DataManager;
    if (_dm != null)
      _settings = _dm.Settings;
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
    AiModelConfigPanel.Visible = false;
    ChatConfigPanel.Visible = false;

    // 显示当前类别对应的面板
    switch (_currentCategory)
    {
      case 0:
        AiModelConfigPanel.Visible = true;
        break;
      case 1:
        ChatConfigPanel.Visible = true;
        break;
      default:
        // 其他类别暂无独立场景，保持隐藏
        break;
    }
  }
}
