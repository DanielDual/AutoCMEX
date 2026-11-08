namespace AutoCMEX.UI.Info;

using System;

/// <summary>
/// 符卡 GIF 集栏接口：供信息面板以接口类型声明 <c>[Node]</c>，并收口本栏的发布请求。
/// </summary>
/// <remarks>
/// <para>
/// AutoInject 的节点适配只在 <c>GodotInterfaces</c> 内置适配器字典里按类型查表，自定义脚本类
/// 不能直接用作 <c>[Node]</c> 字段类型；面板脚本实现本接口后，节点实例本身即满足接口类型
/// （先例：<c>IGuessingPanel</c>、<c>IInfoPanel</c>）。
/// </para>
/// <para>
/// 本栏不再由信息面板用 <c>Setup(...)</c> 手动注入回调：发布按钮改为触发
/// <see cref="PublishRequested"/>，由信息面板订阅后统一编排串行发布。
/// </para>
/// </remarks>
public interface IGifSetPanel
{
  /// <summary>本栏发布请求（点「发布 GIF 集」时触发）。</summary>
  event Action? PublishRequested;
}
