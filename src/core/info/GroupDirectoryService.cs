namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using Chickensoft.Log;
using Chickensoft.Sync.Primitives;

/// <summary>
/// Koishi 回传的一个群条目。
/// </summary>
/// <param name="ChannelId">群聊频道 ID（OneBot 的 <c>channelId</c>）。</param>
/// <param name="GuildId">所属服务器 ID（OneBot 的 <c>guildId</c>），可为空。</param>
/// <param name="Name">群名（展示用），可为空。</param>
public sealed record GroupListEntry(string ChannelId, string GuildId, string Name);

/// <summary>
/// 群目录服务：向 Koishi 发起群列表查询，并把回传结果并入目标群配置。
/// </summary>
/// <remarks>
/// <para>
/// 目标是"不让人手输群 ID"：拉取成功后按 <see cref="TargetGroup.ChannelId"/> 去重合并，已存在的只更新
/// 展示名，不覆盖用户已勾选的 <see cref="TargetGroup.Enabled"/>。
/// </para>
/// <para>
/// 新发现的群默认不勾选：避免"刷新一次群列表"就把内容群发到全部群。
/// </para>
/// </remarks>
public sealed class GroupDirectoryService
{
  private readonly DataManager _dataManager;
  private readonly IWebSocketServer _server;
  private readonly ILog _log;

  /// <summary>
  /// 创建群目录服务（使用默认日志）。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="server">WebSocket 服务端（出站通道）。</param>
  public GroupDirectoryService(DataManager dataManager, IWebSocketServer server)
    : this(dataManager, server, AppLogs.GetOrCreate().GetLogger(nameof(GroupDirectoryService))) { }

  /// <summary>
  /// 创建群目录服务（日志注入）。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="server">WebSocket 服务端（出站通道）。</param>
  /// <param name="log">日志。</param>
  public GroupDirectoryService(DataManager dataManager, IWebSocketServer server, ILog log)
  {
    _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
    _server = server ?? throw new ArgumentNullException(nameof(server));
    _log = log ?? throw new ArgumentNullException(nameof(log));
  }

  /// <summary>
  /// 向 Koishi 发起群列表查询。
  /// </summary>
  /// <returns>请求是否已发出；无可用连接时返回 <c>false</c> 且不静默丢弃。</returns>
  public async Task<bool> RequestAsync()
  {
    if (_server.ConnectionCount == 0)
    {
      _log.Warn("GroupDirectoryService: 无可用 Koishi 连接，群列表查询未发出。");
      return false;
    }

    var message = WebSocketMessage.CreateEvent(
      InfoProtocol.QueryGroupListEvent,
      new { source = "autocmex" }
    );

    await _server.BroadcastAsync(message);
    _log.Print("GroupDirectoryService: 已发出群列表查询。");
    return true;
  }

  /// <summary>
  /// 处理 Koishi 回传的群列表（入站事件 <c>info_group_list_result</c>）。
  /// </summary>
  /// <param name="payload">事件 payload。</param>
  /// <returns>新增的目标群数量；payload 无法解析时为 <c>-1</c>。</returns>
  /// <remarks>必须在主线程调用（会改动 <c>AutoList</c> 并触发界面绑定刷新）。</remarks>
  public int HandleGroupListResult(JsonElement payload)
  {
    if (!TryParseGroups(payload, out var groups))
    {
      _log.Warn("GroupDirectoryService: 收到无法解析的群列表回执。");
      return -1;
    }

    return ApplyGroupList(groups);
  }

  /// <summary>
  /// 把 Koishi 回传的群列表并入目标群配置。
  /// </summary>
  /// <param name="groups">群条目。</param>
  /// <returns>新增的目标群数量。</returns>
  public int ApplyGroupList(IReadOnlyList<GroupListEntry> groups)
  {
    ArgumentNullException.ThrowIfNull(groups);

    var targets = _dataManager.Settings.TargetGroups;
    var added = 0;
    var renamed = 0;

    foreach (var entry in groups)
    {
      if (string.IsNullOrWhiteSpace(entry.ChannelId))
        continue;

      var existing = FindByChannelId(targets, entry.ChannelId);
      if (existing is null)
      {
        targets.Add(
          new TargetGroup
          {
            ChannelId = { Value = entry.ChannelId },
            GuildId = { Value = entry.GuildId ?? string.Empty },
            DisplayName =
            {
              Value = string.IsNullOrWhiteSpace(entry.Name) ? entry.ChannelId : entry.Name,
            },
            Enabled = { Value = false },
          }
        );
        added++;
        continue;
      }

      if (
        !string.IsNullOrWhiteSpace(entry.Name)
        && !string.Equals(existing.DisplayName.Value, entry.Name, StringComparison.Ordinal)
      )
      {
        existing.DisplayName.Value = entry.Name;
        renamed++;
      }

      if (
        !string.IsNullOrWhiteSpace(entry.GuildId)
        && !string.Equals(existing.GuildId.Value, entry.GuildId, StringComparison.Ordinal)
      )
      {
        existing.GuildId.Value = entry.GuildId;
      }
    }

    if (added > 0 || renamed > 0)
      _dataManager.TriggerAutoSave();

    _log.Print(
      $"GroupDirectoryService: 合并群列表 新增 {added}，更名 {renamed}，当前共 {targets.Count} 个目标群。"
    );
    return added;
  }

  /// <summary>
  /// 解析 Koishi 回传的群列表 payload。
  /// </summary>
  /// <param name="payload">消息 payload（形如 <c>{ event, data }</c>）。</param>
  /// <param name="groups">解析出的群条目。</param>
  /// <returns>是否为合法的群列表回执。</returns>
  /// <remarks>
  /// 同时接受 <c>data</c> 直接是数组、或 <c>data.groups</c> 是数组两种形状，避免插件侧结构调整时
  /// 让本端静默失配。
  /// </remarks>
  public static bool TryParseGroups(
    JsonElement payload,
    [NotNullWhen(true)] out List<GroupListEntry>? groups
  )
  {
    groups = null;

    if (payload.ValueKind != JsonValueKind.Object)
      return false;

    if (!payload.TryGetProperty("data", out var data))
      return false;

    if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("groups", out var nested))
      data = nested;

    if (data.ValueKind != JsonValueKind.Array)
      return false;

    var parsed = new List<GroupListEntry>();
    foreach (var item in data.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.Object)
        continue;

      var channelId = ReadString(item, "channelId");
      if (string.IsNullOrWhiteSpace(channelId))
        continue;

      parsed.Add(
        new GroupListEntry(channelId, ReadString(item, "guildId"), ReadString(item, "name"))
      );
    }

    groups = parsed;
    return true;
  }

  private static TargetGroup? FindByChannelId(AutoList<TargetGroup> targets, string channelId)
  {
    foreach (var target in targets)
    {
      if (string.Equals(target.ChannelId.Value, channelId, StringComparison.Ordinal))
        return target;
    }

    return null;
  }

  private static string ReadString(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
      ? value.GetString() ?? string.Empty
      : string.Empty;
}
