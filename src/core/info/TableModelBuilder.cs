namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>表格列的对齐方式。</summary>
public enum TableColumnAlign
{
  /// <summary>左对齐（文本列）。</summary>
  Left = 0,

  /// <summary>居中（编号/计数列）。</summary>
  Center = 1,

  /// <summary>右对齐。</summary>
  Right = 2,
}

/// <summary>表格列定义（表头文本 + 对齐方式）。</summary>
public sealed class TableColumn
{
  /// <summary>表头文本。</summary>
  public string Header { get; init; } = string.Empty;

  /// <summary>该列所有单元格的对齐方式。</summary>
  public TableColumnAlign Align { get; init; } = TableColumnAlign.Left;
}

/// <summary>表格数据行。</summary>
public sealed class TableRow
{
  /// <summary>单元格文本（顺序与 <see cref="TableModel.Columns"/> 一一对应）。</summary>
  public IReadOnlyList<string> Cells { get; init; } = Array.Empty<string>();

  /// <summary>是否整行染绿（展示与发布图共用同一判定）。</summary>
  public bool IsHighlighted { get; init; }
}

/// <summary>
/// 通用表格渲染模型（与 Godot 无关的纯数据），供展示层与出图层共同消费。
/// </summary>
public sealed class TableModel
{
  /// <summary>标题（展示与出图共用，保证两者形式一致）。</summary>
  public string Title { get; init; } = string.Empty;

  /// <summary>列定义。</summary>
  public IReadOnlyList<TableColumn> Columns { get; init; } = Array.Empty<TableColumn>();

  /// <summary>数据行。</summary>
  public IReadOnlyList<TableRow> Rows { get; init; } = Array.Empty<TableRow>();

  /// <summary>是否有数据行；为 false 时不应发布（无可发布内容）。</summary>
  public bool HasContent => Rows.Count > 0;
}

/// <summary>
/// 把信息板块的两张表逻辑模型转成通用表格渲染模型。
/// </summary>
/// <remarks>
/// <para>
/// 纯逻辑、无 Godot 类型，可直接单测。列结构与需求逐字对齐：
/// </para>
/// <list type="bullet">
/// <item>猜测表 = 3 栏（创作者 / 符卡编号 / 符卡名），已猜出行整行染绿；</item>
/// <item>创作者剩余表 = 2 栏（创作者 / 剩余未被猜测符卡数），剩余为 0 的行整行染绿。</item>
/// </list>
/// </remarks>
public static class TableModelBuilder
{
  /// <summary>猜测表的标题。</summary>
  public const string GuessingTableTitle = "符卡猜测情况表";

  /// <summary>创作者剩余表的标题。</summary>
  public const string CreatorRemainingTableTitle = "创作者————剩余未被猜测符卡数表";

  /// <summary>构建符卡猜测情况表：创作者 / 符卡编号 / 符卡名。</summary>
  /// <param name="snapshot">信息表快照。</param>
  /// <returns>表格渲染模型；无数据时 <see cref="TableModel.HasContent"/> 为 false。</returns>
  public static TableModel BuildGuessingTable(InfoTableSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    var columns = new[]
    {
      new TableColumn { Header = "创作者", Align = TableColumnAlign.Left },
      new TableColumn { Header = "符卡编号", Align = TableColumnAlign.Center },
      new TableColumn { Header = "符卡名", Align = TableColumnAlign.Left },
    };

    var rows = new List<TableRow>(snapshot.GuessingRows.Count);
    foreach (var row in snapshot.GuessingRows)
    {
      rows.Add(
        new TableRow
        {
          Cells = new[]
          {
            // 数据层已按「未猜出 → 空串」遮罩，这里直接透传
            row.CreatorDisplay,
            row.Index.ToString(CultureInfo.InvariantCulture),
            row.SpellCardName,
          },
          IsHighlighted = row.IsGuessedOut,
        }
      );
    }

    return new TableModel
    {
      Title = GuessingTableTitle,
      Columns = columns,
      Rows = rows,
    };
  }

  /// <summary>构建创作者————剩余未被猜测符卡数表：创作者 / 剩余未被猜测符卡数。</summary>
  /// <param name="snapshot">信息表快照。</param>
  /// <returns>表格渲染模型；无数据时 <see cref="TableModel.HasContent"/> 为 false。</returns>
  public static TableModel BuildCreatorRemainingTable(InfoTableSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    var columns = new[]
    {
      new TableColumn { Header = "创作者", Align = TableColumnAlign.Left },
      new TableColumn { Header = "剩余未被猜测符卡数", Align = TableColumnAlign.Center },
    };

    var rows = new List<TableRow>(snapshot.CreatorRows.Count);
    foreach (var row in snapshot.CreatorRows)
    {
      rows.Add(
        new TableRow
        {
          Cells = new[] { row.Creator, row.Remaining.ToString(CultureInfo.InvariantCulture) },
          IsHighlighted = row.IsFullyGuessedOut,
        }
      );
    }

    return new TableModel
    {
      Title = CreatorRemainingTableTitle,
      Columns = columns,
      Rows = rows,
    };
  }
}
