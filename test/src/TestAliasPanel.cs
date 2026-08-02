namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class TestAliasPanel : TestClass
{
  private AliasPanel _panel = default!;
  private DataManager _dm = default!;
  private readonly List<Node> _toCleanup = new();

  public TestAliasPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new AliasPanel();
    _toCleanup.Add(_panel);

    var tree = new Tree();
    _panel.AddChild(tree);
    _panel.AliasTree = tree;

    var importBtn = new Button();
    _panel.AddChild(importBtn);
    _panel.ImportAliasBtn = importBtn;

    var exportBtn = new Button();
    _panel.AddChild(exportBtn);
    _panel.ExportAliasBtn = exportBtn;

    var addBtn = new Button();
    _panel.AddChild(addBtn);
    _panel.AddAliasBtn = addBtn;

    var addToCreatorBtn = new Button();
    _panel.AddChild(addToCreatorBtn);
    _panel.AddAliasToCreatorBtn = addToCreatorBtn;

    var deleteBtn = new Button();
    _panel.AddChild(deleteBtn);
    _panel.DeleteAliasBtn = deleteBtn;

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
  public void AddAlias_AddsToDataManager()
  {
    _dm.Aliases.Count.ShouldBe(0);
    _panel.GetOnAlias()();
    _dm.Aliases.Count.ShouldBe(1);
    _dm.Aliases[0].MainName.ShouldBe("新创作者");
  }

  [Test]
  public void Refresh_UpdatesTree()
  {
    _dm.Aliases.Add(new CreatorAlias { MainName = "测试创作者" });
    _panel.Refresh();
    var root = _panel.AliasTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBe(1);
  }

  [Test]
  public void GetDataManager_ReturnsInjectedInstance()
  {
    _panel.GetDataManager().ShouldBe(_dm);
  }
}
