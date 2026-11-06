namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// GIF 解码与播放单测：真解码（ImageSharp 现场生成 GIF）、帧延时回退、播放推进、
/// 重复点播不重解码、失败上报，以及可见性调度的驻留规则。
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

  /// <summary>同路径解码失败后必须允许重新尝试：失败会清空 CTS，下一次 Play 重新解码。</summary>
  [Test]
  public async Task Player_RetriesAfterFailure()
  {
    var broken = Path.Combine(_dir, "broken_retry.gif");
    File.WriteAllText(broken, "not a gif at all");

    var player = CreatePlayer();
    var failures = 0;
    player.LoadFailed += _ => failures++;

    player.Play(broken);
    (await WaitUntil(player, () => failures == 1)).ShouldBeTrue("首次解码失败必须回报");
    player.SourcePath.ShouldBe(broken, "失败后保留路径，供下一次重试");
    player.IsLoaded.ShouldBeFalse();

    // 同路径再次点播：失败后 CTS 已清空，不能被"同路径在途复用"挡掉
    player.Play(broken);
    (await WaitUntil(player, () => failures == 2)).ShouldBeTrue("失败后同路径 Play 应重新尝试");
    player.IsLoaded.ShouldBeFalse();
  }

  /// <summary>同路径在途解码必须复用：主线程内连续两次 Play 只启动一次解码。</summary>
  [Test]
  public async Task Player_ReusesInFlightDecodeForSamePath()
  {
    var path = WriteGif("sample_reuse.gif", 3);
    var player = CreatePlayer();

    player.Play(path);
    player.Play(path); // 同一帧内第二次调用时，第一次的解码仍在途（continuation 抢不到主线程）

    player.DecodeStartCount.ShouldBe(1, "同路径在途解码不应被第二次 Play 重启");

    (await WaitUntil(player, () => player.IsLoaded)).ShouldBeTrue("第一次解码最终要完成");
    player.DecodeStartCount.ShouldBe(1);

    player.Play(path); // 已加载后再点播：仍不重解码
    player.DecodeStartCount.ShouldBe(1);
  }

  /// <summary>可见性判定：凡与视口相交的项全部返回，不按数量截断（含"相交 4 张"的回归断言）。</summary>
  [Test]
  public void GifVisibility_PicksEveryIntersectingItem()
  {
    var bounds = new List<(float Top, float Bottom)> { (0f, 50f), (60f, 110f), (120f, 170f) };

    // 视口内相交多少张就返回多少张：不再按"最多几张"截断
    GifVisibility.Compute(bounds, 0f, 300f).ShouldBe(new[] { 0, 1, 2 });
    GifVisibility.Compute(bounds, 55f, 115f).ShouldBe(new[] { 1 });
    GifVisibility.Compute(bounds, 400f, 500f).ShouldBeEmpty();
    GifVisibility.Compute(new List<(float, float)>(), 0f, 10f).ShouldBeEmpty();

    // 视口上下沿倒置（滚动到底部的瞬时值）应被规范化
    GifVisibility.Compute(bounds, 115f, 55f).ShouldBe(new[] { 1 });

    // 边界贴合算可见，否则滚到底部时最后一张永远不播
    GifVisibility.Compute(bounds, 170f, 200f).ShouldBe(new[] { 2 });

    // 回归：视口内相交 4 张（含只露出一点的）时全部返回——
    // 旧实现按"常驻上限 3"按内容顺序截断，丢掉完全可见的第 4 张，正是信息页 GIF 空白的根因
    var five = new List<(float Top, float Bottom)>
    {
      (-200f, -100f),
      (0f, 100f),
      (110f, 210f),
      (220f, 320f),
      (330f, 430f),
    };
    GifVisibility.Compute(five, 0f, 350f).ShouldBe(new[] { 1, 2, 3, 4 });
  }

  /// <summary>驻留调度：视口内相交的卡片全部驻留解码；视口缩窄后其余立即释放。</summary>
  [Test]
  public async Task Coordinator_KeepsEveryVisibleCard()
  {
    var players = Enumerable.Range(0, 5).Select(_ => CreatePlayer()).ToList();
    var cards = players
      .Select(
        (player, i) =>
          new GifCardBinding
          {
            Player = player,
            GifPath = WriteGif($"sample_c{i}.gif", 2),
            ContentRect = new Rect2(0, i * 100, 100, 90),
          }
      )
      .ToList();

    var coordinator = new GifPlaybackCoordinator();

    // 视口覆盖全部 5 张：5 张都要驻留（旧实现按内容顺序只留前 3 张，后两张即便完全可见也空白）
    var resident = coordinator.Update(cards, 0f, 500f);
    resident.Count.ShouldBe(5);
    foreach (var player in players)
      (await WaitUntil(player, () => player.IsLoaded)).ShouldBeTrue("凡与视口相交的卡片都应解码");

    // 视口只覆盖第 3、4 张（第 5 张的顶沿在视口下沿之外）：其余立即释放
    resident = coordinator.Update(cards, 210f, 395f);
    resident.Select(r => r.Player).ShouldBe(new[] { players[2], players[3] });
    players[0].IsLoaded.ShouldBeFalse();
    players[1].IsLoaded.ShouldBeFalse();
    players[2].IsLoaded.ShouldBeTrue();
    players[3].IsLoaded.ShouldBeTrue();
    players[4].IsLoaded.ShouldBeFalse();

    // 桌面调整/切集时全量释放
    GifPlaybackCoordinator.ReleaseAll(cards);
    foreach (var player in players)
      player.IsLoaded.ShouldBeFalse();

    coordinator.Update(new List<GifCardBinding>(), 0f, 100f).ShouldBeEmpty();
  }
}
