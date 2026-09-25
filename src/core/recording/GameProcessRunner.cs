namespace AutoCMEX.Core.Recording;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// 引擎进程的最小抽象：只暴露运行器真正用到的能力，便于单测注入假进程。
/// </summary>
/// <remarks>
/// 单测不能真启游戏进程（需要引擎与 ffmpeg），故运行器一律通过
/// <c>Func&lt;ProcessStartInfo, IEngineProcess&gt;</c> 工厂创建进程。
/// </remarks>
public interface IEngineProcess : IDisposable
{
  /// <summary>进程 Id（仅用于日志）。</summary>
  int Id { get; }

  /// <summary>是否已退出。</summary>
  bool HasExited { get; }

  /// <summary>退出码；进程未退出时取值无意义。</summary>
  int ExitCode { get; }

  /// <summary>启动进程。</summary>
  /// <returns>启动成功返回 true。</returns>
  bool Start();

  /// <summary>读到标准输出结束。</summary>
  /// <returns>进程的全部标准输出。</returns>
  Task<string> ReadStandardOutputAsync();

  /// <summary>读到标准错误结束。</summary>
  /// <returns>进程的全部标准错误。</returns>
  Task<string> ReadStandardErrorAsync();

  /// <summary>等待进程退出。</summary>
  /// <param name="cancellationToken">取消令牌；取消时不再等待（由调用方杀进程）。</param>
  /// <returns>等待任务。</returns>
  Task WaitForExitAsync(CancellationToken cancellationToken);

  /// <summary>杀掉进程树。</summary>
  void KillTree();
}

/// <summary>
/// <see cref="IEngineProcess"/> 的默认实现，包装 <see cref="Process"/>。
/// </summary>
public sealed class EngineProcess : IEngineProcess
{
  private readonly Process _process;

  /// <summary>
  /// 按启动信息创建引擎进程包装。
  /// </summary>
  /// <param name="startInfo">进程启动信息。</param>
  public EngineProcess(ProcessStartInfo startInfo)
  {
    _process = new Process { StartInfo = startInfo };
  }

  /// <inheritdoc />
  public int Id => _process.Id;

  /// <inheritdoc />
  public bool HasExited => _process.HasExited;

  /// <inheritdoc />
  public int ExitCode => _process.ExitCode;

  /// <inheritdoc />
  public bool Start() => _process.Start();

  /// <inheritdoc />
  public Task<string> ReadStandardOutputAsync() => _process.StandardOutput.ReadToEndAsync();

  /// <inheritdoc />
  public Task<string> ReadStandardErrorAsync() => _process.StandardError.ReadToEndAsync();

  /// <inheritdoc />
  public Task WaitForExitAsync(CancellationToken cancellationToken) =>
    _process.WaitForExitAsync(cancellationToken);

  /// <inheritdoc />
  public void KillTree() => _process.Kill(entireProcessTree: true);

  /// <inheritdoc />
  public void Dispose() => _process.Dispose();
}

/// <summary>
/// 一次引擎进程调用的结果（成功、超时、被杀、启动失败都走同一返回对象）。
/// </summary>
/// <param name="StartupError">进程未能启动的原因；为空表示已实际启动。</param>
/// <param name="TimedOut">是否因超时被杀。</param>
/// <param name="Cancelled">是否因调用方取消被杀。</param>
/// <param name="ExitCode">进程退出码；进程未启动时为 <c>null</c>。</param>
/// <param name="StandardOutput">标准输出（超长时保留尾部）。</param>
/// <param name="StandardError">标准错误（超长时保留尾部）。</param>
/// <param name="EngineLogTail">引擎 <c>game/engine.log</c> 的尾部若干行，供失败诊断。</param>
/// <param name="Result">解析成功且 <c>job_id</c> 相符的结果；否则为 <c>null</c>。</param>
/// <param name="ResultError">结果文件层面的问题（缺失/读取失败/解析失败/job_id 不符）。</param>
public sealed record GameProcessOutcome(
  string? StartupError,
  bool TimedOut,
  bool Cancelled,
  int? ExitCode,
  string StandardOutput,
  string StandardError,
  string? EngineLogTail,
  RecordingJobResult? Result,
  string? ResultError
)
{
  /// <summary>进程是否正常跑完（未启动、超时、取消都为 false）。</summary>
  public bool RanToCompletion => StartupError == null && !TimedOut && !Cancelled;
}

