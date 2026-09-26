namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// 发布项类别；枚举顺序即「一键转发」的固定发布顺序。
/// </summary>
/// <remarks>
/// 顺序与界面四栏一致（GIF 集 → 猜测表 → 创作者表 → 活动规则），由
/// <see cref="PublishOrder"/> 统一暴露给发布编排与重试逻辑，避免两处各写一份顺序。
/// </remarks>
public enum PublishItemKind
{
  /// <summary>符卡 GIF 集：合并转发，逐条 = <c>序号. 符卡名</c> + GIF。</summary>
  GifSet = 0,

  /// <summary>符卡猜测情况表：整表出图后发布。</summary>
  GuessingTable = 1,

  /// <summary>创作者剩余未被猜测符卡数表：整表出图后发布。</summary>
  CreatorRemainingTable = 2,

  /// <summary>活动规则：纯文本发布。</summary>
  ActivityRule = 3,
}

/// <summary>发布项在群内的呈现方式。</summary>
public enum PublishSendMode
{
  /// <summary>合并转发：整条消息由若干节点组成，群内以一张可展开的转发卡片呈现。</summary>
  Forward = 0,

  /// <summary>直接发图：每个节点作为一条普通群消息里的图片，群内直接看到图。</summary>
  Image = 1,
}

/// <summary>发布项类别的固定顺序与展示名。</summary>
public static class PublishOrder
{
  /// <summary>固定发布顺序：GIF 集 → 猜测表 → 创作者表 → 活动规则。</summary>
  public static readonly IReadOnlyList<PublishItemKind> Fixed = new[]
  {
    PublishItemKind.GifSet,
    PublishItemKind.GuessingTable,
    PublishItemKind.CreatorRemainingTable,
    PublishItemKind.ActivityRule,
  };

  /// <summary>
  /// 取发布项的群内呈现方式：只有 GIF 集需要合并转发，两张表直接发图。
  /// </summary>
  /// <param name="kind">发布项类别。</param>
  /// <returns>呈现方式。</returns>
  /// <remarks>
  /// 表是「一眼看完」的整图，塞进转发卡片反而要多点一次才能看到；GIF 集逐条展开才有意义，故保留合并转发。
  /// 活动规则是纯文本、没有图片形态，沿用合并转发不变。
  /// </remarks>
  public static PublishSendMode GetSendMode(PublishItemKind kind) =>
    kind switch
    {
      PublishItemKind.GuessingTable => PublishSendMode.Image,
      PublishItemKind.CreatorRemainingTable => PublishSendMode.Image,
      _ => PublishSendMode.Forward,
    };

  /// <summary>取发布项的展示名（结果报告与日志共用，保证两处措辞一致）。</summary>
  /// <param name="kind">发布项类别。</param>
  /// <returns>展示名。</returns>
  public static string GetTitle(PublishItemKind kind) =>
    kind switch
    {
      PublishItemKind.GifSet => "符卡 GIF 集",
      PublishItemKind.GuessingTable => "符卡猜测情况表",
      PublishItemKind.CreatorRemainingTable => "创作者剩余未被猜测符卡数表",
      PublishItemKind.ActivityRule => "活动规则",
      _ => kind.ToString(),
    };
}

/// <summary>
/// 单条失败明细。
/// </summary>
/// <remarks>
/// 必须能定位到具体条目：合并转发逐条发送时，失败只报「整条消息失败」无法排查是第几张
/// 图片有问题，因此序号与标题随失败一起回传。
/// </remarks>
/// <param name="Index">失败条目序号（0 表示非逐条内容，如整表出图或规则文本）。</param>
/// <param name="Title">失败条目标题。</param>
/// <param name="Reason">失败原因（来自 Koishi 回执或本地预检）。</param>
public sealed record PublishFailure(int Index, string Title, string Reason)
{
  /// <summary>格式化为一行展示文本。</summary>
  /// <returns>形如 <c>第 12 条（12. xxx）：原因</c> 或 <c>「标题」：原因</c>。</returns>
  public string ToDisplayText() =>
    Index > 0 ? $"第 {Index} 条（{Title}）：{Reason}" : $"「{Title}」：{Reason}";
}

