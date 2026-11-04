namespace AutoCMEX.Helpers.Windows;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

/// <summary>
/// 用 Windows OLE 把文件/图片拖出窗口（拖到资源管理器得到 PNG 文件，拖到聊天软件可作为图片）。
/// </summary>
/// <remarks>
/// <para>
/// 数据对象同时提供 <c>CF_HDROP</c>（文件）、<c>CF_DIB</c>（位图）与注册格式 <c>PNG</c>：
/// 资源管理器按文件接收，聊天软件通常取位图；同时保留 PNG 以尽量保留清晰度。
/// </para>
/// <para>
/// 拖放光标（小方框/绿色加号/🚫）由 OLE 默认反馈绘制，本类只提供拖影；
/// <c>DoDragDrop</c> 在 Windows 上是同步调用，拖拽期间主线程被系统拖放循环占用（松手恢复）。
/// </para>
/// <para>必须在主线程调用；非 Windows 直接返回失败。</para>
/// </remarks>
public static class Win32DragOut
{
  private const uint DropeffectCopy = 1;
  private const uint DropeffectNone = 0;
  private const uint MkLbutton = 0x0001;
  private const uint DatadirGet = 1;
  private const uint TymedHGlobal = 0x0001;
  private const uint DvaspectContent = 1;

  private const int SOK = 0;
  private const int SFalse = 1;
  private const int ENotImpl = unchecked((int)0x80004001);
  private const int DvEFormatEtc = unchecked((int)0x80040064);
  private const int OleEAdviseNotSupported = unchecked((int)0x80040003);
  private const int DataSSameFormatEtc = 0x00040130;
  private const int DragdropSDrop = 0x00040100;
  private const int DragdropSCancel = 0x00040101;
  private const int DragdropSUseDefaultCursors = 0x00040102;

  /// <summary>拖影缩略图最大边长（像素）；按原图等比缩放。</summary>
  private const int DragImageMaxSize = 240;

  private const string DragImageHelperClassId = "4657278A-411B-11D2-839A-00C04FD918D0";

