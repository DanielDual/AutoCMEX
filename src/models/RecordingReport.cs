namespace AutoCMEX.Models;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>录制进度所处的阶段（供录制对话框显示）。</summary>
public static class RecordingStage
{
  /// <summary>枚举卡表阶段。</summary>
  public const string Enumerate = "枚举中";

  /// <summary>逐卡录制阶段。</summary>
  public const string Record = "录制中";

  /// <summary>归集产物与写报告阶段。</summary>
  public const string Collect = "归集中";
}

/// <summary>单张卡在报告里的状态。</summary>
public static class RecordingCardStatus
{
  /// <summary>已录成并落到输出目录。</summary>
  public const string Ok = "ok";

  /// <summary>尝试过并失败（报告里带原因）。</summary>
  public const string Failed = "failed";

  /// <summary>没有开始录（取消、或承载它的 worker 未启动）。</summary>
  public const string Unrecorded = "unrecorded";
}

/// <summary>
/// 一轮录制的输入。
/// </summary>
/// <param name="EngineDir">用户选定的引擎根目录（其下须有 <c>game/</c>）。</param>
/// <param name="ModPackZipPath">用户手选的工程包 <c>.zip</c> 路径（只复制进沙箱，不改动引擎目录）。</param>
/// <param name="OutputDir">GIF 集输出目录（不存在时创建；同名产物被覆盖）。</param>
/// <param name="Config">录制配置（并行度、沙箱根、帧数上限与两档抽帧间隔）。</param>
public sealed record RecordingRequest(
  string EngineDir,
  string ModPackZipPath,
  string OutputDir,
  RecordingConfig Config
);

/// <summary>
/// 一轮录制的结果（编排层唯一出口）。
/// </summary>
/// <param name="Error">
/// 前置失败原因（引擎不可用、沙箱建不起来、枚举失败、没有可录的卡等）；成功为 <c>null</c>。
/// 逐卡失败不体现在这里，而在 <see cref="Report"/> 的卡片记录里。
/// </param>
/// <param name="Report">本轮报告（已落盘到输出目录）。</param>
/// <param name="OutputDir">GIF 集输出目录。</param>
/// <param name="Warning">非致命异常提示（如并行度因磁盘空间被压缩）；无则为 <c>null</c>。</param>
/// <param name="EngineLogTail">前置失败时的 <c>engine.log</c> 尾部，用于展示原因。</param>
public sealed record RecordingRunResult(
  string? Error,
  RecordingReport Report,
  string OutputDir,
  string? Warning = null,
  string? EngineLogTail = null
)
{
  /// <summary>本轮是否跑完（不等于每张卡都录成，逐卡结果看 <see cref="RecordingReport.Cards"/>）。</summary>
  public bool Succeeded => Error == null;

  /// <summary>是否被取消（已归集的产物保留）。</summary>
  public bool Cancelled => Report.Cancelled;
}

/// <summary>某个 worker 正在录的卡（进度快照的一项）。</summary>
/// <param name="WorkerIndex">worker 序号（1 基）。</param>
/// <param name="CombatOrdinal">战斗阶段序号（1 基）。</param>
/// <param name="EntryName">清单名（符卡名或「普通攻击 N」）。</param>
/// <param name="Attempt">当前尝试序号（1 = 首次；2 = 截断后换挡重录）。</param>
public sealed record RecordingWorkerProgress(
  int WorkerIndex,
  int CombatOrdinal,
  string EntryName,
  int Attempt
);

/// <summary>
/// 一次进度上报的不可变快照。
/// </summary>
/// <param name="Stage">当前阶段，取 <see cref="RecordingStage"/> 之一。</param>
/// <param name="Total">本轮战斗卡总数。</param>
/// <param name="Completed">已成功落盘的卡数。</param>
/// <param name="Failed">已判定失败的卡数。</param>
/// <param name="Running">各 worker 正在录的卡（空闲的 worker 不在其中）。</param>
public sealed record RecordingProgress(
  string Stage,
  int Total,
  int Completed,
  int Failed,
  IReadOnlyList<RecordingWorkerProgress> Running
);

/// <summary>
/// 一张卡在报告里的记录（落盘 <c>recording_report.json</c> 的 <c>cards</c> 项）。
/// </summary>
public sealed class RecordingCardReport
{
  /// <summary>战斗阶段序号（1 基），即集内文件名的主干。</summary>
  [JsonPropertyName("combat_ordinal")]
  public int CombatOrdinal { get; set; }

  /// <summary>在插件卡表里的绝对下标（1 基，含对话阶段）。</summary>
  [JsonPropertyName("absolute_index")]
  public int AbsoluteIndex { get; set; }

  /// <summary>清单名：符卡名或「普通攻击 N」。</summary>
  [JsonPropertyName("entry_name")]
  public string EntryName { get; set; } = string.Empty;

