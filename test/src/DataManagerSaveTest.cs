namespace AutoCMEX;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// DataManager 保存链路回归测试（批1-A：单消费者 + 快照 + 原子写 + 加载失败处理）。
/// </summary>
/// <remarks>
/// <para>
/// 背景：改动只经 <see cref="DataManager.TriggerAutoSave"/> 触发保存，而真正写盘在 1500ms 防抖之后；
/// 旧实现用 <c>_isSaving</c> check-then-set，保存进行中再到期就直接丢弃这次改动；写盘直接
/// <c>File.WriteAllText</c>（非原子）；<c>LoadJson</c> 吞异常把半截文件当空数据。
/// </para>
/// <para>
/// 这里守住以下契约：Dispose 时同步落盘且幂等；连打/并发 TriggerAutoSave 不抛且单消费者写盘；
/// 保存与模型修改交错不炸；写盘不残留临时文件；坏 JSON 被备份且日志记录错误；
/// 释放后 TriggerAutoSave 不再排任务；自动保存的「捕获 + 序列化」经 dispatcher 回主线程
/// （池线程只做原子写盘），主线程持续改模型时自动保存也必须真正成功。
/// </para>
/// </remarks>
public class DataManagerSaveTest : TestClass
{
  /// <summary>SaveAll 负责写出的 7 个数据文件。</summary>
  private static readonly string[] SavedFileNames =
  {
    "spellcard_table.json",
    "alias_table.json",
    "app_settings.json",
    "creator_packages.json",
    "merge_config.json",
    "info_config.json",
    "recording_config.json",
  };

  private string _root = string.Empty;
  private string _dataDir = string.Empty;
  private AesEncryptor _encryptor = default!;
  private Mock<ILog> _log = default!;

  /// <summary>
  /// 构造测试类实例（GoDotTest 注入测试场景节点）。
  /// </summary>
  /// <param name="testScene">测试场景节点。</param>
  public DataManagerSaveTest(Node testScene)
    : base(testScene) { }

