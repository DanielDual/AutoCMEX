namespace AutoCMEX.Helpers.Windows;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
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
  private const uint TymedIStream = 0x0004;
  private const uint DvaspectContent = 1;

  /// <summary>单个流格式的读取上限（字节）：防止异常流把内存吃满。</summary>
  private const int MaxStreamBytes = 64 * 1024 * 1024;

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

  /// <summary>CLSID_DragDropHelper：提供 IDragSourceHelper2 的系统拖影管理器。</summary>
  private const string DragImageHelperClassId = "4657278A-411B-11D2-839A-00C04FD918D0";

  /// <summary>COM FORMATETC：描述剪贴板/OLE 数据的格式与传输介质。</summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct FormatEtc
  {
    /// <summary>剪贴板格式 id（CF_* 或 RegisterClipboardFormat 返回值）。</summary>
    public ushort CfFormat;

    /// <summary>DVTARGETDEVICE 指针；本实现恒为 NULL。</summary>
    public IntPtr Ptd;

    /// <summary>数据方面（DVASPECT_CONTENT = 1）。</summary>
    public uint DwAspect;

    /// <summary>数据索引；-1 表示整个对象。</summary>
    public int Lindex;

    /// <summary>传输介质（TYMED_HGLOBAL = 1 / TYMED_ISTREAM = 4）。</summary>
    public uint Tymed;
  }

  /// <summary>COM STGMEDIUM：OLE 传输数据的载体（本实现用 HGLOBAL）。</summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct StgMedium
  {
    /// <summary>传输介质类型（TYMED_HGLOBAL / TYMED_ISTREAM 等）。</summary>
    public uint Tymed;

    /// <summary>介质句柄（HGLOBAL 时为全局内存句柄，IStream 时为接口指针）。</summary>
    public IntPtr UnionMember;

    /// <summary>负责释放介质的接口指针；通常为 NULL。</summary>
    public IntPtr PUnkForRelease;
  }

  /// <summary>SHDRAGIMAGE：拖影位图、尺寸与相对光标的热点。</summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct ShDragImage
  {
    /// <summary>拖影宽度（像素）。</summary>
    public int Cx;

    /// <summary>拖影高度（像素）。</summary>
    public int Cy;

    /// <summary>拖影左上角相对光标的横向偏移。</summary>
    public int XOffset;

    /// <summary>拖影左上角相对光标的纵向偏移。</summary>
    public int YOffset;

    /// <summary>拖影位图（32bpp 带 Alpha）。</summary>
    public IntPtr HBitmap;

    /// <summary>透明色键（Alpha 位图不用，置 0）。</summary>
    public uint CrColorKey;
  }

  /// <summary>OLE 数据对象（CF_HDROP/CF_DIB/PNG 的提供方；COM 互操作声明）。</summary>
  [ComImport]
  [Guid("0000010E-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IDataObject
  {
    /// <summary>按格式取出数据；成功时由调用方负责释放 STGMEDIUM。</summary>
    [PreserveSig]
    int GetData(ref FormatEtc pformatetcIn, out StgMedium pmedium);

    /// <summary>取数据到调用方提供的介质；本实现不支持。</summary>
    [PreserveSig]
    int GetDataHere(ref FormatEtc pformatetc, ref StgMedium pmedium);

    /// <summary>查询是否支持给定格式。</summary>
    [PreserveSig]
    int QueryGetData(ref FormatEtc pformatetc);

    /// <summary>返回规范化格式；本实现返回同一格式。</summary>
    [PreserveSig]
    int GetCanonicalFormatEtc(ref FormatEtc pformatectIn, out FormatEtc pformatetcOut);

    /// <summary>写入数据（拖影管理器通过它注入 DragImageBits）。</summary>
    /// <remarks><c>fRelease</c> 为 true 时介质所有权转移给本对象，由本对象负责释放。</remarks>
    [PreserveSig]
    int SetData(ref FormatEtc pformatetc, ref StgMedium pmedium, bool fRelease);

    /// <summary>枚举支持的格式。</summary>
    [PreserveSig]
    int EnumFormatEtc(uint dwDirection, out IEnumFORMATETC ppenumFormatEtc);

    /// <summary>订阅数据变更；本实现不支持。</summary>
    [PreserveSig]
    int DAdvise(ref FormatEtc pformatetc, uint advf, IntPtr pAdvSink, out uint pdwConnection);

    /// <summary>取消订阅；本实现不支持。</summary>
    [PreserveSig]
    int DUnadvise(uint dwConnection);

    /// <summary>枚举订阅；本实现不支持。</summary>
    [PreserveSig]
    int EnumDAdvise(out IntPtr ppenumAdvise);
  }

  /// <summary>COM IEnumFORMATETC：格式枚举器。</summary>
  [ComImport]
  [Guid("00000103-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IEnumFORMATETC
  {
    /// <summary>取出至多 celt 个格式（<c>rgelt</c> 为 FORMATETC 数组指针）。</summary>
    [PreserveSig]
    int Next(uint celt, IntPtr rgelt, IntPtr pceltFetched);

    /// <summary>跳过 celt 个格式。</summary>
    [PreserveSig]
    int Skip(uint celt);

    /// <summary>重置枚举位置。</summary>
    [PreserveSig]
    int Reset();

    /// <summary>复制一个同状态的枚举器。</summary>
    [PreserveSig]
    int Clone(out IEnumFORMATETC newEnum);
  }

  /// <summary>OLE 拖放源：决定拖放何时结束、光标反馈。</summary>
  [ComImport]
  [Guid("00000121-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IDropSource
  {
    /// <summary>按下 ESC 取消；松开左键完成拖放；其余继续。</summary>
    [PreserveSig]
    int QueryContinueDrag(int fEscapePressed, uint grfKeyState);

    /// <summary>使用系统默认拖放光标（小方框/加号/🚫 由系统绘制）。</summary>
    [PreserveSig]
    int GiveFeedback(uint dwEffect);
  }

  /// <summary>拖影管理器（IDragSourceHelper2）：把位图注册为跟随光标的拖影。</summary>
  [ComImport]
  [Guid("83E07D0D-0C5F-4163-BF1A-60B274051E40")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IDragSourceHelper2
  {
    /// <summary>用位图初始化拖影；成功后数据对象会带上 DragImageBits。</summary>
    [PreserveSig]
    int InitializeFromBitmap(ref ShDragImage pshdi, IDataObject pDataObject);

    /// <summary>用窗口截图初始化拖影；本实现不使用。</summary>
    [PreserveSig]
    int InitializeFromWindow(IntPtr hwnd, ref int ppt, IDataObject pDataObject);

    /// <summary>设置拖影行为标志；本实现不使用。</summary>
    [PreserveSig]
    int SetFlags(uint dwFlags);
  }

  /// <summary>CLSID_DragDropHelper 的 COM 类声明（用于创建拖影管理器实例）。</summary>
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

  [DllImport("ole32.dll", PreserveSig = true)]
  private static extern int CreateStreamOnHGlobal(
    IntPtr hGlobal,
    bool fDeleteOnRelease,
    out IntPtr ppstm
  );

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
        {
          if (!TrySetDragImage(dataObject, imageHandle, width, height, out var dragImageError))
            GD.PushWarning($"Win32DragOut: 拖影设置失败：{dragImageError}");
        }
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

  /// <summary>
  /// OLE 数据对象的托管实现：提供 CF_HDROP / CF_DIB / PNG，并接受拖影管理器注入的格式。
  /// </summary>
  /// <remarks>
  /// 由 <see cref="TryDragFiles"/> 创建并通过 CCW 传给 <c>DoDragDrop</c>；
  /// 所有方法都在 OLE 拖放循环线程（主线程）上调用。
  /// </remarks>
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

    /// <summary>构造数据对象。</summary>
    /// <param name="paths">拖出的文件路径（写入 CF_HDROP）。</param>
    /// <param name="image">作为位图提供的内容（为 null 时只提供文件格式）。</param>
    public DataObjectImpl(IReadOnlyList<string> paths, Image? image)
    {
      _hdrop = Win32Bitmap.BuildDropFilesBytes(paths);

      if (image is null)
        return;

      _dib = Win32Bitmap.BuildDibBytes(image);
      _png = Win32Bitmap.BuildPngBytes(image);
    }

    /// <inheritdoc/>
    /// <remarks>成功时每次新建 HGLOBAL，所有权交给调用方（由 OLE 释放）。</remarks>
    public int GetData(ref FormatEtc pformatetcIn, out StgMedium pmedium)
    {
      pmedium = default;

      var bytes = GetBytes(pformatetcIn);
      if (bytes is null)
        return DvEFormatEtc;

      // 拖影管理器注入的流式格式（如 DragContext）要求以 IStream 归还，
      // 否则外壳拿不到拖影上下文、不会绘制拖影。
      if ((pformatetcIn.Tymed & TymedIStream) != 0 && IsExtraStreamFormat(pformatetcIn))
        return CreateStreamMedium(bytes, out pmedium);

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

    /// <inheritdoc/>
    public int GetDataHere(ref FormatEtc pformatetc, ref StgMedium pmedium) => ENotImpl;

    /// <inheritdoc/>
    public int QueryGetData(ref FormatEtc pformatetc) =>
      IsSupported(pformatetc) ? SOK : DvEFormatEtc;

    /// <inheritdoc/>
    public int GetCanonicalFormatEtc(ref FormatEtc pformatectIn, out FormatEtc pformatetcOut)
    {
      pformatetcOut = pformatectIn;
      return DataSSameFormatEtc;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 拖影管理器用它注入 <c>DragImageBits</c>：HGLOBAL 内容会被拷贝保存，之后由 GetData 原样返回。
    /// <c>fRelease</c> 为 true 时调用 <c>ReleaseStgMedium</c> 归还调用方介质；未实现的辅助格式返回值成功但不保存。
    /// </remarks>
    public int SetData(ref FormatEtc pformatetc, ref StgMedium pmedium, bool fRelease)
    {
      try
      {
        if ((pmedium.Tymed & TymedHGlobal) != 0 && pmedium.UnionMember != IntPtr.Zero)
        {
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

        if ((pmedium.Tymed & TymedIStream) != 0)
        {
          var bytes = ReadStream(pmedium.UnionMember);
          if (bytes is null)
            return ENotImpl;

          _extraFormats.Add((pformatetc, bytes));
          return SOK;
        }

        return ENotImpl;
      }
      finally
      {
        if (fRelease)
          ReleaseStgMedium(ref pmedium);
      }
    }

    /// <inheritdoc/>
    public int EnumFormatEtc(uint dwDirection, out IEnumFORMATETC ppenumFormatEtc)
    {
      ppenumFormatEtc = new EnumFormatEtcImpl(BuildFormats());
      return dwDirection == DatadirGet ? SOK : ENotImpl;
    }

    /// <inheritdoc/>
    public int DAdvise(ref FormatEtc pformatetc, uint advf, IntPtr pAdvSink, out uint pdwConnection)
    {
      pdwConnection = 0;
      return OleEAdviseNotSupported;
    }

    /// <inheritdoc/>
    public int DUnadvise(uint dwConnection) => ENotImpl;

    /// <inheritdoc/>
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

      if (format.Tymed != 0 && (format.Tymed & (TymedHGlobal | TymedIStream)) == 0)
        return false;

      // 拖影由 IDragSourceHelper 通过 SetData 注入，注入前也要对该格式回答“支持”
      if (DragImageBitsFormat != 0 && format.CfFormat == DragImageBitsFormat)
        return true;

      return GetBytes(format) is not null;
    }

    private bool IsExtraStreamFormat(FormatEtc format)
    {
      foreach (var extra in _extraFormats)
      {
        if (
          extra.Format.CfFormat == format.CfFormat
          && extra.Format.DwAspect == format.DwAspect
          && extra.Format.Lindex == format.Lindex
          && (extra.Format.Tymed & TymedIStream) != 0
        )
          return true;
      }

      return false;
    }

    private int CreateStreamMedium(byte[] bytes, out StgMedium medium)
    {
      medium = default;

      var handle = AllocateHGlobal(bytes);
      if (handle == IntPtr.Zero)
        return unchecked((int)0x8007000E); // E_OUTOFMEMORY

      var result = CreateStreamOnHGlobal(handle, true, out var stream);
      if (result < 0 || stream == IntPtr.Zero)
      {
        Win32Bitmap.GlobalFree(handle);
        return result < 0 ? result : unchecked((int)0x80004005); // E_FAIL
      }

      medium = new StgMedium
      {
        Tymed = TymedIStream,
        UnionMember = stream,
        PUnkForRelease = IntPtr.Zero,
      };
      return SOK;
    }

    private static byte[]? ReadStream(IntPtr streamPointer)
    {
      if (streamPointer == IntPtr.Zero)
        return null;

      object? comObject = null;
      try
      {
        comObject = Marshal.GetObjectForIUnknown(streamPointer);
        if (comObject is not IStream stream)
          return null;

        var buffer = new byte[16 * 1024];
        var readPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
          using var collected = new System.IO.MemoryStream();
          while (true)
          {
            Marshal.WriteInt32(readPointer, 0);
            stream.Read(buffer, buffer.Length, readPointer);
            var read = Math.Clamp(Marshal.ReadInt32(readPointer), 0, buffer.Length);
            if (read == 0)
              break;

            collected.Write(buffer, 0, read);
            if (collected.Length > MaxStreamBytes)
              return null;
          }

          return collected.ToArray();
        }
        finally
        {
          Marshal.FreeHGlobal(readPointer);
        }
      }
      catch
      {
        return null;
      }
      finally
      {
        if (comObject is not null && Marshal.IsComObject(comObject))
          Marshal.ReleaseComObject(comObject);
      }
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

  /// <summary>OLE 格式枚举器的托管实现（只读队列，供 <c>IDataObject.EnumFormatEtc</c> 返回）。</summary>
  [ComVisible(true)]
  public sealed class EnumFormatEtcImpl : IEnumFORMATETC
  {
    private readonly FormatEtc[] _formats;
    private int _index;

    /// <summary>创建枚举器。</summary>
    /// <param name="formats">要枚举的格式列表。</param>
    public EnumFormatEtcImpl(FormatEtc[] formats)
    {
      _formats = formats ?? Array.Empty<FormatEtc>();
    }

    /// <inheritdoc/>
    /// <remarks><paramref name="rgelt"/> 为 FORMATETC 数组指针，本方法直接写入非托管内存。</remarks>
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

    /// <inheritdoc/>
    public int Skip(uint celt)
    {
      _index = Math.Min(_formats.Length, _index + (int)celt);
      return SOK;
    }

    /// <inheritdoc/>
    public int Reset()
    {
      _index = 0;
      return SOK;
    }

    /// <inheritdoc/>
    public int Clone(out IEnumFORMATETC newEnum)
    {
      newEnum = new EnumFormatEtcImpl(_formats);
      return SOK;
    }
  }

  /// <summary>OLE 拖放源的托管实现：左键松开完成、ESC 取消，光标交回系统默认。</summary>
  [ComVisible(true)]
  public sealed class DropSourceImpl : IDropSource
  {
    /// <inheritdoc/>
    public int QueryContinueDrag(int fEscapePressed, uint grfKeyState)
    {
      if (fEscapePressed != 0)
        return DragdropSCancel;

      if ((grfKeyState & MkLbutton) == 0)
        return DragdropSDrop;

      return SOK;
    }

    /// <inheritdoc/>
    public int GiveFeedback(uint dwEffect) => DragdropSUseDefaultCursors;
  }
}
