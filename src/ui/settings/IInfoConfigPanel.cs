namespace AutoCMEX.UI.Settings;

/// <summary>
/// 设置面板「信息」类别页的标记接口。
/// </summary>
/// <remarks>
/// 必须显式声明并让面板实现：AutoInject 注入子面板时按运行时精确类型查表（只覆盖 Godot 原生类型），
/// 漏做会在解析设置面板时抛 <c>KeyNotFoundException</c>。先例见 <c>IAiModelConfigPanel</c>。
/// </remarks>
public interface IInfoConfigPanel { }
