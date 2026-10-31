namespace AutoCMEX.Core.Recording;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chickensoft.Log;

/// <summary>沙箱无法建立时抛出（原因直接面向用户，可展示在报告里）。</summary>
public sealed class RecordingSandboxException : Exception
{
  /// <summary>用面向用户的原因构造。</summary>
  /// <param name="message">失败原因。</param>
  public RecordingSandboxException(string message)
    : base(message) { }
}

/// <summary>
/// 一个 worker 独占的「引擎目录副本」（沙箱）：把真实引擎目录按固定清单复制一份，
/// 让每个 worker 在自己的副本里跑录制任务。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须复制而不能共享引擎目录</b>：录制器把 <c>danmaku_recorder/tmp</c> 当进程级工作区
/// （启动即清空）、产物名取秒级 <c>os.time()</c>，多进程共享同一目录会互相删帧、撞名。
/// </para>
/// <para>
/// <b>为什么不用 junction/symlink</b>：引擎资源层会把拼接出的路径与真实路径做大小写比对，
/// 重解析点一解析就报「路径不匹配，存在大小写不一致的部分」，连 <c>core.lua</c> 都加载不到（实测）。
/// 硬链接同样可行但要同卷、需 P/Invoke，且存在写穿真实引擎文件的软隔离风险，故不用。
/// </para>
/// <para>
/// <b>清单是白名单而非「根级全复制」</b>：真实 <c>game/</c> 根下混着用户杂物（旁挂插件 zip、
/// 额外引擎 exe、214 KB 的 <c>engine.log</c>）与陈旧产物（<c>danmaku_recorder/output</c> 里的老 GIF），
/// 全复制既慢又会把无用文件带进沙箱。
/// </para>
/// <para>
/// <c>danmaku_recorder/</c> <b>故意不预建</b>：录制器只在它不存在时才创建 <c>output/</c>，
/// 预建会让 ffmpeg 没有输出目录、录制以 <c>gif_not_produced</c> 失败（实测踩过）。
/// </para>
/// </remarks>
public sealed class RecordingSandbox
{
  /// <summary>沙箱目录名前缀（也用于识别可清理的残留）。</summary>
  public const string DirPrefix = "sb_";

  /// <summary>镜像的 <c>game/</c> 子目录（引擎的资源与插件数据）。</summary>
  private static readonly string[] _mirroredDirNames = { "packages", "plugins", "userdata" };

  /// <summary>必需存在的源目录（缺任一即沙箱不可用）。</summary>
  private static readonly string[] _requiredDirNames = { "packages", "plugins" };

  /// <summary>镜像的 <c>game/</c> 根级固定文件。</summary>
  private static readonly string[] _mirroredRootFileNames =
  {
    EngineLocator.LaunchFileName,
    "config.json",
    "imgui.ini",
  };

  /// <summary>镜像的 <c>game/</c> 根级通配文件（DLL 与引擎版本标记）。</summary>
  private static readonly string[] _mirroredRootPatterns = { "*.dll", "0.*" };

  /// <summary>录制用到的本插件落地目录名（判据与部署动作统一由 <see cref="PluginDeployer"/> 承载）。</summary>
  public const string AutocmexPluginDirName = PluginDeployer.AutocmexPluginDirName;

  /// <summary>录制器的插件目录名关键字（实际为 <c>[pluginpackage]danmaku_recorder_x.y.z</c>）。</summary>
  public const string RecorderPluginKeyword = PluginDeployer.RecorderPluginKeyword;

  /// <summary>沙箱清单里的待录制工程包目录名。</summary>
  public const string ModDirName = "mod";

  /// <summary>残留沙箱的判定时长（名字时间戳早于此值即视为上次崩溃的残留）。</summary>
  public static readonly TimeSpan LeftoverMaxAge = TimeSpan.FromHours(1);

  private readonly ILog _log;

  private RecordingSandbox(ILog log, string engineDir, int workerIndex)
  {
    _log = log;
    EngineDir = engineDir;
    WorkerIndex = workerIndex;
  }

