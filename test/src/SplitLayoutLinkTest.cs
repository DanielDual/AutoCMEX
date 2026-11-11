namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="SplitLayoutLink"/> 的单元测试：记忆值应用、拖拽回写与退出补写。
/// </summary>
/// <remarks>
/// 使用真实 <see cref="HSplitContainer"/> + 临时目录 <see cref="DataManager"/>（与
/// <c>DataManagerSaveTest</c> 相同的隔离方式），验证「场景分割条 ↔ ui_layout.json 记录」的完整链路。
/// 读写统一经 <see cref="ISplitContainer"/> 适配器：既与生产代码同源，也避开 Godot 4.7 里
/// 已标记过时的具体类 <c>SplitOffset</c> 属性。
/// </remarks>
public class SplitLayoutLinkTest : TestClass
{
  /// <summary>待释放的临时节点（真实分割条）。</summary>
  private readonly List<Node> _toCleanup = new();

  /// <summary>待删除的临时数据根目录。</summary>
  private readonly List<string> _tempRoots = new();

  public SplitLayoutLinkTest(Node testScene)
    : base(testScene) { }

  /// <summary>清理临时节点与数据目录。</summary>
  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }

    _toCleanup.Clear();

    foreach (var root in _tempRoots)
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }

    _tempRoots.Clear();
  }

  // ==================== 夹具 ====================

  /// <summary>建一份临时目录里的 <see cref="DataManager"/>（数据目录为 root/data）。</summary>
  private DataManager CreateDataManager(out string dataDir)
  {
    var root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_SplitLink_" + Guid.NewGuid().ToString("N")[..8]
    );
    _tempRoots.Add(root);

    dataDir = Path.Combine(root, "data");
    var dataManager = new DataManager(dataDir, new AesEncryptor("test-key"));
    dataManager.LoadAll();
    return dataManager;
  }

  /// <summary>建一个不入场景树的真实水平分割条（由 Cleanup 释放）。</summary>
  private HSplitContainer NewSplit()
  {
    var split = new HSplitContainer();
    _toCleanup.Add(split);
    return split;
  }

  /// <summary>把真实分割条适配成 <see cref="ISplitContainer"/>（与 AutoConnect 生产方式一致）。</summary>
  private static ISplitContainer Adapt(HSplitContainer split) =>
    GodotInterfaces.Adapt<ISplitContainer>(split);

  // ==================== Apply ====================

  /// <summary>模型里有记录时，offset 与折叠状态都应用到场景节点。</summary>
  [Test]
  public void Apply_WithRecord_AppliesOffsetAndCollapsed()
  {
    var dataManager = CreateDataManager(out _);
    try
    {
      dataManager.UiLayout.Splits.Add(
        new SplitLayoutRecord
        {
          Key = new(UiLayoutKeys.MainNav),
          Offset = new(640),
          Collapsed = new(true),
        }
      );

      var split = Adapt(NewSplit());
      new SplitLayoutLink(split, UiLayoutKeys.MainNav, dataManager).Apply();

      split.SplitOffset.ShouldBe(640);
      split.Collapsed.ShouldBeTrue();
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>模型里没有记录时保持场景当前值（不能被默认 0 覆盖）。</summary>
  [Test]
  public void Apply_WithoutRecord_KeepsSceneValues()
  {
    var dataManager = CreateDataManager(out _);
    try
    {
      var split = Adapt(NewSplit());
      split.SplitOffset = 123;

      new SplitLayoutLink(split, UiLayoutKeys.MainNav, dataManager).Apply();

      split.SplitOffset.ShouldBe(123);
      dataManager.UiLayout.Splits.Count.ShouldBe(0);
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  // ==================== 拖拽回写 ====================

  /// <summary>拖拽中的偏移更新进内存模型（offset 取信号参数）。</summary>
  [Test]
  public void Dragged_UpdatesModelOffsetOnlyInMemory()
  {
    var dataManager = CreateDataManager(out _);
    try
    {
      dataManager.UiLayout.Splits.Add(
        new SplitLayoutRecord { Key = new(UiLayoutKeys.MainNav), Offset = new(100) }
      );

      var node = NewSplit();
      new SplitLayoutLink(Adapt(node), UiLayoutKeys.MainNav, dataManager).Apply();

      node.EmitSignal(SplitContainer.SignalName.Dragged, 640);

      var record = dataManager.UiLayout.Splits.Single(item =>
        item.Key.Value == UiLayoutKeys.MainNav
      );
      record.Offset.Value.ShouldBe(640);
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>拖拽结束后落盘：新实例 LoadAll 能读到这次偏移。</summary>
  [Test]
  public void DragEnded_PersistsOffsetAcrossDataManagerInstances()
  {
    var dataManager = CreateDataManager(out var dataDir);
    try
    {
      var node = NewSplit();
      var split = Adapt(node);
      new SplitLayoutLink(split, UiLayoutKeys.MainNav, dataManager).Apply();

      // 引擎拖拽结束前已把节点 offset 更新为新值，结束后由接线器补写模型
      split.SplitOffset = 640;
      node.EmitSignal(SplitContainer.SignalName.DragEnded);

      dataManager.SaveAll();

      var reloaded = new DataManager(dataDir, new AesEncryptor("test-key"));
      try
      {
        reloaded.LoadAll();
        var record = reloaded.UiLayout.Splits.Single(item =>
          item.Key.Value == UiLayoutKeys.MainNav
        );
        record.Offset.Value.ShouldBe(640);
      }
      finally
      {
        reloaded.Dispose();
      }
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  // ==================== Capture ====================

  /// <summary>退出补写：折叠状态变化写进模型（不新增重复记录）。</summary>
  [Test]
  public void Capture_WritesCollapsedChange()
  {
    var dataManager = CreateDataManager(out _);
    try
    {
      var split = Adapt(NewSplit());
      var link = new SplitLayoutLink(split, UiLayoutKeys.MainNav, dataManager);
      link.Apply();

      split.Collapsed = true;
      link.Capture();

      var record = dataManager.UiLayout.Splits.Single();
      record.Key.Value.ShouldBe(UiLayoutKeys.MainNav);
      record.Collapsed.Value.ShouldBeTrue();
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>初值未变化时 Capture 不新增记录（避免每次退出都写一条无意义数据）。</summary>
  [Test]
  public void Capture_WithoutChange_AddsNoRecord()
  {
    var dataManager = CreateDataManager(out _);
    try
    {
      var split = Adapt(NewSplit());
      split.SplitOffset = 77;
      var link = new SplitLayoutLink(split, UiLayoutKeys.MainNav, dataManager);
      link.Apply();

      link.Capture();

      dataManager.UiLayout.Splits.Count.ShouldBe(0);
    }
    finally
    {
      dataManager.Dispose();
    }
  }
}
