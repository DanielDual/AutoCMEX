namespace AutoCMEX.UI.Info;

using System.Threading.Tasks;

/// <summary>
/// 栏目发布回调：由信息面板解析目标群并执行发布，栏目自身只负责触发与忙碌态。
/// </summary>
/// <remarks>
/// 沿用仓库既有的回调注入范式（参考 <c>ModelEntryPanel.SetTestCallback</c>）：栏目面板不依赖
/// 发送链路的具体实现，单测可替换为计数用的空实现。
/// </remarks>
public delegate Task ColumnPublishHandler();
