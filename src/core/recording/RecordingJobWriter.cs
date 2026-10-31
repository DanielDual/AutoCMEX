namespace AutoCMEX.Core.Recording;

using System;
using System.IO;
using AutoCMEX.Models;

/// <summary>
/// 生成给游戏侧插件的任务文件，并维护引擎目录下的运行期目录结构。
/// </summary>
/// <remarks>
/// <para>
/// 运行期目录固定为 <c>&lt;引擎&gt;/game/autocmex/{jobs,results,logs}/</c>：任务由 CMEX 写，
/// 结果与插件日志由插件写（路径由任务里的 <c>result_path</c> / <c>log_path</c> 给出）。
/// 三个目录必须由 CMEX 预先建好，插件只负责写文件。
/// </para>
/// <para>
/// 任务文件里的路径一律用正斜杠、相对 <c>game/</c>：引擎进程的工作目录就是 <c>game/</c>，
/// 插件按原样 <c>io.open</c>，正斜杠在两个平台都被接受。
/// </para>
/// </remarks>
public static class RecordingJobWriter
{
  /// <summary>运行期根目录名（相对 <c>game/</c>）。</summary>
  public const string RuntimeDirName = "autocmex";

  /// <summary>任务目录名（相对运行期根目录）。</summary>
  public const string JobsDirName = "jobs";

  /// <summary>结果目录名（相对运行期根目录）。</summary>
  public const string ResultsDirName = "results";

  /// <summary>插件日志目录名（相对运行期根目录）。</summary>
  public const string LogsDirName = "logs";

  /// <summary>取运行期根目录绝对路径。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns><c>&lt;引擎&gt;/game/autocmex</c>。</returns>
  public static string GetRuntimeDir(string engineDir) =>
    Path.Combine(EngineLocator.GetGameDir(engineDir), RuntimeDirName);

  /// <summary>取任务目录绝对路径。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns><c>&lt;引擎&gt;/game/autocmex/jobs</c>。</returns>
  public static string GetJobsDir(string engineDir) =>
    Path.Combine(GetRuntimeDir(engineDir), JobsDirName);

  /// <summary>取结果目录绝对路径。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns><c>&lt;引擎&gt;/game/autocmex/results</c>。</returns>
  public static string GetResultsDir(string engineDir) =>
    Path.Combine(GetRuntimeDir(engineDir), ResultsDirName);

  /// <summary>取插件日志目录绝对路径。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns><c>&lt;引擎&gt;/game/autocmex/logs</c>。</returns>
  public static string GetLogsDir(string engineDir) =>
    Path.Combine(GetRuntimeDir(engineDir), LogsDirName);

  /// <summary>
  /// 取任务文件相对 <c>game/</c> 的路径（正斜杠，可直接填进启动参数）。
  /// </summary>
  /// <param name="spec">任务描述。</param>
  /// <returns>形如 <c>autocmex/jobs/{jobId}.json</c>。</returns>
  /// <remarks>写出与启动两侧共用本函数，避免命名规则在两处各写一遍后漂移。</remarks>
  public static string GetJobRelativePath(RecordingJobSpec spec) =>
    $"{RuntimeDirName}/{JobsDirName}/{spec.JobId}.json";

  /// <summary>取任务文件绝对路径。</summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="spec">任务描述。</param>
  /// <returns>任务文件绝对路径。</returns>
  public static string GetJobAbsolutePath(string engineDir, RecordingJobSpec spec) =>
    Path.Combine(EngineLocator.GetGameDir(engineDir), ToLocalPath(GetJobRelativePath(spec)));

  /// <summary>
  /// 建好运行期三个目录（幂等）。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  public static void EnsureDirectories(string engineDir)
  {
    Directory.CreateDirectory(GetJobsDir(engineDir));
    Directory.CreateDirectory(GetResultsDir(engineDir));
    Directory.CreateDirectory(GetLogsDir(engineDir));
  }

