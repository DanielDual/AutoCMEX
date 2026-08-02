namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// 符卡数据模型 — 使用 AutoValue&lt;T&gt; 实现属性级变更通知，
/// 配合 AutoList&lt;SpellCard&gt; 的集合级通知实现完整数据同步。
/// </summary>
public class SpellCard
{
  /// <summary>符卡名称</summary>
  public AutoValue<string> Name { get; set; } = new(string.Empty);

  /// <summary>创作者（答案），可为空</summary>
  public AutoValue<string> Creator { get; set; } = new(string.Empty);

  /// <summary>是否已揭晓</summary>
  public AutoValue<bool> IsRevealed { get; set; } = new(false);

  /// <summary>是否已被猜出（猜测全部正确后标记）</summary>
  public AutoValue<bool> IsGuessedOut { get; set; } = new(false);
}
