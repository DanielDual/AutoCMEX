namespace AutoCMEX.Core.Info;

using System.Linq;

/// <summary>
/// 表格内容指纹：把标题、行高亮标记与单元格文本拼成可直接比较的字符串。
/// </summary>
/// <remarks>
/// 用于「内容没变就不重复渲染/不重复推送」的判定；同一份模型每次构建的指纹必须一致，
/// 且不同内容（含高亮状态）必须得到不同指纹。
/// </remarks>
public static class TableModelSignature
{
  /// <summary>行内各字段之间的分隔符（不可见控制符，避免与真实文本冲突）。</summary>
  private const char FieldSeparator = '\u001f';

  /// <summary>构建表内容指纹。</summary>
  /// <param name="model">表格模型；为 null 时返回空串。</param>
  /// <returns>可直接用字符串相等比较的指纹。</returns>
  public static string Build(TableModel? model)
  {
    if (model is null)
      return string.Empty;

    return string.Join(
      '\n',
      new[] { model.Title }.Concat(
        model.Rows.Select(row =>
          (row.IsHighlighted ? "1" : "0") + FieldSeparator + string.Join(FieldSeparator, row.Cells)
        )
      )
    );
  }
}