  /// <summary>
  /// 每个用例一份隔离的临时目录与日志 Mock，避免用例间互相污染。
  /// </summary>
  [Setup]
  public void Setup()
  {
    _root = Path.Combine(Path.GetTempPath(), "AutoCMEX_Save_" + Guid.NewGuid().ToString("N")[..8]);
    _dataDir = Path.Combine(_root, "data");
    Directory.CreateDirectory(_dataDir);

    // 密钥文件放在数据目录之外，数据目录里就只会有 SaveAll 写出的 JSON
    _encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_root));
    _log = new Mock<ILog>();
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
      Directory.Delete(_root, recursive: true);
  }

  /// <summary>
  /// 防抖未到期就 Dispose：必须把等待中的改动同步落盘（S2 的核心回归）。
  /// </summary>
  [Test]
  public void Dispose_FlushesChangesInsideDebounceWindow()
  {
    var dm = new DataManager(_dataDir, _encryptor, _log.Object);
    dm.LoadAll();
    dm.Settings.WebSocketPort.Value = 12345;
    dm.Settings.MessageFilterMode.Value = "ai";

    // 只排了防抖：此时磁盘上还没有任何数据文件，改动只停在内存
    dm.TriggerAutoSave();
    File.Exists(Path.Combine(_dataDir, "app_settings.json")).ShouldBeFalse();

    // 关窗/退出：Dispose 必须先同步落盘
    dm.Dispose();

    // 原子写只留目标文件，不留临时文件
    Directory.GetFiles(_dataDir, "*.tmp").ShouldBeEmpty();

    var reloaded = new DataManager(_dataDir, _encryptor);
    reloaded.LoadAll();
    reloaded.Settings.WebSocketPort.Value.ShouldBe(12345);
    reloaded.Settings.MessageFilterMode.Value.ShouldBe("ai");
    reloaded.Dispose();
  }

  /// <summary>
  /// Dispose 幂等：重复关闭不重复写盘，也不抛异常，数据保持最后一次写入的值。
  /// </summary>
  [Test]
  public void Dispose_IsIdempotent_WritesOnlyOnce()
  {
    var dm = new DataManager(_dataDir, _encryptor, _log.Object);
    dm.LoadAll();
    dm.Settings.WebSocketPort.Value = 4321;
    dm.TriggerAutoSave();

    dm.Dispose();
    Should.NotThrow(() => dm.Dispose());

    // 第二次 Dispose 直接返回：SaveAll 只跑过一次
    _log.Verify(l => l.Print(It.Is<string>(s => s.Contains("SaveAll running"))), Times.Once);

    var reloaded = new DataManager(_dataDir, _encryptor);
    reloaded.LoadAll();
    reloaded.Settings.WebSocketPort.Value.ShouldBe(4321);
    reloaded.Dispose();
  }

  /// <summary>
  /// 退出路径不允许抛异常：写盘失败只记错误日志，Dispose 照常完成释放。
  /// </summary>
  [Test]
  public void Dispose_SaveFailure_DoesNotThrow_AndLogsError()
  {
    var dm = new DataManager(_dataDir, _encryptor, _log.Object);
    dm.LoadAll();

    // 把数据目录换成同名文件：SaveAll 的写盘必然失败（模拟目录不可写/磁盘异常的退出场景）
    Directory.Delete(_dataDir);
    File.WriteAllText(_dataDir, "not a directory");

    Should.NotThrow(() => dm.Dispose());

    _log.Verify(l => l.Err(It.Is<string>(s => s.Contains("flush on dispose failed"))), Times.Once);
  }

  /// <summary>
  /// 连打/并发 TriggerAutoSave：不抛异常，单消费者写盘，最终状态完整落盘且不残留临时文件。
  /// </summary>
  /// <remarks>
  /// 旧实现是 <c>_isSaving</c> 的 check-then-set：保存进行中防抖再到期会把这次改动直接丢掉。
  /// 现在抢不到写权只置脏标记，由持有者在收尾时补写。
  /// </remarks>
  [Test]
  public async Task ConcurrentTriggerAutoSave_DoesNotThrow_AndPersistsFinalState()
  {
    // 本用例从多个线程触发保存，日志走真实 logger（Moq 的调用记录不适合拿来并发压测）
    var dm = new DataManager(_dataDir, _encryptor);
    dm.LoadAll();
    try
    {
      dm.Settings.WebSocketPort.Value = 24680;
      dm.Bosses.Add(new Boss { Name = "并发-Boss" });

      Should.NotThrow(() =>
      {
        Parallel.For(0, 16, _ => dm.TriggerAutoSave());
      });
      Should.NotThrow(() =>
      {
        for (var i = 0; i < 64; i++)
          dm.TriggerAutoSave();
      });

      // 防抖默认 1500ms：等自动保存真正把 7 个文件写完
      await WaitUntilAsync(
        () => SavedFileNames.All(name => File.Exists(Path.Combine(_dataDir, name))),
        timeoutMs: 8000
      );
      await WaitUntilAsync(
        () => Directory.GetFiles(_dataDir, "*.tmp").Length == 0,
        timeoutMs: 2000
      );

      // 原子替换后每个文件都必须是完整可解析的 JSON
      foreach (var name in SavedFileNames)
        ShouldParseAsJson(Path.Combine(_dataDir, name));

      var reloaded = new DataManager(_dataDir, _encryptor);
      reloaded.LoadAll();
      reloaded.Settings.WebSocketPort.Value.ShouldBe(24680);
      reloaded.Bosses.Count.ShouldBe(1);
      reloaded.Dispose();
    }
    finally
    {
      dm.Dispose();
    }
  }

  /// <summary>
  /// 保存与模型修改交错：写盘只在快照上做，调用方不应收到异常，最终状态可完整重载。
  /// </summary>
  [Test]
  public async Task Save_InterleavedWithModelChanges_DoesNotThrow()
  {
    var dm = CreateManager();
    Exception? thrown = null;
    try
    {
      // 1.5s 后开始写盘：在这 2s 窗口里持续改模型（标量 + 集合），制造真实交错
      dm.TriggerAutoSave();
      for (var i = 0; i < 40; i++)
      {
        dm.Settings.AiTimeoutSeconds.Value = 100 + i;
        dm.Bosses.Add(new Boss { Name = $"Boss-{i}" });
        dm.Aliases.Add(new CreatorAlias { MainName = $"Alias-{i}" });
        await Task.Delay(50);
      }

      // 收尾用同步保存取最终快照，避免断言与后台防抖时序耦合
      dm.SaveAll();
    }
    catch (Exception ex)
    {
      thrown = ex;
    }

    thrown.ShouldBeNull("保存与模型修改交错不应向调用方抛异常");

    foreach (var name in SavedFileNames)
      ShouldParseAsJson(Path.Combine(_dataDir, name));

    var reloaded = new DataManager(_dataDir, _encryptor);
    reloaded.LoadAll();
    reloaded.Bosses.Count.ShouldBe(40);
    reloaded.Aliases.Count.ShouldBe(40);
    reloaded.Settings.AiTimeoutSeconds.Value.ShouldBe(139);
    reloaded.Dispose();

    dm.Dispose();
  }

  /// <summary>
  /// 主线程持续改模型期间触发的自动保存必须真正成功：捕获 + 序列化经 dispatcher 回主线程，
  /// 池线程只做原子写盘；断言完成日志、主线程捕获与文件可解析，而不只是「不抛」。
  /// </summary>
  /// <remarks>
  /// 队列调度器 + 主线程排空模拟 <c>Callable.From(action).CallDeferred()</c>：
  /// 防抖到期发生在池线程，捕获动作必须排队后由主线程执行，否则读活 AutoList 会与主线程
  /// 的模型修改竞态。这里同时守住三条：<c>auto-save completed</c> 至少一次、捕获线程 == 主线程、
  /// 7 个文件均可解析。
  /// </remarks>
  [Test]
  public async Task AutoSave_WhileMainThreadMutates_CapturesOnMainThreadAndSucceeds()
  {
    var mainThreadId = System.Environment.CurrentManagedThreadId;
    var pending = new ConcurrentQueue<Action>();
    var captureThreadId = 0;
    var dispatchThreadId = 0;
    var completed = 0;

    _log.Setup(l => l.Print(It.Is<string>(s => s.Contains("auto-save completed"))))
      .Callback(() =>
      {
        Interlocked.Increment(ref completed);
      });

    var dm = new DataManager(
      _dataDir,
      _encryptor,
      _log.Object,
      action =>
      {
        // 防抖到期在池线程：调度器只排队，动作由主线程排空时执行
        Interlocked.Exchange(ref dispatchThreadId, System.Environment.CurrentManagedThreadId);
        pending.Enqueue(() =>
        {
          Interlocked.Exchange(ref captureThreadId, System.Environment.CurrentManagedThreadId);
          action();
        });
      }
    );
    dm.LoadAll();

    Exception? thrown = null;
    try
    {
      dm.TriggerAutoSave();

      // 主线程持续改模型（标量 + 集合），同时排空 dispatcher：
      // 覆盖「防抖到期写盘」与「捕获必须在主线程」两条约束
      for (var i = 0; i < 50; i++)
      {
        dm.Settings.AiTimeoutSeconds.Value = 100 + i;
        dm.Bosses.Add(new Boss { Name = $"Boss-{i}" });
        dm.Aliases.Add(new CreatorAlias { MainName = $"Alias-{i}" });
        Drain(pending);
        await Task.Delay(50);
      }

      // 等自动保存完成（防抖 1500ms + 主线程排空 dispatcher）
      await WaitUntilAsync(
        () =>
        {
          Drain(pending);
          return Volatile.Read(ref completed) >= 1;
        },
        timeoutMs: 8000
      );
      Drain(pending);

      // 让自动保存任务彻底收尾，避免与随后的同步保存并发写盘/记日志
      await Task.Delay(100);
    }
    catch (Exception ex)
    {
      thrown = ex;
    }

    try
    {
      thrown.ShouldBeNull("自动保存不应向调用方抛异常");
      Volatile.Read(ref completed).ShouldBeGreaterThanOrEqualTo(1);
      Volatile.Read(ref captureThreadId).ShouldBe(mainThreadId, "捕获+序列化必须在主线程执行");
      // 防抖到期发生在池线程：必须经 dispatcher 回主线程后才执行捕获
      Volatile.Read(ref dispatchThreadId).ShouldNotBe(mainThreadId);

      // 自动保存已落盘的 7 个文件都必须是完整可解析的 JSON
      foreach (var name in SavedFileNames)
        ShouldParseAsJson(Path.Combine(_dataDir, name));

      // 收尾同步保存取最终状态（主线程），避免断言与自动保存的防抖时序耦合
      dm.SaveAll();
      var reloaded = new DataManager(_dataDir, _encryptor);
      reloaded.LoadAll();
      reloaded.Bosses.Count.ShouldBe(50);
      reloaded.Aliases.Count.ShouldBe(50);
      reloaded.Settings.AiTimeoutSeconds.Value.ShouldBe(149);
      reloaded.Dispose();
    }
    finally
    {
      dm.Dispose();
    }
  }

  /// <summary>
  /// 已排队的 deferred 捕获在 Dispose 之后执行时必须安全跳过：不碰已释放的集合、不覆盖末次落盘。
  /// </summary>
  [Test]
  public async Task Dispose_WithQueuedCapture_SkipsItAndKeepsFinalFlush()
  {
    var pending = new ConcurrentQueue<Action>();
    var dm = new DataManager(_dataDir, _encryptor, _log.Object, action => pending.Enqueue(action));
    dm.LoadAll();
    dm.Settings.WebSocketPort.Value = 7777;
    dm.TriggerAutoSave();

    // 等防抖到期：池线程把「捕获 + 序列化」排进 dispatcher 队列，尚未执行
    await WaitUntilAsync(() => pending.Count > 0, timeoutMs: 8000);

    // 关窗/退出：末次落盘写下最新状态并释放集合；随后才轮到已排队的捕获
    dm.Dispose();
    File.Exists(Path.Combine(_dataDir, "app_settings.json")).ShouldBeTrue();

    // deferred 捕获在 Dispose 之后执行：必须发现已释放而跳过，既不抛也不覆盖末次落盘
    Should.NotThrow(() => Drain(pending));

    var reloaded = new DataManager(_dataDir, _encryptor);
    reloaded.LoadAll();
    reloaded.Settings.WebSocketPort.Value.ShouldBe(7777);
    reloaded.Dispose();
  }

  /// <summary>
  /// 坏 JSON 不再静默：记录错误 + 备份坏文件，LoadAll 继续（空数据），备份内容保持原样。
  /// </summary>
  [Test]
  public void Load_CorruptJson_BacksUpFile_AndLogsError()
  {
    const string corrupt = "{ \"broken\": [1, 2";
    var corruptPath = Path.Combine(_dataDir, "spellcard_table.json");
    File.WriteAllText(corruptPath, corrupt);

    var dm = new DataManager(_dataDir, _encryptor, _log.Object);
    Should.NotThrow(() => dm.LoadAll());

    // LoadAll 仍可继续：坏文件按空数据处理，其它设置回落到默认值
    dm.Bosses.ShouldBeEmpty();
    dm.Settings.WebSocketPort.Value.ShouldBe(5140);

    var backups = Directory.GetFiles(_dataDir, "spellcard_table.json.corrupt-*");
    backups.Length.ShouldBe(1);
    Path.GetFileName(backups[0]).ShouldMatch(@"^spellcard_table\.json\.corrupt-\d{14}(-\d+)?$");
    File.ReadAllText(backups[0]).ShouldBe(corrupt);

    _log.Verify(
      l => l.Err(It.Is<string>(s => s.Contains("failed to load spellcard_table.json"))),
      Times.AtLeastOnce
    );

    dm.Dispose();
  }

  /// <summary>
  /// 释放后 TriggerAutoSave 不再排保存任务（不重建已删除的数据文件）。
  /// </summary>
  [Test]
  public async Task TriggerAutoSave_AfterDispose_DoesNotScheduleSave()
  {
    var dm = CreateManager();
    dm.Settings.WebSocketPort.Value = 5555;
    dm.Dispose();

    var settingsPath = Path.Combine(_dataDir, "app_settings.json");
    File.Delete(settingsPath);

    dm.TriggerAutoSave();

    // 跨过默认防抖窗口（1500ms）：已释放后不应再有任何写盘
    await Task.Delay(2200);

    File.Exists(settingsPath).ShouldBeFalse();
  }

  /// <summary>
  /// 新建并加载一份 DataManager（写日志走 Mock，避免污染全局日志）。
  /// </summary>
  /// <returns>已加载的 DataManager。</returns>
  private DataManager CreateManager()
  {
    var dm = new DataManager(_dataDir, _encryptor, _log.Object);
    dm.LoadAll();
    return dm;
  }

  /// <summary>
  /// 在调用线程（主线程）排空调度器队列，模拟 Godot 的 deferred 回调执行时机。
  /// </summary>
  /// <param name="pending">待执行的主线程动作。</param>
  private static void Drain(ConcurrentQueue<Action> pending)
  {
    while (pending.TryDequeue(out var next))
      next();
  }

  /// <summary>
  /// 轮询等待条件成立；超时抛异常让用例失败（避免固定 Sleep 造成偶发失败）。
  /// </summary>
  /// <param name="condition">待满足条件。</param>
  /// <param name="timeoutMs">超时毫秒数。</param>
  private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
  {
    var deadline = System.Environment.TickCount64 + timeoutMs;
    while (!condition())
    {
      if (System.Environment.TickCount64 > deadline)
        throw new TimeoutException($"等待条件超时（{timeoutMs}ms）");
      await Task.Delay(50);
    }
  }

  /// <summary>
  /// 断言文件内容是一份完整、可解析的 JSON。
  /// </summary>
  /// <param name="path">文件绝对路径。</param>
  private static void ShouldParseAsJson(string path)
  {
    var json = File.ReadAllText(path);
    Should.NotThrow(() =>
    {
      using var document = JsonDocument.Parse(json);
    });
  }
}