  /// <summary>
  /// 生成本次任务的任务号。
  /// </summary>
  /// <param name="prefix">前缀（如 <c>enum</c> / <c>rec</c>），用于人工辨识。</param>
  /// <returns>形如 <c>rec_20260925_183012_345</c> 的任务号。</returns>
  /// <remarks>
  /// 秒级时间戳不足以区分同一秒内的多次尝试（录制器自身就有产物撞名问题），故带上毫秒，
  /// 保证「同一次运行内的每个任务号唯一」，结果文件不会被上一轮覆盖。
  /// </remarks>
  public static string NewJobId(string prefix) => $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}";

  /// <summary>
  /// 构造并写出枚举阶段的任务。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="jobId">任务号，见 <see cref="NewJobId"/>。</param>
  /// <returns>写出的任务描述（可继续用于取任务/结果文件路径）。</returns>
  /// <exception cref="IOException">任务文件写盘失败（如目录不可写）。</exception>
  /// <exception cref="UnauthorizedAccessException">无写权限。</exception>
  public static RecordingJobSpec WriteEnumerateJob(string engineDir, string jobId)
  {
    var spec = CreateSpec(jobId, RecordingJobPhase.Enumerate);
    Write(engineDir, spec);
    return spec;
  }

  /// <summary>
  /// 构造并写出录制阶段的任务。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="jobId">任务号，见 <see cref="NewJobId"/>。</param>
  /// <param name="absoluteIndex">目标卡在 <c>cards</c> 中的绝对下标（1 基）。</param>
  /// <param name="interval">抽帧间隔（1..60）。</param>
  /// <param name="maxFrame">帧数上限（1..1000）。</param>
  /// <param name="scale">产物放缩比（0.1..1.0）；插件据此调录制器的 <c>set_scale</c>。</param>
  /// <param name="bossClass">Boss 类名；为空则插件自动定位（多候选会报错）。</param>
  /// <returns>写出的任务描述（可继续用于取任务/结果文件路径）。</returns>
  /// <exception cref="IOException">任务文件写盘失败（如目录不可写）。</exception>
  /// <exception cref="UnauthorizedAccessException">无写权限。</exception>
  public static RecordingJobSpec WriteRecordJob(
    string engineDir,
    string jobId,
    int absoluteIndex,
    int interval,
    int maxFrame,
    double scale,
    string? bossClass = null
  )
  {
    var spec = CreateSpec(jobId, RecordingJobPhase.Record);
    spec.BossClass = string.IsNullOrWhiteSpace(bossClass) ? null : bossClass;
    spec.AbsoluteIndex = absoluteIndex;
    spec.Interval = interval;
    spec.MaxFrame = maxFrame;
    spec.Scale = scale;
    // 前一阶段即 60 帧入场移动，不含任何台词，故固定不演前序阶段。
    spec.IncludePrevious = false;
    Write(engineDir, spec);
    return spec;
  }

  /// <summary>
  /// 删除上一次遗留的结果文件（若存在）。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="spec">任务描述。</param>
  /// <remarks>
  /// 任务号每次唯一，理论上不会读到陈旧结果；这里仍显式清除，避免「插件在写出结果前退出」
  /// 时把上一轮的同名结果当成本次结果。
  /// </remarks>
  public static void DeleteResultIfExists(string engineDir, RecordingJobSpec spec)
  {
    var absolutePath = Path.Combine(
      EngineLocator.GetGameDir(engineDir),
      ToLocalPath(spec.ResultPath)
    );
    if (File.Exists(absolutePath))
    {
      File.Delete(absolutePath);
    }
  }

  /// <summary>
  /// 读回结果文件的绝对路径。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="spec">任务描述。</param>
  /// <returns>结果文件绝对路径。</returns>
  public static string GetResultAbsolutePath(string engineDir, RecordingJobSpec spec) =>
    Path.Combine(EngineLocator.GetGameDir(engineDir), ToLocalPath(spec.ResultPath));

  /// <summary>
  /// 构造任务骨架（仅任务号与阶段，结果/日志路径按任务号派生）。
  /// </summary>
  /// <param name="jobId">任务号。</param>
  /// <param name="phase">阶段，取 <see cref="RecordingJobPhase"/> 之一。</param>
  /// <returns>任务描述（尚未写盘）。</returns>
  private static RecordingJobSpec CreateSpec(string jobId, string phase) =>
    new()
    {
      JobId = jobId,
      Phase = phase,
      ResultPath = $"{RuntimeDirName}/{ResultsDirName}/{jobId}.json",
      LogPath = $"{RuntimeDirName}/{LogsDirName}/{jobId}.log",
    };

  /// <summary>
  /// 把任务写到 <c>&lt;引擎&gt;/game/autocmex/jobs/{jobId}.json</c>。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="spec">任务描述。</param>
  private static void Write(string engineDir, RecordingJobSpec spec)
  {
    EnsureDirectories(engineDir);

    File.WriteAllText(GetJobAbsolutePath(engineDir, spec), spec.ToJson());
  }

  /// <summary>把任务里的正斜杠相对路径转成本机路径分隔符。</summary>
  /// <param name="relativePath">正斜杠相对路径。</param>
  /// <returns>本机分隔符路径。</returns>
  private static string ToLocalPath(string relativePath) =>
    relativePath.Replace('/', Path.DirectorySeparatorChar);
}
