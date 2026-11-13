namespace AutoCMEX.UI.Settings;

/// <summary>
/// 设置面板「通用」类别页的标记接口。
/// </summary>
/// <remarks>
/// 供 AutoInject <c>[Node]</c> 属性引用自定义脚本面板时使用：使面板实例自身即满足接口类型，
/// 从而在 AutoConnect 阶段被直接赋值而无需触发 GodotNodeInterfaces 的节点接口适配。
/// 必须显式声明并让面板实现：AutoInject 注入子面板时按运行时精确类型查表（只覆盖 Godot 原生类型），
/// 漏做会在解析设置面板时抛 <c>KeyNotFoundException</c>。先例见 <c>IChatConfigPanel</c> / <c>IInfoConfigPanel</c>。
/// </remarks>
public interface IGeneralConfigPanel { }
