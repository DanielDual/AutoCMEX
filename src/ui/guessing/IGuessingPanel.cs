namespace AutoCMEX.UI.Guessing;

/// <summary>
/// 猜测面板接口：解耦父级（MainWindow）对具体面板类型的依赖。
/// </summary>
/// <remarks>
/// 供 AutoInject <c>[Node]</c> 属性引用自定义脚本面板时使用：使面板实例自身即满足接口类型，
/// 从而在 AutoConnect 阶段被直接赋值而无需触发 GodotNodeInterfaces 的节点接口适配
/// （自定义脚本类的运行时类型不在 <c>GodotInterfaces</c> 的适配器字典中）。
/// </remarks>
public interface IGuessingPanel { }