/// <summary>
/// 启动 LuaSTG 引擎进程执行一次录制任务，并在退出后回收结果与诊断信息。
/// </summary>
/// <remarks>
/// <para>
/// 启动参数串（见方案 §7.1）固定为：
/// <c>setting.mod='&lt;包名&gt;'; setting.autocmex_job='&lt;任务相对路径&gt;'; setting.showcfg=false; start_game=true; cheat=true</c>。
/// 其中 <c>start_game=true</c> 必带（否则引擎会把 <c>setting.mod</c> 覆盖成启动器），
/// <c>cheat=true</c> 必带（全局无敌开关，否则自机被弹幕撞死会让卡提前结束、产物不完整）。
/// </para>
/// <para>
/// <b>不覆盖</b> <c>setting.resx/resy</c>：分辨率由玩家自己的 <c>userdata/setting.json</c> 决定，
/// 覆盖会改坏画面比例（实测踩过）；GIF 尺寸因此随玩家设置浮动，这是既定口径。
/// </para>
/// <para>
/// 同一引擎目录**不可并发**调用：录制器的产物名是秒级时间戳、临时目录启动时会被清空，
/// 并发会互相破坏。串行化由编排层保证。
/// </para>
/// </remarks>
public sealed class GameProcessRunner
{
  /// <summary>引擎日志文件名（位于 <c>game/</c> 下）。</summary>
  public const string EngineLogFileName = "engine.log";

  /// <summary>失败诊断时回收的引擎日志行数。</summary>
  public const int EngineLogTailLines = 40;

  /// <summary>标准输出/错误各自保留的最大字符数（保留尾部，超长时截断前缀）。</summary>
  public const int MaxCapturedChars = 8000;

  /// <summary>被杀进程等待退出的兜底时长。</summary>
  private static readonly TimeSpan _killWaitTimeout = TimeSpan.FromSeconds(10);

  private readonly ILog _log;
  private readonly Func<ProcessStartInfo, IEngineProcess> _processFactory;

  /// <summary>
  /// 创建引擎进程运行器。
  /// </summary>
  /// <param name="log">日志器。</param>
  /// <param name="processFactory">进程工厂；为空时使用真实进程实现（单测注入假进程）。</param>
  public GameProcessRunner(ILog log, Func<ProcessStartInfo, IEngineProcess>? processFactory = null)
  {
    _log = log ?? throw new ArgumentNullException(nameof(log));
    _processFactory = processFactory ?? (startInfo => new EngineProcess(startInfo));
  }

  /// <summary>
  /// 构造启动参数串（单参数，内容为 Lua 赋值代码）。
  /// </summary>
  /// <param name="modPackName">工程包名（<c>mod/</c> 下的 zip 文件名去掉扩展名）。</param>
  /// <param name="jobRelativePath">任务文件相对 <c>game/</c> 的路径（正斜杠）。</param>
  /// <returns>可直接作为进程参数传入的字符串（已用双引号包裹）。</returns>
  /// <remarks>
  /// 整串用双引号包裹，避免工程包名含空格时被拆成多个参数；Lua 侧用单引号字符串，
  /// 故值里不允许出现单引号或双引号（由 <see cref="TryValidateLaunchValues"/> 拦截）。
  /// </remarks>
  public static string BuildArgumentString(string modPackName, string jobRelativePath)
  {
    var inner =
      $"setting.mod='{modPackName}'; setting.autocmex_job='{jobRelativePath}'; "
      + "setting.showcfg=false; start_game=true; cheat=true";
    return "\"" + inner + "\"";
  }

  /// <summary>
  /// 校验启动参数里嵌入的取值是否安全。
  /// </summary>
  /// <param name="modPackName">工程包名。</param>
  /// <param name="jobRelativePath">任务文件相对路径。</param>
  /// <param name="error">不合法时的原因；合法时为空串。</param>
  /// <returns>合法返回 true。</returns>
  public static bool TryValidateLaunchValues(
    string modPackName,
    string jobRelativePath,
    out string error
  )
  {
    if (string.IsNullOrWhiteSpace(modPackName))
    {
      error = "工程包名为空";
      return false;
    }

    if (string.IsNullOrWhiteSpace(jobRelativePath))
    {
      error = "任务文件路径为空";
      return false;
    }

    foreach (
      var (value, label) in new[] { (modPackName, "工程包名"), (jobRelativePath, "任务文件路径") }
    )
    {
      if (value.IndexOfAny(['\'', '"', '\r', '\n']) >= 0)
      {
        error = $"{label}不能包含单引号、双引号或换行：{value}";
        return false;
      }
    }

    error = string.Empty;
    return true;
  }