  /// <summary>沙箱根目录（其下有 <c>game/</c>）；可直接当作既有接口里的 <paramref name="engineDir"/> 用。</summary>
  public string EngineDir { get; }

  /// <summary>沙箱内的 <c>game/</c> 目录（进程工作目录）。</summary>
  public string GameDir => EngineLocator.GetGameDir(EngineDir);

  /// <summary>承载该沙箱的 worker 序号（1 基）。</summary>
  public int WorkerIndex { get; }

  /// <summary>默认沙箱根目录（系统临时目录下）。</summary>
  /// <returns>绝对路径。</returns>
  public static string GetDefaultRootDir() =>
    Path.Combine(Path.GetTempPath(), "AutoCMEX", "recording");

  /// <summary>取生效的沙箱根目录：配置为空则用系统临时目录。</summary>
  /// <param name="configuredRoot">配置里的沙箱根目录（可为空）。</param>
  /// <returns>绝对路径。</returns>
  public static string ResolveRootDir(string? configuredRoot) =>
    string.IsNullOrWhiteSpace(configuredRoot) ? GetDefaultRootDir() : configuredRoot!;

  /// <summary>校验沙箱根目录是否可用于起录（只做一次写入探针，不留文件）。</summary>
  /// <param name="configuredRoot">配置里的沙箱根目录（可为空，空即用默认目录）。</param>
  /// <param name="resolvedRoot">生效的沙箱根目录绝对路径。</param>
  /// <param name="reason">不可用的原因（面向用户）；可用时为空串。</param>
  /// <returns>可用返回 true。</returns>
  /// <remarks>
  /// 与配置校验的分工：本方法只判「能不能用来建沙箱」，不决定配置该不该保存。
  /// 配置为空时允许就地创建默认目录（系统临时目录下），用户手选的目录则必须已存在——不替用户造目录。
  /// </remarks>
  public static bool TryValidateRoot(
    string? configuredRoot,
    out string resolvedRoot,
    out string reason
  )
  {
    resolvedRoot = ResolveRootDir(configuredRoot);
    if (!Directory.Exists(resolvedRoot))
    {
      if (!string.IsNullOrWhiteSpace(configuredRoot))
      {
        reason = $"沙箱根目录不存在：{resolvedRoot}";
        return false;
      }

      try
      {
        Directory.CreateDirectory(resolvedRoot);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        reason = $"默认沙箱根目录无法创建：{resolvedRoot}（{ex.Message}）";
        return false;
      }
    }

    var probe = Path.Combine(resolvedRoot, $".autocmex-probe-{Guid.NewGuid():N}");
    try
    {
      File.WriteAllText(probe, string.Empty);
      File.Delete(probe);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      reason = $"沙箱根目录不可写：{resolvedRoot}（{ex.Message}）";
      return false;
    }
    finally
    {
      if (File.Exists(probe))
      {
        try
        {
          File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
          // 探针残留只影响观感，不改变校验结论
        }
      }
    }

    reason = string.Empty;
    return true;
  }

  /// <summary>
  /// 估算一个沙箱的字节数（= 将被复制的内容之和），用于起录前的磁盘空间预检。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="modPackZipPath">待录制工程包路径；空串表示还没有包（设置页的事前估算），按 0 计。</param>
  /// <param name="engineExeFileName">引擎可执行文件名。</param>
  /// <returns>估算字节数；缺失项按 0 计。</returns>
  /// <remarks>
  /// 允许工程包缺省是给设置页用的：用户配上引擎目录时还没有选中工程包，此处的数字只用于
  /// 「够不够」的量级提示，真正起录前的校验（文件是否存在、空间是否够）仍由沙箱创建负责。
  /// </remarks>
  public static long EstimateFootprint(
    string engineDir,
    string modPackZipPath,
    string engineExeFileName
  )
  {
    var gameDir = EngineLocator.GetGameDir(engineDir);
    long total = 0;
    foreach (var name in _mirroredDirNames)
    {
      total += GetDirectorySize(Path.Combine(gameDir, name));
    }
    foreach (var name in _mirroredRootFileNames)
    {
      total += GetFileSize(Path.Combine(gameDir, name));
    }
    foreach (var pattern in _mirroredRootPatterns)
    {
      if (!Directory.Exists(gameDir))
      {
        break;
      }
      foreach (
        var file in Directory.EnumerateFiles(gameDir, pattern, SearchOption.TopDirectoryOnly)
      )
      {
        total += GetFileSize(file);
      }
    }
    total += GetFileSize(Path.Combine(gameDir, engineExeFileName));
    total += GetFileSize(modPackZipPath);
    return total;
  }

