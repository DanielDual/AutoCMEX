namespace AutoCMEX.Core.Ai;

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// Anthropic 原生格式 API 服务
/// </summary>
public class AnthropicService : IAiService, IDisposable
{
  private readonly AiModelConfig _config;
  private readonly HttpClient _httpClient;
  private readonly ILog _log;
  private bool _disposed;

  public AnthropicService(AiModelConfig config)
    : this(config, AppLogs.GetOrCreate().GetLogger(nameof(AnthropicService))) { }

  public AnthropicService(AiModelConfig config, ILog log)
  {
    _config = config;
    _log = log;
    _httpClient = new HttpClient();
    _httpClient.DefaultRequestHeaders.Add("x-api-key", config.EncryptedApiKey);
    _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
  }

  /// <inheritdoc/>
  public async Task<string> ChatAsync(string systemPrompt, string userMessage)
  {
    var url = _config.EndpointUrl.TrimEnd('/') + "/v1/messages";
    _log.Print($"Anthropic ChatAsync request: model={_config.ModelId}, url={url}");

    var requestBody = new
    {
      model = _config.ModelId,
      max_tokens = 1024,
      system = systemPrompt,
      messages = new object[] { new { role = "user", content = userMessage } },
    };

    var json = JsonSerializer.Serialize(requestBody);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    try
    {
      var response = await _httpClient.PostAsync(url, content);
      var responseBody = await response.Content.ReadAsStringAsync();

      if (!response.IsSuccessStatusCode)
      {
        _log.Err(
          $"Anthropic request failed: status={(int)response.StatusCode}, body_len={responseBody.Length}"
        );
        throw new InvalidOperationException(
          $"API 请求失败 ({response.StatusCode}): {responseBody}"
        );
      }

      using var doc = JsonDocument.Parse(responseBody);
      var contentBlocks = doc.RootElement.GetProperty("content");
      if (contentBlocks.GetArrayLength() == 0)
      {
        _log.Warn("Anthropic response contains no content blocks.");
        throw new InvalidOperationException("API 返回空响应");
      }

      var result = contentBlocks[0].GetProperty("text").GetString() ?? string.Empty;
      _log.Print($"Anthropic response received: length={result.Length}");
      return result;
    }
    catch (TaskCanceledException ex)
    {
      _log.Err($"Anthropic request timed out: {ex.Message}");
      throw;
    }
    catch (HttpRequestException ex)
    {
      _log.Err($"Anthropic HTTP error: {ex.Message}");
      throw;
    }
  }

  /// <inheritdoc/>
  public async Task<bool> TestConnectionAsync()
  {
    _log.Print("Anthropic TestConnection starting.");
    try
    {
      var result = await ChatAsync("Hello", "Hi");
      var ok = !string.IsNullOrEmpty(result);
      _log.Print(
        ok ? "Anthropic TestConnection succeeded." : "Anthropic TestConnection returned empty."
      );
      return ok;
    }
    catch (Exception ex)
    {
      _log.Err($"Anthropic TestConnection failed: {ex.GetType().Name}: {ex.Message}");
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