  /// <summary>
  /// 启动引擎进程跑完一次任务。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="modPackName">工程包名。</param>
  /// <param name="spec">任务描述（须先由 <see cref="RecordingJobWriter.Write"/> 写盘）。</param>
  /// <param name="timeout">进程总时长上限（须含录制器同步编码 GIF 的时间）。</param>
  /// <param name="cancellationToken">取消令牌；取消会杀掉进程树。</param>
  /// <returns>进程调用结果。</returns>
  public async Task<GameProcessOutcome> RunAsync(
    string engineDir,
    string modPackName,
    RecordingJobSpec spec,
    TimeSpan timeout,
    CancellationToken cancellationToken = default
  )
  {
    if (!EngineLocator.TryValidate(engineDir, out var reason))
    {
      return Failed($"引擎目录不可用：{reason}");
    }

    var exePath = EngineLocator.FindEngineExe(engineDir);
    if (exePath == null)
    {
      return Failed($"未在 game/ 下找到 {EngineLocator.ExePattern}");
    }

    var jobAbsolutePath = RecordingJobWriter.GetJobAbsolutePath(engineDir, spec);
    if (!File.Exists(jobAbsolutePath))
    {
      // 未写盘就启动，插件只会回一个含糊的 "job file not readable"，这里提前给出确切原因
      return Failed($"任务文件不存在：{jobAbsolutePath}");
    }

    var jobRelativePath = RecordingJobWriter.GetJobRelativePath(spec);
    if (!TryValidateLaunchValues(modPackName, jobRelativePath, out var valueError))
    {
      return Failed(valueError);
    }

    var gameDir = EngineLocator.GetGameDir(engineDir);
    var arguments = BuildArgumentString(modPackName, jobRelativePath);
    _log.Print(
      $"GameProcessRunner: {exePath} {arguments} (cwd={gameDir}, timeout={timeout.TotalSeconds:F0}s)"
    );

    RecordingJobWriter.DeleteResultIfExists(engineDir, spec);

    var startInfo = new ProcessStartInfo
    {
      FileName = exePath,
      Arguments = arguments,
      WorkingDirectory = gameDir,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };

    IEngineProcess process;
    try
    {
      process = _processFactory(startInfo);
      if (!process.Start())
      {
        process.Dispose();
        return Failed("进程启动失败");
      }
    }
    catch (Exception ex)
      when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
    {
      return Failed($"进程启动异常：{ex.Message}");
    }

    var stdoutTask = process.ReadStandardOutputAsync();
    var stderrTask = process.ReadStandardErrorAsync();

    var timedOut = false;
    var cancelled = false;
    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
    {
      linked.CancelAfter(timeout);
      try
      {
        await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        cancelled = cancellationToken.IsCancellationRequested;
        timedOut = !cancelled;
        _log.Warn(
          $"GameProcessRunner: {(timedOut ? "超时" : "已取消")}，杀进程树 pid={SafePid(process)}"
        );
        TryKillTree(process);
        await WaitAfterKillAsync(process).ConfigureAwait(false);
      }
    }

    var stdout = Truncate(await SafeReadAsync(stdoutTask).ConfigureAwait(false));
    var stderr = Truncate(await SafeReadAsync(stderrTask).ConfigureAwait(false));

    int? exitCode = null;
    try
    {
      exitCode = process.ExitCode;
    }
    catch (InvalidOperationException)
    {
      // 进程未真正启动或已释放，退出码不可用。
    }

    var (result, resultError) = ReadResult(engineDir, spec);
    var logTail = ReadTailLines(Path.Combine(gameDir, EngineLogFileName), EngineLogTailLines);
    process.Dispose();

    _log.Print(
      $"GameProcessRunner: 结束 job={spec.JobId} exit={exitCode} timedOut={timedOut} "
        + $"cancelled={cancelled} resultError={resultError ?? "-"}"
    );

    return new GameProcessOutcome(
      null,
      timedOut,
      cancelled,
      exitCode,
      stdout,
      stderr,
      logTail,
      result,
      resultError
    );
  }

  /// <summary>构造一个「未启动」的结果。</summary>
  /// <param name="error">失败原因。</param>
  /// <returns>仅带 <see cref="GameProcessOutcome.StartupError"/> 的结果。</returns>
  private GameProcessOutcome Failed(string error)
  {
    _log.Err($"GameProcessRunner: {error}");
    return new GameProcessOutcome(
      error,
      false,
      false,
      null,
      string.Empty,
      string.Empty,
      null,
      null,
      null
    );
  }

