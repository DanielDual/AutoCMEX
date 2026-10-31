namespace AutoCMEX.UI.Settings;

/// <summary>
/// AI 模型配置面板接口：解耦父级（SettingsPanel）对具体面板类型的依赖。
/// </summary>
/// <remarks>
/// 供 AutoInject <c>[Node]</c> 属性引用自定义脚本面板时使用：使面板实例自身即满足接口类型，
/// 从而在 AutoConnect 阶段被直接赋值而无需触发 GodotNodeInterfaces 的节点接口适配。
/// </remarks>
public interface IAiModelConfigPanel { }
