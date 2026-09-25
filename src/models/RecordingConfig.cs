namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// GIF 录制模块的持久化配置（落盘 <c>recording_config.json</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="InfoConfig"/> 同构：由 <c>DataManager</c> 统一加载与保存，复用其防抖自动保存
/// 链路，不另起持久化路径。属性用 <see cref="AutoValue{T}"/> 包装，便于设置面板以
/// <c>Bind().OnValue()</c> 驱动刷新。
/// </para>
/// <para>
/// 「引擎目录」指含 <c>game/</c> 与 <c>doc/</c> 的 LuaSTG 根目录（可执行文件在 <c>game/</c> 内），
/// 与合并板块的 Sharp 编辑器目录不是同一个概念，故单独配置。
/// </para>
/// </remarks>
public class RecordingConfig
{
  /// <summary>LuaSTG 引擎根目录绝对路径；为空表示未设置。</summary>
  public AutoValue<string> EngineDir { get; set; } = new(string.Empty);

  /// <summary>单次录制的帧数上限（传给录制器 <c>set_max_frame</c>，录制器自身可接受 1..1000）。</summary>
  public AutoValue<int> MaxFrame { get; set; } = new(350);

  /// <summary>首次尝试的抽帧间隔（1..60）；GIF 帧率 = 60 / 该值，默认 3 即 20 fps。</summary>
  public AutoValue<int> FirstInterval { get; set; } = new(3);

  /// <summary>首次录满被截断后重录所用的抽帧间隔，默认 5 即 12 fps。</summary>
  public AutoValue<int> SecondInterval { get; set; } = new(5);

  /// <summary>上次选择的输出目录，用作下次对话框的默认值。</summary>
  public AutoValue<string> LastOutputDir { get; set; } = new(string.Empty);

  /// <summary>
  /// 恢复被 JSON 显式 <c>null</c> 覆盖的自动同步属性。
  /// </summary>
  /// <remarks>
  /// 与 <see cref="InfoConfig.EnsureIntegrity"/> 同理：反序列化会把显式 <c>null</c> 写入
  /// <see cref="AutoValue{T}"/> 属性，令依赖方调用 <c>Bind()</c> 时抛
  /// <see cref="System.NullReferenceException"/>，加载后必须回填默认值。
  /// </remarks>
  public void EnsureIntegrity()
  {
    EngineDir ??= new(string.Empty);
    MaxFrame ??= new(350);
    FirstInterval ??= new(3);
    SecondInterval ??= new(5);
    LastOutputDir ??= new(string.Empty);
  }
}
