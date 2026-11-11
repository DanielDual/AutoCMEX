namespace AutoCMEX.Core.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Models;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 数据持久化管理：JSON 读写 + 自动保存防抖
/// </summary>
/// <remarks>
/// <para>
/// 自动保存是单消费者流水线：防抖到期后由 <c>_saveGate</c> 选出一个任务，再经构造时注入的
/// <c>dispatch</c> 把「捕获 + 序列化」调度到主线程执行——活的 <see cref="AutoList{T}"/>
/// （含 <c>Boss.SpellCards</c> 等嵌套集合）只在主线程枚举，8 段数据全部序列化成字符串；
/// 线程池只负责把字符串用临时文件 + <see cref="File.Move(string, string, bool)"/> 原子替换写盘。
/// 抢不到写权的到期只置脏标记，由持有者收尾补写，不丢弃改动。
/// </para>
/// <para>
/// <see cref="SaveAll"/> 是同步路径，调用方必须已保证在主线程（关窗 / <see cref="Dispose"/> /
/// 面板块），它直接在调用线程捕获 + 序列化后同步写盘。
/// </para>
/// </remarks>
public class DataManager : IDisposable
{
  private readonly string _dataDir;
  private readonly AesEncryptor _encryptor;
  private readonly JsonSerializerOptions _jsonOptions;
  private readonly ILog _log;

  /// <summary>
  /// 主线程调度器：接收「捕获 + 序列化」动作并保证它在主线程执行。
  /// </summary>
  /// <remarks>
  /// 生产代码必须传入 <c>action =&gt; Callable.From(action).CallDeferred()</c>；
  /// 两参/三参构造默认使用 inline 调度（当前线程直接执行），仅供纯单测与
  /// 「调用方已保证在主线程」的场景。
  /// </remarks>
  private readonly Action<Action> _dispatch;

  /// <summary>
  /// 按模块名取日志器的工厂：让上层为同源子服务/子模块取到带模块名的日志，
  /// 同时避免核心层反向依赖启动层的静态日志定位。
  /// </summary>
  /// <remarks>
  /// 默认返回本管理器自身的日志器（<c>_log</c>）；生产由 MainWindow 注入
  /// <c>_logService.GetLogger</c> 的取用器，测试不需要改动。
  /// </remarks>
  private readonly Func<string, ILog> _loggerFactory;

  private AutoList<Boss> _bosses = new();
  private AutoList<CreatorAlias> _aliases = new();
  private AppSettings _settings = new();
  private AutoList<CreatorPackage> _creatorPackages = new();
  private MergeConfig _mergeConfig = new();
  private InfoConfig _infoConfig = new();
  private RecordingConfig _recordingConfig = new();
  private UiLayoutData _uiLayout = new();

  /// <summary>
  /// UI 布局记忆的只读保护标记（<c>true</c> = 盘上版本高于当前程序，本次运行放弃读写该段）。
  /// </summary>
  /// <remarks>
  /// 布局文件版本过新说明用户用过更新的程序：此时既不能把新格式当旧格式解释，也不能用旧格式
  /// 覆盖它，所以整段退化为「只在内存用默认值、保存时跳过该文件」，其余 7 段照常读写。
  /// </remarks>
  private bool _uiLayoutReadOnly;

  private CancellationTokenSource? _saveCts;

  /// <summary>
  /// 防抖 CTS 的小锁：只保护 <see cref="_saveCts"/> 的取消/替换，
  /// 不和写盘临界区混用，避免 TriggerAutoSave 为换令牌去等盘上 IO。
  /// </summary>
  private readonly object _ctsLock = new();

  /// <summary>
  /// 写盘锁：只给真正写文件的地方（自动保存任务 / <see cref="SaveAll"/> / <see cref="Dispose"/>
  /// 末次落盘）使用，保证同一批文件不会被两处交错写。
  /// </summary>
  /// <remarks>
  /// 严禁在「等主线程捕获」期间持有它：主线程可能正调用 <see cref="Dispose"/> 等这把锁，
  /// 而捕获又要排在主线程执行，互相等待会死锁。
  /// </remarks>
  private readonly object _writeLock = new();

