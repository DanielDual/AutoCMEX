namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
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

    var searchBar = new LineEdit { PlaceholderText = "搜索配置项..." };
    _panel.AddChild(searchBar);
    _panel.SearchBar = searchBar;

    var categoryList = new ItemList();
    _panel.AddChild(categoryList);
    _panel.CategoryList = categoryList;

    var configArea = new Control();
    _panel.AddChild(configArea);
    _panel.ConfigArea = configArea;

    var aiModelConfigPanel = new Control();
    _panel.AddChild(aiModelConfigPanel);
    _panel.AiModelConfigPanel = aiModelConfigPanel;

    var chatConfigPanel = new Control();
    _panel.AddChild(chatConfigPanel);
    _panel.ChatConfigPanel = chatConfigPanel;

    _panel.FakeDependency<DataManager>(_dm);
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