  /// <summary>检查沙箱根目录所在卷的剩余空间是否够用。</summary>
  /// <param name="rootDir">沙箱根目录（不必已存在）。</param>
  /// <param name="requiredBytes">所需字节数。</param>
  /// <param name="freeBytes">剩余字节数；探测失败为 -1。</param>
  /// <returns>够用（或无法探测）返回 true。</returns>
  public static bool HasEnoughFreeSpace(string rootDir, long requiredBytes, out long freeBytes)
  {
    freeBytes = -1;
    try
    {
      var pathRoot = Path.GetPathRoot(Path.GetFullPath(rootDir));
      if (string.IsNullOrEmpty(pathRoot))
      {
        return true;
      }
      freeBytes = new DriveInfo(pathRoot).AvailableFreeSpace;
      return freeBytes >= requiredBytes;
    }
    catch (ArgumentException)
    {
      return true;
    }
    catch (IOException)
    {
      return true;
    }
    catch (UnauthorizedAccessException)
    {
      return true;
    }
  }

  /// <summary>清理上次崩溃留下的沙箱（只认自己的 <c>sb_</c> 命名，其余目录一律不碰）。</summary>
  /// <param name="rootDir">沙箱根目录。</param>
  /// <param name="maxAge">超过该时长的残留才清。</param>
  /// <param name="log">日志。</param>
  /// <returns>清掉的沙箱数。</returns>
  public static int SweepLeftovers(string rootDir, TimeSpan maxAge, ILog log)
  {
    if (!Directory.Exists(rootDir))
    {
      return 0;
    }

    var swept = 0;
    var deadline = DateTime.Now - maxAge;
    foreach (var dir in Directory.EnumerateDirectories(rootDir, $"{DirPrefix}*"))
    {
      if (!TryParseCreatedAt(Path.GetFileName(dir), out var createdAt) || createdAt > deadline)
      {
        continue;
      }
      try
      {
        Directory.Delete(dir, recursive: true);
        swept++;
      }
      catch (IOException ex)
      {
        log.Warn($"清理残留沙箱失败：{dir}：{ex.Message}");
      }
      catch (UnauthorizedAccessException ex)
      {
        log.Warn($"清理残留沙箱失败：{dir}：{ex.Message}");
      }
    }
    return swept;
  }

  /// <summary>
  /// 建一个沙箱：按白名单把引擎目录复制一份，并把待录制工程包放进 <c>mod/</c>。
  /// </summary>
  /// <param name="engineDir">真实引擎根目录（只读，全程不改动）。</param>
  /// <param name="modPackZipPath">用户手选的工程包。</param>
  /// <param name="engineExeFileName">引擎可执行文件名（由调用方在真实目录里定位一次，保证各 worker 一致）。</param>
  /// <param name="rootDir">沙箱根目录。</param>
  /// <param name="workerIndex">worker 序号（1 基，进目录名便于排查）。</param>
  /// <param name="log">日志。</param>
  /// <param name="cancellationToken">取消令牌。</param>
  /// <returns>建好的沙箱。</returns>
  /// <exception cref="RecordingSandboxException">源环境不满足清单要求。</exception>
  public static Task<RecordingSandbox> CreateAsync(
    string engineDir,
    string modPackZipPath,
    string engineExeFileName,
    string rootDir,
    int workerIndex,
    ILog log,
    CancellationToken cancellationToken = default
  ) =>
    Task.Run(
      () =>
        Build(
          engineDir,
          modPackZipPath,
          engineExeFileName,
          rootDir,
          workerIndex,
          log,
          cancellationToken
        ),
      cancellationToken
    );

