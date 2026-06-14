namespace AutoCMEX.Core.Ai;

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Models;

/// <summary>
/// Anthropic 原生格式 API 服务
/// </summary>
public class AnthropicService : IAiService, IDisposable
{
  private readonly AiModelConfig _config;
  private readonly HttpClient _httpClient;
  private bool _disposed;

  public AnthropicService(AiModelConfig config)
  {
    _config = config;
    _httpClient = new HttpClient();
    _httpClient.DefaultRequestHeaders.Add("x-api-key", config.EncryptedApiKey);
    _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
  }

  /// <inheritdoc/>
  public async Task<string> ChatAsync(string systemPrompt, string userMessage)
  {
    var url = _config.EndpointUrl.TrimEnd('/') + "/v1/messages";

    var requestBody = new
    {
      model = _config.ModelId,
      max_tokens = 1024,
      system = systemPrompt,
      messages = new object[] { new { role = "user", content = userMessage } },
    };

    var json = JsonSerializer.Serialize(requestBody);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await _httpClient.PostAsync(url, content);
    var responseBody = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
      throw new InvalidOperationException($"API 请求失败 ({response.StatusCode}): {responseBody}");

    using var doc = JsonDocument.Parse(responseBody);
    var contentBlocks = doc.RootElement.GetProperty("content");
    if (contentBlocks.GetArrayLength() == 0)
      throw new InvalidOperationException("API 返回空响应");

    return contentBlocks[0].GetProperty("text").GetString() ?? string.Empty;
  }

  /// <inheritdoc/>
  public async Task<bool> TestConnectionAsync()
  {
    try
    {
      var result = await ChatAsync("Hello", "Hi");
      return !string.IsNullOrEmpty(result);
    }
    catch
    {
      return false;
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _httpClient.Dispose();
    GC.SuppressFinalize(this);
  }
}
