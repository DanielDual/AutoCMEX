namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
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

    var searchBar = new Mock<ILineEdit>();
    searchBar.SetupProperty(m => m.PlaceholderText, "搜索配置项...");
    var categoryList = new Mock<IItemList>();
    var configArea = new Mock<IControl>();
    var aiModelConfigPanel = new Mock<IControl>();
    var chatConfigPanel = new Mock<IControl>();

    _panel.FakeNodeTree(
      new()
      {
        ["%SearchBar"] = searchBar.Object,
        ["%CategoryList"] = categoryList.Object,
        ["%ConfigArea"] = configArea.Object,
        ["%AiModelConfigPanel"] = aiModelConfigPanel.Object,
        ["%ChatConfigPanel"] = chatConfigPanel.Object,
      }
    );

    _panel.FakeDependency<DataManager>(_dm);
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
  public void SettingsPanel_SearchBar_HasPlaceholder()
  {
    _panel.SearchBar.PlaceholderText.ShouldContain("搜索");
  }
}