  /// <summary>删除沙箱目录（幂等；失败只记日志，不影响本轮结果）。</summary>
  public void Delete()
  {
    if (!Directory.Exists(EngineDir))
    {
      return;
    }
    try
    {
      Directory.Delete(EngineDir, recursive: true);
    }
    catch (IOException ex)
    {
      _log.Warn($"删除沙箱失败：{EngineDir}：{ex.Message}");
    }
    catch (UnauthorizedAccessException ex)
    {
      _log.Warn($"删除沙箱失败：{EngineDir}：{ex.Message}");
    }
  }

  /// <summary>同步构建沙箱（复制耗时故由 <see cref="CreateAsync"/> 放到线程池）。</summary>
  private static RecordingSandbox Build(
    string engineDir,
    string modPackZipPath,
    string engineExeFileName,
    string rootDir,
    int workerIndex,
    ILog log,
    CancellationToken cancellationToken
  )
  {
    ValidateSource(engineDir, modPackZipPath, engineExeFileName);

    var sourceGame = EngineLocator.GetGameDir(engineDir);
    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
    var sandboxDir = Path.Combine(
      rootDir,
      $"{DirPrefix}{stamp}_{Guid.NewGuid().ToString("N")[..4]}_w{workerIndex}"
    );
    var targetGame = EngineLocator.GetGameDir(sandboxDir);

    try
    {
      Directory.CreateDirectory(targetGame);
      foreach (var name in _mirroredDirNames)
      {
        var source = Path.Combine(sourceGame, name);
        if (Directory.Exists(source))
        {
          MirrorTree(source, Path.Combine(targetGame, name), cancellationToken);
        }
      }

      foreach (var name in _mirroredRootFileNames)
      {
        CopyIfExists(Path.Combine(sourceGame, name), Path.Combine(targetGame, name));
      }
      foreach (var pattern in _mirroredRootPatterns)
      {
        foreach (
          var file in Directory.EnumerateFiles(sourceGame, pattern, SearchOption.TopDirectoryOnly)
        )
        {
          CopyIfExists(file, Path.Combine(targetGame, Path.GetFileName(file)));
        }
      }
      CopyIfExists(
        Path.Combine(sourceGame, engineExeFileName),
        Path.Combine(targetGame, engineExeFileName)
      );

      var modDir = Path.Combine(targetGame, ModDirName);
      Directory.CreateDirectory(modDir);
      File.Copy(modPackZipPath, Path.Combine(modDir, Path.GetFileName(modPackZipPath)), true);

      RecordingJobWriter.EnsureDirectories(sandboxDir);
      cancellationToken.ThrowIfCancellationRequested();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      TryDelete(sandboxDir, log);
      throw new RecordingSandboxException($"建立沙箱失败：{ex.Message}");
    }

    log.Print($"沙箱就绪：worker {workerIndex} → {sandboxDir}");
    return new RecordingSandbox(log, sandboxDir, workerIndex);
  }

