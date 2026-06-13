namespace AutoCMEX.Core.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Models;

/// <summary>
/// 数据持久化管理：JSON 读写 + 自动保存防抖
/// </summary>
public class DataManager
{
  private readonly string _dataDir;
  private readonly AesEncryptor _encryptor;
  private readonly JsonSerializerOptions _jsonOptions;

  private List<Boss> _bosses = new();
  private List<CreatorAlias> _aliases = new();
  private AppSettings _settings = new();

  private CancellationTokenSource? _saveCts;
  private readonly object _saveLock = new();
  private const int DebounceMs = 1500;

  public List<Boss> Bosses => _bosses;
  public List<CreatorAlias> Aliases => _aliases;
  public AppSettings Settings => _settings;

  public DataManager(string dataDir, AesEncryptor encryptor)
  {
    _dataDir = dataDir;
    _encryptor = encryptor;
    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    if (!Directory.Exists(_dataDir))
      Directory.CreateDirectory(_dataDir);
  }

  /// <summary>
  /// 加载所有数据
  /// </summary>
  public void LoadAll()
  {
    _bosses = LoadJson<List<Boss>>("spellcard_table.json") ?? new();
    _aliases = LoadJson<List<CreatorAlias>>("alias_table.json") ?? new();
    _settings = LoadJson<AppSettings>("app_settings.json") ?? new();

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
    lock (_saveLock)
    {
      _saveCts?.Cancel();
      _saveCts = new CancellationTokenSource();
      var token = _saveCts.Token;

      Task.Delay(DebounceMs, token)
        .ContinueWith(
          _ =>
          {
            if (!token.IsCancellationRequested)
              SaveAll();
          },
          token
        );
    }
  }

  /// <summary>
  /// 立即保存所有数据
  /// </summary>
  public void SaveAll()
  {
    // 保存前加密 API 密钥
    var settingsToSave = CloneSettingsForSave();

    SaveJson("spellcard_table.json", _bosses);
    SaveJson("alias_table.json", _aliases);
    SaveJson("app_settings.json", settingsToSave);
  }

  private AppSettings CloneSettingsForSave()
  {
    var clone = new AppSettings
    {
      WebSocketPort = _settings.WebSocketPort,
      MessageFilterMode = _settings.MessageFilterMode,
      KoishiPluginPath = _settings.KoishiPluginPath,
    };

    foreach (var model in _settings.AiModels)
    {
      clone.AiModels.Add(
        new AiModelConfig
        {
          Id = model.Id,
          ApiFormat = model.ApiFormat,
          EndpointUrl = model.EndpointUrl,
          ModelId = model.ModelId,
          EncryptedApiKey = _encryptor.Encrypt(model.EncryptedApiKey),
        }
      );
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
      // 文件损坏，返回 null
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
