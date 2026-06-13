namespace AutoCMEX.Core.Ai;

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Models;

/// <summary>
/// OpenAI 兼容格式 API 服务
/// </summary>
public class OpenAiService : IAiService
{
  private readonly AiModelConfig _config;
  private readonly HttpClient _httpClient;

  public OpenAiService(AiModelConfig config)
  {
    _config = config;
    _httpClient = new HttpClient();
    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.EncryptedApiKey}");
  }

  /// <inheritdoc/>
  public async Task<string> ChatAsync(string systemPrompt, string userMessage)
  {
    var url = _config.EndpointUrl.TrimEnd('/') + "/v1/chat/completions";

    var requestBody = new
    {
      model = _config.ModelId,
      messages = new object[]
      {
        new { role = "system", content = systemPrompt },
        new { role = "user", content = userMessage },
      },
    };

    var json = JsonSerializer.Serialize(requestBody);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await _httpClient.PostAsync(url, content);
    var responseBody = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
      throw new InvalidOperationException($"API 请求失败 ({response.StatusCode}): {responseBody}");

    using var doc = JsonDocument.Parse(responseBody);
    var choices = doc.RootElement.GetProperty("choices");
    if (choices.GetArrayLength() == 0)
      throw new InvalidOperationException("API 返回空响应");

    var message = choices[0].GetProperty("message");
    return message.GetProperty("content").GetString() ?? string.Empty;
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
}
