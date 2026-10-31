namespace AutoCMEX.Services;

using System;
using System.IO;

/// <summary>
/// Koishi 侧的安装落点解析结果。
/// </summary>
/// <param name="AppRoot">Koishi 应用根目录（含 koishi.yml 的目录）。</param>
/// <param name="PluginsDir">插件目录（应用根下的 external，工作区 external/* 的成员）。</param>
/// <param name="InstallDir">本插件的安装目录（插件目录下的包短名）。</param>
/// <param name="LegacyDirExists">是否残留改名前的旧落点目录。</param>
/// <param name="LinkExists">包管理器是否已把本插件链接进 <c>node_modules</c>。</param>
public sealed record KoishiInstallPlan(
  string AppRoot,
  string PluginsDir,
  string InstallDir,
  bool LegacyDirExists,
  bool LinkExists
);

/// <summary>
/// AutoCMEX 在 Koishi 侧的安装约定：落点位置、包短名与控制台里的检索口径。
/// </summary>
/// <remarks>
/// 这些常量是 Koishi 插件包（<c>src/plugin/koishi/</c>）与设置页共用的唯一事实源。
/// 控制台「添加插件」只按包短名做子串检索（包名去掉 <c>koishi-plugin-</c> 前缀），
/// 并按 <c>koishi.category</c> 归类，因此包名、安装目录名、插件自报名三者必须同为短名。
/// </remarks>
public static class KoishiPluginSpec
{
  /// <summary>插件包名（package.json 的 name）。</summary>
  public const string PackageName = "koishi-plugin-adapter-autocmex";

  /// <summary>包短名：包名去掉 <c>koishi-plugin-</c> 前缀。</summary>
  public const string ShortName = "adapter-autocmex";

  /// <summary>插件源码在 Godot 资源目录中的位置。</summary>
  public const string SourceDir = "res://src/plugin/koishi/";

  /// <summary>Koishi 应用根目录的标志文件。</summary>
  public const string AppManifestFileName = "koishi.yml";

  /// <summary>工作区里存放外部插件的目录名。</summary>
  public const string ExternalDirName = "external";

  /// <summary>包管理器安装依赖的目录名。</summary>
  public const string NodeModulesDirName = "node_modules";

  /// <summary>改名前的旧落点目录名（与现落点声明同一个包名，残留会造成工作区重名）。</summary>
  public const string LegacyDirName = "auto-cmex";

  /// <summary>
  /// 解析用户所选目录，得到确定的安装落点。
  /// </summary>
  /// <param name="selectedDir">用户在文件对话框里选择的目录。</param>
  /// <param name="plan">解析成功时的落点信息。</param>
  /// <param name="error">解析失败时的原因（可直接展示给用户）。</param>
  /// <returns>所选目录是 Koishi 应用根目录或它的 external 目录时为 true。</returns>
  public static bool TryResolve(string selectedDir, out KoishiInstallPlan plan, out string error)
  {
    plan = null!;
    error = string.Empty;

    if (string.IsNullOrWhiteSpace(selectedDir))
    {
      error = "请选择 Koishi 应用根目录（含 koishi.yml 的目录），或它的 external 目录。";
      return false;
    }

    if (!TryResolveAppRoot(selectedDir, out var appRoot))
    {
      error =
        $"所选目录不是 Koishi 应用根目录，也不是它的 external 目录：{selectedDir.Trim()}"
        + $"\n请选择含 {AppManifestFileName} 的目录（或该目录下的 {ExternalDirName}）。";
      return false;
    }

    var pluginsDir = Path.Combine(appRoot, ExternalDirName);
    plan = new KoishiInstallPlan(
      appRoot,
      pluginsDir,
      Path.Combine(pluginsDir, ShortName),
      Directory.Exists(Path.Combine(pluginsDir, LegacyDirName)),
      Directory.Exists(Path.Combine(appRoot, NodeModulesDirName, PackageName))
    );
    return true;
  }

  /// <summary>
  /// 拼装安装完成后的后续步骤提示：启用路径、工作区链接缺失提醒、旧版目录残留提醒。
  /// </summary>
  /// <param name="plan">安装落点解析结果。</param>
  /// <returns>可直接作为弹窗文本的多行说明。</returns>
  public static string DescribeInstalled(KoishiInstallPlan plan)
  {
    var text =
      $"插件已安装到 {plan.InstallDir}"
      + $"\n在 Koishi 控制台启用：插件配置 → 右键 → 添加插件 → 搜索 {ShortName}"
      + $"（或直接看「适配器」分类）→ 添加并启用。";

    if (!plan.LinkExists)
    {
      text +=
        $"\n未检测到工作区链接：请先在 {plan.AppRoot} 执行一次 yarn install（或 npm install），"
        + "控制台才能加载该插件。";
    }

    if (plan.LegacyDirExists)
    {
      text +=
        $"\n检测到旧版目录 {Path.Combine(plan.PluginsDir, LegacyDirName)}："
        + "它与本次安装声明同一个包名，请先移走，避免控制台加载到旧文件。";
    }

    return text;
  }

  /// <summary>
  /// 判定应用根目录：所选目录自身含 <c>koishi.yml</c>，或它是某个应用根下的 <c>external</c> 目录。
  /// </summary>
  /// <param name="selectedDir">用户所选目录。</param>
  /// <param name="appRoot">解析出的应用根目录。</param>
  /// <returns>能判定出应用根目录时为 true。</returns>
  private static bool TryResolveAppRoot(string selectedDir, out string appRoot)
  {
    appRoot = string.Empty;

    var full = Path.GetFullPath(selectedDir.Trim());
    var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    if (File.Exists(Path.Combine(trimmed, AppManifestFileName)))
    {
      appRoot = trimmed;
      return true;
    }

    if (
      string.Equals(Path.GetFileName(trimmed), ExternalDirName, StringComparison.OrdinalIgnoreCase)
    )
    {
      var parent = Path.GetDirectoryName(trimmed);
      if (!string.IsNullOrEmpty(parent) && File.Exists(Path.Combine(parent, AppManifestFileName)))
      {
        appRoot = parent;
        return true;
      }
    }

    return false;
  }
}
