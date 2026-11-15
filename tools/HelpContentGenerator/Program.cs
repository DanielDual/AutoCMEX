namespace AutoCMEX.Tools.HelpContentGenerator;

using System;
using System.IO;
using System.Text;
using AutoCMEX.Core.Help.Markdown;

/// <summary>
/// 帮助内容生成器命令行入口：解析参数、调用装配器、写产物或校验产物。
/// </summary>
/// <remarks>
/// 用法：<c>HelpContentGenerator --input &lt;docs/help&gt; --output &lt;生成文件&gt; [--assets &lt;目录&gt;] [--theme-scale &lt;正数&gt;] [--verify]</c>。
/// 退出码：0 成功；1 内容非法或 --verify 不一致；2 参数用法错误。
/// 本类只做参数解析、文件读写与结果打印，全部转换逻辑在 <see cref="HelpPageSetBuilder"/>。
/// </remarks>
internal static class Program
{
  private const int ExitSuccess = 0;
  private const int ExitContentError = 1;
  private const int ExitUsageError = 2;

  private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

  /// <summary>进程入口。</summary>
  /// <param name="args">命令行参数。</param>
  /// <returns>退出码。</returns>
  public static int Main(string[] args)
  {
    string? input = null;
    string? output = null;
    string? assets = null;
    string? themeScaleValue = null;
    bool verify = false;

    for (int i = 0; i < args.Length; i++)
    {
      string arg = args[i];
      switch (arg)
      {
        case "--input":
          if (!TryTakeValue(args, ref i, out input))
          {
            return Usage("--input 需要一个目录参数");
          }
          break;
        case "--output":
          if (!TryTakeValue(args, ref i, out output))
          {
            return Usage("--output 需要一个文件参数");
          }
          break;
        case "--theme-scale":
          if (!TryTakeValue(args, ref i, out themeScaleValue))
          {
            return Usage("--theme-scale 需要一个正数参数");
          }
          break;
        case "--assets":
          if (!TryTakeValue(args, ref i, out assets))
          {
            return Usage("--assets 需要一个目录参数");
          }
          break;
        case "--verify":
          verify = true;
          break;
        case "-h":
        case "--help":
          PrintUsage(Console.Out);
          return ExitSuccess;
        default:
          return Usage($"未知参数：{arg}");
      }
    }

    if (input == null || output == null)
    {
      return Usage("缺少必填参数 --input 或 --output");
    }

    string inputDir = Path.GetFullPath(input);
    string outputFile = Path.GetFullPath(output);
    string assetsDir = assets == null ? DefaultAssetsDir(inputDir) : Path.GetFullPath(assets);

    double themeScale;
    if (themeScaleValue == null)
    {
      themeScale = DetectThemeScale(inputDir);
    }
    else if (
      !double.TryParse(
        themeScaleValue,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture,
        out themeScale
      )
      || themeScale <= 0
    )
    {
      return Usage("--theme-scale 必须是正数（例如 2.0）");
    }

    var builder = new HelpPageSetBuilder(
      relative =>
        File.Exists(Path.Combine(assetsDir, relative.Replace('/', Path.DirectorySeparatorChar))),
      new HelpTypography(themeScale)
    );
    var result = builder.Build(inputDir);
    if (!result.Success)
    {
      foreach (var diagnostic in result.Errors)
      {
        Console.Error.WriteLine(diagnostic.ToString());
      }
      Console.Error.WriteLine(
        $"帮助内容校验失败：{result.Errors.Count} 处问题，未写入 {outputFile}。"
      );
      return ExitContentError;
    }

    string source = HelpPageSetBuilder.RenderSource(result);
    if (verify)
    {
      return Verify(outputFile, source, result.Pages.Count);
    }

    string? directory = Path.GetDirectoryName(outputFile);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }
    File.WriteAllText(outputFile, source, Utf8NoBom);
    Console.WriteLine($"帮助内容已生成：{result.Pages.Count} 页，源码 {source.Length} 字符。");
    if (result.MissingImageCount > 0)
    {
      Console.WriteLine(
        $"{result.MissingImageCount} 张图片缺失、已跳过（对应行不输出；补齐 src/assets/help/images 后重新构建即可）。"
      );
    }
    return ExitSuccess;
  }

  /// <summary>比对新生成的源码与磁盘上的产物，不写文件。</summary>
  /// <param name="outputFile">产物路径。</param>
  /// <param name="source">本次生成的源码。</param>
  /// <param name="pageCount">页数（打印用）。</param>
  /// <returns>退出码。</returns>
  private static int Verify(string outputFile, string source, int pageCount)
  {
    if (!File.Exists(outputFile))
    {
      Console.Error.WriteLine($"--verify 失败：产物不存在（{outputFile}），请先执行一次构建。");
      return ExitContentError;
    }
    string existing = File.ReadAllText(outputFile, Utf8NoBom).Replace("\r\n", "\n");
    if (!string.Equals(existing, source, StringComparison.Ordinal))
    {
      Console.Error.WriteLine(
        $"--verify 失败：产物与 docs/help 不一致（{outputFile}），请重新构建。"
      );
      return ExitContentError;
    }
    Console.WriteLine($"--verify 通过：{pageCount} 页与产物一致。");
    return ExitSuccess;
  }

  /// <summary>取选项后面的值。</summary>
  /// <param name="args">命令行参数。</param>
  /// <param name="index">当前下标（成功时推进）。</param>
  /// <param name="value">输出：选项值。</param>
  /// <returns>取到值返回 true。</returns>
  private static bool TryTakeValue(string[] args, ref int index, out string? value)
  {
    value = null;
    if (index + 1 >= args.Length)
    {
      return false;
    }
    value = args[index + 1];
    index++;
    return true;
  }

  /// <summary>
  /// 推导图片资源根目录：约定 <c>--input</c> 指向 <c>&lt;仓库&gt;/docs/help</c>，
  /// 图片在 <c>&lt;仓库&gt;/src/assets/help</c>。显式给出 --assets 时以参数为准。
  /// </summary>
  /// <param name="inputDir">帮助内容根目录（绝对路径）。</param>
  /// <returns>图片资源根目录（绝对路径）。</returns>
  private static string DefaultAssetsDir(string inputDir)
  {
    string docsDir = Path.GetDirectoryName(inputDir) ?? inputDir;
    string repoRoot = Path.GetDirectoryName(docsDir) ?? docsDir;
    return Path.Combine(repoRoot, "src", "assets", "help");
  }

  /// <summary>
  /// 从项目根目录的 <c>project.godot</c> 读取 <c>gui/theme/default_theme_scale</c>。
  /// </summary>
  /// <remarks>
  /// BBCode 的 <c>[font_size]</c> 是绝对像素、不随主题缩放变化，标题字号必须按实际生效字号换算，
  /// 因此这里读真实缩放；读不到时退回 1.0（Godot 默认）。
  /// </remarks>
  /// <param name="inputDir">帮助内容根目录（绝对路径，约定为 <c>&lt;仓库&gt;/docs/help</c>）。</param>
  /// <returns>主题缩放；读不到时为 1.0。</returns>
  private static double DetectThemeScale(string inputDir)
  {
    string docsDir = Path.GetDirectoryName(inputDir) ?? inputDir;
    string repoRoot = Path.GetDirectoryName(docsDir) ?? docsDir;
    string projectFile = Path.Combine(repoRoot, "project.godot");
    if (!File.Exists(projectFile))
    {
      Console.WriteLine(
        "提示：未找到 project.godot，标题字号按主题缩放 1.0 换算（若项目缩放不是 1.0，标题可能与正文不协调）。"
      );
      return 1.0;
    }

    foreach (string line in File.ReadAllLines(projectFile))
    {
      string trimmed = line.Trim();
      const string key = "theme/default_theme_scale=";
      if (!trimmed.StartsWith(key, StringComparison.Ordinal))
      {
        continue;
      }

      string raw = trimmed[key.Length..].Trim();
      if (
        double.TryParse(
          raw,
          System.Globalization.NumberStyles.Float,
          System.Globalization.CultureInfo.InvariantCulture,
          out double parsed
        )
        && parsed > 0
      )
      {
        return parsed;
      }
    }

    Console.WriteLine(
      "提示：project.godot 里没有可用的 gui/theme/default_theme_scale，标题字号按 1.0 换算。"
    );

    return 1.0;
  }

  /// <summary>打印用法并以用法错误退出。</summary>
  /// <param name="message">错误说明。</param>
  /// <returns>用法错误退出码。</returns>
  private static int Usage(string message)
  {
    Console.Error.WriteLine(message);
    PrintUsage(Console.Error);
    return ExitUsageError;
  }

  /// <summary>打印用法。</summary>
  /// <param name="writer">输出目标。</param>
  private static void PrintUsage(TextWriter writer)
  {
    writer.WriteLine("帮助内容生成器（构建期工具）");
    writer.WriteLine();
    writer.WriteLine("用法：");
    writer.WriteLine(
      "  HelpContentGenerator --input <docs/help> --output <生成文件> [--assets <目录>] [--theme-scale <正数>] [--verify]"
    );
    writer.WriteLine();
    writer.WriteLine("  --input   帮助内容根目录（含 <分区>/<页面>.md）");
    writer.WriteLine(
      "  --output  生成文件路径（通常是 src/core/help/Generated/HelpContent.Generated.cs）"
    );
    writer.WriteLine(
      "  --assets      图片资源根目录，默认由 --input 推导为 <仓库>/src/assets/help"
    );
    writer.WriteLine(
      "  --theme-scale 主题缩放（默认读 project.godot 的 gui/theme/default_theme_scale，缺失按 1.0）"
    );
    writer.WriteLine("  --verify  只比对不写：产物与内容不一致时非零退出");
    writer.WriteLine();
    writer.WriteLine("示例：");
    writer.WriteLine(
      "  HelpContentGenerator --input docs/help --output src/core/help/Generated/HelpContent.Generated.cs"
    );
    writer.WriteLine(
      "  HelpContentGenerator --input docs/help --output src/core/help/Generated/HelpContent.Generated.cs --verify"
    );
  }
}
