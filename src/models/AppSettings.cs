namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// 应用全局配置数据模型
/// </summary>
public class AppSettings
{
  /// <summary>AI 模型配置列表</summary>
  public AutoList<AiModelConfig> AiModels { get; set; } = new();

  /// <summary>当前激活的 AI 模型 ID，对应 AiModels 中某个模型的 Id</summary>
  public AutoValue<string?> ActiveAiModelId { get; set; } = new(default(string?));

  /// <summary>AI 请求超时时间（秒），默认 100</summary>
  public AutoValue<int> AiTimeoutSeconds { get; set; } = new(100);

  /// <summary>WebSocket 监听端口</summary>
  public AutoValue<int> WebSocketPort { get; set; } = new(5140);

  /// <summary>消息筛选模式：strict / ai / strict_then_ai</summary>
  public AutoValue<string> MessageFilterMode { get; set; } = new("strict");

  /// <summary>Koishi 插件安装路径</summary>
  public AutoValue<string> KoishiPluginPath { get; set; } = new(string.Empty);

  /// <summary>是否启用 WebSocket Token 鉴权</summary>
  public AutoValue<bool> WebSocketEnableAuth { get; set; } = new(false);

  /// <summary>WebSocket 鉴权 Token</summary>
  public AutoValue<string> WebSocketAuthToken { get; set; } = new(string.Empty);

  /// <summary>WebSocket 最大并发连接数</summary>
  public AutoValue<int> WebSocketMaxConnections { get; set; } = new(100);

  /// <summary>WebSocket 心跳间隔（毫秒）</summary>
  public AutoValue<int> WebSocketHeartbeatIntervalMs { get; set; } = new(30000);

  /// <summary>WebSocket 心跳超时（毫秒）</summary>
  public AutoValue<int> WebSocketHeartbeatTimeoutMs { get; set; } = new(10000);

  /// <summary>WebSocket 运行模式：Server（默认，等待 Koishi 连接）/ Client（主动连接 Koishi）</summary>
  public AutoValue<string> WebSocketMode { get; set; } = new("Server");

  /// <summary>ws-reserve 模式下 Koishi WebSocket 服务地址（如 ws://localhost:5140）</summary>
  public AutoValue<string> KoishiWebSocketUrl { get; set; } = new(string.Empty);

  /// <summary>当前选中的 Boss 下标，用于共享手动与托管猜测流程的上下文</summary>
  public AutoValue<int> SelectedBossIndex { get; set; } = new(0);

  /// <summary>信息板块的发布目标群聊列表（由设置面板「信息」类别维护）</summary>
  public AutoList<TargetGroup> TargetGroups { get; set; } = new();

  /// <summary>
  /// 恢复关键自动同步属性的非空完整性。
  /// </summary>
  /// <remarks>
  /// 若 <c>app_settings.json</c> 中某字段为显式 <c>null</c>（例如
  /// <c>"activeAiModelId": null</c>），System.Text.Json 反序列化会把对应
  /// <see cref="AutoValue{T}"/>/<see cref="AutoList{T}"/> 属性覆盖为 <c>null</c>，
  /// 导致依赖方（如 <c>GuardingPanel.OnResolved</c>）调用 <c>.Bind()</c> 时抛
  /// <see cref="System.NullReferenceException"/>。此方法在加载后调用，把 null 属性回填为
  /// 构造器默认值，保证单一数据源的自动同步链路始终可用。
  /// </remarks>
  public void EnsureIntegrity()
  {
    AiModels ??= new();
    ActiveAiModelId ??= new(default(string?));
    AiTimeoutSeconds ??= new(100);
    WebSocketPort ??= new(5140);
    MessageFilterMode ??= new("strict");
    KoishiPluginPath ??= new(string.Empty);
    WebSocketEnableAuth ??= new(false);
    WebSocketAuthToken ??= new(string.Empty);
    WebSocketMaxConnections ??= new(100);
    WebSocketHeartbeatIntervalMs ??= new(30000);
    WebSocketHeartbeatTimeoutMs ??= new(10000);
    WebSocketMode ??= new("Server");
    KoishiWebSocketUrl ??= new(string.Empty);
    SelectedBossIndex ??= new(0);
    TargetGroups ??= new();

    foreach (var group in TargetGroups)
    {
      group?.EnsureIntegrity();
    }
  }
}
