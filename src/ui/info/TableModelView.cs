namespace AutoCMEX.UI.Info;

using System;
using AutoCMEX.Core.Info;
using Chickensoft.GodotNodeInterfaces;
using Godot;

/// <summary>
/// 表格展示层：把 <see cref="TableModel"/> 铺成 Godot 控件。
/// </summary>
/// <remarks>
/// <para>
/// 展示层与出图层消费同一个模型（均由 <see cref="TableModelBuilder"/> 产出），配色共用
/// <see cref="InfoTablePalette"/>，因此界面预览与发布出去的图片在列、文本、对齐与行染色上
/// 完全一致；两者的差异只在"画法"（控件 vs 离屏绘制）。
/// </para>
/// <para>
/// 单元格用 <see cref="PanelContainer"/> + <see cref="Label"/>：行染色落到每个单元格的
/// <see cref="StyleBoxFlat.BgColor"/>，边框与内边距与出图层保持同一观感。
/// 必须是 <see cref="PanelContainer"/>（<see cref="Container"/> 子类）而不是 <see cref="Panel"/>：
/// <see cref="Panel"/> 只是带底板的 <see cref="Control"/>，其最小尺寸不含子节点，用它会让
/// <see cref="GridContainer"/> 算出的列宽与行高全为 0 —— 单元格塌成 0×0 并全部叠在同一坐标。
/// </para>
/// </remarks>
public static class TableModelView
{
  /// <summary>单元格横向内边距（像素）。</summary>
  public const int CellPaddingX = 8;

  /// <summary>单元格纵向内边距（像素）。</summary>
  public const int CellPaddingY = 4;

  /// <summary>
  /// 把表格模型填充到网格容器（会先清空既有单元格）。
  /// </summary>
  /// <param name="grid">目标网格容器；列数按模型列数设置。</param>
  /// <param name="model">表格模型；为 null 时仅清空。</param>
  public static void Fill(IGridContainer grid, TableModel? model)
  {
    ArgumentNullException.ThrowIfNull(grid);

    Clear(grid);
    if (model is null)
      return;

    grid.Columns = Math.Max(1, model.Columns.Count);
    grid.AddThemeConstantOverride("h_separation", 0);
    grid.AddThemeConstantOverride("v_separation", 0);

    foreach (var column in model.Columns)
    {
      grid.AddChild(CreateCell(column.Header, column.Align, InfoTablePalette.HeaderBackground));
    }

    foreach (var row in model.Rows)
    {
      var background = row.IsHighlighted
        ? InfoTablePalette.HighlightBackground
        : InfoTablePalette.Background;

      for (var i = 0; i < model.Columns.Count; i++)
      {
        var text = i < row.Cells.Count ? row.Cells[i] : string.Empty;
        grid.AddChild(CreateCell(text, model.Columns[i].Align, background));
      }
    }
  }

  /// <summary>
  /// 清空网格容器内的全部单元格。
  /// </summary>
  /// <param name="grid">目标网格容器。</param>
  /// <remarks>
  /// 先 <c>RemoveChild</c> 再 <c>QueueFree</c>：只 <c>QueueFree</c> 会让旧单元格残留到帧末，
  /// 期间子节点数与 <see cref="GridContainer.Columns"/> 不匹配，出现一帧错位。
  /// </remarks>
  public static void Clear(IGridContainer grid)
  {
    if (grid is null)
      return;

    foreach (var child in grid.GetChildren())
    {
      grid.RemoveChild(child);
      child.QueueFree();
    }
  }

  private static PanelContainer CreateCell(string? text, TableColumnAlign align, Color background)
  {
    var style = new StyleBoxFlat
    {
      BgColor = background,
      BorderColor = InfoTablePalette.Border,
      BorderWidthLeft = 1,
      BorderWidthTop = 1,
      BorderWidthRight = 1,
      BorderWidthBottom = 1,
      ContentMarginLeft = CellPaddingX,
      ContentMarginRight = CellPaddingX,
      ContentMarginTop = CellPaddingY,
      ContentMarginBottom = CellPaddingY,
    };

    // 单元格自身不拦截鼠标，避免遮住外层滚动区域的滚轮手势（Label 同样 Ignore）
    var cell = new PanelContainer
    {
      SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
      MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    cell.AddThemeStyleboxOverride("panel", style);

    var label = new Label
    {
      Text = text ?? string.Empty,
      SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
      HorizontalAlignment = ToGodotAlign(align),
      // 单元格只做展示，不拦截鼠标，避免遮住外层滚动区域的滚轮手势
      MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    label.AddThemeColorOverride("font_color", InfoTablePalette.Text);

    cell.AddChild(label);
    return cell;
  }

  private static HorizontalAlignment ToGodotAlign(TableColumnAlign align) =>
    align switch
    {
      TableColumnAlign.Center => HorizontalAlignment.Center,
      TableColumnAlign.Right => HorizontalAlignment.Right,
      _ => HorizontalAlignment.Left,
    };
}
