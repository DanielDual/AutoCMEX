namespace AutoCMEX.Models;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 录制任务阶段（与游戏侧插件 <c>job.lua</c> 的 <c>phase</c> 取值一一对应）。
/// </summary>
public static class RecordingJobPhase
{
  /// <summary>枚举阶段：读出 Boss 卡表与每张卡的名字/是否符卡/时长。</summary>
  public const string Enumerate = "enumerate";

  /// <summary>录制阶段：跳到指定绝对下标的卡并录 GIF。</summary>
  public const string Record = "record";
}

/// <summary>插件写回的结果状态。</summary>
public static class RecordingJobStatus
{
  /// <summary>正常完成（录制阶段仍可能 <c>complete=false</c>）。</summary>
  public const string Ok = "ok";

  /// <summary>插件侧判定失败，详见 <see cref="RecordingJobResult.Error"/>。</summary>
  public const string Error = "error";
}

/// <summary>
/// 写给游戏侧插件的任务描述（落盘 <c>&lt;引擎&gt;/game/autocmex/jobs/{jobId}.json</c>）。
/// </summary>
/// <remarks>
/// 键名固定为 snake_case，与插件 <c>job.lua</c> 的读取键一一对应；路径类字段一律相对
/// <c>game/</c>（进程工作目录），避免绝对路径与转义问题。
/// </remarks>
public sealed class RecordingJobSpec
{
  /// <summary>写盘用的序列化选项（缩进便于人工排查）。</summary>
  private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

  /// <summary>任务标识；插件会把它原样回写到结果文件，供 CMEX 校验是否为本次结果。</summary>
  [JsonPropertyName("job_id")]
  public string JobId { get; set; } = string.Empty;

  /// <summary>阶段，取 <see cref="RecordingJobPhase"/> 之一。</summary>
  [JsonPropertyName("phase")]
  public string Phase { get; set; } = RecordingJobPhase.Enumerate;

  /// <summary>结果文件相对路径（相对 <c>game/</c>）。</summary>
  [JsonPropertyName("result_path")]
  public string ResultPath { get; set; } = string.Empty;

  /// <summary>插件诊断日志相对路径（相对 <c>game/</c>）。</summary>
  [JsonPropertyName("log_path")]
  public string LogPath { get; set; } = string.Empty;

  /// <summary>Boss 类名；为空时由插件在 <c>_editor_class</c> 中自动定位（多候选则报错）。</summary>
  [JsonPropertyName("boss_class")]
  public string? BossClass { get; set; }

  /// <summary>目标卡在 <c>cards</c> 中的绝对下标（1 基，含对话阶段）；仅录制阶段需要。</summary>
  [JsonPropertyName("absolute_index")]
  public int? AbsoluteIndex { get; set; }

  /// <summary>抽帧间隔（录制阶段）；为空时插件用默认值 3。</summary>
  [JsonPropertyName("interval")]
  public int? Interval { get; set; }

  /// <summary>帧数上限（录制阶段）；为空时插件用默认值 350（与 <see cref="RecordingConfig.MaxFrame"/> 一致）。</summary>
  [JsonPropertyName("max_frame")]
  public int? MaxFrame { get; set; }

  /// <summary>是否把目标卡的前一阶段一并演（默认 false：前一阶段即 60 帧入场移动）。</summary>
  [JsonPropertyName("include_previous")]
  public bool IncludePrevious { get; set; }

  /// <summary>序列化为 JSON 文本。</summary>
  /// <returns>可直接写盘的 JSON 文本。</returns>
  public string ToJson() => JsonSerializer.Serialize(this, _writeOptions);
}

/// <summary>
/// 插件写回的结果（<c>&lt;引擎&gt;/game/autocmex/results/{jobId}.json</c>）。
/// </summary>
/// <remarks>
/// 两个阶段共用同一模型：枚举阶段只填 <see cref="Cards"/> 等字段，录制阶段追加产物相关字段。
/// 全部可空，以便区分「字段缺失」与「字段为假」，由调用方按阶段校验。
/// </remarks>
public sealed class RecordingJobResult
{
  /// <summary>读取结果用的序列化选项（键名大小写不敏感，容忍人工改动）。</summary>
  private static readonly JsonSerializerOptions _readOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  /// <summary>任务标识；调用方必须校验它与本次任务一致，避免读到历史结果。</summary>
  [JsonPropertyName("job_id")]
  public string JobId { get; set; } = string.Empty;

