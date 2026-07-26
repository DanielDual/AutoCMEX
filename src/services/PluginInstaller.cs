namespace AutoCMEX.Services;

using Godot;

/// <summary>
/// 插件安装服务，封装插件目录的递归复制操作。
/// </summary>
public static class PluginInstaller
{
  /// <summary>
  /// 递归复制插件目录到目标位置。
  /// </summary>
  /// <param name="sourceDir">源目录路径。</param>
  /// <param name="destDir">目标目录路径。</param>
  public static void CopyPluginDir(string sourceDir, string destDir)
  {
    var dir = DirAccess.Open(sourceDir);
    if (dir == null)
      return;

    DirAccess.MakeDirAbsolute(destDir);

    dir.ListDirBegin();
    var fileName = dir.GetNext();
    while (!string.IsNullOrEmpty(fileName))
    {
      if (fileName != "." && fileName != "..")
      {
        var srcPath = System.IO.Path.Combine(sourceDir, fileName);
        var dstPath = System.IO.Path.Combine(destDir, fileName);

        if (dir.CurrentIsDir())
          CopyPluginDir(srcPath, dstPath);
        else
          DirAccess.CopyAbsolute(srcPath, dstPath);
      }
      fileName = dir.GetNext();
    }
    dir.ListDirEnd();
  }
}
