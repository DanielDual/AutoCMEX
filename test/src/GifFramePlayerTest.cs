namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// GIF 解码与播放单测：真解码（ImageSharp 现场生成 GIF）、帧延时回退、播放推进、
/// 重复点播不重解码、失败上报，以及可见性调度的常驻上限。
/// </summary>
/// <remarks>
/// 帧纹理要在主线程创建，因此播放类用例把控件挂在测试场景上；为避免引擎自动 <c>_Process</c>
/// 与手工推进互相干扰，加载完成后立即 <c>SetProcess(false)</c>，再逐次手工喂 delta。
/// </remarks>
public class GifFramePlayerTest : TestClass
{
  private const ushort FrameDelayCentiseconds = 10; // 0.1s

  private string _dir = string.Empty;
  private readonly List<Node> _toCleanup = new();

  public GifFramePlayerTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_GifTest_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_dir);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
        node.Free();
    }

    _toCleanup.Clear();

    if (Directory.Exists(_dir))
      Directory.Delete(_dir, true);
  }

  /// <summary>现场生成一张多帧 GIF；每帧像素不同，便于验证"确实换了帧"。</summary>
  private string WriteGif(
    string fileName,
    int frameCount,
    ushort frameDelay = FrameDelayCentiseconds
  )
  {
    using var image = new Image<Rgba32>(4, 4);
    while (image.Frames.Count < frameCount)
      image.Frames.CreateFrame();

    for (var i = 0; i < frameCount; i++)
    {
      image.Frames[i].Metadata.GetGifMetadata().FrameDelay = frameDelay;
      image.Frames[i][0, 0] = new Rgba32((byte)(20 * (i + 1)), 0, 0, 255);
    }

    var path = Path.Combine(_dir, fileName);
    image.Save(path);
    return path;
  }

  private GifFramePlayer CreatePlayer()
  {
    var player = new GifFramePlayer();
    TestScene.AddChild(player);
    _toCleanup.Add(player);
    return player;
  }

  private static async Task<bool> WaitUntil(Node node, Func<bool> condition, int maxFrames = 600)
  {
    for (var i = 0; i < maxFrames; i++)
    {
      if (condition())
        return true;

      await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    return condition();
  }

  [Test]
  public void GifDecoder_DecodesFramesSizeAndDelays()
  {
    var path = WriteGif("sample_3f.gif", 3);

    var data = GifDecoder.Decode(path);

    data.Width.ShouldBe(4);
    data.Height.ShouldBe(4);
    data.FrameCount.ShouldBe(3);
    data.Delays.Count.ShouldBe(3);
    data.Delays.ShouldAllBe(delay => Math.Abs(delay - 0.1) < 0.0001);
    data.FramePixels.Count.ShouldBe(3);

    foreach (var pixels in data.FramePixels)
      pixels.Length.ShouldBe(4 * 4 * 4);
  }

  [Test]
  public void GifDecoder_SingleFrameGifIsAllowed()
  {
    var path = WriteGif("sample_static.gif", 1);

    GifDecoder.Decode(path).FrameCount.ShouldBe(1);
  }

  [Test]
  public void GifDecoder_UndeclaredFrameDelay_FallsBackToDefault()
  {
    var path = WriteGif("sample_nodelay.gif", 2, frameDelay: 0);

    var data = GifDecoder.Decode(path);

    data.FrameCount.ShouldBe(2);
    data.Delays.ShouldAllBe(delay =>
      Math.Abs(delay - GifDecoder.DefaultFrameDelaySeconds) < 0.0001
    );
  }

  [Test]
  public void GifDecoder_MissingFile_Throws()
  {
    Should.Throw<FileNotFoundException>(() => GifDecoder.Decode(Path.Combine(_dir, "missing.gif")));
  }

  [Test]
  public void GifFrameSequence_CreatesTexturesAndDisposesOnce()
  {
    var path = WriteGif("sample_seq.gif", 2);
    var data = GifDecoder.Decode(path);

    var sequence = GifFrameSequence.CreateFrom(data);

    sequence.FrameCount.ShouldBe(2);
    sequence.Frames.Count.ShouldBe(2);
    sequence.Frames[0].ShouldNotBeNull();
    Math.Abs(sequence.TotalDuration - 0.2).ShouldBeLessThan(0.0001);

    sequence.Dispose();
    // 重复释放必须安全（面板退出与显式释放可能都会走一遍）
    Should.NotThrow(sequence.Dispose);

    Should.Throw<ArgumentNullException>(() => GifFrameSequence.CreateFrom(null!));
  }

  [Test]
  public async Task Player_Play_LoadsFramesThenAdvancesByDelay()
  {
    var path = WriteGif("sample_play.gif", 3);
    var player = CreatePlayer();

    player.Play(path);
    player.SourcePath.ShouldBe(path);

    (await WaitUntil(player, () => player.IsLoaded)).ShouldBeTrue("GIF 解码应在若干帧内完成");

    player.SetProcess(false);
    player.FrameCount.ShouldBe(3);
    player.Texture.ShouldNotBeNull("加载完成应立刻显示首帧");

    var start = player.CurrentFrameIndex;
    player._Process(0.05);
    player.CurrentFrameIndex.ShouldBe(start, "未满一个帧延时不应换帧");

    player._Process(0.05);
    player.CurrentFrameIndex.ShouldBe((start + 1) % 3);

    // 循环播放：连续推进总量超过一轮后回到起点
    player._Process(0.1);
    player._Process(0.1);
    player.CurrentFrameIndex.ShouldBe((start + 3) % 3);
    player.Texture.ShouldNotBeNull();

    player.Release();
    player.IsLoaded.ShouldBeFalse();
    player.SourcePath.ShouldBe(string.Empty);
    player.CurrentFrameIndex.ShouldBe(0);
    player.Texture.ShouldBeNull();
  }

  [Test]
  public async Task Player_PlaySamePathTwice_DoesNotReloadFromStart()
  {
    var path = WriteGif("sample_replay.gif", 3);
    var player = CreatePlayer();

    player.Play(path);
    (await WaitUntil(player, () => player.IsLoaded)).ShouldBeTrue();

    player.SetProcess(false);
    player._Process(0.1);
    var advanced = player.CurrentFrameIndex;

    // 同一路径重复点播：直接返回，不清空已播进度
    player.Play(path);

    player.IsLoaded.ShouldBeTrue();
    player.CurrentFrameIndex.ShouldBe(advanced);
    player.SourcePath.ShouldBe(path);
  }

  [Test]
  public async Task Player_PlayOtherPath_SwitchesSource()
  {
    var first = WriteGif("sample_a.gif", 2);
    var second = WriteGif("sample_b.gif", 3);
    var player = CreatePlayer();

    player.Play(first);
    (await WaitUntil(player, () => player.IsLoaded && player.FrameCount == 2)).ShouldBeTrue();

    player.Play(second);
    player.SourcePath.ShouldBe(second);
    (await WaitUntil(player, () => player.IsLoaded && player.FrameCount == 3)).ShouldBeTrue();

    player.Release();
  }

  [Test]
  public void Player_PlayEmptyPath_Releases()
  {
    var player = CreatePlayer();

    player.Play(string.Empty);

    player.IsLoaded.ShouldBeFalse();
    player.SourcePath.ShouldBe(string.Empty);
  }

  [Test]
  public async Task Player_PlayBrokenFile_EmitsLoadFailed()
  {
    var broken = Path.Combine(_dir, "broken.gif");
    File.WriteAllText(broken, "not a gif at all");

    var player = CreatePlayer();
    string? reason = null;
    player.LoadFailed += text => reason = text;

    player.Play(broken);

    (await WaitUntil(player, () => reason is not null)).ShouldBeTrue("解码失败必须回报，不能静默");
    reason!.ShouldContain("GIF 解码失败");
    player.IsLoaded.ShouldBeFalse();
  }

  [Test]
  public void GifVisibility_PicksIntersectingItemsUpToLimit()
  {
    var bounds = new List<(float Top, float Bottom)> { (0f, 50f), (60f, 110f), (120f, 170f) };

    GifVisibility.Compute(bounds, 0f, 300f, 0).ShouldBe(new[] { 0, 1, 2 });
    GifVisibility.Compute(bounds, 0f, 300f, 2).ShouldBe(new[] { 0, 1 });
    GifVisibility.Compute(bounds, 55f, 115f, 0).ShouldBe(new[] { 1 });
    GifVisibility.Compute(bounds, 400f, 500f, 0).ShouldBeEmpty();
    GifVisibility.Compute(new List<(float, float)>(), 0f, 10f, 3).ShouldBeEmpty();

    // 视口上下沿倒置（滚动到底部的瞬时值）应被规范化
    GifVisibility.Compute(bounds, 115f, 55f, 0).ShouldBe(new[] { 1 });

    // 边界贴合算可见，否则滚到底部时最后一张永远不播
    GifVisibility.Compute(bounds, 170f, 200f, 0).ShouldBe(new[] { 2 });
  }

  [Test]
  public async Task Coordinator_KeepsOnlyVisibleCardsWithinLimit()
  {
    var player0 = CreatePlayer();
    var player1 = CreatePlayer();
    var player2 = CreatePlayer();

    var cards = new List<GifCardBinding>
    {
      new()
      {
        Player = player0,
        GifPath = WriteGif("sample_c0.gif", 2),
        ContentRect = new Rect2(0, 0, 100, 50),
      },
      new()
      {
        Player = player1,
        GifPath = WriteGif("sample_c1.gif", 2),
        ContentRect = new Rect2(0, 60, 100, 50),
      },
      new()
      {
        Player = player2,
        GifPath = WriteGif("sample_c2.gif", 2),
        ContentRect = new Rect2(0, 120, 100, 50),
      },
    };

    var coordinator = new GifPlaybackCoordinator(maxResident: 1);

    var resident = coordinator.Update(cards, 0f, 300f);
    resident.Count.ShouldBe(1);
    resident[0].Player.ShouldBe(player0);

    (await WaitUntil(player0, () => player0.IsLoaded)).ShouldBeTrue("常驻卡片应被解码");
    player1.IsLoaded.ShouldBeFalse("超出常驻上限的卡片必须释放，不能同时驻留");
    player2.IsLoaded.ShouldBeFalse();

    // 只滚到第二张的视口范围：常驻项整体换人
    resident = coordinator.Update(cards, 65f, 115f);
    resident.Count.ShouldBe(1);
    resident[0].Player.ShouldBe(player1);

    (await WaitUntil(player1, () => player1.IsLoaded)).ShouldBeTrue();
    player0.IsLoaded.ShouldBeFalse();

    // 桌面调整/切集时全量释放
    GifPlaybackCoordinator.ReleaseAll(cards);
    player1.IsLoaded.ShouldBeFalse();

    coordinator.Update(new List<GifCardBinding>(), 0f, 100f).ShouldBeEmpty();
    coordinator.MaxResident.ShouldBe(1);
    new GifPlaybackCoordinator().MaxResident.ShouldBe(GifPlaybackCoordinator.DefaultMaxResident);
  }
}