/// <summary>单个发布项的执行结果。</summary>
public sealed class PublishItemResult
{
  /// <summary>发布项类别。</summary>
  public PublishItemKind Kind { get; init; }

  /// <summary>发布项展示名。</summary>
  public string Title { get; init; } = string.Empty;

  /// <summary>目标群总数（本项计划发送的群数）。</summary>
  public int TargetTotal { get; init; }

  /// <summary>成功发送的目标群数。</summary>
  public int TargetSucceeded { get; init; }

  /// <summary>整项未发送的原因（预检失败、无目标群等）；为空表示已尝试发送。</summary>
  public string ErrorMessage { get; init; } = string.Empty;

  /// <summary>失败明细（逐群或逐条）。</summary>
  public IReadOnlyList<PublishFailure> Failures { get; init; } = Array.Empty<PublishFailure>();

  /// <summary>本项是否成功（无预检错误且无失败明细）。</summary>
  public bool IsSuccess => ErrorMessage.Length == 0 && Failures.Count == 0;

  /// <summary>构造「预检未通过、未发送任何内容」的结果。</summary>
  /// <param name="kind">发布项类别。</param>
  /// <param name="targetTotal">目标群总数。</param>
  /// <param name="errorMessage">未发送原因。</param>
  /// <returns>结果。</returns>
  public static PublishItemResult Blocked(
    PublishItemKind kind,
    int targetTotal,
    string errorMessage
  ) =>
    new()
    {
      Kind = kind,
      Title = PublishOrder.GetTitle(kind),
      TargetTotal = targetTotal,
      TargetSucceeded = 0,
      ErrorMessage = errorMessage,
    };

  /// <summary>构造「已尝试发送」的结果。</summary>
  /// <param name="kind">发布项类别。</param>
  /// <param name="targetTotal">目标群总数。</param>
  /// <param name="targetSucceeded">成功发送的目标群数。</param>
  /// <param name="failures">失败明细。</param>
  /// <returns>结果。</returns>
  public static PublishItemResult Attempted(
    PublishItemKind kind,
    int targetTotal,
    int targetSucceeded,
    IReadOnlyList<PublishFailure> failures
  ) =>
    new()
    {
      Kind = kind,
      Title = PublishOrder.GetTitle(kind),
      TargetTotal = targetTotal,
      TargetSucceeded = targetSucceeded,
      Failures = failures ?? Array.Empty<PublishFailure>(),
    };
}

/// <summary>一次发布的整体结果（单项发布时只有一项）。</summary>
public sealed class PublishReport
{
  /// <summary>逐项结果，顺序与 <see cref="PublishOrder.Fixed"/> 一致。</summary>
  public IReadOnlyList<PublishItemResult> Items { get; init; } = Array.Empty<PublishItemResult>();

  /// <summary>失败项类别（供「只重试失败项」直接复用）。</summary>
  public IReadOnlyList<PublishItemKind> FailedKinds =>
    Items.Where(item => !item.IsSuccess).Select(item => item.Kind).ToArray();

  /// <summary>是否全部成功。</summary>
  public bool IsSuccess => Items.Count > 0 && Items.All(item => item.IsSuccess);

  /// <summary>格式化为报告对话框用的多行文本。</summary>
  /// <returns>逐项列出成功/失败与失败明细。</returns>
  public string ToDisplayText()
  {
    var builder = new StringBuilder();
    var succeeded = Items.Count(item => item.IsSuccess);
    builder.AppendLine(
      $"共 {Items.Count} 项，成功 {succeeded} 项，失败 {Items.Count - succeeded} 项。"
    );
    builder.AppendLine();

    foreach (var item in Items)
    {
      var head = item.IsSuccess ? $"[成功] {item.Title}" : $"[失败] {item.Title}";

      if (item.TargetTotal > 0)
        head += $"（{item.TargetSucceeded}/{item.TargetTotal} 个目标群）";

      builder.AppendLine(head);

      if (item.ErrorMessage.Length > 0)
        builder.AppendLine($"    {item.ErrorMessage}");

      foreach (var failure in item.Failures)
        builder.AppendLine($"    - {failure.ToDisplayText()}");
    }

    return builder.ToString();
  }
}
