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
public class DataManager : IDisposable
{
  private readonly string _dataDir;
  private readonly AesEncryptor _encryptor;
  private readonly JsonSerializerOptions _jsonOptions;
  private readonly ILog _log;

  private AutoList<Boss> _bosses = new();
  private AutoList<CreatorAlias> _aliases = new();
  private AppSettings _settings = new();

  private CancellationTokenSource? _saveCts;
  private readonly object _saveLock = new();
  private const int DebounceMs = 1500;
  private volatile bool _isSaving;
  private bool _disposed;

  public AutoList<Boss> Bosses => _bosses;
  public AutoList<CreatorAlias> Aliases => _aliases;
  public AppSettings Settings => _settings;

  /// <summary>数据变更事件（用于 UI 刷新）</summary>
  public event Action? DataChanged;

  /// <summary>触发数据变更通知</summary>
  public void NotifyDataChanged() => DataChanged?.Invoke();

  /// <summary>
  /// 订阅 AutoValue 属性变更，自动通知 UI 组件
  /// </summary>
  private void BindSettingsChanges()
  {
    if (_settings.ActiveAiModelId != null)
      _settings
        .ActiveAiModelId.Bind()
        .OnValue(_ =>
        {
          _log.Print("DataManager: ActiveAiModelId changed, notifying UI...");
          NotifyDataChanged();
        });
    if (_settings.WebSocketPort != null)
      _settings
        .WebSocketPort.Bind()
        .OnValue(_ =>
        {
          _log.Print("DataManager: WebSocketPort changed, notifying UI...");
          NotifyDataChanged();
        });
    if (_settings.WebSocketMode != null)
      _settings
        .WebSocketMode.Bind()
        .OnValue(_ =>
        {
          _log.Print("DataManager: WebSocketMode changed, notifying UI...");
          NotifyDataChanged();
        });
    if (_settings.KoishiWebSocketUrl != null)
      _settings
        .KoishiWebSocketUrl.Bind()
        .OnValue(_ =>
        {
          _log.Print("DataManager: KoishiWebSocketUrl changed, notifying UI...");
          NotifyDataChanged();
        });
  }

  public DataManager(string dataDir, AesEncryptor encryptor)
    : this(dataDir, encryptor, AppLogs.GetOrCreate().GetLogger(nameof(DataManager))) { }

  public DataManager(string dataDir, AesEncryptor encryptor, ILog log)
  {
    _dataDir = dataDir;
    _encryptor = encryptor;
    _log = log;
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
    // Subscribe to AutoValue changes to notify UI components
    BindSettingsChanges();
    _log.Print(
      $"DataManager.LoadAll: bosses={_bosses.Count}, aliases={_aliases.Count}, "
        + $"aiModels={_settings.AiModels.Count}"
    );

    // 解密 API 密钥
    foreach (var model in _settings.AiModels)
    {
      if (!string.IsNullOrEmpty(model.EncryptedApiKey))
      {
        model.EncryptedApiKey = _encryptor.Decrypt(model.EncryptedApiKey);
      }
    }
  }

  /// <summary>
  /// 触发自动保存（防抖）
  /// </summary>
  public void TriggerAutoSave()
  {
    _log.Print("DataManager: TriggerAutoSave called.");
    lock (_saveLock)
    {
      _saveCts?.Cancel();
      _saveCts?.Dispose();
      _saveCts = new CancellationTokenSource();
      var token = _saveCts.Token;

      _ = SaveDelayedAsync(token);
    }
  }

  private async Task SaveDelayedAsync(CancellationToken token)
  {
    try
    {
      await Task.Delay(DebounceMs, token);
    }
    catch (TaskCanceledException)
    {
      return;
    }

    if (_isSaving)
    {
      _log.Print("DataManager: save already in progress, skipping.");
      return;
    }

    _isSaving = true;
    try
    {
      SaveAll();
      _log.Print("DataManager: auto-save completed.");
    }
    catch (Exception ex)
    {
      _log.Err($"DataManager: auto-save failed: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
      _isSaving = false;
    }
  }

  /// <summary>
  /// 立即保存所有数据
  /// </summary>
  public void SaveAll()
  {
    _log.Print("DataManager: SaveAll running.");
    // 保存前加密 API 密钥
    var settingsToSave = CloneSettingsForSave();

    try
    {
      // 将 AutoList 转换为 List<T> 以便 JSON 序列化
      SaveJson("spellcard_table.json", new List<Boss>(_bosses));
      SaveJson("alias_table.json", new List<CreatorAlias>(_aliases));
      SaveJson("app_settings.json", settingsToSave);
      _log.Print("DataManager: SaveAll succeeded.");
    }
    catch (Exception ex)
    {
      _log.Err($"DataManager: SaveAll failed: {ex.GetType().Name}: {ex.Message}");
      throw;
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _saveCts?.Cancel();
    _saveCts?.Dispose();
    _saveCts = null;
    _bosses.Dispose();
    _aliases.Dispose();
    GC.SuppressFinalize(this);
  }

  private AppSettings CloneSettingsForSave()
  {
    var json = JsonSerializer.Serialize(_settings, _jsonOptions);
    var clone = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();

    // 保存前加密 API 密钥
    foreach (var model in clone.AiModels)
    {
      if (!string.IsNullOrEmpty(model.EncryptedApiKey))
      {
        model.EncryptedApiKey = _encryptor.Encrypt(model.EncryptedApiKey);
      }
    }

    return clone;
  }

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
    catch (Exception)
    {
      return null;
    }
  }

  private void SaveJson<T>(string fileName, T data)
  {
    var path = Path.Combine(_dataDir, fileName);
    var json = JsonSerializer.Serialize(data, _jsonOptions);
    File.WriteAllText(path, json);
  }
}
