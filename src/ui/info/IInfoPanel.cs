namespace AutoCMEX.UI.Info;

/// <summary>
/// 信息板块面板接口：用于父级（MainWindow）以接口类型持有面板实例，解耦具体脚本类型
/// </summary>
/// <remarks>
/// <para>
/// AutoInject 的节点适配仅在 <c>GodotInterfaces</c> 内置的适配器字典中按运行时类型查表，
/// 自定义脚本类不在其中；因此以具体脚本类型声明 <c>[Node]</c> 字段会在 AutoConnect 阶段抛
/// <see cref="System.Collections.Generic.KeyNotFoundException"/>。
/// </para>
/// <para>
/// 面板脚本自身实现本接口后，节点实例即满足接口类型，父级字段声明为接口类型即可正常注入
/// （先例：<c>IGuessingPanel</c>、<c>IMergePanel</c>、<c>ILogPanel</c>、<c>IWebSocketPanel</c>）。
/// </para>
/// </remarks>
public interface IInfoPanel { }
