namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Guessing;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Chickensoft.Sync.Primitives;
using Godot;
using Shouldly;

public class TestSpellCardPanel : TestClass
{
  private SpellCardPanel _panel = default!;
  private DataManager _dm = default!;
  private readonly List<Node> _toCleanup = new();

  public TestSpellCardPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new SpellCardPanel();

    // 手动设置子节点属性（不依赖 [Node] 解析）
    var tree = new Tree();
    _panel.AddChild(tree);
    _panel.SpellCardTree = tree;

    var importBtn = new Button();
    _panel.AddChild(importBtn);
    _panel.ImportCardBtn = importBtn;

    var exportBtn = new Button();
    _panel.AddChild(exportBtn);
    _panel.ExportCardBtn = exportBtn;

    var addBossBtn = new Button();
    _panel.AddChild(addBossBtn);
    _panel.AddBossBtn = addBossBtn;

    var addCardBtn = new Button();
    _panel.AddChild(addCardBtn);
    _panel.AddCardBtn = addCardBtn;

    var deleteBtn = new Button();
    _panel.AddChild(deleteBtn);
    _panel.DeleteBtn = deleteBtn;

    // 添加对话框节点
    var importDialog = new FileDialog();
    _panel.AddChild(importDialog);
    _panel.ImportFileDialog = importDialog;

    var exportDialog = new FileDialog();
    _panel.AddChild(exportDialog);
    _panel.ExportFileDialog = exportDialog;

    var errorDialog = new AcceptDialog();
    _panel.AddChild(errorDialog);
    _panel.ErrorDialog = errorDialog;

    // 设置 FakeDependency
    _panel.FakeDependency<DataManager>(_dm);

    // 触发 AutoInject 生命周期
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
  public void AddBoss_AddsToDataManager()
  {
    _dm.Bosses.Count.ShouldBe(0);
    _panel.GetOnAddBoss()();
    _dm.Bosses.Count.ShouldBe(1);
    _dm.Bosses[0].Name.ShouldBe("新 Boss");
  }

  [Test]
  public void Refresh_UpdatesTree()
  {
    _dm.Bosses.Add(new Boss { Name = "测试Boss" });
    _dm.Bosses[0].SpellCards.Add(new SpellCard { Name = new AutoValue<string>("符卡1") });
    _panel.GetSelectBoss()(_dm.Bosses[0]);
    _panel.Refresh();
    var root = _panel.SpellCardTree.GetRoot();
    root.ShouldNotBeNull();
    root.GetChildCount().ShouldBeGreaterThan(0);
  }

  [Test]
  public void GetDataManager_ReturnsInjectedInstance()
  {
    _panel.GetDataManager().ShouldBe(_dm);
  }
}
