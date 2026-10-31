namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using GodotImage = Godot.Image;

/// <summary>
/// 后台线程解码出的 GIF 帧数据（纯数据、不含任何 Godot 对象，可跨线程传递）。
/// </summary>
public sealed class GifFrameData
{
  /// <summary>画布宽度（像素）。</summary>
  public int Width { get; init; }

  /// <summary>画布高度（像素）。</summary>
  public int Height { get; init; }

  /// <summary>逐帧 RGBA8 像素数据（长度为 <c>Width * Height * 4</c>）。</summary>
  public IReadOnlyList<byte[]> FramePixels { get; init; } = Array.Empty<byte[]>();

  /// <summary>逐帧时长（秒），与 <see cref="FramePixels"/> 一一对应。</summary>
  public IReadOnlyList<double> Delays { get; init; } = Array.Empty<double>();

  /// <summary>帧数。</summary>
  public int FrameCount => FramePixels.Count;
}

/// <summary>
/// GIF 解码器（六边形解码由 SixLabors.ImageSharp 承担，不自行解析 GIF 格式）。
/// </summary>
public static class GifDecoder
{
  /// <summary>GIF 未声明帧延时时使用的默认时长（秒）。</summary>
  public const double DefaultFrameDelaySeconds = 0.1;

  /// <summary>把 GIF 文件解码为逐帧 RGBA 数据；可在后台线程调用。</summary>
  /// <param name="path">GIF 文件绝对路径。</param>
  /// <returns>解码结果（多帧 GIF 返回多帧，静态图返回单帧）。</returns>
  /// <exception cref="FileNotFoundException">文件不存在。</exception>
  public static GifFrameData Decode(string path)
  {
    if (!File.Exists(path))
      throw new FileNotFoundException("GIF 文件不存在。", path);

    using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
    var width = image.Width;
    var height = image.Height;
    var frameCount = image.Frames.Count;

    var pixels = new List<byte[]>(frameCount);
    var delays = new List<double>(frameCount);

    for (var i = 0; i < frameCount; i++)
    {
      var frame = image.Frames[i];
      var buffer = new byte[width * height * 4];
      frame.CopyPixelDataTo(buffer);
      pixels.Add(buffer);

      // FrameDelay 单位为 1/100 秒；0 视为未声明
      var declared = frame.Metadata.GetGifMetadata().FrameDelay;
      delays.Add(declared > 0 ? declared / 100.0 : DefaultFrameDelaySeconds);
    }

    return new GifFrameData
    {
      Width = width,
      Height = height,
      FramePixels = pixels,
      Delays = delays,
    };
  }
}

/// <summary>
/// 解码后的 GIF 帧序列（持有 Godot 纹理，必须在主线程创建与释放）。
/// </summary>
public sealed class GifFrameSequence : IDisposable
{
  private readonly List<Texture2D> _frames;
  private readonly List<double> _delays;
  private bool _disposed;

  private GifFrameSequence(List<Texture2D> frames, List<double> delays)
  {
    _frames = frames;
    _delays = delays;
  }

  /// <summary>帧纹理（至少一帧）。</summary>
  public IReadOnlyList<Texture2D> Frames => _frames;

  /// <summary>逐帧时长（秒）。</summary>
  public IReadOnlyList<double> Delays => _delays;

  /// <summary>帧数。</summary>
  public int FrameCount => _frames.Count;

  /// <summary>单次播放总时长（秒）。</summary>
  public double TotalDuration
  {
    get
    {
      var total = 0.0;
      foreach (var delay in _delays)
        total += delay;

      return total;
    }
  }

  /// <summary>在主线程由解码数据创建纹理序列。</summary>
  /// <param name="data">后台解码结果。</param>
  /// <returns>帧序列。</returns>
  public static GifFrameSequence CreateFrom(GifFrameData data)
  {
    ArgumentNullException.ThrowIfNull(data);

    var frames = new List<Texture2D>(data.FrameCount);
    for (var i = 0; i < data.FrameCount; i++)
    {
      var image = GodotImage.CreateFromData(
        data.Width,
        data.Height,
        useMipmaps: false,
        GodotImage.Format.Rgba8,
        data.FramePixels[i]
      );
      frames.Add(ImageTexture.CreateFromImage(image));
      image.Dispose();
    }

    return new GifFrameSequence(frames, new List<double>(data.Delays));
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    foreach (var frame in _frames)
    {
      if (GodotObject.IsInstanceValid(frame))
        frame.Dispose();
    }

    _frames.Clear();
    _delays.Clear();
  }
}