  /// <summary>
  /// 自动保存的单消费者信号量：同一时刻只允许一个任务真正执行保存，
  /// 替代原先 <c>_isSaving</c> 的 check-then-set（volatile 只保证可见性、不提供互斥）。
  /// </summary>
  /// <remarks>
  /// 不在 <see cref="Dispose"/> 中释放：已排队的防抖任务可能仍在 Wait/Release，
  /// 释放会抛 <see cref="ObjectDisposedException"/>（未观察的任务异常）；
  /// 该信号量不持有原生句柄，交给 GC 回收即可。
  /// </remarks>
  private readonly SemaphoreSlim _saveGate = new(1, 1);

  private const int DebounceMs = 1500;

  /// <summary>
  /// 待落盘标记（1 = 有改动尚未被保存消费）。
  /// 防抖到期时若已有保存在写，只置脏，由持有者在收尾时补写，避免直接丢弃改动。
  /// </summary>
  private int _saveRequested;

  /// <summary>保存代次：每次在主线程捕获快照时自增，用来判定快照的新旧。</summary>
  private int _saveSequence;

  /// <summary>
  /// 已落盘的最大代次（只在 <c>_writeLock</c> 内读写）。
  /// 捕获与写盘已拆到不同线程，必须用它挡住「先捕获的旧快照晚到、覆盖后捕获的新快照」。
  /// </summary>
  private int _lastWrittenSequence;

  /// <summary>释放状态（1 = 已进入 Dispose），用于幂等。</summary>
  private int _disposeState;

  private volatile bool _disposed;

  public AutoList<Boss> Bosses => _bosses;
  public AutoList<CreatorAlias> Aliases => _aliases;
  public AppSettings Settings => _settings;
  public AutoList<CreatorPackage> CreatorPackages => _creatorPackages;
  public MergeConfig MergeConfig => _mergeConfig;
  public InfoConfig InfoConfig => _infoConfig;
  public RecordingConfig RecordingConfig => _recordingConfig;

  /// <summary>
  /// UI 布局记忆数据（主窗口几何 / 文件选择窗口尺寸 / 各板块分割条）。
  /// </summary>
  /// <remarks>
  /// 这是一次性快照而非双向数据源：UI 在启动或弹窗弹出时读一次并应用，用户调整后回写这里，
  /// 因此不要对它 <c>Bind()</c> 做模型 → UI 的刷新（详见 <see cref="UiLayoutData"/> 的类注释）。
  /// </remarks>
  public UiLayoutData UiLayout => _uiLayout;

  /// <summary>
  /// 数据目录绝对路径。
  /// </summary>
  /// <remarks>
  /// 供服务层在数据目录下托管自有子目录（如符卡 GIF 集的解压落点），避免各处重复拼接
  /// 数据目录导致落点不一致。
  /// </remarks>
  public string DataDir => _dataDir;

  /// <summary>
  /// 取指定模块的日志器。
  /// </summary>
  /// <param name="moduleName">模块名（一般为类名/子系统名，如 <c>Recording</c>）。</param>
  /// <returns>该模块的 <see cref="ILog"/>；未注入工厂时退回本管理器的日志器。</returns>
  public ILog GetLogger(string moduleName) => _loggerFactory(moduleName);

  private AutoValue<string?>.Binding? _activeAiModelIdBinding;
  private AutoValue<int>.Binding? _webSocketPortBinding;
  private AutoValue<string>.Binding? _webSocketModeBinding;
  private AutoValue<string>.Binding? _koishiWebSocketUrlBinding;

