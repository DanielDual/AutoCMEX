namespace AutoCMEX.Core.Ai;

using System;
using System.Linq;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// AI 服务工厂：根据当前激活的模型配置创建对应的 IAiService 实例
/// </summary>
public class AiServiceFactory
{
  private readonly DataManager _dataManager;
  private readonly ILog _log;

  /// <summary>
  /// 创建 AI 服务工厂。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="log">
  /// 日志；为 <c>null</c> 时使用 <see cref="NullLog.Instance"/>（生产/测试可显式注入）。
  /// 该日志会透传给工厂创建的各 AI 服务。
  /// </param>
  public AiServiceFactory(DataManager dataManager, ILog? log = null)
  {
    _dataManager = dataManager;
    _log = log ?? NullLog.Instance;
  }

  /// <summary>
  /// 获取当前激活的 AI 服务实例
  /// </summary>
  public virtual IAiService GetActiveService()
  {
    var config = GetActiveModelConfig();
    // 按具体实现类取模块日志器，保持日志面板的模块筛选粒度。
    var moduleLog = string.Equals(config.ApiFormat.Value, "Anthropic", StringComparison.Ordinal)
      ? _dataManager.GetLogger(nameof(AnthropicService))
      : _dataManager.GetLogger(nameof(OpenAiService));
    return CreateService(config, _dataManager.Settings.AiTimeoutSeconds.Value, moduleLog);
  }

  /// <summary>
  /// 获取当前激活的 AI 模型配置
  /// </summary>
  public virtual AiModelConfig GetActiveModelConfig()
  {
    var settings = _dataManager.Settings;

    // 优先使用用户选中的模型
    if (!string.IsNullOrEmpty(settings.ActiveAiModelId.Value))
    {
      var selected = settings.AiModels.FirstOrDefault(m =>
        m.Id.Value == settings.ActiveAiModelId.Value
      );
      if (selected != null && IsModelValid(selected))
        return selected;

      throw new InvalidOperationException(
        "当前选中的 AI 模型配置不完整或已被删除，请重新选择有效的模型"
      );
    }

    // 无选中模型时，尝试使用第一个有效模型
    var firstValid = settings.AiModels.FirstOrDefault(IsModelValid);
    if (firstValid != null)
      return firstValid;

    throw new InvalidOperationException("没有可用的 AI 模型配置，请先在设置中添加并配置 AI 模型");
  }

  /// <summary>
  /// 判断模型配置是否有效（EndpointUrl、ModelId、EncryptedApiKey 均不为空）
  /// </summary>
  public static bool IsModelValid(AiModelConfig config)
  {
    return !string.IsNullOrEmpty(config.EndpointUrl.Value)
      && !string.IsNullOrEmpty(config.ModelId.Value)
      && !string.IsNullOrEmpty(config.EncryptedApiKey.Value);
  }

  /// <summary>
  /// 根据模型配置创建对应的 IAiService 实例
  /// </summary>
  /// <param name="config">模型配置。</param>
  /// <param name="timeoutSeconds">单次 HTTP 请求的超时秒数；默认与设置项默认值一致。</param>
  /// <param name="log">日志；为 <c>null</c> 时使用 <see cref="NullLog.Instance"/>（测试兜底）。</param>
  public static IAiService CreateService(
    AiModelConfig config,
    int timeoutSeconds = 100,
    ILog? log = null
  )
  {
    var logger = log ?? NullLog.Instance;
    return config.ApiFormat.Value switch
    {
      "OpenAI" => new OpenAiService(config, logger, timeoutSeconds),
      "Anthropic" => new AnthropicService(config, logger, timeoutSeconds),
      _ => throw new ArgumentException($"不支持的 API 格式: {config.ApiFormat.Value}"),
    };
  }
}
