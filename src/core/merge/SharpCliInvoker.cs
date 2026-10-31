namespace AutoCMEX.Core.Merge;

using System;
using System.Diagnostics;
using System.IO;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// 外调 LuaSTGEditorSharp.Core.Cli.exe，把合并后的顶层工程编译打包成 mod zip。
/// 命令形如：<code>&lt;in&gt;.lstgproj -d &lt;out_dir&gt; -n &lt;name&gt; -p &lt;plugin.dll&gt;</code>。
/// 运行正确性由 Sharp 官方 Cli 兜底；本类只负责构造参数并调起进程、收敛输出到日志。
/// </summary>
public class SharpCliInvoker
{
  private static readonly string CliFileName = "LuaSTGEditorSharp.Core.Cli.exe";

  private readonly ILog _log;

  public SharpCliInvoker() => _log = AppLogs.GetOrCreate().GetLogger(nameof(SharpCliInvoker));

  /// <summary>
  /// 构造 Cli 进程参数数组（纯逻辑，便于单元测试断言）。
  /// </summary>
  public static string[] BuildArguments(
    string inputProject,
    string outputDir,
    string outputName,
    string pluginDll
  ) => new[] { inputProject, "-d", outputDir, "-n", outputName, "-p", pluginDll };

  /// <summary>
  /// 调起 Cli 编译打包。返回是否成功（退出码 0）。stdout/stderr 收敛进日志，不刷屏。
  /// </summary>
  /// <param name="editorPath">Sharp 安装目录（Cli.exe 所在目录，作为进程工作目录）。</param>
  /// <param name="inputProject">合并后的顶层工程文件路径。</param>
  /// <param name="outputDir">输出目录（mod zip 会写入该目录）。</param>
  /// <param name="outputName">输出文件名（不带动 zip 后缀）。</param>
  /// <param name="pluginDll">编译插件 dll 文件名（如 LuaSTGPlusLib.dll）。</param>
  /// <param name="exitCode">进程退出码。</param>
  /// <param name="output">捕获到的最新输出（供日志/错误展示）。</param>
  public bool Run(
    string editorPath,
    string inputProject,
    string outputDir,
    string outputName,
    string pluginDll,
    out int exitCode,
    out string output
  )
  {
    exitCode = -1;
    output = string.Empty;

    var cliPath = Path.Combine(editorPath, CliFileName);
    if (!File.Exists(cliPath))
    {
      _log.Warn($"SharpCliInvoker: Cli not found at {cliPath}. Skip packaging.");
      return false;
    }

    if (!File.Exists(inputProject))
    {
      _log.Warn($"SharpCliInvoker: input project not found at {inputProject}.");
      return false;
    }

    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = cliPath,
        WorkingDirectory = Path.GetDirectoryName(cliPath) ?? editorPath,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      foreach (var arg in BuildArguments(inputProject, outputDir, outputName, pluginDll))
        psi.ArgumentList.Add(arg);

      using var proc = Process.Start(psi);
      if (proc == null)
      {
        _log.Warn("SharpCliInvoker: failed to start process.");
        return false;
      }

      var stdout = proc.StandardOutput.ReadToEnd();
      var stderr = proc.StandardError.ReadToEnd();
      proc.WaitForExit();

      exitCode = proc.ExitCode;
      output = stdout;
      if (!string.IsNullOrWhiteSpace(stderr))
        output += "\n[stderr]\n" + stderr;

      if (exitCode == 0)
        _log.Print($"SharpCliInvoker: packaging succeeded -> {outputName}.zip");
      else
        _log.Warn($"SharpCliInvoker: packaging failed (exit {exitCode}). {output}");

      return exitCode == 0;
    }
    catch (Exception ex)
    {
      _log.Warn($"SharpCliInvoker: exception while invoking Cli: {ex.Message}");
      return false;
    }
  }
}