  /// <summary>状态，取 <see cref="RecordingJobStatus"/> 之一。</summary>
  [JsonPropertyName("status")]
  public string Status { get; set; } = string.Empty;

  /// <summary>失败原因（插件侧错误码或异常文本）。</summary>
  [JsonPropertyName("error")]
  public string? Error { get; set; }

  /// <summary>非致命告警（如插件补设了无敌开关）。</summary>
  [JsonPropertyName("warning")]
  public string? Warning { get; set; }

  /// <summary>Boss 显示名（枚举阶段填）。</summary>
  [JsonPropertyName("boss_name")]
  public string? BossName { get; set; }

  /// <summary>Boss 类名。</summary>
  [JsonPropertyName("boss_class")]
  public string? BossClass { get; set; }

  /// <summary>卡表（枚举阶段填）。</summary>
  [JsonPropertyName("cards")]
  public List<RecordingCardInfo>? Cards { get; set; }

  /// <summary>本次录制的目标卡绝对下标（录制阶段填）。</summary>
  [JsonPropertyName("absolute_index")]
  public int? AbsoluteIndex { get; set; }

  /// <summary>目标卡名字（录制阶段填；非符为空串）。</summary>
  [JsonPropertyName("card_name")]
  public string? CardName { get; set; }

  /// <summary>录制器产物标识（即 GIF 文件名去掉扩展名）。</summary>
  [JsonPropertyName("task_name")]
  public string? TaskName { get; set; }

  /// <summary>GIF 相对路径（相对 <c>game/</c>）。</summary>
  [JsonPropertyName("gif_path")]
  public string? GifPath { get; set; }

  /// <summary>实际录制的帧数。</summary>
  [JsonPropertyName("frames")]
  public int? Frames { get; set; }

  /// <summary>本次使用的抽帧间隔。</summary>
  [JsonPropertyName("interval")]
  public int? Interval { get; set; }

  /// <summary>本次使用的帧数上限。</summary>
  [JsonPropertyName("max_frame")]
  public int? MaxFrame { get; set; }

  /// <summary>是否录满（<c>frames &gt;= max_frame</c> 即为 true，表示卡被截断而非演完）。</summary>
  [JsonPropertyName("complete")]
  public bool? Complete { get; set; }

  /// <summary>录制器自报的编码是否成功。</summary>
  [JsonPropertyName("success")]
  public bool? Success { get; set; }

  /// <summary>GIF 字节数。</summary>
  [JsonPropertyName("size")]
  public long? Size { get; set; }

  /// <summary>
  /// 解析结果 JSON。
  /// </summary>
  /// <param name="json">结果文件内容。</param>
  /// <param name="result">解析成功时的结果对象；失败为 <c>null</c>。</param>
  /// <returns>解析成功返回 true。</returns>
  public static bool TryParse(string json, out RecordingJobResult? result)
  {
    try
    {
      result = JsonSerializer.Deserialize<RecordingJobResult>(json, _readOptions);
      return result != null;
    }
    catch (JsonException)
    {
      result = null;
      return false;
    }
  }
}

/// <summary>卡表中的一个阶段项（枚举阶段的产出）。</summary>
public sealed class RecordingCardInfo
{
  /// <summary>在 <c>_editor_class[boss].cards</c> 中的绝对下标（1 基，含对话阶段）。</summary>
  [JsonPropertyName("absolute_index")]
  public int AbsoluteIndex { get; set; }

  /// <summary>阶段名字；非符与入场移动为空串。</summary>
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  /// <summary>是否符卡（名字非空）。</summary>
  [JsonPropertyName("is_sc")]
  public bool IsSpellCard { get; set; }

  /// <summary>是否战斗阶段（非符或符卡为 true，对话为 false）。</summary>
  [JsonPropertyName("is_combat")]
  public bool IsCombat { get; set; }

  /// <summary>最长时长（秒）；对话阶段为 0。</summary>
  [JsonPropertyName("t3")]
  public double T3Seconds { get; set; }
}
