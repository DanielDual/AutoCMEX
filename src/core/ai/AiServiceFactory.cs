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
  public virtual IAiService GetActiveService()
  {
    var config = GetActiveModelConfig();
    return CreateService(config);
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
  public static IAiService CreateService(AiModelConfig config)
  {
    return config.ApiFormat.Value switch
    {
      "OpenAI" => new OpenAiService(config),
      "Anthropic" => new AnthropicService(config),
      _ => throw new ArgumentException($"不支持的 API 格式: {config.ApiFormat.Value}"),
    };
  }
}