  /// <summary>
  /// 订阅若干设置项变化，仅用于打印变更日志（UI 刷新由各面板自身的 Bind().OnModify() / Bind().OnValue() 驱动）
  /// </summary>
  private void BindSettingsChanges()
  {
    // 重新绑定时先释放旧绑定，避免重复 LoadAll() 时订阅泄漏
    _activeAiModelIdBinding?.Dispose();
    _webSocketPortBinding?.Dispose();
    _webSocketModeBinding?.Dispose();
    _koishiWebSocketUrlBinding?.Dispose();

    if (_settings.ActiveAiModelId != null)
      _activeAiModelIdBinding = _settings
        .ActiveAiModelId.Bind()
        .OnValue(_ => _log.Print("DataManager: ActiveAiModelId changed"));
    if (_settings.WebSocketPort != null)
      _webSocketPortBinding = _settings
        .WebSocketPort.Bind()
        .OnValue(_ => _log.Print("DataManager: WebSocketPort changed"));
    if (_settings.WebSocketMode != null)
      _webSocketModeBinding = _settings
        .WebSocketMode.Bind()
        .OnValue(_ => _log.Print("DataManager: WebSocketMode changed"));
    if (_settings.KoishiWebSocketUrl != null)
      _koishiWebSocketUrlBinding = _settings
        .KoishiWebSocketUrl.Bind()
        .OnValue(_ => _log.Print("DataManager: KoishiWebSocketUrl changed"));
  }

  /// <summary>
  /// 创建数据管理器，自动保存的主线程调度器使用 inline（调用方必须已保证在主线程）。
  /// </summary>
  /// <param name="dataDir">数据目录。</param>
  /// <param name="encryptor">密钥加解密器。</param>
  public DataManager(string dataDir, AesEncryptor encryptor)
    : this(dataDir, encryptor, NullLog.Instance, static action => action()) { }

  /// <summary>
  /// 创建数据管理器，自动保存的主线程调度器使用 inline（调用方必须已保证在主线程）。
  /// </summary>
  /// <param name="dataDir">数据目录。</param>
  /// <param name="encryptor">密钥加解密器。</param>
  /// <param name="log">日志。</param>
  public DataManager(string dataDir, AesEncryptor encryptor, ILog log)
    : this(dataDir, encryptor, log, static action => action()) { }

  /// <summary>
  /// 创建数据管理器。
  /// </summary>
  /// <param name="dataDir">数据目录。</param>
  /// <param name="encryptor">密钥加解密器。</param>
  /// <param name="log">日志。</param>
  /// <param name="dispatch">
  /// 主线程调度器：接收「捕获 + 序列化」动作并保证它在主线程执行，例如
  /// <c>action =&gt; Callable.From(action).CallDeferred()</c>。
  /// </param>
  /// <param name="loggerFactory">
  /// 可选：按模块名取日志器的工厂（<see cref="GetLogger"/> 使用）。为空时退回 <paramref name="log"/>，
  /// 保持既有构造调用的行为不变（测试无需改动）。
  /// </param>
  public DataManager(
    string dataDir,
    AesEncryptor encryptor,
    ILog log,
    Action<Action> dispatch,
    Func<string, ILog>? loggerFactory = null
  )
  {
    _dataDir = dataDir;
    _encryptor = encryptor;
    _log = log;
    _loggerFactory = loggerFactory ?? (_ => _log);
    _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      Converters =
      {
        new AutoListConverter<Boss>(),
        new AutoListConverter<CreatorAlias>(),
        new AutoListConverter<AiModelConfig>(),
        new AutoListConverter<string>(),
        new AutoListConverter<CreatorPackage>(),
        new AutoListConverter<SpellCardMappingEntry>(),
        new AutoListConverter<GifSetRecord>(),
        new AutoListConverter<TargetGroup>(),
        new AutoListConverter<SplitLayoutRecord>(),
        new AutoValueJsonConverterFactory(),
      },
    };

