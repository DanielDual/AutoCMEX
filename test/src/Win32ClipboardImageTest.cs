namespace AutoCMEX;

using System;
using AutoCMEX.Helpers.Windows;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="Win32ClipboardImage"/> 单测：写入后可从 Godot 读回同尺寸图片。
/// </summary>
/// <remarks>仅在 Windows 上执行；其它平台跳过（项目只发布 Windows）。</remarks>
public class Win32ClipboardImageTest : TestClass
{
  public Win32ClipboardImageTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void TryCopy_WritesImageReadableFromGodot()
  {
    if (!OperatingSystem.IsWindows())
      return;

    var image = Image.CreateEmpty(16, 8, false, Image.Format.Rgba8);
    image.Fill(Colors.Red);

    Win32ClipboardImage.TryCopy(image, out var error).ShouldBeTrue(error);
    DisplayServer.ClipboardHasImage().ShouldBeTrue();

    var readBack = DisplayServer.ClipboardGetImage();
    readBack.ShouldNotBeNull();
    readBack!.GetWidth().ShouldBe(16);
    readBack.GetHeight().ShouldBe(8);
  }

  [Test]
  public void TryCopy_EmptyImage_ReturnsFailure()
  {
    if (!OperatingSystem.IsWindows())
      return;

    Win32ClipboardImage.TryCopy(null!, out var error).ShouldBeFalse();
    error.ShouldNotBeNullOrWhiteSpace();
  }
}
