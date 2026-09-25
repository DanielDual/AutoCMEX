namespace AutoCMEX.Core.Recording;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoCMEX.Models;
using AutoCMEX.Services;

/// <summary>插件部署动作（一键安装/启用）失败时抛出。</summary>
/// <remarks>消息面向用户，可直接展示在设置面板上，含失败的步骤与路径。</remarks>
public sealed class PluginDeployException : Exception
{
  /// <summary>用面向用户的原因构造。</summary>
  /// <param name="message">失败原因。</param>
  public PluginDeployException(string message)
    : base(message) { }
}

/// <summary>
/// 录制所需插件在引擎目录里的部署检查与一键安装/启用（本插件 <c>autocmex</c> 与第三方弹幕录制器）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="RecordingSandbox"/> 共用同一套判据：沙箱起录前的校验走 <see cref="TryValidateForRecording"/>，
/// 设置面板的状态渲染走 <see cref="Inspect"/>，避免「面板说就绪、起录却被拒」的两套口径。
/// </para>
/// <para>
/// 只有 <see cref="InstallAutocmex"/> 与 <see cref="EnableRecorder"/> 会写引擎目录，且写入范围仅限
/// <c>&lt;引擎&gt;/game/plugins/autocmex/</c>、<c>&lt;引擎&gt;/game/plugins/plugins.json</c> 与备份文件；
/// 查询动作一律只读。改写前必备份，落盘走「临时文件 + 原子替换」。
/// </para>
/// </remarks>
public static class PluginDeployer
{
  /// <summary>引擎的插件清单文件名（相对 <c>game/plugins/</c>）。</summary>
  public const string ManifestFileName = "plugins.json";

  /// <summary>引擎里的插件目录名（相对 <c>game/</c>）。</summary>
  public const string PluginsDirName = "plugins";

  /// <summary>本插件（录制功能）的落地目录名。</summary>
  public const string AutocmexPluginDirName = "autocmex";

  /// <summary>本插件在引擎里的入口文件名（引擎按固定名加载插件目录）。</summary>
  public const string AutocmexEntryFileName = "__init__.lua";

  /// <summary>第三方弹幕录制器的插件目录名关键字（实际为 <c>[pluginpackage]danmaku_recorder_x.y.z</c>）。</summary>
  public const string RecorderPluginKeyword = "danmaku_recorder";

  /// <summary>引擎目录里被改动前的备份后缀（<c>plugins.json.autocmex-bak</c>）。</summary>
  public const string BackupSuffix = ".autocmex-bak";

  /// <summary>写入清单时的临时文件后缀（写完即原子替换掉正式文件）。</summary>
  public const string TempSuffix = ".autocmex-tmp";

  /// <summary>本插件在应用资源里的源目录（复制到引擎目录的源头）。</summary>
  public const string AutocmexSourceDir = "res://src/plugin/luastg/autocmex/";

  private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

  /// <summary>只读检查两个插件在引擎目录里的部署状态（不写盘）。</summary>
  /// <param name="engineDir">引擎根目录（可为空）。</param>
  /// <returns>汇总状态。</returns>
  /// <remarks>
  /// 引擎目录无效或清单损坏时不猜状态：前者整体标为未就绪，后者标为 <see cref="RecordingPluginState.ManifestBroken"/>
  /// 并禁用一切部署动作（避免覆盖用户文件）。
  /// </remarks>
  public static RecordingPluginInspection Inspect(string? engineDir)
  {
    if (!EngineLocator.TryValidate(engineDir, out var reason))
    {
      return new RecordingPluginInspection
      {
        EngineDirUsable = false,
        EngineDir = engineDir ?? string.Empty,
        Autocmex = Unusable(AutocmexPluginDirName, reason),
        Recorder = Unusable(RecorderPluginKeyword, reason),
      };
    }

    var engine = engineDir!;
    var pluginsDir = Path.Combine(EngineLocator.GetGameDir(engine), PluginsDirName);
    var manifestPath = Path.Combine(pluginsDir, ManifestFileName);
    var manifestExists = File.Exists(manifestPath);
    var manifest = TryLoadManifest(manifestPath, manifestExists, out var brokenReason);

    if (manifest is null)
    {
      return new RecordingPluginInspection
      {
        EngineDirUsable = true,
        EngineDir = engine,
        ManifestPath = manifestPath,
        ManifestExists = manifestExists,
        Autocmex = Broken(AutocmexPluginDirName, manifestPath, brokenReason),
        Recorder = Broken(RecorderPluginKeyword, manifestPath, brokenReason),
      };
    }

    return new RecordingPluginInspection
    {
      EngineDirUsable = true,
      EngineDir = engine,
      ManifestPath = manifestPath,
      ManifestExists = manifestExists,
      Autocmex = InspectAutocmex(pluginsDir, manifest),
      Recorder = InspectRecorder(pluginsDir, manifest),
    };
  }

