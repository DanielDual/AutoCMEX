namespace AutoCMEX.Models;

/// <summary>
/// AI 模型配置数据模型
/// </summary>
public class AiModelConfig
{
  /// <summary>配置唯一标识</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>API 格式：OpenAI 或 Anthropic</summary>
  public string ApiFormat { get; set; } = "OpenAI";

  /// <summary>API 端点 URL</summary>
  public string EndpointUrl { get; set; } = string.Empty;

  /// <summary>模型 ID</summary>
  public string ModelId { get; set; } = string.Empty;

  /// <summary>AES 加密后的 API 密钥</summary>
  public string EncryptedApiKey { get; set; } = string.Empty;
}
