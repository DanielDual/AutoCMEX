namespace AutoCMEX.Test.Drivers;

using System.IO;
using System.Text;

/// <summary>
/// 造最小可用的 GIF 字节（文件头 + 结束符），供单测验证归集时的尺寸读回。
/// </summary>
/// <remarks>
/// 真产物由录制器调 ffmpeg 生成，单测不依赖 ffmpeg，故只造出「魔数 + 逻辑屏幕宽高」这一段
/// 被实现的读取逻辑真正用到的字节。
/// </remarks>
public static class SyntheticGif
{
  /// <summary>GIF 的结束符。</summary>
  private const byte Trailer = 0x3B;

  /// <summary>造一份指定尺寸的最小 GIF 字节。</summary>
  /// <param name="width">宽（像素）。</param>
  /// <param name="height">高（像素）。</param>
  /// <returns>GIF 字节。</returns>
  public static byte[] Create(int width, int height)
  {
    var bytes = new byte[14];
    Encoding.ASCII.GetBytes("GIF89a").CopyTo(bytes, 0);

    // 逻辑屏幕宽高：各 2 字节小端
    bytes[6] = (byte)(width & 0xFF);
    bytes[7] = (byte)((width >> 8) & 0xFF);
    bytes[8] = (byte)(height & 0xFF);
    bytes[9] = (byte)((height >> 8) & 0xFF);

    bytes[13] = Trailer;
    return bytes;
  }

  /// <summary>把一份指定尺寸的最小 GIF 写到磁盘。</summary>
  /// <param name="path">目标路径（父目录须已存在）。</param>
  /// <param name="width">宽（像素）。</param>
  /// <param name="height">高（像素）。</param>
  public static void WriteFile(string path, int width, int height) =>
    File.WriteAllBytes(path, Create(width, height));
}