  /// <summary>录制前的插件可用性校验（沙箱建副本之前调用）。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="reason">不满足时的原因（面向用户，含「请先在设置页…」指引）。</param>
  /// <returns>两个插件都可用返回 true。</returns>
  public static bool TryValidateForRecording(string engineDir, out string reason)
  {
    // 引擎目录本身的有效性由调用方先行校验，这里只判插件，便于把原因文案聚合在插件这一层
    var gameDir = EngineLocator.GetGameDir(engineDir);
    var pluginsDir = Path.Combine(gameDir, PluginsDirName);
    var manifestPath = Path.Combine(pluginsDir, ManifestFileName);
    if (!File.Exists(manifestPath))
    {
      reason = $"引擎插件清单不存在：{manifestPath}";
      return false;
    }

    if (TryLoadManifest(manifestPath, exists: true, out var brokenReason) is not { } manifest)
    {
      reason = $"插件清单损坏，未做任何改动：{manifestPath}（{brokenReason}）";
      return false;
    }

    var autocmex = InspectAutocmex(pluginsDir, manifest);
    reason = autocmex.State switch
    {
      RecordingPluginState.Missing => string.IsNullOrEmpty(autocmex.Detail)
        ? "录制插件未安装到引擎，请先在设置页安装或启用插件"
        : $"录制插件未安装到引擎（{autocmex.Detail}），请先在设置页安装或启用插件",
      RecordingPluginState.InstalledDisabled => "录制插件未启用，请先在设置页启用插件",
      _ => string.Empty,
    };
    if (reason.Length > 0)
    {
      return false;
    }

    var recorder = InspectRecorder(pluginsDir, manifest);
    reason = recorder.State switch
    {
      RecordingPluginState.Missing => "未找到弹幕录制器插件，请先在设置页安装或启用插件",
      RecordingPluginState.InstalledDisabled => "弹幕录制器插件未启用，请先在设置页启用插件",
      _ => string.Empty,
    };
    return reason.Length == 0;
  }

