namespace AutoCMEX.Core.Info;

using System;
using System.Text.Json;

/// <summary>
/// 信息板块入站事件总线：把 WebSocket 收到的 <c>info_*</c> 事件转给界面侧服务。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要它：入站 <c>event</c> 消息只由 <see cref="AutoCMEX.Core.WebSocket.EventHandler"/>
/// 分发，而该处理器由 <c>WebSocketInitializer</c> 在每次 WebSocket 重建时新建，界面拿不到它的引用。
/// 这里沿用仓库既有做法（<c>IGuessProcessingService</c> 被注入处理器并同时提供给界面），
/// 用一个 DI 单例做中转：处理器发布，信息面板订阅。
/// </para>
/// <para>
/// 事件名统一带 <c>info_</c> 前缀（见 <see cref="InfoProtocol"/>），
/// <see cref="IsInfoEvent"/> 据此判断，避免影响其它模块的入站分支。
/// </para>
/// <para>
/// 处理器在后台线程投递，订阅方自行通过 <c>CallDeferred</c> 回到主线程更新界面。
/// </para>
/// </remarks>
public sealed class InfoEventBus
{
  /// <summary>事件名前缀。</summary>
  public const string EventNamePrefix = "info_";

  /// <summary>收到信息板块事件（参数：事件名、事件 payload）。</summary>
  public event Action<string, JsonElement>? Received;

  /// <summary>判断事件名是否属于信息板块。</summary>
  /// <param name="eventName">事件名。</param>
  /// <returns>是否属于信息板块。</returns>
  public static bool IsInfoEvent(string eventName) =>
    !string.IsNullOrEmpty(eventName)
    && eventName.StartsWith(EventNamePrefix, StringComparison.Ordinal);

  /// <summary>投递一条信息板块事件。</summary>
  /// <param name="eventName">事件名。</param>
  /// <param name="payload">事件 payload。</param>
  public void Publish(string eventName, JsonElement payload) =>
    Received?.Invoke(eventName, payload);
}
