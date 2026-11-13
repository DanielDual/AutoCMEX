namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// SettingsPanel 单元测试
/// </summary>
public class SettingsPanelTest : TestClass
{
  private SettingsPanel _panel = default!;
  private DataManager _dm = default!;
  private Mock<IItemList> _categoryList = default!;
  private Mock<ISplitContainer> _contentArea = default!;
  private InfoConfigPanel _infoConfigPanel = default!;
  private GeneralConfigPanel _generalConfigPanel = default!;
  private readonly List<Node> _toCleanup = new();

  public SettingsPanelTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new SettingsPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    var searchBar = new Mock<ILineEdit>();
    searchBar.SetupProperty(m => m.PlaceholderText);
    _categoryList = new Mock<IItemList>();
    var configArea = new Mock<IControl>();
    _contentArea = new Mock<ISplitContainer>();
    _contentArea.SetupProperty(m => m.SplitOffset);
    _contentArea.SetupProperty(m => m.Collapsed);

    _panel.FakeNodeTree(
      new()
      {
        ["%SearchBar"] = searchBar.Object,
        ["%CategoryList"] = _categoryList.Object,
        ["%ConfigArea"] = configArea.Object,
        ["%ContentArea"] = _contentArea.Object,
      }
    );

    // 配置子面板使用真实实例直接赋给 [Node] 属性（已赋值属性会被 AutoConnect 跳过）。
    _panel.AiModelConfigPanel = new AiModelConfigPanel();
    _panel.ChatConfigPanel = new ChatConfigPanel();
    _infoConfigPanel = new InfoConfigPanel();
    _panel.InfoConfigPanel = _infoConfigPanel;
    _toCleanup.Add(_infoConfigPanel);
    _generalConfigPanel = new GeneralConfigPanel();
    _panel.GeneralConfigPanel = _generalConfigPanel;
    _toCleanup.Add(_generalConfigPanel);

    _panel.FakeDependency<DataManager>(_dm);

    // 预置内容栏分割条布局记忆：OnResolved 建立接线路时应用
    _dm.UiLayout.Splits.Add(
      new SplitLayoutRecord { Key = new(UiLayoutKeys.SettingsContent), Offset = new(260) }
    );

    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }
    _toCleanup.Clear();
  }

  [Test]
  public void SettingsPanel_LoadsSuccessfully()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_SearchBar_Exists()
  {
    _panel.SearchBar.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_CategoryList_Exists()
  {
    _panel.CategoryList.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_ConfigArea_Exists()
  {
    _panel.ConfigArea.ShouldNotBeNull();
  }

  [Test]
  public void SettingsPanel_CategoryList_HasSevenCategories()
  {
    _categoryList.Verify(
      m => m.AddItem(It.IsAny<string>(), It.IsAny<Texture2D>(), It.IsAny<bool>()),
      Times.Exactly(7)
    );
  }

  [Test]
  public void SettingsPanel_AiModelConfigPanel_IsInterface()
  {
    _panel.AiModelConfigPanel.ShouldNotBeNull();
    _panel.AiModelConfigPanel.ShouldBeAssignableTo<IAiModelConfigPanel>();
  }

  [Test]
  public void SettingsPanel_ChatConfigPanel_IsInterface()
  {
    _panel.ChatConfigPanel.ShouldNotBeNull();
    _panel.ChatConfigPanel.ShouldBeAssignableTo<IChatConfigPanel>();
  }

  [Test]
  public void SettingsPanel_InfoConfigPanel_IsInterface()
  {
    _panel.InfoConfigPanel.ShouldNotBeNull();
    _panel.InfoConfigPanel.ShouldBeAssignableTo<IInfoConfigPanel>();
  }

  [Test]
  public void SettingsPanel_InfoCategory_ShowsInfoConfigPanelOnly()
  {
    // 「信息」是 _categories 的第 5 项（下标 4）
    _infoConfigPanel.Visible.ShouldBeFalse();

    _categoryList.Raise(m => m.ItemSelected += null, 4L);
    _infoConfigPanel.Visible.ShouldBeTrue();
    ((Control)_panel.AiModelConfigPanel).Visible.ShouldBeFalse();

    // 切回「AI模型」应重新隐藏「信息」页
    _categoryList.Raise(m => m.ItemSelected += null, 0L);
    _infoConfigPanel.Visible.ShouldBeFalse();
  }

  [Test]
  public void SettingsPanel_GeneralConfigPanel_IsInterface()
  {
    _panel.GeneralConfigPanel.ShouldNotBeNull();
    _panel.GeneralConfigPanel.ShouldBeAssignableTo<IGeneralConfigPanel>();
  }

  [Test]
  public void SettingsPanel_GeneralCategory_ShowsGeneralConfigPanelOnly()
  {
    // 「通用」是 _categories 的最后一项（下标 6）
    _generalConfigPanel.Visible.ShouldBeFalse();

    _categoryList.Raise(m => m.ItemSelected += null, 6L);
    _generalConfigPanel.Visible.ShouldBeTrue();
    _infoConfigPanel.Visible.ShouldBeFalse();
    ((Control)_panel.AiModelConfigPanel).Visible.ShouldBeFalse();

    // 切回「AI模型」应重新隐藏「通用」页
    _categoryList.Raise(m => m.ItemSelected += null, 0L);
    _generalConfigPanel.Visible.ShouldBeFalse();
  }

  // ==================== 分割条布局记忆 ====================

  /// <summary>OnResolved 把内容栏分割条的记忆 offset 应用到场景节点。</summary>
  [Test]
  public void SettingsPanel_ContentArea_AppliesRememberedOffset()
  {
    _contentArea.Object.SplitOffset.ShouldBe(260);
  }

  /// <summary>拖拽内容栏分割条：模型里同键记录被更新为新 offset。</summary>
  [Test]
  public void SettingsPanel_ContentArea_Dragged_UpdatesModel()
  {
    _contentArea.Raise(m => m.Dragged += null, 480L);

    _dm.UiLayout.Splits.Single(record => record.Key.Value == UiLayoutKeys.SettingsContent)
      .Offset.Value.ShouldBe(480);
  }
}
