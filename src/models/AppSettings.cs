namespace AutoCMEX.Models;

using System;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 应用全局配置数据模型
/// </summary>
public class AppSettings
{
  /// <summary>AI 模型配置列表</summary>
  public AutoList<AiModelConfig> AiModels { get; set; } = new();

  private string? _activeAiModelId;

  /// <summary>当前激活的 AI 模型 ID，对应 AiModels 中某个模型的 Id</summary>
  public string? ActiveAiModelId
  {
    get => _activeAiModelId;
    set
    {
      _activeAiModelId = value;
      OnPropertyChanged();
    }
  }

  /// <summary>AI 请求超时时间（秒），默认 100</summary>
  public int AiTimeoutSeconds { get; set; } = 100;

  private int _webSocketPort = 5140;

  /// <summary>WebSocket 监听端口</summary>
  public int WebSocketPort
  {
    get => _webSocketPort;
    set
    {
      _webSocketPort = value;
      OnPropertyChanged();
    }
  }

  /// <summary>消息筛选模式：strict / ai / strict_then_ai</summary>
  public string MessageFilterMode { get; set; } = "strict";

  /// <summary>Koishi 插件安装路径</summary>
  public string KoishiPluginPath { get; set; } = string.Empty;

  /// <summary>是否启用 WebSocket Token 鉴权</summary>
  public bool WebSocketEnableAuth { get; set; } = false;

  /// <summary>WebSocket 鉴权 Token</summary>
  public string WebSocketAuthToken { get; set; } = string.Empty;

  /// <summary>WebSocket 最大并发连接数</summary>
  public int WebSocketMaxConnections { get; set; } = 100;

  /// <summary>WebSocket 心跳间隔（毫秒）</summary>
  public int WebSocketHeartbeatIntervalMs { get; set; } = 30000;

  /// <summary>WebSocket 心跳超时（毫秒）</summary>
  public int WebSocketHeartbeatTimeoutMs { get; set; } = 10000;

  private string _webSocketMode = "Server";

  /// <summary>WebSocket 运行模式：Server（默认，等待 Koishi 连接）/ Client（主动连接 Koishi）</summary>
  public string WebSocketMode
  {
    get => _webSocketMode;
    set
    {
      _webSocketMode = value;
      OnPropertyChanged();
    }
  }

  private string _koishiWebSocketUrl = string.Empty;

  /// <summary>ws-reserve 模式下 Koishi WebSocket 服务地址（如 ws://localhost:5140）</summary>
  public string KoishiWebSocketUrl
  {
    get => _koishiWebSocketUrl;
    set
    {
      _koishiWebSocketUrl = value;
      OnPropertyChanged();
    }
  }

  /// <summary>当前选中的 Boss 下标，用于共享手动与托管猜测流程的上下文</summary>
  public int SelectedBossIndex { get; set; } = 0;

  /// <summary>属性变更事件（参数：属性名）</summary>
  public event Action<string>? PropertyChanged;

  private void OnPropertyChanged(
    [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null
  )
  {
    PropertyChanged?.Invoke(propertyName ?? string.Empty);
  }
}