  /// <summary>校验源引擎目录具备沙箱清单所需的一切。</summary>
  /// <param name="engineDir">真实引擎根目录。</param>
  /// <param name="modPackZipPath">待录制工程包路径。</param>
  /// <param name="engineExeFileName">引擎可执行文件名。</param>
  /// <exception cref="RecordingSandboxException">缺任一必需项。</exception>
  private static void ValidateSource(
    string engineDir,
    string modPackZipPath,
    string engineExeFileName
  )
  {
    var sourceGame = EngineLocator.GetGameDir(engineDir);
    if (!Directory.Exists(sourceGame))
    {
      throw new RecordingSandboxException($"引擎目录不存在：{sourceGame}");
    }
    if (!File.Exists(Path.Combine(sourceGame, EngineLocator.LaunchFileName)))
    {
      throw new RecordingSandboxException(
        $"引擎目录缺少 {EngineLocator.LaunchFileName}：{sourceGame}"
      );
    }
    if (!File.Exists(Path.Combine(sourceGame, engineExeFileName)))
    {
      throw new RecordingSandboxException(
        $"引擎可执行文件不存在：{Path.Combine(sourceGame, engineExeFileName)}"
      );
    }
    foreach (var name in _requiredDirNames)
    {
      if (!Directory.Exists(Path.Combine(sourceGame, name)))
      {
        throw new RecordingSandboxException($"引擎目录缺少 {name}/：{sourceGame}");
      }
    }

    // 插件可用性（清单存在性、两个插件是否安装并启用）与设置面板共用同一判据，
    // 避免出现「面板显示就绪、起录却被拒」的两套口径
    if (!PluginDeployer.TryValidateForRecording(engineDir, out var pluginReason))
    {
      throw new RecordingSandboxException(pluginReason);
    }
    if (!File.Exists(modPackZipPath))
    {
      throw new RecordingSandboxException($"工程包不存在：{modPackZipPath}");
    }
  }

  /// <summary>递归复制目录（先建全部子目录，保住空目录语义）。</summary>
  private static void MirrorTree(
    string sourceDir,
    string targetDir,
    CancellationToken cancellationToken
  )
  {
    Directory.CreateDirectory(targetDir);
    foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
    {
      Directory.CreateDirectory(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, dir)));
    }
    foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
      cancellationToken.ThrowIfCancellationRequested();
      File.Copy(file, Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file)), true);
    }
  }

  /// <summary>源文件存在才复制。</summary>
  private static void CopyIfExists(string source, string target)
  {
    if (File.Exists(source))
    {
      File.Copy(source, target, true);
    }
  }

  /// <summary>从目录名解析创建时间（名字形如 <c>sb_yyyyMMdd_HHmmss_fff_xxxx_wN</c>）。</summary>
  private static bool TryParseCreatedAt(string dirName, out DateTime createdAt)
  {
    createdAt = default;
    var parts = dirName.Split('_');
    if (parts.Length < 5 || parts[0] != DirPrefix.TrimEnd('_'))
    {
      return false;
    }
    var stamp = $"{parts[1]}_{parts[2]}_{parts[3]}";
    return DateTime.TryParseExact(
      stamp,
      "yyyyMMdd_HHmmss_fff",
      CultureInfo.InvariantCulture,
      DateTimeStyles.None,
      out createdAt
    );
  }

  /// <summary>统计目录字节数（失败按 0 计）。</summary>
  private static long GetDirectorySize(string path)
  {
    if (!Directory.Exists(path))
    {
      return 0;
    }
    try
    {
      return Directory
        .EnumerateFiles(path, "*", SearchOption.AllDirectories)
        .Sum(file => GetFileSize(file));
    }
    catch (IOException)
    {
      return 0;
    }
    catch (UnauthorizedAccessException)
    {
      return 0;
    }
  }

  /// <summary>取文件字节数（路径为空、文件不存在或读取失败一律按 0 计）。</summary>
  private static long GetFileSize(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return 0;
    }

    try
    {
      var info = new FileInfo(path);
      return info.Exists ? info.Length : 0;
    }
    catch (IOException)
    {
      return 0;
    }
    catch (UnauthorizedAccessException)
    {
      return 0;
    }
  }

  /// <summary>尽力删除目录（构建中途失败时用）。</summary>
  private static void TryDelete(string dir, ILog log)
  {
    try
    {
      if (Directory.Exists(dir))
      {
        Directory.Delete(dir, recursive: true);
      }
    }
    catch (IOException ex)
    {
      log.Warn($"清理半成品沙箱失败：{dir}：{ex.Message}");
    }
    catch (UnauthorizedAccessException ex)
    {
      log.Warn($"清理半成品沙箱失败：{dir}：{ex.Message}");
    }
  }
}
