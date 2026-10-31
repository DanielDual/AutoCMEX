namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// 表格配色的唯一定义处：界面预览与出图共用同一套颜色，保证「展示形式与发布形式一致」。
/// </summary>
public static class InfoTablePalette
{
  /// <summary>表体背景色。</summary>
  public static readonly Color Background = Color.FromHtml("#ffffff");

  /// <summary>表头背景色。</summary>
  public static readonly Color HeaderBackground = Color.FromHtml("#dcdcdc");

  /// <summary>整行染绿（已猜出 / 剩余为 0）的背景色。</summary>
  public static readonly Color HighlightBackground = Color.FromHtml("#8fd98f");

  /// <summary>网格与边框色。</summary>
  public static readonly Color Border = Color.FromHtml("#3a3a3a");

  /// <summary>文字色。</summary>
  public static readonly Color Text = Color.FromHtml("#101010");
}

/// <summary>表格出图结果。</summary>
public sealed class TableImageResult
{
  /// <summary>是否成功。</summary>
  public bool IsSuccess { get; init; }

  /// <summary>成功时的 PNG 绝对路径。</summary>
  public string Path { get; init; } = string.Empty;

  /// <summary>成功时的图片尺寸（像素）。</summary>
  public Vector2I Size { get; init; }

  /// <summary>成功时的文件字节数。</summary>
  public long FileSizeBytes { get; init; }

  /// <summary>失败原因（单行，可直接展示）。</summary>
  public string ErrorMessage { get; init; } = string.Empty;

  /// <summary>构造成功结果。</summary>
  public static TableImageResult Success(string path, Vector2I size, long fileSizeBytes) =>
    new()
    {
      IsSuccess = true,
      Path = path,
      Size = size,
      FileSizeBytes = fileSizeBytes,
    };

  /// <summary>构造失败结果。</summary>
  public static TableImageResult Error(string message) =>
    new() { IsSuccess = false, ErrorMessage = message };
}

/// <summary>
/// 表格出图：把 <see cref="TableModel"/> 画到 <see cref="SubViewport"/> 离屏画布，再取纹理转
/// <see cref="Image"/> 存 PNG。
/// </summary>
/// <remarks>
/// <para>
/// 画布尺寸按内容测量自适应（列宽 = 该列最长文本宽 + 内边距，行高 = 字体行高 + 内边距），
/// 不留白、不裁切。
/// </para>
/// <para>
/// 需要真实的 <see cref="SubViewport"/> 渲染，因此调用前宿主节点必须已在场景树内且有可用窗口。
/// </para>
/// </remarks>
public static class TableImageRenderer
{
  /// <summary>单元格横向内边距（像素）。</summary>
  public const int CellPaddingX = 16;

  /// <summary>单元格纵向内边距（像素）。</summary>
  public const int CellPaddingY = 10;

  /// <summary>标题相对正文字号的增量。</summary>
  public const int TitleFontSizeDelta = 4;

  /// <summary>取不到主题字号时的回退字号。</summary>
  public const int FallbackFontSize = 24;

  /// <summary>单列最小宽度（像素）。</summary>
  public const float MinColumnWidth = 60f;

  private const string TempOutputDirName = "AutoCMEX_Info";

  /// <summary>
  /// 在系统临时目录下构造出图输出路径（不写入项目目录）。
  /// </summary>
  /// <param name="fileName">文件名（不含目录）。</param>
  /// <returns>绝对路径。</returns>
  public static string BuildTempOutputPath(string fileName)
  {
    var dir = Path.Combine(OS.GetTempDir(), TempOutputDirName);
    return Path.Combine(dir, fileName);
  }

  /// <summary>
  /// 把表格渲染为 PNG。
  /// </summary>
  /// <param name="host">宿主节点（需在场景树内）。</param>
  /// <param name="model">表格渲染模型。</param>
  /// <param name="outputPath">输出 PNG 绝对路径（目录会自动创建，已存在则覆盖）。</param>
  /// <returns>出图结果。</returns>
  public static async Task<TableImageResult> RenderToPngAsync(
    Node host,
    TableModel model,
    string outputPath
  )
  {
    if (host is null || !GodotObject.IsInstanceValid(host) || !host.IsInsideTree())
      return TableImageResult.Error("出图失败：渲染宿主节点无效或不在场景树内。");

    if (model is null)
      return TableImageResult.Error("出图失败：表格模型为空。");

    if (!model.HasContent)
      return TableImageResult.Error("出图失败：表格没有可发布的数据行。");

    if (string.IsNullOrWhiteSpace(outputPath))
      return TableImageResult.Error("出图失败：输出路径为空。");

    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    var viewport = new SubViewport
    {
      Name = "InfoTableImageViewport",
      TransparentBg = false,
      Disable3D = true,
      GuiDisableInput = true,
      RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
      Size = new Vector2I(1, 1),
    };

    try
    {
      host.AddChild(viewport);

      var (font, fontSize) = ResolveThemeFont();
      if (font is null)
        return TableImageResult.Error("出图失败：无法取到主题字体。");

      var canvas = new TableGridCanvas(model, font, fontSize)
      {
        MouseFilter = Control.MouseFilterEnum.Ignore,
      };
      viewport.AddChild(canvas);

      var contentSize = canvas.ComputedSize;
      canvas.Size = contentSize;
      viewport.Size = new Vector2I(
        Mathf.Max(1, Mathf.CeilToInt(contentSize.X)),
        Mathf.Max(1, Mathf.CeilToInt(contentSize.Y))
      );
      canvas.QueueRedraw();

      viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
      await WaitPostDrawFramesAsync(host, 2);

      var image = viewport.GetTexture()?.GetImage();
      if (image is null)
        return TableImageResult.Error("出图失败：离屏纹理为空（渲染未完成）。");

      var error = image.SavePng(outputPath);
      if (error != Error.Ok)
        return TableImageResult.Error($"出图失败：保存 PNG 出错（{error}）。");

      var fileSize = new FileInfo(outputPath).Exists ? new FileInfo(outputPath).Length : 0L;
      return TableImageResult.Success(
        outputPath,
        new Vector2I(image.GetWidth(), image.GetHeight()),
        fileSize
      );
    }
    finally
    {
      if (GodotObject.IsInstanceValid(viewport))
        viewport.QueueFree();
    }
  }