  [StructLayout(LayoutKind.Sequential)]
  public struct FormatEtc
  {
    public ushort CfFormat;
    public IntPtr Ptd;
    public uint DwAspect;
    public int Lindex;
    public uint Tymed;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct StgMedium
  {
    public uint Tymed;
    public IntPtr UnionMember;
    public IntPtr PUnkForRelease;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct ShDragImage
  {
    public int Cx;
    public int Cy;
    public int XOffset;
    public int YOffset;
    public IntPtr HBitmap;
    public uint CrColorKey;
  }

  [ComImport]
  [Guid("0000010E-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IDataObject
  {
    [PreserveSig]
    int GetData(ref FormatEtc pformatetcIn, out StgMedium pmedium);

    [PreserveSig]
    int GetDataHere(ref FormatEtc pformatetc, ref StgMedium pmedium);

    [PreserveSig]
    int QueryGetData(ref FormatEtc pformatetc);

    [PreserveSig]
    int GetCanonicalFormatEtc(ref FormatEtc pformatectIn, out FormatEtc pformatetcOut);

    [PreserveSig]
    int SetData(ref FormatEtc pformatetc, ref StgMedium pmedium, bool fRelease);

    [PreserveSig]
    int EnumFormatEtc(uint dwDirection, out IEnumFORMATETC ppenumFormatEtc);

    [PreserveSig]
    int DAdvise(ref FormatEtc pformatetc, uint advf, IntPtr pAdvSink, out uint pdwConnection);

    [PreserveSig]
    int DUnadvise(uint dwConnection);

    [PreserveSig]
    int EnumDAdvise(out IntPtr ppenumAdvise);
  }

  [ComImport]
  [Guid("00000103-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IEnumFORMATETC
  {
    [PreserveSig]
    int Next(uint celt, IntPtr rgelt, IntPtr pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumFORMATETC newEnum);
  }

  [ComImport]
  [Guid("00000121-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IDropSource
  {
    [PreserveSig]
    int QueryContinueDrag(int fEscapePressed, uint grfKeyState);

    [PreserveSig]
    int GiveFeedback(uint dwEffect);
  }

  [ComImport]
  [Guid("83E07D0D-0C5F-4163-BF1A-60B274051E40")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IDragSourceHelper2
  {
    [PreserveSig]
    int InitializeFromBitmap(ref ShDragImage pshdi, IDataObject pDataObject);

    [PreserveSig]
    int InitializeFromWindow(IntPtr hwnd, ref int ppt, IDataObject pDataObject);

    [PreserveSig]
    int SetFlags(uint dwFlags);
  }

  [ComImport]
  [Guid(DragImageHelperClassId)]
  public class DragDropHelper { }

  [DllImport("ole32.dll", PreserveSig = true)]
  private static extern int DoDragDrop(
    [MarshalAs(UnmanagedType.Interface)] IDataObject pDataObj,
    [MarshalAs(UnmanagedType.Interface)] IDropSource pDropSource,
    uint dwOKEffects,
    out uint pdwEffect
  );

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern uint RegisterClipboardFormatW(string lpszFormat);

  [DllImport("ole32.dll")]
  private static extern void ReleaseStgMedium(ref StgMedium pmedium);

  /// <summary>
  /// 发起一次系统拖放。
  /// </summary>
  /// <param name="paths">要拖出的文件绝对路径（需已存在）。</param>
  /// <param name="dragImage">拖影缩略图（可空：为空时只显示系统默认拖放光标）。</param>
  /// <param name="error">失败原因（单行，可直接展示）；成功时为空串。</param>
  /// <returns>是否成功完成拖放（用户取消也视为成功完成）。</returns>
  public static bool TryDragFiles(IReadOnlyList<string> paths, Image? dragImage, out string error)
  {
    error = string.Empty;

    if (!OperatingSystem.IsWindows())
    {
      error = "当前平台暂不支持拖拽导出。";
      return false;
    }

    if (paths is null || paths.Count == 0)
    {
      error = "拖拽失败：没有可拖出的文件。";
      return false;
    }

    foreach (var path in paths)
    {
      if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
      {
        error = $"拖拽失败：文件不存在（{path}）。";
        return false;
      }
    }

    var imageHandle = IntPtr.Zero;
    try
    {
      var dataObject = new DataObjectImpl(paths, dragImage);

      if (dragImage is not null)
      {
        var preview = ScaleDragImage(dragImage);
        imageHandle = Win32Bitmap.CreateHBitmap(preview, out var width, out var height);
        if (imageHandle != IntPtr.Zero)
          _ = TrySetDragImage(dataObject, imageHandle, width, height, out _);
      }

      var result = DoDragDrop(dataObject, new DropSourceImpl(), DropeffectCopy, out var effect);
      if (result < 0)
      {
        error = $"拖拽失败（HRESULT 0x{result:X8}）。";
        return false;
      }

      if (effect == DropeffectNone)
      {
        // 取消/目标拒绝：不是错误，静默结束
        return true;
      }

      return true;
    }
    catch (Exception ex)
    {
      error = $"拖拽失败：{ex.Message}";
      return false;
    }
    finally
    {
      if (imageHandle != IntPtr.Zero)
        Win32Bitmap.DeleteHBitmap(imageHandle);
    }
  }

  /// <summary>把缩略图位图设置为拖影（供拖拽流程与单测使用）。</summary>
  /// <param name="dataObject">已构造的数据对象。</param>
  /// <param name="hBitmap">缩略图位图。</param>
  /// <param name="width">位图宽度。</param>
  /// <param name="height">位图高度。</param>
  /// <returns>是否设置成功；失败时拖拽仍可继续（无拖影）。</returns>
  internal static bool TrySetDragImage(
    DataObjectImpl dataObject,
    IntPtr hBitmap,
    int width,
    int height,
    out string error
  )
  {
    error = string.Empty;

    try
    {
      var helper = (IDragSourceHelper2)new DragDropHelper();
      var image = new ShDragImage
      {
        Cx = width,
        Cy = height,
        XOffset = width / 2,
        YOffset = height / 2,
        HBitmap = hBitmap,
        CrColorKey = 0,
      };

      var result = helper.InitializeFromBitmap(ref image, dataObject);
      if (result < 0)
        error = $"拖影初始化失败（HRESULT 0x{result:X8}）。";
      return result >= 0;
    }
    catch (Exception ex)
    {
      // 拖影不是必需能力：设置失败时继续走系统默认光标
      error = ex.Message;
      return false;
    }
  }

  private static Image ScaleDragImage(Image image)
  {
    var width = image.GetWidth();
    var height = image.GetHeight();
    if (width <= DragImageMaxSize && height <= DragImageMaxSize)
      return image;

    var scale = (float)DragImageMaxSize / Math.Max(width, height);
    var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
    var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

    var copy = (Image)image.Duplicate();
    copy.Resize(targetWidth, targetHeight, Image.Interpolation.Bilinear);
    return copy;
  }

  private static IntPtr AllocateHGlobal(byte[] bytes)
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

  [ComVisible(true)]
  public sealed class DataObjectImpl : IDataObject
  {
    private static readonly ushort PngFormat = (ushort)RegisterClipboardFormatW("PNG");

    private static readonly ushort DragImageBitsFormat = (ushort)RegisterClipboardFormatW(
      "DragImageBits"
    );

    private readonly byte[] _hdrop;
    private readonly byte[]? _dib;
    private readonly byte[]? _png;
    private readonly List<(FormatEtc Format, byte[] Bytes)> _extraFormats = new();

    public DataObjectImpl(IReadOnlyList<string> paths, Image? image)
    {
      _hdrop = Win32Bitmap.BuildDropFilesBytes(paths);

      if (image is null)
        return;

      _dib = Win32Bitmap.BuildDibBytes(image);
      _png = Win32Bitmap.BuildPngBytes(image);
    }

    public int GetData(ref FormatEtc pformatetcIn, out StgMedium pmedium)
    {
      pmedium = default;

      var bytes = GetBytes(pformatetcIn);
      if (bytes is null)
        return DvEFormatEtc;

      var handle = AllocateHGlobal(bytes);
      if (handle == IntPtr.Zero)
        return unchecked((int)0x8007000E); // E_OUTOFMEMORY

      pmedium = new StgMedium
      {
        Tymed = TymedHGlobal,
        UnionMember = handle,
        PUnkForRelease = IntPtr.Zero,
      };
      return SOK;
    }

    public int GetDataHere(ref FormatEtc pformatetc, ref StgMedium pmedium) => ENotImpl;

    public int QueryGetData(ref FormatEtc pformatetc) =>
      IsSupported(pformatetc) ? SOK : DvEFormatEtc;

    public int GetCanonicalFormatEtc(ref FormatEtc pformatectIn, out FormatEtc pformatetcOut)
    {
      pformatetcOut = pformatectIn;
      return DataSSameFormatEtc;
    }

    public int SetData(ref FormatEtc pformatetc, ref StgMedium pmedium, bool fRelease)
    {
      try
      {
        // 非 HGLOBAL 格式（如拖影管理器附带设置的流式上下文）当前不消费；
        // 返回成功让拖影初始化继续，避免整个拖影因未实现的辅助格式被放弃。
        if ((pmedium.Tymed & TymedHGlobal) == 0 || pmedium.UnionMember == IntPtr.Zero)
          return SOK;

        var size = (int)Win32Bitmap.GlobalSize(pmedium.UnionMember);
        if (size <= 0)
          return ENotImpl;

        var pointer = Win32Bitmap.GlobalLock(pmedium.UnionMember);
        if (pointer == IntPtr.Zero)
          return ENotImpl;

        var bytes = new byte[size];
        try
        {
          Marshal.Copy(pointer, bytes, 0, size);
        }
        finally
        {
          Win32Bitmap.GlobalUnlock(pmedium.UnionMember);
        }

        _extraFormats.Add((pformatetc, bytes));
        return SOK;
      }
      finally
      {
        if (fRelease)
          ReleaseStgMedium(ref pmedium);
      }
    }

    public int EnumFormatEtc(uint dwDirection, out IEnumFORMATETC ppenumFormatEtc)
    {
      ppenumFormatEtc = new EnumFormatEtcImpl(BuildFormats());
      return dwDirection == DatadirGet ? SOK : ENotImpl;
    }

    public int DAdvise(ref FormatEtc pformatetc, uint advf, IntPtr pAdvSink, out uint pdwConnection)
    {
      pdwConnection = 0;
      return OleEAdviseNotSupported;
    }

    public int DUnadvise(uint dwConnection) => ENotImpl;

    public int EnumDAdvise(out IntPtr ppenumAdvise)
    {
      ppenumAdvise = IntPtr.Zero;
      return OleEAdviseNotSupported;
    }

    private FormatEtc[] BuildFormats()
    {
      var formats = new List<FormatEtc>
      {
        new()
        {
          CfFormat = Win32Bitmap.CfHdrop,
          DwAspect = DvaspectContent,
          Lindex = -1,
          Tymed = TymedHGlobal,
        },
      };

      if (_dib is not null)
      {
        formats.Add(
          new FormatEtc
          {
            CfFormat = Win32Bitmap.CfDib,
            DwAspect = DvaspectContent,
            Lindex = -1,
            Tymed = TymedHGlobal,
          }
        );
      }

      if (_png is not null && PngFormat != 0)
      {
        formats.Add(
          new FormatEtc
          {
            CfFormat = PngFormat,
            DwAspect = DvaspectContent,
            Lindex = -1,
            Tymed = TymedHGlobal,
          }
        );
      }

      foreach (var extra in _extraFormats)
        formats.Add(extra.Format);

      return formats.ToArray();
    }

    private bool IsSupported(FormatEtc format)
    {
      if (format.DwAspect != DvaspectContent || format.Lindex != -1)
        return false;

      if (format.Tymed != 0 && (format.Tymed & TymedHGlobal) == 0)
        return false;

      // 拖影由 IDragSourceHelper 通过 SetData 注入，注入前也要对该格式回答“支持”
      if (DragImageBitsFormat != 0 && format.CfFormat == DragImageBitsFormat)
        return true;

      return GetBytes(format) is not null;
    }

    private byte[]? GetBytes(FormatEtc format)
    {
      if (format.CfFormat == Win32Bitmap.CfHdrop)
        return _hdrop;

      if (format.CfFormat == Win32Bitmap.CfDib)
        return _dib;

      if (PngFormat != 0 && format.CfFormat == PngFormat)
        return _png;

      foreach (var extra in _extraFormats)
      {
        if (
          extra.Format.CfFormat == format.CfFormat
          && extra.Format.DwAspect == format.DwAspect
          && extra.Format.Lindex == format.Lindex
        )
          return extra.Bytes;
      }

      return null;
    }
  }

  [ComVisible(true)]
  public sealed class EnumFormatEtcImpl : IEnumFORMATETC
  {
    private readonly FormatEtc[] _formats;
    private int _index;

    public EnumFormatEtcImpl(FormatEtc[] formats)
    {
      _formats = formats ?? Array.Empty<FormatEtc>();
    }

    public int Next(uint celt, IntPtr rgelt, IntPtr pceltFetched)
    {
      var size = Marshal.SizeOf<FormatEtc>();
      var fetched = 0;

      for (var i = 0; i < celt && _index < _formats.Length; i++)
      {
        Marshal.StructureToPtr(_formats[_index], IntPtr.Add(rgelt, i * size), false);
        _index++;
        fetched++;
      }

      if (pceltFetched != IntPtr.Zero)
        Marshal.WriteInt32(pceltFetched, fetched);

      return fetched == celt ? SOK : SFalse;
    }

    public int Skip(uint celt)
    {
      _index = Math.Min(_formats.Length, _index + (int)celt);
      return SOK;
    }

    public int Reset()
    {
      _index = 0;
      return SOK;
    }

    public int Clone(out IEnumFORMATETC newEnum)
    {
      newEnum = new EnumFormatEtcImpl(_formats);
      return SOK;
    }
  }

  [ComVisible(true)]
  public sealed class DropSourceImpl : IDropSource
  {
    public int QueryContinueDrag(int fEscapePressed, uint grfKeyState)
    {
      if (fEscapePressed != 0)
        return DragdropSCancel;

      if ((grfKeyState & MkLbutton) == 0)
        return DragdropSDrop;

      return SOK;
    }

    public int GiveFeedback(uint dwEffect) => DragdropSUseDefaultCursors;
  }
}
