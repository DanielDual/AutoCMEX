namespace AutoCMEX.Helpers.Windows;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Godot;

/// <summary>
/// Windows 位图数据转换：Godot <see cref="Image"/> ↔ DIB / HBITMAP / HGLOBAL / PNG 字节。
/// </summary>
/// <remarks>
/// 只做格式转换与 Win32 资源创建，不涉及拖拽/剪贴板流程编排；
/// 所有权规则由调用方负责（HBITMAP 用 <c>DeleteObject</c>，HGLOBAL 交给剪贴板后不可再释放）。
/// </remarks>
internal static class Win32Bitmap
{
  /// <summary>CF_DIB 格式（32bpp BI_RGB）。</summary>
  public const ushort CfDib = 8;

  /// <summary>CF_HDROP 格式（文件拖放列表）。</summary>
  public const ushort CfHdrop = 15;

  /// <summary>GMEM_MOVEABLE：剪贴板/OLE 要求的可移动全局内存。</summary>
  public const uint GmemMoveable = 0x0002;

  /// <summary>TYMED_HGLOBAL：OLE 传输介质类型。</summary>
  public const uint TymedHGlobal = 0x0001;

  private const uint DibRgbColors = 0;
  private const uint BiRgb = 0;

  [StructLayout(LayoutKind.Sequential)]
  private struct BitmapInfoHeader
  {
    public uint BiSize;
    public int BiWidth;
    public int BiHeight;
    public ushort BiPlanes;
    public ushort BiBitCount;
    public uint BiCompression;
    public uint BiSizeImage;
    public int BiXPelsPerMeter;
    public int BiYPelsPerMeter;
    public uint BiClrUsed;
    public uint BiClrImportant;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct BitmapInfo
  {
    public BitmapInfoHeader Header;
    public uint Colors;
  }

  [DllImport("gdi32.dll", SetLastError = true)]
  private static extern IntPtr CreateDIBSection(
    IntPtr hdc,
    ref BitmapInfo pbmi,
    uint usage,
    out IntPtr ppvBits,
    IntPtr hSection,
    uint offset
  );

  [DllImport("gdi32.dll")]
  private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

  [DllImport("gdi32.dll")]
  private static extern bool DeleteDC(IntPtr hdc);

  [DllImport("gdi32.dll")]
  private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

  [DllImport("gdi32.dll")]
  private static extern bool DeleteObject(IntPtr hObject);