/// <summary>
/// 可见项范围计算（纯逻辑，可单测）：从纵向范围列表中挑出与视口相交的项，并按上限截断。
/// </summary>
public static class GifVisibility
{
  /// <summary>
  /// 计算需要驻留的项下标。
  /// </summary>
  /// <param name="itemBounds">各项在内容坐标系中的纵向范围（Top, Bottom）。</param>
  /// <param name="viewTop">视口上沿在内容坐标系中的位置。</param>
  /// <param name="viewBottom">视口下沿在内容坐标系中的位置。</param>
  /// <param name="maxConcurrent">最多驻留的项数（≤0 视为不限）。</param>
  /// <returns>需要驻留的项下标（按内容顺序；与视口相交优先，超出上限时取靠前者）。</returns>
  public static IReadOnlyList<int> Compute(
    IReadOnlyList<(float Top, float Bottom)> itemBounds,
    float viewTop,
    float viewBottom,
    int maxConcurrent
  )
  {
    var result = new List<int>();
    if (itemBounds is null || itemBounds.Count == 0)
      return result;

    if (viewBottom < viewTop)
      (viewTop, viewBottom) = (viewBottom, viewTop);

    for (var i = 0; i < itemBounds.Count; i++)
    {
      var (top, bottom) = itemBounds[i];
      // 视口上沿与项下沿重合也算可见，避免滚动到底部时最后一张被判为不可见
      var intersects = bottom >= viewTop && top <= viewBottom;
      if (!intersects)
        continue;

      result.Add(i);
      if (maxConcurrent > 0 && result.Count >= maxConcurrent)
        break;
    }

    return result;
  }
}

/// <summary>GIF 卡片绑定：控件 + 要播放的文件 + 在滚动内容坐标系中的位置。</summary>
public sealed class GifCardBinding
{
  /// <summary>卡片播放控件。</summary>
  public GifFramePlayer Player { get; init; } = default!;

  /// <summary>卡片对应的 GIF 文件绝对路径。</summary>
  public string GifPath { get; init; } = string.Empty;

  /// <summary>卡片在滚动内容坐标系中的矩形。</summary>
  public Rect2 ContentRect { get; init; }
}

/// <summary>
/// 按可见性调度 GIF 解码与释放，常驻帧缓存不超过上限。
/// </summary>
/// <remarks>
/// 面板每次滚动/布局变化时调用一次 <see cref="Update"/> 即可；未被选中的卡片会被立即释放纹理。
/// </remarks>
public sealed class GifPlaybackCoordinator
{
  /// <summary>默认常驻上限（张）。</summary>
  public const int DefaultMaxResident = 3;

  /// <summary>创建调度器。</summary>
  /// <param name="maxResident">最多同时驻留解码结果的卡片数（≤0 视为不限）。</param>
  public GifPlaybackCoordinator(int maxResident = DefaultMaxResident)
  {
    MaxResident = maxResident;
  }

  /// <summary>常驻上限（张）。</summary>
  public int MaxResident { get; }

  /// <summary>
  /// 按视口范围更新各卡片的解码状态。
  /// </summary>
  /// <param name="cards">全部卡片绑定。</param>
  /// <param name="viewTop">视口上沿（内容坐标系）。</param>
  /// <param name="viewBottom">视口下沿（内容坐标系）。</param>
  /// <returns>本次处于驻留状态的卡片绑定。</returns>
  public IReadOnlyList<GifCardBinding> Update(
    IReadOnlyList<GifCardBinding> cards,
    float viewTop,
    float viewBottom
  )
  {
    var resident = new List<GifCardBinding>();
    if (cards is null || cards.Count == 0)
      return resident;

    var bounds = new List<(float Top, float Bottom)>(cards.Count);
    foreach (var card in cards)
      bounds.Add((card.ContentRect.Position.Y, card.ContentRect.End.Y));

    var visible = GifVisibility.Compute(bounds, viewTop, viewBottom, MaxResident);
    var keep = new HashSet<int>(visible);

    for (var i = 0; i < cards.Count; i++)
    {
      var card = cards[i];
      if (card.Player is null || !GodotObject.IsInstanceValid(card.Player))
        continue;

      if (keep.Contains(i))
      {
        card.Player.Play(card.GifPath);
        resident.Add(card);
      }
      else
      {
        card.Player.Release();
      }
    }

    return resident;
  }

