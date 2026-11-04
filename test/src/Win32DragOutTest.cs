namespace AutoCMEX;

using System;
using System.IO;
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