  [DllImport("user32.dll")]
  private static extern IntPtr GetDC(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr GlobalLock(IntPtr hMem);

  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool GlobalUnlock(IntPtr hMem);

  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern UIntPtr GlobalSize(IntPtr hMem);

  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr GlobalFree(IntPtr hMem);

  /// <summary>
  /// 把 Godot 图片转成 CF_DIB 字节（BITMAPINFOHEADER + 自底向上 32bpp BGRA）。
  /// </summary>
  /// <param name="image">源图片。</param>
  /// <returns>可直接写入 HGLOBAL 的 DIB 字节。</returns>
  public static byte[] BuildDibBytes(Image image)
  {
    ArgumentNullException.ThrowIfNull(image);

    var rgba = EnsureRgba8(image);
    var width = rgba.GetWidth();
    var height = rgba.GetHeight();
    var source = rgba.GetData();
    var stride = width * 4;
    var result = new byte[40 + (stride * height)];

    WriteUInt32(result, 0, 40);
    WriteInt32(result, 4, width);
    WriteInt32(result, 8, height);
    WriteUInt16(result, 12, 1);
    WriteUInt16(result, 14, 32);
    WriteUInt32(result, 16, BiRgb);
    WriteUInt32(result, 20, (uint)(stride * height));
    WriteInt32(result, 24, 0);
    WriteInt32(result, 28, 0);
    WriteUInt32(result, 32, 0);
    WriteUInt32(result, 36, 0);

    for (var y = 0; y < height; y++)
    {
      var sourceRow = y * stride;
      var targetRow = 40 + ((height - 1 - y) * stride);
      for (var x = 0; x < width; x++)
      {
        var s = sourceRow + (x * 4);
        var t = targetRow + (x * 4);
        result[t] = source[s + 2];
        result[t + 1] = source[s + 1];
        result[t + 2] = source[s];
        // CF_DIB 为 BI_RGB，Alpha 字节不参与合成；统一写 255，避免目标程序把透明读成黑底
        result[t + 3] = 255;
      }
    }

    return result;
  }

  /// <summary>
  /// 把 Godot 图片转成 PNG 字节（注册格式 <c>PNG</c> 用）。
  /// </summary>
  /// <param name="image">源图片。</param>
  /// <returns>PNG 字节。</returns>
  public static byte[] BuildPngBytes(Image image)
  {
    ArgumentNullException.ThrowIfNull(image);
    return image.SavePngToBuffer();
  }

  /// <summary>
  /// 构造 CF_HDROP 字节（DROPFILES 头 + UTF-16 双空结尾路径列表）。
  /// </summary>
  /// <param name="paths">文件绝对路径列表。</param>
  /// <returns>可直接写入 HGLOBAL 的 CF_HDROP 字节。</returns>
  public static byte[] BuildDropFilesBytes(IReadOnlyList<string> paths)
  {
    ArgumentNullException.ThrowIfNull(paths);

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

    // DROPFILES：pFiles(4) + pt(8) + fNC(4) + fWide(4) = 20 字节
    writer.Write((uint)20);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(1);

    foreach (var path in paths)
    {
      writer.Write(Encoding.Unicode.GetBytes(Path.GetFullPath(path)));
      writer.Write((ushort)0);
    }

    writer.Write((ushort)0);
    return stream.ToArray();
  }

  /// <summary>
  /// 创建 32bpp 自顶向下 HBITMAP（带 Alpha 通道），用作 OLE 拖拽缩略图。
  /// </summary>
  /// <param name="image">源图片。</param>
  /// <param name="width">创建出的位图宽度。</param>
  /// <param name="height">创建出的位图高度。</param>
  /// <returns>HBITMAP；失败时为 <see cref="IntPtr.Zero"/>，调用方负责 <c>DeleteObject</c>。</returns>
  public static IntPtr CreateHBitmap(Image image, out int width, out int height)
  {
    ArgumentNullException.ThrowIfNull(image);

    var rgba = EnsureRgba8(image);
    width = rgba.GetWidth();
    height = rgba.GetHeight();
    if (width <= 0 || height <= 0)
      return IntPtr.Zero;

    var source = rgba.GetData();
    var stride = width * 4;
    var buffer = new byte[stride * height];

    for (var y = 0; y < height; y++)
    {
      var sourceRow = y * stride;
      var targetRow = y * stride;
      for (var x = 0; x < width; x++)
      {
        var s = sourceRow + (x * 4);
        var t = targetRow + (x * 4);
        buffer[t] = source[s + 2];
        buffer[t + 1] = source[s + 1];
        buffer[t + 2] = source[s];
        buffer[t + 3] = source[s + 3];
      }
    }

    var header = new BitmapInfo
    {
      Header = new BitmapInfoHeader
      {
        BiSize = 40,
        BiWidth = width,
        BiHeight = -height,
        BiPlanes = 1,
        BiBitCount = 32,
        BiCompression = BiRgb,
        BiSizeImage = (uint)(stride * height),
      },
    };

    var screenDc = GetDC(IntPtr.Zero);
    if (screenDc == IntPtr.Zero)
      return IntPtr.Zero;

    var memoryDc = CreateCompatibleDC(screenDc);
    if (memoryDc == IntPtr.Zero)
    {
      _ = ReleaseDC(IntPtr.Zero, screenDc);
      return IntPtr.Zero;
    }

    try
    {
      var hBitmap = CreateDIBSection(
        memoryDc,
        ref header,
        DibRgbColors,
        out var bits,
        IntPtr.Zero,
        0
      );
      if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
      {
        if (hBitmap != IntPtr.Zero)
          DeleteObject(hBitmap);
        return IntPtr.Zero;
      }

      try
      {
        Marshal.Copy(buffer, 0, bits, buffer.Length);
        return hBitmap;
      }
      catch
      {
        DeleteObject(hBitmap);
        return IntPtr.Zero;
      }
    }
    finally
    {
      DeleteDC(memoryDc);
      _ = ReleaseDC(IntPtr.Zero, screenDc);
    }
  }

  /// <summary>删除 HBITMAP（吞掉重复删除引发的异常）。</summary>
  /// <param name="hBitmap">位图句柄。</param>
  public static void DeleteHBitmap(IntPtr hBitmap)
  {
    if (hBitmap != IntPtr.Zero)
      DeleteObject(hBitmap);
  }

  private static Image EnsureRgba8(Image image)
  {
    if (image.GetFormat() == Image.Format.Rgba8)
      return image;

    var copy = (Image)image.Duplicate();
    copy.Convert(Image.Format.Rgba8);
    return copy;
  }

  private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
    BitConverter.GetBytes(value).CopyTo(buffer, offset);

  private static void WriteInt32(byte[] buffer, int offset, int value) =>
    BitConverter.GetBytes(value).CopyTo(buffer, offset);

  private static void WriteUInt16(byte[] buffer, int offset, ushort value) =>
    BitConverter.GetBytes(value).CopyTo(buffer, offset);
}