  /// <summary>取项目主题的默认字体与字号（与界面文字同源）。</summary>
  /// <returns>字体与字号；取不到字体时为 (null, 回退字号)。</returns>
  private static (Font? Font, int FontSize) ResolveThemeFont()
  {
    var probe = new Label();
    try
    {
      var font = probe.GetThemeFont("font");
      var fontSize = probe.GetThemeFontSize("font_size");
      return (font, fontSize > 0 ? fontSize : FallbackFontSize);
    }
    finally
    {
      probe.Free();
    }
  }

  /// <summary>等待若干次「帧绘制完成」，确保离屏目标已真实渲染。</summary>
  /// <param name="host">宿主节点。</param>
  /// <param name="count">等待帧数。</param>
  private static async Task WaitPostDrawFramesAsync(Node host, int count)
  {
    for (var i = 0; i < count; i++)
    {
      await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
  }
}

/// <summary>
/// 表格画布：按测量结果一次性绘制标题、表头、数据行与网格线。
/// </summary>
/// <remarks>
/// 只负责「把行/列/颜色画出来」，不含任何业务判定；行染色与文本均来自
/// <see cref="TableModel"/>（由 <see cref="TableModelBuilder"/> 产出）。
/// </remarks>
internal sealed partial class TableGridCanvas : Control
{
  private readonly string _title;
  private readonly Font _font;
  private readonly int _fontSize;
  private readonly int _titleFontSize;
  private readonly string[] _headers;
  private readonly TableColumnAlign[] _aligns;
  private readonly float[] _columnLefts;
  private readonly float[] _columnWidths;
  private readonly List<TableRow> _rows;
  private readonly float _titleBandHeight;
  private readonly float _rowHeight;

  /// <summary>创建画布并完成内容测量。</summary>
  /// <param name="model">表格渲染模型。</param>
  /// <param name="font">绘制字体。</param>
  /// <param name="fontSize">正文字号。</param>
  public TableGridCanvas(TableModel model, Font font, int fontSize)
  {
    _title = model.Title;
    _font = font;
    _fontSize = fontSize;
    _titleFontSize = fontSize + TableImageRenderer.TitleFontSizeDelta;
    _rows = new List<TableRow>(model.Rows);

    var columnCount = model.Columns.Count;
    _headers = new string[columnCount];
    _aligns = new TableColumnAlign[columnCount];
    _columnLefts = new float[columnCount];
    _columnWidths = new float[columnCount];

    for (var c = 0; c < columnCount; c++)
    {
      _headers[c] = model.Columns[c].Header;
      _aligns[c] = model.Columns[c].Align;
    }

    // 列宽 = 该列（表头 + 所有单元格）最长文本宽 + 左右内边距
    for (var c = 0; c < columnCount; c++)
    {
      var maxWidth = MeasureText(_headers[c], _fontSize).X;
      foreach (var row in _rows)
      {
        if (c >= row.Cells.Count)
          continue;

        maxWidth = Mathf.Max(maxWidth, MeasureText(row.Cells[c] ?? string.Empty, _fontSize).X);
      }

      _columnWidths[c] = Mathf.Max(
        TableImageRenderer.MinColumnWidth,
        Mathf.Ceil(maxWidth) + (TableImageRenderer.CellPaddingX * 2f)
      );
    }

    var totalWidth = 0f;
    for (var c = 0; c < columnCount; c++)
    {
      _columnLefts[c] = totalWidth;
      totalWidth += _columnWidths[c];
    }

    _titleBandHeight = string.IsNullOrEmpty(_title)
      ? 0f
      : Mathf.Ceil(_font.GetHeight(_titleFontSize)) + (TableImageRenderer.CellPaddingY * 2f);
    _rowHeight = Mathf.Ceil(_font.GetHeight(_fontSize)) + (TableImageRenderer.CellPaddingY * 2f);

    // 至少保证一行，避免零宽零高画布
    ComputedSize = new Vector2(
      Mathf.Max(1f, totalWidth),
      Mathf.Max(1f, _titleBandHeight + (_rowHeight * (1 + _rows.Count)))
    );
  }

