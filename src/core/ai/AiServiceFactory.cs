namespace AutoCMEX.Core.Ai;

using System;
using System.Linq;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;

/// <summary>
/// AI 服务工厂：根据当前激活的模型配置创建对应的 IAiService 实例
/// </summary>
public class AiServiceFactory
{
  private readonly DataManager _dataManager;

  public AiServiceFactory(DataManager dataManager)
  {
    _dataManager = dataManager;
  }

  /// <summary>
  /// 获取当前激活的 AI 服务实例
  /// </summary>
  /// <returns>IAiService 实例</returns>
  /// <exception cref="InvalidOperationException">没有配置有效的 AI 模型时抛出</exception>
  public IAiService GetActiveService()
  {
    var config = GetActiveModelConfig();
    var timeout = _dataManager.Settings.AiTimeoutSeconds;
    return CreateService(config, timeout);
  }

  /// <summary>
  /// 获取当前激活的模型配置
  /// </summary>
  /// <exception cref="InvalidOperationException">没有选中模型或选中模型无效时抛出</exception>
  public AiModelConfig GetActiveModelConfig()
  {
    var settings = _dataManager.Settings;

    // 优先使用用户选中的模型
    if (!string.IsNullOrEmpty(settings.ActiveAiModelId))
    {
      var selected = settings.AiModels.Find(m => m.Id == settings.ActiveAiModelId);
      if (selected != null && IsModelValid(selected))
        return selected;

      throw new InvalidOperationException(
        "当前选中的 AI 模型配置不完整或已被删除，请重新选择有效的模型"
      );
    }

    // 没有选中模型时报错
    throw new InvalidOperationException("未选择 AI 模型，请在设置中选择一个模型");
  }

  /// <summary>
  /// 检查模型配置是否完整有效
  /// </summary>
  public static bool IsModelValid(AiModelConfig model)
  {
    return !string.IsNullOrEmpty(model.EndpointUrl)
      && !string.IsNullOrEmpty(model.ModelId)
      && !string.IsNullOrEmpty(model.EncryptedApiKey);
  }

  /// <summary>
  /// 根据模型配置创建对应的 AI 服务实例
  /// </summary>
  public static IAiService CreateService(AiModelConfig config, int timeoutSeconds = 100)
  {
    return config.ApiFormat == "Anthropic"
      ? new AnthropicService(config, timeoutSeconds)
      : new OpenAiService(config, timeoutSeconds);
  }
}
