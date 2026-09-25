namespace AutoCMEX.Core.Recording;

using System;
using System.IO;
using System.Text;

/// <summary>
/// 把录制器产物归集成与手工整理同构的 GIF 集目录。
/// </summary>
/// <remarks>
/// <para>
/// 本阶段（P2「单卡录制闭环」）只落「取件改名」与「读 GIF 头」两步：录制器的产物名是秒级
/// 时间戳且带 <c>task_</c> 前缀，必须改成集内约定的 <c>{序号}.gif</c> 才能被信息板块复用。
/// </para>
/// <para>
/// 清单 <c>manifest.json</c> 与报告由后续阶段在同一入口下补齐。
/// </para>
/// </remarks>
public static class GifSetBuilder
{
  /// <summary>集内 GIF 的扩展名。</summary>
  public const string GifExtension = ".gif";

  /// <summary>
  /// 录制器产物目录（相对 <c>game/</c> 的正斜杠路径），与插件里的 <c>OUTPUT_DIR</c> 同源。
  /// </summary>
  public const string RecorderOutputDirName = "danmaku_recorder/output";

  /// <summary>GIF 文件头的固定长度（魔数 6 字节 + 逻辑屏幕宽高各 2 字节）。</summary>
  private const int HeaderLength = 10;

  /// <summary>可接受的 GIF 魔数（87a 与 89a 两种版本）。</summary>
  private static readonly string[] MagicValues = { "GIF87a", "GIF89a" };

  /// <summary>取集内 GIF 的文件名。</summary>
  /// <param name="combatOrdinal">战斗阶段序号（1 基）。</param>
  /// <returns>形如 <c>3.gif</c>。</returns>
  public static string EntryFileName(int combatOrdinal) => $"{combatOrdinal}{GifExtension}";

  /// <summary>
  /// 取录制器产物的绝对路径。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <param name="gifRelativePath">结果里的 <c>gif_path</c>（正斜杠、相对 <c>game/</c>）。</param>
  /// <returns>产物绝对路径。</returns>
  /// <exception cref="ArgumentNullException"><paramref name="gifRelativePath"/> 为 <c>null</c>。</exception>
  public static string GetRecorderGifAbsolutePath(string engineDir, string gifRelativePath)
  {
    ArgumentNullException.ThrowIfNull(gifRelativePath);

    return Path.Combine(
      EngineLocator.GetGameDir(engineDir),
      gifRelativePath.Replace('/', Path.DirectorySeparatorChar)
    );
  }

  /// <summary>
  /// 把录制器产物复制为集内文件 <c>{序号}.gif</c>（同名覆盖上一次运行的产物）。
  /// </summary>
  /// <param name="sourceAbsolutePath">录制器产物绝对路径（须已存在）。</param>
  /// <param name="outputDir">GIF 集输出目录（不存在时创建）。</param>
  /// <param name="combatOrdinal">战斗阶段序号（1 基）。</param>
  /// <returns>集内 GIF 的绝对路径。</returns>
  /// <exception cref="FileNotFoundException">产物不存在。</exception>
  /// <exception cref="IOException">复制失败（磁盘满、目标被占用等）。</exception>
  /// <exception cref="UnauthorizedAccessException">目标目录不可写。</exception>
  public static string PlaceCardGif(string sourceAbsolutePath, string outputDir, int combatOrdinal)
  {
    Directory.CreateDirectory(outputDir);

    var target = Path.Combine(outputDir, EntryFileName(combatOrdinal));
    File.Copy(sourceAbsolutePath, target, overwrite: true);
    return target;
  }

  /// <summary>
  /// 读 GIF 头取画面尺寸。
  /// </summary>
  /// <remarks>
  /// 只读文件头 10 字节：GIF 的宽高是逻辑屏幕描述符里的两个小端 16 位整数，位于偏移 6 与 8。
  /// 尺寸随玩家的引擎分辨率浮动（方案不覆盖 <c>setting.resx/resy</c>），故只能事后读回，
  /// 清单里的宽高即取自此。
  /// </remarks>
  /// <param name="gifAbsolutePath">GIF 绝对路径。</param>
  /// <returns>宽与高（像素）。</returns>
  /// <exception cref="InvalidDataException">不是 GIF，或文件短于文件头。</exception>
  /// <exception cref="IOException">读取失败。</exception>
  public static (int Width, int Height) ReadGifSize(string gifAbsolutePath)
  {
    using var stream = File.OpenRead(gifAbsolutePath);

    var header = new byte[HeaderLength];
    if (stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) < HeaderLength)
    {
      throw new InvalidDataException($"GIF 文件头不完整：{gifAbsolutePath}");
    }

    var magic = Encoding.ASCII.GetString(header, 0, 6);
    if (Array.IndexOf(MagicValues, magic) < 0)
    {
      throw new InvalidDataException($"不是 GIF 文件（魔数 {magic}）：{gifAbsolutePath}");
    }

    return (header[6] | (header[7] << 8), header[8] | (header[9] << 8));
  }
}
