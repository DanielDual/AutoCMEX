namespace AutoCMEX.Models;

using System.Collections.Generic;

/// <summary>
/// 应用全局配置数据模型
/// </summary>
public class AppSettings
{
    /// <summary>AI 模型配置列表</summary>
    public List<AiModelConfig> AiModels { get; set; } = new();

    /// <summary>WebSocket 监听端口</summary>
    public int WebSocketPort { get; set; } = 5140;

    /// <summary>消息筛选模式：strict / ai / strict_then_ai</summary>
    public string MessageFilterMode { get; set; } = "strict";

    /// <summary>Koishi 插件安装路径</summary>
    public string KoishiPluginPath { get; set; } = string.Empty;
}
