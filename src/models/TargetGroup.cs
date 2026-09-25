namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// 信息板块的发布目标群聊。
/// </summary>
/// <remarks>
/// 目标群配置不加密（与 <see cref="AiModelConfig"/> 等普通配置一致，仅 API Key 走 AES）。
/// 群列表由设置面板「信息」类别维护，可选群从 Koishi 拉取，避免手输 ID 出错。
/// </remarks>
public class TargetGroup
{
  /// <summary>群聊频道 ID（OneBot 的 <c>channelId</c>）。</summary>
  public AutoValue<string> ChannelId { get; set; } = new(string.Empty);

  /// <summary>所属服务器 ID（OneBot 的 <c>guildId</c>）；仅频道型目标需要，可为空。</summary>
  public AutoValue<string> GuildId { get; set; } = new(string.Empty);

  /// <summary>展示名（从 Koishi 拉取时记录，便于人工辨认；可为空）。</summary>
  public AutoValue<string> DisplayName { get; set; } = new(string.Empty);

  /// <summary>是否在信息板块下栏默认勾选（仅勾选的目标群参与发布）。</summary>
  public AutoValue<bool> Enabled { get; set; } = new(false);

  /// <summary>
  /// 恢复被 JSON 显式 <c>null</c> 覆盖的自动同步属性。
  /// </summary>
  public void EnsureIntegrity()
  {
    ChannelId ??= new(string.Empty);
    GuildId ??= new(string.Empty);
    DisplayName ??= new(string.Empty);
    Enabled ??= new(false);
  }
}