  /// <summary>按内容测量得到的画布尺寸。</summary>
  public Vector2 ComputedSize { get; }

  /// <inheritdoc/>
  public override void _Draw()
  {
    var size = ComputedSize;
    DrawRect(new Rect2(Vector2.Zero, size), InfoTablePalette.Background, filled: true);

    var y = 0f;

    if (_titleBandHeight > 0f)
    {
      var titleBaseline = y + TableImageRenderer.CellPaddingY + _font.GetAscent(_titleFontSize);
      DrawString(
        _font,
        new Vector2(0f, titleBaseline),
        _title,
        HorizontalAlignment.Center,
        size.X,
        _titleFontSize,
        InfoTablePalette.Text
      );
      y += _titleBandHeight;
      DrawRect(new Rect2(0f, y, size.X, 0.001f), InfoTablePalette.Background, filled: true);
    }

    DrawRow(y, _headers, isHeader: true, isHighlighted: false);
    y += _rowHeight;

    foreach (var row in _rows)
    {
      var cells = new string[_headers.Length];
      for (var c = 0; c < cells.Length; c++)
        cells[c] = c < row.Cells.Count ? row.Cells[c] ?? string.Empty : string.Empty;

      DrawRow(y, cells, isHeader: false, isHighlighted: row.IsHighlighted);
      y += _rowHeight;
    }

    DrawGrid(size);
  }

  /// <summary>绘制一行（含底色与各单元格文本）。</summary>
  /// <param name="top">行顶 y 坐标。</param>
  /// <param name="cells">单元格文本。</param>
  /// <param name="isHeader">是否表头行。</param>
  /// <param name="isHighlighted">是否整行染绿。</param>
  private void DrawRow(float top, string[] cells, bool isHeader, bool isHighlighted)
  {
    var width =
      _columnLefts.Length == 0
        ? 0f
        : _columnLefts[_columnLefts.Length - 1] + _columnWidths[_columnWidths.Length - 1];

    var background =
      isHeader ? InfoTablePalette.HeaderBackground
      : isHighlighted ? InfoTablePalette.HighlightBackground
      : InfoTablePalette.Background;

    if (background != InfoTablePalette.Background)
      DrawRect(new Rect2(0f, top, width, _rowHeight), background, filled: true);

    var baseline = top + TableImageRenderer.CellPaddingY + _font.GetAscent(_fontSize);

    for (var c = 0; c < cells.Length && c < _headers.Length; c++)
    {
      var text = cells[c] ?? string.Empty;
      if (string.IsNullOrEmpty(text))
        continue;

      DrawString(
        _font,
        new Vector2(_columnLefts[c], baseline),
        text,
        ToHorizontalAlignment(_aligns[c]),
        _columnWidths[c],
        _fontSize,
        InfoTablePalette.Text
      );
    }
  }

  /// <summary>绘制网格线与外边框。</summary>
  /// <param name="size">画布尺寸。</param>
  private void DrawGrid(Vector2 size)
  {
    var lineWidth = 1f;
    var color = InfoTablePalette.Border;

    var y = _titleBandHeight;
    var bottom = size.Y;
    while (y <= bottom + 0.5f)
    {
      DrawLine(new Vector2(0f, y), new Vector2(size.X, y), color, lineWidth);
      y += _rowHeight;
    }

    foreach (var left in _columnLefts)
    {
      DrawLine(new Vector2(left, _titleBandHeight), new Vector2(left, bottom), color, lineWidth);
    }

    DrawLine(
      new Vector2(size.X - lineWidth, _titleBandHeight),
      new Vector2(size.X - lineWidth, bottom),
      color,
      lineWidth
    );

    if (_titleBandHeight > 0f)
    {
      DrawLine(
        new Vector2(0f, _titleBandHeight),
        new Vector2(size.X, _titleBandHeight),
        color,
        lineWidth
      );
    }

    DrawRect(new Rect2(Vector2.Zero, size), color, filled: false, width: lineWidth);
  }

  /// <summary>测量文本宽度（对齐方式与列宽无关，取自然宽度）。</summary>
  /// <param name="text">文本。</param>
  /// <param name="fontSize">字号。</param>
  /// <returns>文本尺寸。</returns>
  private Vector2 MeasureText(string text, int fontSize) =>
    string.IsNullOrEmpty(text)
      ? Vector2.Zero
      : _font.GetStringSize(text, HorizontalAlignment.Left, -1f, fontSize);

  /// <summary>列对齐方式 → Godot 文本对齐。</summary>
  /// <param name="align">列对齐方式。</param>
  /// <returns>Godot 文本对齐枚举。</returns>
  private static HorizontalAlignment ToHorizontalAlignment(TableColumnAlign align) =>
    align switch
    {
      TableColumnAlign.Center => HorizontalAlignment.Center,
      TableColumnAlign.Right => HorizontalAlignment.Right,
      _ => HorizontalAlignment.Left,
    };
}
