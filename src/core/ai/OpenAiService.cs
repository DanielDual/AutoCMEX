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
/// OpenAI 兼容格式 API 服务
/// </summary>
public class OpenAiService : IAiService, IDisposable
{
  private readonly AiModelConfig _config;
  private readonly HttpClient _httpClient;
  private readonly ILog _log;
  private bool _disposed;

  public OpenAiService(AiModelConfig config, int timeoutSeconds = 100)
    : this(config, AppLogs.GetOrCreate().GetLogger(nameof(OpenAiService)), timeoutSeconds) { }

  public OpenAiService(AiModelConfig config, ILog log, int timeoutSeconds = 100)
  {
    _config = config;
    _log = log;
    _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    _httpClient.DefaultRequestHeaders.Add(
      "Authorization",
      $"Bearer {config.EncryptedApiKey.Value}"
    );
  }

  /// <inheritdoc/>
  public async Task<string> ChatAsync(string systemPrompt, string userMessage)
  {
    var url = _config.EndpointUrl.Value.TrimEnd('/') + "/chat/completions";
    _log.Print($"OpenAI ChatAsync request: model={_config.ModelId.Value}, url={url}");

    var requestBody = new
    {
      model = _config.ModelId.Value,
      messages = new object[]
      {
        new { role = "system", content = systemPrompt },
        new { role = "user", content = userMessage },
      },
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
          $"OpenAI request failed: status={(int)response.StatusCode}, body_len={responseBody.Length}"
        );
        throw new InvalidOperationException(
          $"API 请求失败 ({response.StatusCode}): {responseBody}"
        );
      }

      using var doc = JsonDocument.Parse(responseBody);
      var choices = doc.RootElement.GetProperty("choices");
      if (choices.GetArrayLength() == 0)
      {
        _log.Warn("OpenAI response contains no choices.");
        throw new InvalidOperationException("API 返回空响应");
      }

      var message = choices[0].GetProperty("message");
      var result = message.GetProperty("content").GetString() ?? string.Empty;
      _log.Print($"OpenAI response received: length={result.Length}");
      return result;
    }
    catch (TaskCanceledException ex)
    {
      _log.Err($"OpenAI request timed out: {ex.Message}");
      throw;
    }
    catch (HttpRequestException ex)
    {
      _log.Err($"OpenAI HTTP error: {ex.Message}");
      throw;
    }
  }

  /// <inheritdoc/>
  public async Task<bool> TestConnectionAsync()
  {
    _log.Print("OpenAI TestConnection starting.");
    try
    {
      var result = await ChatAsync("Hello", "Hi");
      var ok = !string.IsNullOrEmpty(result);
      _log.Print(ok ? "OpenAI TestConnection succeeded." : "OpenAI TestConnection returned empty.");
      return ok;
    }
    catch (Exception ex)
    {
      _log.Err($"OpenAI TestConnection failed: {ex.GetType().Name}: {ex.Message}");
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
