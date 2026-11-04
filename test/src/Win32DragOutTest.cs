namespace AutoCMEX;

using System;
using System.IO;
using System.Runtime.InteropServices;
using AutoCMEX.Helpers.Windows;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="Win32DragOut"/> 单测：数据对象格式与拖影初始化（不触发真实 DoDragDrop）。
/// </summary>
/// <remarks>
/// 真实拖拽需要鼠标按住并被系统拖放循环接管，无法在单测中模拟；这里覆盖最容易出错的两段：
/// 数据对象格式（CF_HDROP/CF_DIB）与 <c>IDragSourceHelper</c> 的 COM 包装。
/// </remarks>
public class Win32DragOutTest : TestClass
{
  public Win32DragOutTest(Node testScene)
    : base(testScene) { }

  private static Image CreateImage()
  {
    var image = Image.CreateEmpty(20, 10, false, Image.Format.Rgba8);
    image.Fill(Colors.White);
    return image;
  }

  private static Win32DragOut.DataObjectImpl CreateDataObject(Image image) =>
    new(new[] { Path.Combine(Path.GetTempPath(), "AutoCMEX_drag_source.png") }, image);

  [Test]
  public void DataObject_SupportsHdropAndDibFormats()
  {
    if (!OperatingSystem.IsWindows())
      return;

    var data = CreateDataObject(CreateImage());

    var hdrop = new Win32DragOut.FormatEtc
    {
      CfFormat = Win32Bitmap.CfHdrop,
      DwAspect = 1,
      Lindex = -1,
      Tymed = 1,
    };
    data.QueryGetData(ref hdrop).ShouldBe(0);

    var dib = new Win32DragOut.FormatEtc
    {
      CfFormat = Win32Bitmap.CfDib,
      DwAspect = 1,
      Lindex = -1,
      Tymed = 1,
    };
    data.QueryGetData(ref dib).ShouldBe(0);

    data.GetData(ref hdrop, out var medium).ShouldBe(0);
    medium.UnionMember.ShouldNotBe(IntPtr.Zero);
    Win32Bitmap.GlobalFree(medium.UnionMember);
  }

  [Test]
  public void DragImage_CanBeInitializedFromBitmap()
  {
    if (!OperatingSystem.IsWindows())
      return;

    var image = CreateImage();
    var data = CreateDataObject(image);
    var hBitmap = Win32Bitmap.CreateHBitmap(image, out var width, out var height);
    hBitmap.ShouldNotBe(IntPtr.Zero);

    try
    {
      width.ShouldBe(20);
      height.ShouldBe(10);
      Win32DragOut.TrySetDragImage(data, hBitmap, width, height, out var error).ShouldBeTrue(error);
    }
    finally
    {
      Win32Bitmap.DeleteHBitmap(hBitmap);
    }
  }

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern uint RegisterClipboardFormatW(string lpszFormat);

  [Test]
  public void DragImage_StoresImageBitsAndDragContext()
  {
    if (!OperatingSystem.IsWindows())
      return;

    var image = CreateImage();
    var data = CreateDataObject(image);
    var hBitmap = Win32Bitmap.CreateHBitmap(image, out var width, out var height);

    try
    {
      Win32DragOut.TrySetDragImage(data, hBitmap, width, height, out var error).ShouldBeTrue(error);

      // 拖影管理器把位图信息写进 DragImageBits（HGLOBAL）：外壳按它决定拖影大小与位图。
      var bitsFormat = (ushort)RegisterClipboardFormatW("DragImageBits");
      bitsFormat.ShouldNotBe((ushort)0);
      var bits = new Win32DragOut.FormatEtc
      {
        CfFormat = bitsFormat,
        DwAspect = 1,
        Lindex = -1,
        Tymed = 1,
      };
      data.QueryGetData(ref bits).ShouldBe(0);
      data.GetData(ref bits, out var medium).ShouldBe(0);
      medium.Tymed.ShouldBe(1u);

      var pointer = Win32Bitmap.GlobalLock(medium.UnionMember);
      try
      {
        var dragImage = Marshal.PtrToStructure<Win32DragOut.ShDragImage>(pointer);
        dragImage.Cx.ShouldBe(width);
        dragImage.Cy.ShouldBe(height);
        dragImage.HBitmap.ShouldNotBe(IntPtr.Zero);
      }
      finally
      {
        Win32Bitmap.GlobalUnlock(medium.UnionMember);
        Win32Bitmap.GlobalFree(medium.UnionMember);
      }

      // 拖影管理器还会以流格式注入 DragContext；取不回来时外壳不会绘制拖影。
      var contextFormat = (ushort)RegisterClipboardFormatW("DragContext");
      var context = new Win32DragOut.FormatEtc
      {
        CfFormat = contextFormat,
        DwAspect = 1,
        Lindex = -1,
        Tymed = 4,
      };
      data.QueryGetData(ref context).ShouldBe(0);
    }
    finally
    {
      Win32Bitmap.DeleteHBitmap(hBitmap);
    }
  }

  [Test]
  public void TryDragFiles_EmptyPaths_ReturnsFailure()
  {
    Win32DragOut.TryDragFiles(Array.Empty<string>(), null, out var error).ShouldBeFalse();
    error.ShouldNotBeNullOrWhiteSpace();
  }

  [Test]
  public void TryDragFiles_MissingFile_ReturnsFailure()
  {
    if (!OperatingSystem.IsWindows())
      return;

    Win32DragOut
      .TryDragFiles(
        new[] { Path.Combine(Path.GetTempPath(), "AutoCMEX_not_exists.png") },
        CreateImage(),
        out var error
      )
      .ShouldBeFalse();
    error.ShouldContain("文件不存在");
  }
}