  /// <summary>释放所有卡片（面板退出/切换集时调用）。</summary>
  /// <param name="cards">全部卡片绑定。</param>
  public static void ReleaseAll(IReadOnlyList<GifCardBinding> cards)
  {
    if (cards is null)
      return;

    foreach (var card in cards)
    {
      if (card.Player is not null && GodotObject.IsInstanceValid(card.Player))
        card.Player.Release();
    }
  }
}

/// <summary>
/// GIF 播放控件：按帧延时播放 <see cref="GifFrameSequence"/>，支持按需加载与释放。
/// </summary>
/// <remarks>
/// 解码在后台线程进行（避免卡住主线程），纹理创建与播放始终在主线程；同一时刻只持有一个序列。
/// </remarks>
public sealed partial class GifFramePlayer : TextureRect
{
  /// <summary>帧序列加载完成（可安全取用 <see cref="FrameCount"/>）时发出。</summary>
  /// <param name="frameCount">帧数。</param>
  [Signal]
  public delegate void FramesLoadedEventHandler(int frameCount);

  /// <summary>加载失败时发出。</summary>
  /// <param name="reason">失败原因（可直接展示）。</param>
  [Signal]
  public delegate void LoadFailedEventHandler(string reason);

  private GifFrameSequence? _sequence;
  private CancellationTokenSource? _cts;
  private int _frameIndex;
  private double _elapsed;

  /// <summary>当前已加载的 GIF 路径；未加载时为空串。</summary>
  public string SourcePath { get; private set; } = string.Empty;

  /// <summary>是否已加载帧序列。</summary>
  public bool IsLoaded => _sequence is not null;

  /// <summary>当前帧数；未加载时为 0。</summary>
  public int FrameCount => _sequence?.FrameCount ?? 0;

  /// <summary>当前帧下标；未加载时为 0。</summary>
  public int CurrentFrameIndex => _frameIndex;

  /// <summary>
  /// 加载并播放指定 GIF；同一路径重复调用不会重复解码。
  /// </summary>
  /// <param name="path">GIF 文件绝对路径。</param>
  public void Play(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      Release();
      return;
    }

    if (string.Equals(path, SourcePath, StringComparison.Ordinal) && _sequence is not null)
      return;

    Release();
    SourcePath = path;

    _cts = new CancellationTokenSource();
    _ = LoadAsync(path, _cts.Token);
  }

  /// <summary>释放帧缓存并停止播放。</summary>
  public void Release()
  {
    _cts?.Cancel();
    _cts?.Dispose();
    _cts = null;

    SourcePath = string.Empty;
    _frameIndex = 0;
    _elapsed = 0;

    // 先摘掉纹理引用再释放，避免 Godot 报告引用已释放资源
    Texture = null;
    _sequence?.Dispose();
    _sequence = null;
    SetProcess(false);
  }

  /// <inheritdoc/>
  public override void _Process(double delta)
  {
    var sequence = _sequence;
    if (sequence is null || sequence.FrameCount <= 1)
      return;

    _elapsed += delta;

    var guard = 0;
    while (guard++ <= sequence.FrameCount)
    {
      var delay = sequence.Delays[_frameIndex];
      if (delay <= 0)
        delay = GifDecoder.DefaultFrameDelaySeconds;

      if (_elapsed < delay)
        break;

      _elapsed -= delay;
      _frameIndex = (_frameIndex + 1) % sequence.FrameCount;
      Texture = sequence.Frames[_frameIndex];
    }
  }

  /// <inheritdoc/>
  public override void _ExitTree() => Release();

  private async Task LoadAsync(string path, CancellationToken token)
  {
    GifFrameData data;
    try
    {
      data = await Task.Run(() => GifDecoder.Decode(path), token);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    catch (Exception ex)
    {
      if (!token.IsCancellationRequested && GodotObject.IsInstanceValid(this))
      {
        EmitSignal(SignalName.LoadFailed, $"GIF 解码失败：{ex.Message}");
      }

      return;
    }

    // 解码期间可能已被释放或切换到其它 GIF
    if (
      token.IsCancellationRequested
      || !GodotObject.IsInstanceValid(this)
      || !string.Equals(path, SourcePath, StringComparison.Ordinal)
    )
    {
      return;
    }

    var sequence = GifFrameSequence.CreateFrom(data);
    if (sequence.FrameCount == 0)
    {
      sequence.Dispose();
      EmitSignal(SignalName.LoadFailed, "GIF 不含任何帧。");
      return;
    }

    _sequence = sequence;
    _frameIndex = 0;
    _elapsed = 0;
    Texture = sequence.Frames[0];
    SetProcess(true);
    EmitSignal(SignalName.FramesLoaded, sequence.FrameCount);
  }
}
