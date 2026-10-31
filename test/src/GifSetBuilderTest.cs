namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Recording;
using AutoCMEX.Test.Drivers;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// GIF 集归集工具单测：取件改名与读 GIF 头。
/// </summary>
public class GifSetBuilderTest : TestClass
{
  private string _root = string.Empty;
  private string _engineDir = string.Empty;
  private string _outputDir = string.Empty;

  public GifSetBuilderTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_GifSet_" + Guid.NewGuid().ToString("N")[..8]
    );
    _engineDir = Path.Combine(_root, "LuaSTGSub");
    _outputDir = Path.Combine(_root, "out");
    Directory.CreateDirectory(Path.Combine(_engineDir, EngineLocator.GameDirName));
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  [Test]
  public void EntryFileName_UsesOrdinalAndGifExtension() =>
    GifSetBuilder.EntryFileName(3).ShouldBe("3.gif");

  [Test]
  public void GetRecorderGifAbsolutePath_JoinsGameDirWithRelativeGifPath()
  {
    var path = GifSetBuilder.GetRecorderGifAbsolutePath(
      _engineDir,
      "danmaku_recorder/output/task_1.gif"
    );

    path.ShouldBe(
      Path.Combine(
        _engineDir,
        EngineLocator.GameDirName,
        "danmaku_recorder",
        "output",
        "task_1.gif"
      )
    );
  }

  [Test]
  public void ReadGifSize_ReadsLogicalScreenSize()
  {
    var gif = Path.Combine(_root, "size.gif");
    SyntheticGif.WriteFile(gif, 640, 480);

    var (width, height) = GifSetBuilder.ReadGifSize(gif);

    width.ShouldBe(640);
    height.ShouldBe(480);
  }

  [Test]
  public void ReadGifSize_WithoutGifMagic_Throws()
  {
    var notGif = Path.Combine(_root, "not.gif");
    File.WriteAllText(notGif, "this is not a gif, but long enough for the header");

    Should
      .Throw<InvalidDataException>(() => GifSetBuilder.ReadGifSize(notGif))
      .Message.ShouldContain("不是 GIF");
  }

  [Test]
  public void ReadGifSize_TruncatedHeader_Throws()
  {
    var truncated = Path.Combine(_root, "short.gif");
    File.WriteAllBytes(truncated, new byte[] { 0x47, 0x49, 0x46 });

    Should
      .Throw<InvalidDataException>(() => GifSetBuilder.ReadGifSize(truncated))
      .Message.ShouldContain("文件头不完整");
  }

  [Test]
  public void PlaceCardGif_CopiesRenamesAndOverwrites()
  {
    var source = Path.Combine(_root, "task_1.gif");
    SyntheticGif.WriteFile(source, 320, 240);

    var first = GifSetBuilder.PlaceCardGif(source, _outputDir, 2);

    first.ShouldBe(Path.Combine(_outputDir, "2.gif"));
    File.Exists(first).ShouldBeTrue();
    GifSetBuilder.ReadGifSize(first).ShouldBe((320, 240));

    // 上一轮同名产物被覆盖，而不是并存或报错
    SyntheticGif.WriteFile(source, 800, 600);
    GifSetBuilder.PlaceCardGif(source, _outputDir, 2);

    GifSetBuilder.ReadGifSize(first).ShouldBe((800, 600));
    Directory.GetFiles(_outputDir).Length.ShouldBe(1);
    Directory.GetFiles(_outputDir).Single().ShouldEndWith("2.gif");
  }
}