  /// <summary>一键安装并启用本插件：备份旧目录 → 复制资源 → 在清单里登记为启用。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns>面向用户的结果说明（含备份文件名）。</returns>
  /// <exception cref="PluginDeployException">引擎目录不可用、复制失败或清单写入失败；失败时原文件保持完好。</exception>
  /// <remarks>
  /// 幂等：目标目录已存在时只在「尚无备份」的前提下先改名备份，否则就地覆盖复制；
  /// 清单里已有条目则只把 <c>enable</c> 置 true，其余字段与其他条目一律原样保留。
  /// </remarks>
  public static string InstallAutocmex(string engineDir)
  {
    var pluginsDir = ResolvePluginsDir(engineDir);
    var manifestPath = Path.Combine(pluginsDir, ManifestFileName);
    var manifest = LoadManifestForWrite(manifestPath, allowMissing: true);

    var targetDir = Path.Combine(pluginsDir, AutocmexPluginDirName);
    var backupDir = targetDir + BackupSuffix;
    var backupName = string.Empty;
    try
    {
      if (Directory.Exists(targetDir) && !Directory.Exists(backupDir))
      {
        Directory.Move(targetDir, backupDir);
        backupName = Path.GetFileName(backupDir);
      }

      PluginInstaller.CopyPluginDir(AutocmexSourceDir, targetDir);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      throw new PluginDeployException($"复制插件失败：{targetDir}（{ex.Message}）");
    }

    var entryFile = Path.Combine(targetDir, AutocmexEntryFileName);
    if (!File.Exists(entryFile))
    {
      throw new PluginDeployException(
        $"插件复制不完整：未找到 {AutocmexEntryFileName}（源目录 {AutocmexSourceDir}）"
      );
    }

    var entry = FindEntry(manifest, AutocmexPluginDirName);
    if (entry is null)
    {
      manifest.Add(
        new JsonObject
        {
          ["directory_mode"] = true,
          ["name"] = AutocmexPluginDirName,
          ["path"] = $"{PluginsDirName}/{AutocmexPluginDirName}/",
          ["enable"] = true,
        }
      );
    }
    else
    {
      entry["enable"] = true;
    }
    SaveManifest(manifestPath, manifest);

    return backupName.Length == 0
      ? "已安装并启用；插件清单已登记该插件。"
      : $"已安装并启用；原目录已备份为 {backupName}。";
  }

  /// <summary>一键启用弹幕录制器：把清单里该条目的 <c>enable</c> 置 true（不动任何文件）。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns>面向用户的结果说明。</returns>
  /// <exception cref="PluginDeployException">引擎目录不可用、清单缺失/损坏或没有录制器条目。</exception>
  public static string EnableRecorder(string engineDir)
  {
    var pluginsDir = ResolvePluginsDir(engineDir);
    var manifestPath = Path.Combine(pluginsDir, ManifestFileName);
    var manifest = LoadManifestForWrite(manifestPath, allowMissing: false);
    var entry =
      FindEntry(manifest, RecorderPluginKeyword)
      ?? throw new PluginDeployException($"插件清单里没有弹幕录制器条目：{manifestPath}");
    entry["enable"] = true;
    SaveManifest(manifestPath, manifest);
    return "已启用弹幕录制器插件。";
  }

  /// <summary>取引擎里的插件目录（并校验引擎目录可用）。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns><c>game/plugins</c> 绝对路径。</returns>
  /// <exception cref="PluginDeployException">引擎目录不可用或缺 <c>plugins/</c>。</exception>
  private static string ResolvePluginsDir(string engineDir)
  {
    if (!EngineLocator.TryValidate(engineDir, out var reason))
    {
      throw new PluginDeployException($"引擎目录不可用：{reason}");
    }

    var pluginsDir = Path.Combine(EngineLocator.GetGameDir(engineDir), PluginsDirName);
    if (!Directory.Exists(pluginsDir))
    {
      throw new PluginDeployException($"引擎目录缺少 {PluginsDirName}/：{pluginsDir}");
    }
    return pluginsDir;
  }

  /// <summary>判定本插件的状态。</summary>
  /// <param name="pluginsDir">引擎的 <c>game/plugins</c> 目录。</param>
  /// <param name="manifest">已解析的插件清单。</param>
  /// <returns>本插件状态。</returns>
  private static RecordingPluginStatus InspectAutocmex(string pluginsDir, JsonArray manifest)
  {
    var dir = Path.Combine(pluginsDir, AutocmexPluginDirName);
    var entryFile = Path.Combine(dir, AutocmexEntryFileName);
    if (!File.Exists(entryFile))
    {
      return new RecordingPluginStatus
      {
        Name = AutocmexPluginDirName,
        State = RecordingPluginState.Missing,
        Installed = false,
        Enabled = false,
        CanInstall = true,
        Detail = Directory.Exists(dir)
          ? $"目录存在但缺少入口文件 {AutocmexEntryFileName}"
          : $"未安装到引擎目录：{dir}",
      };
    }

    var entry = FindEntry(manifest, AutocmexPluginDirName);
    var enabled = EntryEnabled(entry);
    return new RecordingPluginStatus
    {
      Name = AutocmexPluginDirName,
      State = enabled ? RecordingPluginState.Ready : RecordingPluginState.InstalledDisabled,
      Installed = true,
      Enabled = enabled,
      // 自家插件始终可重装：应用升级后旧插件文件需要在同一处「刷新」一次，安装动作本身幂等
      CanInstall = true,
      CanEnable = !enabled,
      Detail = enabled
        ? entry is null
          ? "已就绪（引擎会自动登记并启用）"
          : "已安装并启用"
        : "已安装，但清单里被显式禁用",
    };
  }

