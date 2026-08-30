namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// AI 模型配置数据模型（Sync 管理，属性变更自动通知）
/// </summary>
public class AiModelConfig
{
  /// <summary>配置唯一标识</summary>
  public AutoValue<string> Id { get; set; } = new(string.Empty);

  /// <summary>API 格式：OpenAI 或 Anthropic</summary>
  public AutoValue<string> ApiFormat { get; set; } = new("OpenAI");

  /// <summary>API 端点 URL</summary>
  public AutoValue<string> EndpointUrl { get; set; } = new(string.Empty);

  /// <summary>模型 ID</summary>
  public AutoValue<string> ModelId { get; set; } = new(string.Empty);

  /// <summary>AES 加密后的 API 密钥</summary>
  public AutoValue<string> EncryptedApiKey { get; set; } = new(string.Empty);
}
