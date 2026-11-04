namespace AutoCMEX.Helpers.Windows;

using System;
using System.Runtime.InteropServices;
using Godot;

/// <summary>
/// 把图片写入 Windows 系统剪贴板（CF_DIB + 注册格式 PNG）。
/// </summary>
/// <remarks>
/// 剪贴板必须由当前线程 <c>OpenClipboard</c>；调用方负责在主线程调用。
/// 同时写两种格式：<c>CF_DIB</c> 兼容画图/浏览器等老程序，注册格式 <c>PNG</c> 保留透明通道与更高质量。
/// </remarks>
public static class Win32ClipboardImage
{
  private const uint PngFormatName = 0;

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool OpenClipboard(IntPtr hWndNewOwner);

  [DllImport("user32.dll")]
  private static extern bool CloseClipboard();

  [DllImport("user32.dll")]
  private static extern bool EmptyClipboard();

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern uint RegisterClipboardFormatW(string lpszFormat);

  /// <summary>
  /// 把图片写入系统剪贴板。
  /// </summary>
  /// <param name="image">要复制的图片（Godot 图片，任意格式，内部会转 RGBA8）。</param>
  /// <param name="error">失败原因（单行，可直接展示）；成功时为空串。</param>
  /// <returns>是否成功。</returns>
  public static bool TryCopy(Image image, out string error)
  {
    error = string.Empty;

    if (!OperatingSystem.IsWindows())
    {
      error = "当前平台暂不支持复制图片到剪贴板。";
      return false;
    }

    if (image is null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
    {
      error = "复制失败：图片为空。";
      return false;
    }

    byte[] dib;
    byte[] png;
    try
    {
      dib = Win32Bitmap.BuildDibBytes(image);
      png = Win32Bitmap.BuildPngBytes(image);
    }
    catch (Exception ex)
    {
      error = $"复制失败：图片转换出错（{ex.Message}）。";
      return false;
    }

    var opened = false;
    try
    {
      for (var attempt = 0; attempt < 2 && !opened; attempt++)
      {
        opened = OpenClipboard(IntPtr.Zero);
        if (!opened && attempt == 0)
          System.Threading.Thread.Sleep(10);
      }

      if (!opened)
      {
        error = $"复制失败：剪贴板正被其它程序占用（Win32 错误 {Marshal.GetLastWin32Error()}）。";
        return false;
      }

      if (!EmptyClipboard())
      {
        error = "复制失败：无法清空剪贴板。";
        return false;
      }

      var dibHandle = AllocateGlobal(dib);
      if (dibHandle == IntPtr.Zero)
      {
        error = "复制失败：分配 DIB 内存失败。";
        return false;
      }

      if (SetClipboardData(Win32Bitmap.CfDib, dibHandle) == IntPtr.Zero)
      {
        Win32Bitmap.GlobalFree(dibHandle);
        error = "复制失败：写入 DIB 剪贴板数据失败。";
        return false;
      }

      // CF_DIB 已成功；PNG 是增强格式，写入失败不影响整体成功
      var pngFormat = RegisterClipboardFormatW("PNG");
      if (pngFormat != PngFormatName)
      {
        var pngHandle = AllocateGlobal(png);
        if (pngHandle != IntPtr.Zero && SetClipboardData(pngFormat, pngHandle) == IntPtr.Zero)
          Win32Bitmap.GlobalFree(pngHandle);
      }

      return true;
    }
    finally
    {
      if (opened)
        CloseClipboard();
    }
  }

  private static IntPtr AllocateGlobal(byte[] bytes)
  {
    var handle = Win32Bitmap.GlobalAlloc(Win32Bitmap.GmemMoveable, (UIntPtr)bytes.Length);
    if (handle == IntPtr.Zero)
      return IntPtr.Zero;

    var pointer = Win32Bitmap.GlobalLock(handle);
    if (pointer == IntPtr.Zero)
    {
      Win32Bitmap.GlobalFree(handle);
      return IntPtr.Zero;
    }

    try
    {
      Marshal.Copy(bytes, 0, pointer, bytes.Length);
    }
    catch
    {
      Win32Bitmap.GlobalFree(handle);
      return IntPtr.Zero;
    }
    finally
    {
      Win32Bitmap.GlobalUnlock(handle);
    }

    return handle;
  }
}
