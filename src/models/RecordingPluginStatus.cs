namespace AutoCMEX.Models;

/// <summary>录制所需插件在引擎目录里的部署状态。</summary>
/// <remarks>
/// 由 <c>PluginDeployer.Inspect</c> 判定，只读查询、不写盘，供设置面板渲染状态与按钮可用性。
/// </remarks>
public enum RecordingPluginState
{
  /// <summary>已安装且已启用，录制可用。</summary>
  Ready,

  /// <summary>已安装，但在 <c>plugins.json</c> 里被显式禁用（引擎不会加载它）。</summary>
  InstalledDisabled,

  /// <summary>未安装到引擎目录。</summary>
  Missing,

  /// <summary><c>plugins.json</c> 存在但无法解析，此时任何部署动作都不该执行（避免覆盖用户文件）。</summary>
  ManifestBroken,
}

/// <summary>单个插件的部署状态快照。</summary>
public sealed class RecordingPluginStatus
{
  /// <summary>插件名（目录名关键字），用于文案与排查。</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>部署状态。</summary>
  public RecordingPluginState State { get; init; } = RecordingPluginState.Missing;

  /// <summary>面向用户的说明（缺失原因、入口文件情况等）。</summary>
  public string Detail { get; init; } = string.Empty;

  /// <summary>是否已安装（目录与入口文件已在引擎目录里）。</summary>
  public bool Installed { get; init; }

  /// <summary>引擎是否会加载它（<c>plugins.json</c> 未禁用或无条目）。</summary>
  public bool Enabled { get; init; }

  /// <summary>是否可执行「一键安装」（仅自带插件；第三方插件永远为 false）。</summary>
  public bool CanInstall { get; init; }

  /// <summary>是否可执行「一键启用」（仅「已安装但被显式禁用」时为 true）。</summary>
  public bool CanEnable { get; init; }
}

/// <summary>一次插件部署检查的汇总结果。</summary>
public sealed class RecordingPluginInspection
{
  /// <summary>引擎目录是否通过 <c>EngineLocator.TryValidate</c>。</summary>
  public bool EngineDirUsable { get; init; }

  /// <summary>被检查的引擎根目录（原样回传，可能为空）。</summary>
  public string EngineDir { get; init; } = string.Empty;

  /// <summary>插件的 <c>plugins.json</c> 绝对路径（引擎目录不可用时为空）。</summary>
  public string ManifestPath { get; init; } = string.Empty;

  /// <summary><c>plugins.json</c> 是否存在。</summary>
  public bool ManifestExists { get; init; }

  /// <summary>本插件（<c>autocmex</c>）的状态。</summary>
  public RecordingPluginStatus Autocmex { get; init; } = new();

  /// <summary>第三方弹幕录制器（<c>danmaku_recorder</c>）的状态。</summary>
  public RecordingPluginStatus Recorder { get; init; } = new();

  /// <summary>两个插件都就绪（可起录）。</summary>
  public bool AllReady =>
    Autocmex.State == RecordingPluginState.Ready && Recorder.State == RecordingPluginState.Ready;
}