    if (!Directory.Exists(_dataDir))
    {
      Directory.CreateDirectory(_dataDir);
      _log.Print($"DataManager: created data dir {_dataDir}");
    }
  }

  /// <summary>
  /// 加载所有数据
  /// </summary>
  public void LoadAll()
  {
    _log.Print($"DataManager.LoadAll: dir={_dataDir}");

    // 加载并创建新的 AutoList
    var loadedBosses = LoadJson<List<Boss>>("spellcard_table.json") ?? new();
    _bosses = new AutoList<Boss>(loadedBosses);

    var loadedAliases = LoadJson<List<CreatorAlias>>("alias_table.json") ?? new();
    _aliases = new AutoList<CreatorAlias>(loadedAliases);

    _settings = LoadJson<AppSettings>("app_settings.json") ?? new();
    // 回填被 JSON 显式 null 覆盖的 AutoValue/AutoList 属性，保证同步绑定链路非空
    _settings.EnsureIntegrity();
    // Subscribe to AutoValue changes only for change logging (UI refresh is driven by each panel's own Bind())
    BindSettingsChanges();

    var loadedCreatorPackages = LoadJson<List<CreatorPackage>>("creator_packages.json") ?? new();
    _creatorPackages = new AutoList<CreatorPackage>(loadedCreatorPackages);

    _mergeConfig = LoadJson<MergeConfig>("merge_config.json") ?? new();

    _infoConfig = LoadJson<InfoConfig>("info_config.json") ?? new();
    // 回填被 JSON 显式 null 覆盖的 AutoValue/AutoList 属性，保证同步绑定链路非空
    _infoConfig.EnsureIntegrity();

    _recordingConfig = LoadJson<RecordingConfig>("recording_config.json") ?? new();
    // 同上：录制配置也含 AutoValue 属性
    _recordingConfig.EnsureIntegrity();

    // 第 8 段：UI 布局记忆。版本高于当前程序时整段退化为只读（内存用默认值、保存时跳过该文件），
    // 避免旧版本用自己认识的字段把新格式覆盖掉。
    var loadedUiLayout = LoadJson<UiLayoutData>("ui_layout.json");
    var loadedUiLayoutVersion = loadedUiLayout?.Version?.Value ?? UiLayoutData.CurrentVersion;
    if (loadedUiLayoutVersion > UiLayoutData.CurrentVersion)
    {
      _uiLayout = new();
      _uiLayoutReadOnly = true;
      _log.Warn(
        $"DataManager: ui_layout.json version {loadedUiLayoutVersion} is newer than "
          + $"supported {UiLayoutData.CurrentVersion}; UI layout is read-only for this run."
      );
    }
    else
    {
      _uiLayout = loadedUiLayout ?? new();
      // 回填被 JSON 显式 null 覆盖的 AutoValue/AutoList 属性，再收敛非法值（顺序不可颠倒）
      _uiLayout.EnsureIntegrity();
      _uiLayout.Sanitize();
      MigrateUiLayout(_uiLayout);
      // 同一次 LoadAll 内可能先读到过新版本文件、之后文件被换回旧格式：标记必须随之复位
      _uiLayoutReadOnly = false;
    }

    _log.Print(
      $"DataManager.LoadAll: bosses={_bosses.Count}, aliases={_aliases.Count}, "
        + $"aiModels={_settings.AiModels.Count}, creatorPackages={_creatorPackages.Count}, "
        + $"gifSets={_infoConfig.GifSets.Count}, targetGroups={_settings.TargetGroups.Count}, "
        + $"uiLayoutSplits={_uiLayout.Splits.Count}"
    );

    // 解密 API 密钥
    foreach (var model in _settings.AiModels)
    {
      if (!string.IsNullOrEmpty(model.EncryptedApiKey.Value))
      {
        model.EncryptedApiKey.Value = _encryptor.Decrypt(model.EncryptedApiKey.Value);
      }
    }
  }

  /// <summary>
  /// 按版本逐级迁移 UI 布局数据（当前数据结构只有 v1，没有实际迁移动作）。
  /// </summary>
  /// <remarks>
  /// 保留方法作为逐版本迁移的固定落点：每段只负责一次升版，避免跨版本跳级时漏掉中间版本的字段
  /// 改写。以后新增 v2 时在此追加 <c>if (data.Version.Value &lt; 2) { ...; data.Version.Value = 2; }</c>。
  /// 版本修正（<c>&lt;= 0</c> 视为当前版本）由 <see cref="UiLayoutData.Sanitize"/> 负责，因此这里
  /// 正常情况下直接返回。
  /// </remarks>
  /// <param name="data">已完成 <see cref="UiLayoutData.EnsureIntegrity"/> 与
  /// <see cref="UiLayoutData.Sanitize"/> 的布局数据。</param>
  private static void MigrateUiLayout(UiLayoutData data)
  {
    if (data.Version.Value == UiLayoutData.CurrentVersion)
      return;

    // 兜底对齐：迁移动作全部执行完后统一收口到当前版本
    data.Version.Value = UiLayoutData.CurrentVersion;
  }

  /// <summary>
  /// 触发自动保存（防抖）
  /// </summary>
  public void TriggerAutoSave()
  {
    _log.Print("DataManager: TriggerAutoSave called.");
    lock (_ctsLock)
    {
      // 已释放（关窗/退出）：UI 绑定可能还有最后一次回调，不再排新的防抖保存
      if (_disposed)
        return;

      _saveCts?.Cancel();
      _saveCts?.Dispose();
      _saveCts = new CancellationTokenSource();

      _ = SaveDelayedAsync(_saveCts.Token);
    }
  }

  /// <summary>
  /// 防抖到期后的保存入口：单消费者抢 <c>_saveGate</c>；抢不到只置脏标记，由持有者收尾补写。
  /// 捕获 + 序列化经 dispatcher 回主线程执行，线程池只做原子写盘。
  /// </summary>
  /// <param name="token">防抖取消令牌。</param>
  private async Task SaveDelayedAsync(CancellationToken token)
  {
    try
    {
      await Task.Delay(DebounceMs, token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }

    // 已释放（关窗/退出）：Dispose 已同步落盘并释放了同步集合，这里不再写盘
    while (!_disposed)
    {
      // 先置脏再抢写权：即使下面抢不到，持有者（DrainPendingSavesAsync 收尾）也会看到脏标记补写一次，
      // 不会像原实现那样在 _isSaving 时直接丢掉这次改动
      Interlocked.Exchange(ref _saveRequested, 1);

      if (!_saveGate.Wait(0))
      {
        _log.Print("DataManager: auto-save already running, change queued.");
        return;
      }

      try
      {
        if (!await DrainPendingSavesAsync().ConfigureAwait(false))
          return;
      }
      finally
      {
        _saveGate.Release();
      }

      // 释放后补看一次：覆盖「最后一次脏检查之后、释放信号量之前」又被置脏的窗口，
      // 保证脏标记一定有消费者
      if (_disposed || Volatile.Read(ref _saveRequested) == 0)
        return;
    }
  }

  /// <summary>
  /// 在已持有 <c>_saveGate</c> 时消费脏标记：先清脏再保存；保存期间新到的改动会在本轮收尾时再补一次。
  /// </summary>
  /// <returns>保存是否成功；失败时本轮结束，等下一次触发或 Dispose 兜底。</returns>
  private async Task<bool> DrainPendingSavesAsync()
  {
    do
    {
      Interlocked.Exchange(ref _saveRequested, 0);
      if (!await TrySaveOnceAsync().ConfigureAwait(false))
        return false;
    } while (!_disposed && Volatile.Read(ref _saveRequested) == 1);

    return true;
  }

  /// <summary>
  /// 自动保存路径的一次保存：先在主线程捕获 + 序列化，再在写盘锁内原子写文件。
  /// </summary>
  /// <returns>是否真的写了一次盘；已释放时直接跳过（Dispose 已做末次落盘）。</returns>
  private async Task<bool> TrySaveOnceAsync()
  {
    SavePayload? payload;
    try
    {
      payload = await CapturePayloadOnMainThreadAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _log.Err($"DataManager: auto-save failed: {ex.GetType().Name}: {ex.Message}");
      return false;
    }

    // deferred 到期时发现已释放：Dispose 已同步落盘，这里不碰已释放的集合
    if (payload == null)
      return false;

    lock (_writeLock)
    {
      if (_disposed)
        return false;

      try
      {
        // 已经序列化好的文本，这里只做临时文件 + 原子替换
        WritePayloadIfNewer(payload);
        _log.Print("DataManager: auto-save completed.");
        return true;
      }
      catch (Exception ex)
      {
        _log.Err($"DataManager: auto-save failed: {ex.GetType().Name}: {ex.Message}");
        return false;
      }
    }
  }

  /// <summary>
  /// 把「捕获 + 序列化」调度到主线程执行；已释放时不枚举任何集合。
  /// </summary>
  /// <returns>8 段 JSON 快照；已释放时为 <c>null</c>。</returns>
  private Task<SavePayload?> CapturePayloadOnMainThreadAsync()
  {
    var completion = new TaskCompletionSource<SavePayload?>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );

    try
    {
      _dispatch(() =>
      {
        // 排队期间可能已关窗/释放：安全跳过，不碰已释放的 AutoList
        if (_disposed)
        {
          completion.TrySetResult(null);
          return;
        }

        try
        {
          completion.TrySetResult(BuildSavePayload());
        }
        catch (Exception ex)
        {
          completion.TrySetException(ex);
        }
      });
    }
    catch (Exception ex)
    {
      completion.TrySetException(ex);
    }

    return completion.Task;
  }

  /// <summary>
  /// 立即保存所有数据（同步；调用方必须已保证在主线程）。
  /// </summary>
  /// <remarks>
  /// 捕获与序列化在调用线程（主线程）直接枚举活的 <see cref="AutoList{T}"/>，随后持
  /// <c>_writeLock</c> 同步写盘：<see cref="Dispose"/> 的末次落盘会经这里等待在飞写盘结束，
  /// 两次写盘共用同一把锁，避免交错写同一批文件。写失败记录后向调用方抛出。
  /// </remarks>
  public void SaveAll()
  {
    _log.Print("DataManager: SaveAll running.");

    try
    {
      // 主线程捕获 + 序列化：Boss.SpellCards 等嵌套集合也只在这里枚举
      var payload = BuildSavePayload();

      lock (_writeLock)
      {
        WritePayloadIfNewer(payload);
      }

      _log.Print("DataManager: SaveAll succeeded.");
    }
    catch (Exception ex)
    {
      _log.Err($"DataManager: SaveAll failed: {ex.GetType().Name}: {ex.Message}");
      throw;
    }
  }

  /// <inheritdoc/>
  /// <remarks>
  /// 退出前必须同步落盘：<see cref="TriggerAutoSave"/> 的唯一保存入口在 1500ms 防抖之后，
  /// 关窗/退出时防抖往往还没到期，只取消 CTS 会把最后一笔改动丢掉，所以保存要排在释放集合之前。
  /// 末次落盘走 <see cref="SaveAll"/>：捕获在主线程，写盘持 <c>_writeLock</c> 等在飞写盘结束；
  /// 已排队的 deferred 捕获会在执行时发现 <c>_disposed</c> 而跳过。
  /// 写失败只记日志，不向关窗路径抛异常。
  /// </remarks>
  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposeState, 1) != 0)
      return;

    _disposed = true;

    // CTS 只用小锁处理：先取消防抖，后续以末次落盘为权威路径
    lock (_ctsLock)
    {
      _saveCts?.Cancel();
      _saveCts?.Dispose();
      _saveCts = null;
    }

    // 末次落盘：取消 CTS 会丢掉防抖窗口里的改动，因此必须同步写一次
    try
    {
      SaveAll();
    }
    catch (Exception ex)
    {
      _log.Err($"DataManager: flush on dispose failed: {ex.GetType().Name}: {ex.Message}");
    }

    _activeAiModelIdBinding?.Dispose();
    _webSocketPortBinding?.Dispose();
    _webSocketModeBinding?.Dispose();
    _koishiWebSocketUrlBinding?.Dispose();
    _bosses.Dispose();
    _aliases.Dispose();
    _creatorPackages.Dispose();
    _mergeConfig.Mapping.Dispose();
    _infoConfig.GifSets?.Dispose();
    _uiLayout.Splits?.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// 一次保存的 8 段已序列化 JSON：捕获阶段产出，写盘阶段只落文本。
  /// </summary>
  private sealed class SavePayload
  {
    /// <summary>快照代次：捕获时自增，越大越新。</summary>
    public int Sequence { get; init; }

    /// <summary>符卡表 JSON（<c>spellcard_table.json</c>）。</summary>
    public string SpellCardTableJson { get; init; } = "[]";

    /// <summary>别名表 JSON（<c>alias_table.json</c>）。</summary>
    public string AliasTableJson { get; init; } = "[]";

    /// <summary>应用设置 JSON（<c>app_settings.json</c>，API 密钥已加密）。</summary>
    public string AppSettingsJson { get; init; } = "{}";

    /// <summary>创作者包 JSON（<c>creator_packages.json</c>）。</summary>
    public string CreatorPackagesJson { get; init; } = "[]";

    /// <summary>整合配置 JSON（<c>merge_config.json</c>）。</summary>
    public string MergeConfigJson { get; init; } = "{}";

    /// <summary>信息配置 JSON（<c>info_config.json</c>）。</summary>
    public string InfoConfigJson { get; init; } = "{}";

    /// <summary>录制配置 JSON（<c>recording_config.json</c>）。</summary>
    public string RecordingConfigJson { get; init; } = "{}";

    /// <summary>
    /// UI 布局记忆 JSON（<c>ui_layout.json</c>）；<c>null</c> 表示本次跳过写该文件
    /// （盘上版本高于当前程序时的只读保护）。
    /// </summary>
    public string? UiLayoutJson { get; init; }
  }

  /// <summary>
  /// 在主线程捕获并序列化 8 段数据（深序列化：活 <see cref="AutoList{T}"/> 与嵌套集合都在这里
  /// 变成字符串，池线程不再触碰任何活集合）。
  /// </summary>
  /// <returns>与活模型脱钩的 8 段 JSON。</returns>
  private SavePayload BuildSavePayload() =>
    new()
    {
      Sequence = Interlocked.Increment(ref _saveSequence),
      SpellCardTableJson = JsonSerializer.Serialize(_bosses, _jsonOptions),
      AliasTableJson = JsonSerializer.Serialize(_aliases, _jsonOptions),
      AppSettingsJson = SerializeSettingsForSave(),
      CreatorPackagesJson = JsonSerializer.Serialize(_creatorPackages, _jsonOptions),
      MergeConfigJson = JsonSerializer.Serialize(_mergeConfig, _jsonOptions),
      InfoConfigJson = JsonSerializer.Serialize(_infoConfig, _jsonOptions),
      RecordingConfigJson = JsonSerializer.Serialize(_recordingConfig, _jsonOptions),
      // 只读保护：版本过新时置 null，WritePayload 据此跳过 ui_layout.json
      UiLayoutJson = _uiLayoutReadOnly ? null : JsonSerializer.Serialize(_uiLayout, _jsonOptions),
    };

  /// <summary>
  /// 序列化应用设置：先 JSON 克隆再对克隆体的 API 密钥加密，避免改动活的模型；
  /// 克隆与序列化都在主线程完成。
  /// </summary>
  /// <returns>已加密的 <c>app_settings.json</c> 文本。</returns>
  private string SerializeSettingsForSave()
  {
    var json = JsonSerializer.Serialize(_settings, _jsonOptions);
    var clone = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();

    // 保存前加密 API 密钥（只改克隆体，不动活模型）
    foreach (var model in clone.AiModels)
    {
      if (!string.IsNullOrEmpty(model.EncryptedApiKey.Value))
      {
        model.EncryptedApiKey.Value = _encryptor.Encrypt(model.EncryptedApiKey.Value);
      }
    }

    return JsonSerializer.Serialize(clone, _jsonOptions);
  }

  /// <summary>
  /// 按代次写盘：只有比已落盘快照更新的快照才允许覆盖文件。
  /// </summary>
  /// <remarks>
  /// 调用方必须已持 <c>_writeLock</c>。捕获在主线程、写盘在池线程，两者的完成顺序可能颠倒；
  /// 没有这道闸时，先捕获的旧快照晚到会把关窗时刚写下的新数据覆盖回旧值。
  /// </remarks>
  /// <param name="payload">本次快照。</param>
  private void WritePayloadIfNewer(SavePayload payload)
  {
    // 已经有更新的快照落盘：旧快照直接丢弃，绝不回退磁盘上的数据
    if (payload.Sequence <= _lastWrittenSequence)
      return;

    WritePayload(payload);
    _lastWrittenSequence = payload.Sequence;
  }

  /// <summary>
  /// 把 8 段已序列化 JSON 原子写入数据目录；只做文件 IO，可由线程池执行。
  /// </summary>
  /// <param name="payload">待写入的 JSON 文本。</param>
  private void WritePayload(SavePayload payload)
  {
    WriteJsonAtomic(Path.Combine(_dataDir, "spellcard_table.json"), payload.SpellCardTableJson);
    WriteJsonAtomic(Path.Combine(_dataDir, "alias_table.json"), payload.AliasTableJson);
    WriteJsonAtomic(Path.Combine(_dataDir, "app_settings.json"), payload.AppSettingsJson);
    WriteJsonAtomic(Path.Combine(_dataDir, "creator_packages.json"), payload.CreatorPackagesJson);
    WriteJsonAtomic(Path.Combine(_dataDir, "merge_config.json"), payload.MergeConfigJson);
    WriteJsonAtomic(Path.Combine(_dataDir, "info_config.json"), payload.InfoConfigJson);
    WriteJsonAtomic(Path.Combine(_dataDir, "recording_config.json"), payload.RecordingConfigJson);

    // 版本过新的只读保护：UiLayoutJson 为 null 时本次跳过该文件，其余 7 段照常落盘
    if (payload.UiLayoutJson != null)
      WriteJsonAtomic(Path.Combine(_dataDir, "ui_layout.json"), payload.UiLayoutJson);
  }

  /// <summary>
  /// 加载单个 JSON 文件。
  /// </summary>
  /// <typeparam name="T">目标类型。</typeparam>
  /// <param name="fileName">数据目录内的文件名。</param>
  /// <returns>反序列化结果；文件不存在、损坏或解析失败时为 <c>null</c>。</returns>
  /// <remarks>
  /// 失败不再静默吞掉：记录错误并把坏文件尽力备份为
  /// <c>&lt;name&gt;.corrupt-&lt;yyyyMMddHHmmss&gt;</c>，避免下一次保存用空数据
  /// 把仅存的现场直接覆盖。返回 <c>null</c> 后 <see cref="LoadAll"/> 继续按空数据处理。
  /// </remarks>
  private T? LoadJson<T>(string fileName)
    where T : class
  {
    var path = Path.Combine(_dataDir, fileName);
    if (!File.Exists(path))
      return null;

    try
    {
      var json = File.ReadAllText(path);
      return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }
    catch (Exception ex)
    {
      _log.Err($"DataManager: failed to load {fileName}: {ex.GetType().Name}: {ex.Message}");
      BackupCorruptFile(path);
      return null;
    }
  }

  /// <summary>
  /// 把无法解析的文件复制为 <c>&lt;name&gt;.corrupt-&lt;yyyyMMddHHmmss&gt;</c>（尽力而为）。
  /// </summary>
  /// <param name="path">坏文件绝对路径。</param>
  private void BackupCorruptFile(string path)
  {
    var backupBase = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
    try
    {
      // 同一秒内多次损坏时追加序号，避免覆盖已有备份
      var candidate = backupBase;
      for (var index = 1; File.Exists(candidate); index++)
        candidate = $"{backupBase}-{index}";

      File.Copy(path, candidate, overwrite: false);
      _log.Print($"DataManager: corrupt file backed up to {candidate}");
    }
    catch (Exception ex)
    {
      _log.Err(
        $"DataManager: failed to back up corrupt file {path}: {ex.GetType().Name}: {ex.Message}"
      );
    }
  }

  /// <summary>
  /// 原子写 JSON：先写同目录唯一临时文件，再 <see cref="File.Move(string, string, bool)"/>
  /// （overwrite）替换目标文件，避免进程中断留下半截文件；失败时清理临时文件。
  /// </summary>
  /// <param name="path">目标文件绝对路径。</param>
  /// <param name="json">已序列化的 JSON 文本。</param>
  private void WriteJsonAtomic(string path, string json)
  {
    // 临时文件名带进程号 + GUID：多实例/多任务写同名目标时不会互相踩临时文件
    var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
    try
    {
      File.WriteAllText(tempPath, json);
      File.Move(tempPath, path, overwrite: true);
    }
    catch (Exception)
    {
      TryDeleteFile(tempPath);
      throw;
    }
  }

  /// <summary>
  /// 尽力删除临时文件；删除失败只记日志，不掩盖原始异常。
  /// </summary>
  /// <param name="path">临时文件绝对路径。</param>
  private void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch (Exception ex)
    {
      _log.Err(
        $"DataManager: failed to delete temp file {path}: {ex.GetType().Name}: {ex.Message}"
      );
    }
  }
}