  /// <summary>读回并解析结果文件，并校验 <c>job_id</c> 与本次任务一致。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="spec">任务描述。</param>
  /// <returns>结果对象与问题说明（两者必有其一为 null）。</returns>
  private (RecordingJobResult? Result, string? Error) ReadResult(
    string engineDir,
    RecordingJobSpec spec
  )
  {
    var path = RecordingJobWriter.GetResultAbsolutePath(engineDir, spec);
    if (!File.Exists(path))
    {
      return (null, "result_missing");
    }

    RecordingJobResult? result;
    try
    {
      var json = File.ReadAllText(path);
      if (!RecordingJobResult.TryParse(json, out result) || result == null)
      {
        return (null, "result_parse_failed");
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return (null, $"result_read_failed: {ex.Message}");
    }

    // 结果必须回传同一 job_id：进程被杀/异常退出时可能留下上一轮的同名结果。
    if (!string.Equals(result.JobId, spec.JobId, StringComparison.Ordinal))
    {
      return (null, $"job_id_mismatch: 期望 {spec.JobId}，实际 {result.JobId}");
    }

    // 插件侧自报失败（方案 §6.6）：进程可能仍以 0 退出，只有 status 能区分
    if (!string.Equals(result.Status, RecordingJobStatus.Ok, StringComparison.Ordinal))
    {
      var detail = string.IsNullOrWhiteSpace(result.Error)
        ? result.Status
        : $"{result.Status}: {result.Error}";
      return (null, $"status_error: {detail}");
    }

    // 枚举阶段还要求卡表非空（方案 §6.2）：空表无法派生序号，等于没结果
    if (
      spec.Phase == RecordingJobPhase.Enumerate
      && (result.Cards == null || result.Cards.Count == 0)
    )
    {
      return (null, "cards_empty: 插件未枚举到任何卡");
    }

    return (result, null);
  }

  /// <summary>读取文件尾部若干行（流式，避免整文件入内存）。</summary>
  /// <param name="path">文件路径。</param>
  /// <param name="lineCount">保留的行数。</param>
  /// <returns>尾部文本；文件不存在或读取失败返回 <c>null</c>。</returns>
  private static string? ReadTailLines(string path, int lineCount)
  {
    try
    {
      if (!File.Exists(path))
      {
        return null;
      }

      var buffer = new Queue<string>(lineCount + 1);
      foreach (var line in File.ReadLines(path))
      {
        if (buffer.Count == lineCount)
        {
          buffer.Dequeue();
        }

        buffer.Enqueue(line);
      }

      return string.Join(Environment.NewLine, buffer);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return null;
    }
  }

  /// <summary>安全地等待输出读取任务（管道被强制关闭时会抛异常）。</summary>
  /// <param name="readTask">读取任务。</param>
  /// <returns>读到的文本；失败为空串。</returns>
  private static async Task<string> SafeReadAsync(Task<string> readTask)
  {
    try
    {
      return await readTask.ConfigureAwait(false);
    }
    catch (Exception ex)
      when (ex is IOException or ObjectDisposedException or InvalidOperationException)
    {
      return string.Empty;
    }
  }

  /// <summary>超长时保留尾部，避免一次运行刷爆日志。</summary>
  /// <param name="text">原始文本。</param>
  /// <returns>截断后的文本。</returns>
  private static string Truncate(string text)
  {
    if (string.IsNullOrEmpty(text) || text.Length <= MaxCapturedChars)
    {
      return text ?? string.Empty;
    }

    return "…（前段已省略）…" + text[^MaxCapturedChars..];
  }

  /// <summary>杀进程树（引擎会拉起同一进程树内的子进程）。</summary>
  /// <param name="process">目标进程。</param>
  private void TryKillTree(IEngineProcess process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.KillTree();
      }
    }
    catch (Exception ex)
      when (ex
          is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException
      )
    {
      _log.Warn($"GameProcessRunner: 杀进程失败：{ex.Message}");
    }
  }

  /// <summary>杀进程后等待其真正退出（带兜底超时）。</summary>
  /// <param name="process">目标进程。</param>
  /// <returns>等待任务。</returns>
  private async Task WaitAfterKillAsync(IEngineProcess process)
  {
    using var cts = new CancellationTokenSource(_killWaitTimeout);
    try
    {
      await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      _log.Warn("GameProcessRunner: 杀进程后等待退出超时");
    }
    catch (InvalidOperationException)
    {
      // 进程已释放。
    }
  }

  /// <summary>取进程 Id（进程未启动或已释放时返回 0，仅用于日志）。</summary>
  /// <param name="process">目标进程。</param>
  /// <returns>进程 Id 或 0。</returns>
  private static int SafePid(IEngineProcess process)
  {
    try
    {
      return process.Id;
    }
    catch (InvalidOperationException)
    {
      return 0;
    }
  }
}
