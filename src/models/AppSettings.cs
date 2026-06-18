namespace AutoCMEX.Models;

using System.Collections.Generic;

/// <summary>
/// 应用全局配置数据模型
/// </summary>
public class AppSettings
{
  /// <summary>AI 模型配置列表</summary>
  public List<AiModelConfig> AiModels { get; set; } = new();

  /// <summary>当前激活的 AI 模型 ID，对应 AiModels 中某个模型的 Id</summary>
  public string? ActiveAiModelId { get; set; }

  /// <summary>AI 请求超时时间（秒），默认 100</summary>
  public int AiTimeoutSeconds { get; set; } = 100;

  /// <summary>WebSocket 监听端口</summary>
  public int WebSocketPort { get; set; } = 5140;

  /// <summary>消息筛选模式：strict / ai / strict_then_ai</summary>
  public string MessageFilterMode { get; set; } = "strict";

  /// <summary>Koishi 插件安装路径</summary>
  public string KoishiPluginPath { get; set; } = string.Empty;
}