  /// <summary>集内文件名（如 <c>3.gif</c>）；未录制时为空串。</summary>
  [JsonPropertyName("file_name")]
  public string FileName { get; set; } = string.Empty;

  /// <summary>产物宽（像素，录成后读 GIF 头得到）；未录成时为 0。</summary>
  [JsonPropertyName("width")]
  public int Width { get; set; }

  /// <summary>产物高（像素）；未录成时为 0。</summary>
  [JsonPropertyName("height")]
  public int Height { get; set; }

  /// <summary>采用产物的帧数。</summary>
  [JsonPropertyName("frames")]
  public int Frames { get; set; }

  /// <summary>采用产物的帧率（GIF 帧率 = 60 / 抽帧间隔）。</summary>
  [JsonPropertyName("fps")]
  public double Fps { get; set; }

  /// <summary>采用产物的时长（秒）= 帧数 × 抽帧间隔 ÷ 60。</summary>
  [JsonPropertyName("duration_seconds")]
  public double DurationSeconds { get; set; }

  /// <summary>是否完整录完；凡触发换挡重录的卡一律为 <c>false</c>（保守口径）。</summary>
  [JsonPropertyName("complete")]
  public bool Complete { get; set; }

  /// <summary>区间尝试次数（1 = 首次即录完；2 = 首次截断后重录）。</summary>
  [JsonPropertyName("attempts")]
  public int Attempts { get; set; }

  /// <summary>实际启动引擎进程的次数（含单次尝试内部的失败重试）。</summary>
  [JsonPropertyName("runs")]
  public int Runs { get; set; }

  /// <summary>承载这张卡的 worker 序号（1 基）；未录制时为 0。</summary>
  [JsonPropertyName("worker")]
  public int Worker { get; set; }

  /// <summary>状态，取 <see cref="RecordingCardStatus"/> 之一。</summary>
  [JsonPropertyName("status")]
  public string Status { get; set; } = RecordingCardStatus.Unrecorded;

  /// <summary>失败原因；成功为 <c>null</c>。</summary>
  [JsonPropertyName("error")]
  public string? Error { get; set; }

  /// <summary>失败时的 <c>engine.log</c> 尾部（便于定位插件侧异常）。</summary>
  [JsonPropertyName("engine_log_tail")]
  public string? EngineLogTail { get; set; }
}

/// <summary>
/// 一轮录制的报告（落盘 <c>&lt;输出目录&gt;/recording_report.json</c>）。
/// </summary>
/// <remarks>
/// <see cref="Cards"/> 按战斗序号升序，与并行完成顺序无关；只有它记录了「哪张卡成了、
/// 哪张没成、为什么」，故它既是给用户看的结果，也是排查问题的第一手材料。
/// </remarks>
public sealed class RecordingReport
{
  /// <summary>写盘用的序列化选项（缩进便于人工排查）。</summary>
  private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

  /// <summary>工程包名（<c>.zip</c> 去扩展名，即传给引擎的 <c>setting.mod</c>）。</summary>
  [JsonPropertyName("mod_pack")]
  public string ModPackName { get; set; } = string.Empty;

  /// <summary>Boss 显示名。</summary>
  [JsonPropertyName("boss_name")]
  public string? BossName { get; set; }

  /// <summary>Boss 类名。</summary>
  [JsonPropertyName("boss_class")]
  public string? BossClass { get; set; }

  /// <summary>本轮开始时间（ISO 8601）。</summary>
  [JsonPropertyName("generated_at")]
  public string GeneratedAt { get; set; } = string.Empty;

  /// <summary>本次使用的并行度（实际生效的 worker 数可能更小，见 <see cref="WorkersStarted"/>）。</summary>
  [JsonPropertyName("parallelism")]
  public int Parallelism { get; set; }

  /// <summary>实际启动的 worker 数（= 实际建的沙箱数）。</summary>
  [JsonPropertyName("workers")]
  public int WorkersStarted { get; set; }

  /// <summary>是否被取消（已落盘的产物保留）。</summary>
  [JsonPropertyName("cancelled")]
  public bool Cancelled { get; set; }

  /// <summary>战斗卡总数。</summary>
  [JsonPropertyName("total_combat")]
  public int TotalCombat { get; set; }

  /// <summary>成功落盘的卡数。</summary>
  [JsonPropertyName("succeeded")]
  public int Succeeded { get; set; }

  /// <summary>失败的卡数（不含未录制）。</summary>
  [JsonPropertyName("failed")]
  public int Failed { get; set; }

  /// <summary>逐卡记录（按战斗序号升序）。</summary>
  [JsonPropertyName("cards")]
  public List<RecordingCardReport> Cards { get; set; } = new();

  /// <summary>序列化为 JSON 文本。</summary>
  /// <returns>可直接写盘的 JSON 文本。</returns>
  public string ToJson() => JsonSerializer.Serialize(this, _writeOptions);
}
