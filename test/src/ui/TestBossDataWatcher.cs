namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Info;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 两表数据观察者测试：守住「猜测板块改了符卡，两张表要立刻跟着变」这条同步契约。
/// </summary>
/// <remarks>
/// 猜测模块改的是符卡的 <c>AutoValue</c> 属性（是否已猜出 / 创作者 / 符卡名），符卡列表对象本身没有
/// 增删。观察者若只订阅列表的集合级通知就会漏掉这类变化，界面一直停在旧数据上，只有重启重新读盘
/// 才同步；本用例把「属性级变更必须触发通知」连同换 Boss、符卡增删、释放后的边界一起钉死。
/// </remarks>
public class TestBossDataWatcher : TestClass
{
  private readonly List<DataManager> _dataManagers = new();
  private readonly List<string> _tempDirs = new();

  public TestBossDataWatcher(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var dataManager in _dataManagers)
      dataManager.Dispose();

    _dataManagers.Clear();

    foreach (var dir in _tempDirs)
    {
      if (Directory.Exists(dir))
        Directory.Delete(dir, true);
    }

    _tempDirs.Clear();
  }

  [Test]
  public void SpellCardValueChange_NotifiesEachTime()
  {
    var dataManager = CreateDataManager(CreateBoss("A"));
    var notifications = 0;
    using var watcher = new BossDataWatcher(dataManager, () => notifications++);

    var card = dataManager.Bosses[0].SpellCards[0];
    var baseline = notifications;

    // 猜测板块标记「已猜出」：符卡列表没有增删，只有符卡属性变化
    card.IsGuessedOut.Value = true;
    notifications.ShouldBe(baseline + 1);

    // 改名与改创作者同样要通知：两张表的单元格与创作者计数都依赖它们
    card.Name.Value = "改过的符卡名";
    card.Creator.Value = "Beta";
    notifications.ShouldBe(baseline + 3);
  }

  [Test]
  public void RebindAfterBossSwitch_DoesNotFloodNotifications()
  {
    var dataManager = CreateDataManager(CreateBoss("A", 60), CreateBoss("B", 60));
    var notifications = 0;
    using var watcher = new BossDataWatcher(dataManager, () => notifications++);

    // 订阅 AutoValue 会立刻回放当前值：60 张符卡 × 3 个属性若不做抑制，一次重挂就是 180 次全表重建
    notifications.ShouldBeLessThan(3);

    var before = notifications;
    dataManager.Settings.SelectedBossIndex.Value = 1;
    (notifications - before).ShouldBeLessThan(3);
  }

  [Test]
  public void BindingsFollowCardListAndCurrentBoss()
  {
    var first = CreateBoss("A");
    var second = CreateBoss("B");
    var dataManager = CreateDataManager(first, second);
    var notifications = 0;
    using var watcher = new BossDataWatcher(dataManager, () => notifications++);

    // 运行时新增的符卡要立刻纳入监听（不会有人为它重建观察者）
    var added = new SpellCard { Name = { Value = "新符卡" } };
    first.SpellCards.Add(added);
    var afterAdd = notifications;
    added.IsGuessedOut.Value = true;
    notifications.ShouldBe(afterAdd + 1);

    // 切到另一个 Boss：新 Boss 的符卡接管，旧 Boss 的符卡不再影响当前预览
    dataManager.Settings.SelectedBossIndex.Value = 1;
    var afterSwitch = notifications;

    added.IsGuessedOut.Value = false;
    notifications.ShouldBe(afterSwitch, "已切走的 Boss 的符卡不应再触发刷新");

    second.SpellCards[0].IsGuessedOut.Value = true;
    notifications.ShouldBe(afterSwitch + 1);
  }

  [Test]
  public void Dispose_StopsNotifications()
  {
    var boss = CreateBoss("A");
    var dataManager = CreateDataManager(boss);
    var notifications = 0;
    var watcher = new BossDataWatcher(dataManager, () => notifications++);

    watcher.Dispose();
    var afterDispose = notifications;

    boss.SpellCards[0].IsGuessedOut.Value = true;
    boss.SpellCards.Add(new SpellCard());
    notifications.ShouldBe(afterDispose);
  }

  [Test]
  public void OffThreadSelectionChange_OnlyQueuesStructuralRebind()
  {
    var dataManager = CreateDataManager(CreateBoss("A", 2), CreateBoss("B", 4));
    var notifications = 0;
    var notifyThreadId = 0;
    var mainThreadId = System.Environment.CurrentManagedThreadId;
    var pending = new Queue<Action>();
    var pendingGate = new object();
    using var watcher = new BossDataWatcher(
      dataManager,
      () =>
      {
        Interlocked.Increment(ref notifications);
        Volatile.Write(ref notifyThreadId, System.Environment.CurrentManagedThreadId);
      },
      action =>
      {
        lock (pendingGate)
          pending.Enqueue(action);
      }
    );

    // 构造期 OnValue 回放也会排一次重挂：先按主线程排空
    DrainQueue(pending, pendingGate);
    watcher.CardBindingCountForTests.ShouldBe(6); // 当前 Boss A：2 张符卡 × 3 个属性
    var baseline = notifications;

    Task.Run(() => dataManager.Settings.SelectedBossIndex.Value = 1).GetAwaiter().GetResult();

    // 数据线程只排队：绑定集合没有被改动，通知仍是同步发出的（不在主线程）
    watcher.CardBindingCountForTests.ShouldBe(6);
    notifications.ShouldBe(baseline + 1);
    Volatile.Read(ref notifyThreadId).ShouldNotBe(mainThreadId);
    lock (pendingGate)
      pending.Count.ShouldBe(1);

    // 主线程排空后才真正重挂到 Boss B：4 张符卡 × 3 个属性，且重挂本身不额外发通知
    DrainQueue(pending, pendingGate);
    watcher.CardBindingCountForTests.ShouldBe(12);
    notifications.ShouldBe(baseline + 1);
  }

  [Test]
  public void Dispose_WithPendingRebind_SkipsItWithoutThrowing()
  {
    var dataManager = CreateDataManager(CreateBoss("A", 2), CreateBoss("B", 4));
    var pending = new Queue<Action>();
    var pendingGate = new object();
    var watcher = new BossDataWatcher(
      dataManager,
      () => { },
      action =>
      {
        lock (pendingGate)
          pending.Enqueue(action);
      }
    );

    DrainQueue(pending, pendingGate);
    dataManager.Settings.SelectedBossIndex.Value = 1;
    lock (pendingGate)
      pending.Count.ShouldBe(1);

    watcher.Dispose();
    watcher.Dispose(); // 重复释放必须安全

    // 释放后到期的 deferred 重挂要安全跳过，不碰已释放的绑定
    Should.NotThrow(() => DrainQueue(pending, pendingGate));
    watcher.CardBindingCountForTests.ShouldBe(0);

    // 绑定已全部释放：后续数据变化不会再排队
    dataManager.Settings.SelectedBossIndex.Value = 0;
    lock (pendingGate)
      pending.Count.ShouldBe(0);
  }

  [Test]
  public void SharedWatcher_NotifiesConstructorCallbackAndChangedListeners()
  {
    var dataManager = CreateDataManager(CreateBoss("A"));
    var fromConstructor = 0;
    var fromEvent = 0;
    using var watcher = new BossDataWatcher(dataManager, () => fromConstructor++);
    watcher.Changed += () => fromEvent++;

    var constructorBaseline = fromConstructor;
    var eventBaseline = fromEvent;

    // 两张表栏共用一个 watcher：一次属性变化要让所有订阅方都收到
    dataManager.Bosses[0].SpellCards[0].IsGuessedOut.Value = true;

    fromConstructor.ShouldBe(constructorBaseline + 1);
    fromEvent.ShouldBe(eventBaseline + 1);
  }

  [Test]
  public void DispatchOnlyConstructor_RebindsInlineAndRaisesChanged()
  {
    var dataManager = CreateDataManager(CreateBoss("A", 2), CreateBoss("B", 4));
    Action<Action> inline = action => action();
    using var watcher = new BossDataWatcher(dataManager, inline);
    var notifications = 0;
    watcher.Changed += () => notifications++;

    watcher.CardBindingCountForTests.ShouldBe(6);

    dataManager.Settings.SelectedBossIndex.Value = 1;

    watcher.CardBindingCountForTests.ShouldBe(12);
    notifications.ShouldBe(1);
  }

  private static void DrainQueue(Queue<Action> pending, object gate)
  {
    while (true)
    {
      Action next;
      lock (gate)
      {
        if (pending.Count == 0)
          return;

        next = pending.Dequeue();
      }

      next();
    }
  }

  private DataManager CreateDataManager(params Boss[] bosses)
  {
    var dir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_BossDataWatcherTest_{Guid.NewGuid():N}");
    _tempDirs.Add(dir);

    var dataManager = new DataManager(dir, new AesEncryptor("test-key"));
    dataManager.LoadAll();

    foreach (var boss in bosses)
      dataManager.Bosses.Add(boss);

    _dataManagers.Add(dataManager);
    return dataManager;
  }

  /// <summary>造一个全部「未猜出」的 Boss。</summary>
  private static Boss CreateBoss(string name, int cardCount = 3)
  {
    var boss = new Boss { Name = name };
    for (var i = 1; i <= cardCount; i++)
    {
      boss.SpellCards.Add(
        new SpellCard { Name = { Value = $"{name} 符卡 {i}" }, Creator = { Value = "Alpha" } }
      );
    }

    return boss;
  }
}
