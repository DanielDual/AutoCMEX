namespace AutoCMEX.Core.Info;

/// <summary>
/// 信息板块与 Koishi 插件之间的消息名常量。
/// </summary>
/// <remarks>
/// <para>
/// 沿用仓库既有协议：<c>WebSocketMessage.Type</c> 只取 <c>command</c> / <c>event</c> / <c>error</c> /
/// <c>ack</c> 四个字符串常量，因此新增业务分支用「<c>event</c> 类型 + 事件名」表达，不引入枚举，
/// 也不放宽 <c>ProtocolHandler</c> 的入站类型白名单。
/// </para>
/// <para>
/// 出站方向（CMEX → Koishi）用 <see cref="QueryGroupListEvent"/> 等事件名；入站方向
/// （Koishi → CMEX）用带 <c>_result</c> 后缀的事件名，便于在路由层一眼区分请求与回执。
/// </para>
/// </remarks>
public static class InfoProtocol
{
  /// <summary>出站：请求 Koishi 回传群列表。</summary>
  public const string QueryGroupListEvent = "info_group_list_request";

  /// <summary>入站：Koishi 回传的群列表。</summary>
  public const string GroupListResultEvent = "info_group_list_result";

  /// <summary>出站：请求 Koishi 向指定群发送合并转发消息（整条只能由 node 组成）。</summary>
  public const string PublishForwardEvent = "info_publish_forward";

  /// <summary>入站：Koishi 回传的单条发布结果（按目标群逐条回报）。</summary>
  public const string PublishResultEvent = "info_publish_result";

  /// <summary>发布项类别在 payload 中的字段名（取值见 <c>PublishItemKind</c> 的字符串形式）。</summary>
  public const string ItemKindField = "kind";

  /// <summary>把 <c>PublishItemKind</c> 转成跨进程传输用的稳定字符串。</summary>
  /// <param name="kind">发布项类别。</param>
  /// <returns>类别名（与 C# 枚举名一致，便于 TS 侧对照）。</returns>
  public static string ToWireValue(PublishItemKind kind) => kind.ToString();
}
