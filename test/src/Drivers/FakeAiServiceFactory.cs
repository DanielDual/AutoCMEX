namespace AutoCMEX;

using System;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 假 AI 服务工厂：让猜测链路在测试里拿到确定的「AI 回复」或确定的异常，不必真的发请求
/// </summary>
public sealed class FakeAiServiceFactory : AiServiceFactory
{
  private readonly string _response;
  private readonly Exception? _error;

  /// <summary>
  /// 创建假工厂
  /// </summary>
  /// <param name="response">AI 返回的文本（如 <see cref="AiFuzzifier.NotAGuessToken"/> 或一段归一化结果）。</param>
  /// <param name="error">非空时 AI 调用抛出该异常，用于制造丢包。</param>
  public FakeAiServiceFactory(string response, Exception? error = null)
    : base(
      new DataManager(
        Path.GetTempPath(),
        new AesEncryptor(AesEncryptor.GetDefaultKeyPath(Path.GetTempPath()))
      )
    )
  {
    _response = response;
    _error = error;
  }

  /// <inheritdoc/>
  public override IAiService GetActiveService() => new FakeAiService(_response, _error);

  /// <inheritdoc/>
  public override AiModelConfig GetActiveModelConfig() =>
    new()
    {
      Id = new AutoValue<string>("fake-model"),
      EndpointUrl = new AutoValue<string>("https://example.com"),
      ModelId = new AutoValue<string>("fake-model"),
      EncryptedApiKey = new AutoValue<string>("fake-key"),
    };
}

/// <summary>
/// 固定返回文本（或固定抛异常）的假 AI 服务
/// </summary>
public sealed class FakeAiService : IAiService
{
  private readonly string _response;
  private readonly Exception? _error;

  /// <summary>
  /// 创建假 AI 服务
  /// </summary>
  /// <param name="response">固定返回的文本。</param>
  /// <param name="error">非空时 <see cref="ChatAsync"/> 抛出该异常。</param>
  public FakeAiService(string response, Exception? error = null)
  {
    _response = response;
    _error = error;
  }

  /// <inheritdoc/>
  public Task<string> ChatAsync(string systemPrompt, string userMessage) =>
    _error == null ? Task.FromResult(_response) : Task.FromException<string>(_error);

  /// <inheritdoc/>
  public Task<bool> TestConnectionAsync() => Task.FromResult(true);
}
