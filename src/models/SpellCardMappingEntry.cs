namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// 对应表的一行：一张符卡（或非符）及其 Creator。
/// 顺序由其在 AutoList（MergeConfig.Mapping）中的位置决定。
/// </summary>
public class SpellCardMappingEntry
{
  /// <summary>符卡名；非符为空。</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>是否非符（SCName 空 / []）。</summary>
  public AutoValue<bool> IsNonSpell { get; set; } = new(false);

  /// <summary>创作者名。</summary>
  public AutoValue<string> Creator { get; set; } = new(string.Empty);

  /// <summary>来源创作者包名（阶段3 桥接引擎用，用于重建映射）。</summary>
  public string PackageName { get; set; } = string.Empty;

  /// <summary>该包内抽取顺序下标（阶段3 桥接引擎用）。</summary>
  public int SourceCardIndex { get; set; }
}
