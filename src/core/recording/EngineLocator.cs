namespace AutoCMEX.Core.Recording;

using System;
using System.IO;
using System.Linq;

/// <summary>
/// LuaSTG 引擎目录的校验、可执行文件定位，以及从 Sharp 编辑器目录反推引擎目录。
/// </summary>
/// <remarks>
/// <para>
/// 本文件的全部方法都是纯查询（不写盘、不起进程），便于 UI 侧随时调用做状态提示。
/// </para>
/// <para>
/// 「引擎目录」= 含 <c>game/</c> 与 <c>doc/</c> 的 LuaSTG 根目录；可执行文件位于 <c>game/</c> 内
/// （根目录下没有 exe），且进程的工作目录必须是 <c>game/</c> —— 这一点由
/// <c>GameProcessRunner</c> 保证。
/// </para>
/// </remarks>
public static class EngineLocator
{
  /// <summary><c>game/</c> 内的引擎共享初始化脚本名；它的存在是判定「这是引擎目录」的关键。</summary>
  public const string LaunchFileName = "launch";

  /// <summary><c>game/</c> 目录名。</summary>
  public const string GameDirName = "game";

  /// <summary>引擎可执行文件名模式（引擎升级会换版本号命名，故用通配匹配）。</summary>
  public const string ExePattern = "LuaSTGSub*.exe";

  /// <summary>从 Sharp 目录反推时最多上溯的层数。</summary>
  public const int InferMaxDepth = 3;

  /// <summary>
  /// 取引擎的 <c>game/</c> 目录绝对路径（进程工作目录）。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns><c>&lt;引擎&gt;/game</c>。</returns>
  public static string GetGameDir(string engineDir) => Path.Combine(engineDir, GameDirName);

  /// <summary>
  /// 校验引擎目录是否可用。
  /// </summary>
  /// <param name="engineDir">待校验的引擎根目录；允许为空。</param>
  /// <param name="reason">校验失败原因（用于 UI 提示）；成功时为空串。</param>
  /// <returns>可用返回 true。</returns>
  public static bool TryValidate(string? engineDir, out string reason)
  {
    if (string.IsNullOrWhiteSpace(engineDir))
    {
      reason = "未设置引擎目录";
      return false;
    }

    if (!Directory.Exists(engineDir))
    {
      reason = "引擎目录不存在";
      return false;
    }

    var gameDir = GetGameDir(engineDir);
    if (!Directory.Exists(gameDir))
    {
      reason = "目录内没有 game/ 子目录";
      return false;
    }

    if (!File.Exists(Path.Combine(gameDir, LaunchFileName)))
    {
      reason = "game/ 内没有 launch 文件，可能不是 LuaSTG 引擎目录";
      return false;
    }

    reason = string.Empty;
    return true;
  }

  /// <summary>
  /// 定位引擎可执行文件。
  /// </summary>
  /// <param name="engineDir">引擎根目录。</param>
  /// <returns>可执行文件绝对路径；找不到返回 <c>null</c>。</returns>
  /// <remarks>
  /// 优先取无版本号后缀的 <c>LuaSTGSub.exe</c>（本机实测的常规启动入口）；否则取
  /// <c>LuaSTGSub*.exe</c> 中按序号排序的第一个（<c>LuaSTGSub-BentLaser.exe</c> 等定制构建也会命中，
  /// 此时应由用户确认目录是否选错）。
  /// </remarks>
  public static string? FindEngineExe(string? engineDir)
  {
    if (string.IsNullOrWhiteSpace(engineDir))
    {
      return null;
    }

    var gameDir = GetGameDir(engineDir);
    if (!Directory.Exists(gameDir))
    {
      return null;
    }

    var exact = Path.Combine(gameDir, "LuaSTGSub.exe");
    if (File.Exists(exact))
    {
      return exact;
    }

    var candidates = Directory
      .GetFiles(gameDir, ExePattern)
      .OrderBy(p => p, StringComparer.Ordinal)
      .ToList();

    return candidates.FirstOrDefault();
  }

  /// <summary>
  /// 从 Sharp 编辑器目录上溯推断引擎目录。
  /// </summary>
  /// <param name="sharpEditorPath">Sharp 编辑器路径（目录或可执行文件）；来自合并板块配置。</param>
  /// <returns>第一个含 <c>game/launch</c> 的上溯目录；推断失败返回 <c>null</c>。</returns>
  /// <remarks>
  /// 只做「上溯最多 <see cref="InferMaxDepth"/> 层」的保守推断：推断失败不阻塞用户手选，
  /// 设置面板只给出提示。
  /// </remarks>
  public static string? TryInferFromSharpDir(string? sharpEditorPath)
  {
    if (string.IsNullOrWhiteSpace(sharpEditorPath))
    {
      return null;
    }

    var start = Directory.Exists(sharpEditorPath)
      ? sharpEditorPath
      : Path.GetDirectoryName(sharpEditorPath);
    if (string.IsNullOrEmpty(start))
    {
      return null;
    }

    var current = new DirectoryInfo(start);
    for (var depth = 0; depth <= InferMaxDepth && current != null; depth++)
    {
      if (File.Exists(Path.Combine(current.FullName, GameDirName, LaunchFileName)))
      {
        return current.FullName;
      }

      current = current.Parent;
    }

    return null;
  }
}