  /// <summary>判定第三方弹幕录制器的状态（按目录关键字匹配，第三方插件不提供一键安装）。</summary>
  /// <param name="pluginsDir">引擎的 <c>game/plugins</c> 目录。</param>
  /// <param name="manifest">已解析的插件清单。</param>
  /// <returns>录制器状态。</returns>
  private static RecordingPluginStatus InspectRecorder(string pluginsDir, JsonArray manifest)
  {
    var installed =
      Directory.Exists(pluginsDir)
      && Directory.EnumerateDirectories(pluginsDir, $"*{RecorderPluginKeyword}*").Any();
    if (!installed)
    {
      return new RecordingPluginStatus
      {
        Name = RecorderPluginKeyword,
        State = RecordingPluginState.Missing,
        Installed = false,
        Enabled = false,
        CanInstall = false,
        Detail = "未安装；该插件为第三方插件，需自行获取后放入引擎的 plugins/ 目录",
      };
    }

    var entry = FindEntry(manifest, RecorderPluginKeyword);
    var enabled = EntryEnabled(entry);
    return new RecordingPluginStatus
    {
      Name = RecorderPluginKeyword,
      State = enabled ? RecordingPluginState.Ready : RecordingPluginState.InstalledDisabled,
      Installed = true,
      Enabled = enabled,
      CanEnable = !enabled,
      Detail = enabled
        ? entry is null
          ? "已就绪（引擎会自动登记并启用）"
          : "已安装并启用"
        : "已安装，但清单里被显式禁用",
    };
  }

  /// <summary>造一个「引擎目录不可用」状态。</summary>
  /// <param name="name">插件名。</param>
  /// <param name="reason">引擎目录不可用的原因。</param>
  /// <returns>状态。</returns>
  private static RecordingPluginStatus Unusable(string name, string reason) =>
    new()
    {
      Name = name,
      State = RecordingPluginState.Missing,
      Installed = false,
      Enabled = false,
      CanInstall = false,
      CanEnable = false,
      Detail = string.IsNullOrWhiteSpace(reason) ? "引擎目录不可用" : reason,
    };

  /// <summary>造一个「清单损坏」状态。</summary>
  /// <param name="name">插件名。</param>
  /// <param name="manifestPath">清单路径。</param>
  /// <param name="reason">损坏原因。</param>
  /// <returns>状态。</returns>
  private static RecordingPluginStatus Broken(string name, string manifestPath, string reason) =>
    new()
    {
      Name = name,
      State = RecordingPluginState.ManifestBroken,
      Installed = false,
      Enabled = false,
      CanInstall = false,
      CanEnable = false,
      Detail = $"插件清单无法解析（{reason}），未做任何改动：{manifestPath}",
    };

  /// <summary>只读解析清单：不存在、内容不是数组或读取失败都返回 <c>null</c>。</summary>
  /// <param name="manifestPath">清单路径。</param>
  /// <param name="exists">清单是否已确认存在。</param>
  /// <param name="reason">失败原因（成功时为空串）。</param>
  /// <returns>清单数组；无法解析时为 <c>null</c>。</returns>
  private static JsonArray? TryLoadManifest(string manifestPath, bool exists, out string reason)
  {
    reason = string.Empty;
    if (!exists)
    {
      reason = "文件不存在";
      return null;
    }

    try
    {
      if (JsonNode.Parse(File.ReadAllText(manifestPath)) is JsonArray array)
      {
        return array;
      }
      reason = "内容不是 JSON 数组";
    }
    catch (JsonException ex)
    {
      reason = ex.Message;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      reason = ex.Message;
    }
    return null;
  }

  /// <summary>读取清单用于改写：缺失时按需返回空数组，损坏时直接失败（绝不覆盖用户文件）。</summary>
  /// <param name="manifestPath">清单路径。</param>
  /// <param name="allowMissing">清单缺失时是否允许新建空清单。</param>
  /// <returns>可改写的清单数组。</returns>
  /// <exception cref="PluginDeployException">清单缺失且不允许新建，或清单损坏。</exception>
  private static JsonArray LoadManifestForWrite(string manifestPath, bool allowMissing)
  {
    var exists = File.Exists(manifestPath);
    if (!exists && !allowMissing)
    {
      throw new PluginDeployException($"插件清单不存在：{manifestPath}");
    }

    if (TryLoadManifest(manifestPath, exists, out var reason) is { } manifest)
    {
      return manifest;
    }

    // 文件不存在（允许新建）与内容损坏要分开处理，后者必须拦住
    return exists
      ? throw new PluginDeployException($"插件清单损坏，未做任何改动：{manifestPath}（{reason}）")
      : new JsonArray();
  }

  /// <summary>备份并原子替换清单文件。</summary>
  /// <param name="manifestPath">清单路径。</param>
  /// <param name="manifest">新内容。</param>
  /// <exception cref="PluginDeployException">备份或写入失败；此时原文件保持完好。</exception>
  private static void SaveManifest(string manifestPath, JsonArray manifest)
  {
    var backupPath = manifestPath + BackupSuffix;
    var tempPath = manifestPath + TempSuffix;
    try
    {
      if (File.Exists(manifestPath) && !File.Exists(backupPath))
      {
        File.Copy(manifestPath, backupPath);
      }
      File.WriteAllText(tempPath, manifest.ToJsonString(_writeOptions));
      File.Move(tempPath, manifestPath, overwrite: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      TryDeleteFile(tempPath);
      throw new PluginDeployException(
        $"写入插件清单失败，原文件未改动：{manifestPath}（{ex.Message}）"
      );
    }
  }

  /// <summary>删文件并吞掉异常（用于清理临时文件）。</summary>
  /// <param name="path">文件路径。</param>
  private static void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // 清理失败不影响主流程：残留的临时文件会在下次安装时被覆盖
    }
  }

  /// <summary>按关键字找清单条目（<c>name</c> 或 <c>path</c> 命中即算）。</summary>
  /// <param name="manifest">清单数组。</param>
  /// <param name="keyword">插件目录名关键字。</param>
  /// <returns>命中的条目；未命中为 <c>null</c>。</returns>
  private static JsonObject? FindEntry(JsonArray manifest, string keyword) =>
    manifest
      .OfType<JsonObject>()
      .FirstOrDefault(entry =>
        FieldContains(entry, "name", keyword) || FieldContains(entry, "path", keyword)
      );

  /// <summary>判断条目的某个字符串字段是否含关键字（忽略大小写）。</summary>
  /// <param name="entry">清单条目。</param>
  /// <param name="field">字段名。</param>
  /// <param name="keyword">关键字。</param>
  /// <returns>命中返回 true。</returns>
  private static bool FieldContains(JsonObject entry, string field, string keyword) =>
    entry[field] is JsonValue value
    && value.TryGetValue(out string? text)
    && text is not null
    && text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

  /// <summary>取条目的启用状态：无条目或字段缺失都按引擎默认（启用）处理。</summary>
  /// <param name="entry">清单条目（可为 <c>null</c>）。</param>
  /// <returns>引擎是否会加载该插件。</returns>
  private static bool EntryEnabled(JsonObject? entry) =>
    entry?["enable"] is not JsonValue value || !value.TryGetValue(out bool enabled) || enabled;
}
