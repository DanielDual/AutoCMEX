namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using Chickensoft.Log;

/// <summary>
/// 基于 Prometheus v0.3.1 + Lua 5.1.5 的 Lua 源码混淆器（<see cref="ILuaObfuscator"/> 生产实现）。
/// <para>
/// 工具以固定版本随包入库（<c>src/tools/lua-obfuscator/</c>）：运行期先读 <c>manifest.json</c>、
/// 逐文件校验 SHA256，再释放到 <c>%TEMP%/AutoCMEX/tools/&lt;bundle hash&gt;/</c>（已释放且校验通过则复用），
/// 最后用绝对路径子进程调用 <c>lua5.1.exe cli.lua --config &lt;cfg&gt; --Lua51 --nocolors --out &lt;out&gt; &lt;in&gt;</c>。
/// Godot PCK 内的文件不能直接作为子进程执行，故必须先释放到临时目录；不联网下载任何内容。
/// </para>
/// <para>
/// fail-closed：工具缺失/校验失败/超时/退出码非 0/输出为空/输出无法被 Lua 5.1 <c>loadfile</c> 加载时
/// 一律返回失败并带原因（含失败条目），不把原文当混淆结果返回。
/// </para>
/// </summary>
public sealed class PrometheusLuaObfuscator : ILuaObfuscator
{
  /// <summary>随包工具清单的默认资源根（与 <c>export_presets.cfg</c> 的 include_filter 对应）。</summary>
  public const string DefaultBundleResourceRoot = "res://src/tools/lua-obfuscator";

  /// <summary>工具清单文件名。</summary>
  private const string ManifestFileName = "manifest.json";

  /// <summary>工具包默认的单文件混淆超时（毫秒）。实测单文件数百毫秒，留足大文件余量。</summary>
  public const int DefaultObfuscateTimeoutMs = 180000;

  /// <summary>工具包默认的输出加载校验超时（毫秒）。</summary>
  public const int DefaultVerifyTimeoutMs = 60000;

  /// <summary>
  /// 期望的运行时工具包哈希锚：必须同时等于 <c>manifest.json</c> 的 <c>bundleHash</c>
  /// 与按清单重算出的哈希，否则运行期 fail-closed（防止 manifest 与代码/工具文件版本漂移）。
  /// <para>
  /// 升级/替换任何运行期工具文件（cli.lua、src/、LICENSE、lua5.1.exe、lua5.1.dll、COPYRIGHT、
  /// nocff_seed42.lua）时，必须重出 manifest.json 并同步更新本常量；
  /// README.md 等非运行期文档不参与哈希与释放，改它们不需要动锚值。
  /// </para>
  /// </summary>
  public const string ExpectedBundleHash =
    "6c5868a0f594881be6757ad8f70c45f455a658b452f791fae5ad1639ad6e5d0b";

  /// <summary>Lua 5.1 校验脚本：用 <c>loadfile</c> 编译输出，失败时写 stderr 并以 1 退出。</summary>
  private const string LoadCheckScript =
    "local f, e = loadfile(arg[1])\n"
    + "if f == nil then\n"
    + "  io.stderr:write(tostring(e))\n"
    + "  os.exit(1)\n"
    + "end\n";

  private readonly ILog _log;
  private readonly string _bundleResourceRoot;
  private readonly int _obfuscateTimeoutMs;
  private readonly int _verifyTimeoutMs;

  private readonly object _bundleLock = new();
  private bool _bundleResolved;
  private string? _bundleError;
  private string _luaExe = string.Empty;
  private string _cliScript = string.Empty;
  private string _configFile = string.Empty;

  /// <summary>
  /// 创建 Prometheus 混淆器。
  /// </summary>
  /// <param name="log">日志；为 <c>null</c> 时使用 <see cref="NullLog.Instance"/>。</param>
  /// <param name="bundleResourceRoot">
  /// 工具清单资源根；默认 <see cref="DefaultBundleResourceRoot"/>。测试可传物理目录覆盖。
  /// </param>
  /// <param name="obfuscateTimeoutMs">单文件混淆超时（毫秒，最小 1000）。</param>
  /// <param name="verifyTimeoutMs">单文件输出加载校验超时（毫秒，最小 1000）。</param>
  public PrometheusLuaObfuscator(
    ILog? log = null,
    string? bundleResourceRoot = null,
    int obfuscateTimeoutMs = DefaultObfuscateTimeoutMs,
    int verifyTimeoutMs = DefaultVerifyTimeoutMs
  )
  {
    _log = log ?? NullLog.Instance;
    _bundleResourceRoot = string.IsNullOrWhiteSpace(bundleResourceRoot)
      ? DefaultBundleResourceRoot
      : bundleResourceRoot.Trim();
    _obfuscateTimeoutMs = Math.Max(1000, obfuscateTimeoutMs);
    _verifyTimeoutMs = Math.Max(1000, verifyTimeoutMs);
  }

  /// <inheritdoc />
  public LuaObfuscationResult Obfuscate(byte[] source, string entryName)
  {
    var name = string.IsNullOrWhiteSpace(entryName) ? "<unknown>.lua" : entryName;
    if (source == null || source.Length == 0)
      return LuaObfuscationResult.Failure($"Lua 源码为空，无法混淆：{name}");
    if (!EnsureBundle(out var bundleError))
      return LuaObfuscationResult.Failure(bundleError ?? "混淆工具不可用");

    var workDir = Path.Combine(Path.GetTempPath(), "AutoCMEX", "obf", Guid.NewGuid().ToString("N"));
    try
    {
      Directory.CreateDirectory(workDir);
      var inputPath = Path.Combine(workDir, "in.lua");
      var outputPath = Path.Combine(workDir, "out.lua");

      // 去 UTF-8 BOM：Prometheus 对 BOM 会报 Lexing Error（LuaJIT 的 loadfile 能容忍，掩盖差异）。
      File.WriteAllBytes(inputPath, StripUtf8Bom(source));

      var psi = CreateStartInfo(_luaExe);
      psi.ArgumentList.Add(_cliScript);
      psi.ArgumentList.Add("--config");
      psi.ArgumentList.Add(_configFile);
      psi.ArgumentList.Add("--Lua51");
      psi.ArgumentList.Add("--nocolors");
      psi.ArgumentList.Add("--out");
      psi.ArgumentList.Add(outputPath);
      psi.ArgumentList.Add(inputPath);

      var run = RunProcess(psi, _obfuscateTimeoutMs);
      if (run.TimedOut)
        return LuaObfuscationResult.Failure(
          $"混淆超时（{name}，超过 {_obfuscateTimeoutMs}ms，已终止子进程）"
        );
      if (run.ExitCode != 0)
      {
        _log.Warn($"PrometheusLuaObfuscator: obfuscation failed for {name} (exit {run.ExitCode}).");
        return LuaObfuscationResult.Failure(DescribeFailure(name, run));
      }
      if (!File.Exists(outputPath))
        return LuaObfuscationResult.Failure($"混淆工具未产出文件：{name}");

      var output = StripUtf8Bom(File.ReadAllBytes(outputPath));
      if (output.Length == 0)
        return LuaObfuscationResult.Failure($"混淆结果为空：{name}");
      if (Array.IndexOf(output, (byte)0) >= 0)
        return LuaObfuscationResult.Failure($"混淆结果含 NUL 字节（应为纯 Lua 源码文本）：{name}");

      // 校验用的正是要交付的字节（去 BOM 后回写），确保「校验通过」与「交付内容」一致。
      File.WriteAllBytes(outputPath, output);
      if (!VerifyLoadable(outputPath, workDir, out var verifyError))
        return LuaObfuscationResult.Failure($"混淆结果校验失败：{name}：{verifyError}");

      return LuaObfuscationResult.Success(output);
    }
    catch (Exception ex)
    {
      _log.Warn($"PrometheusLuaObfuscator: exception while obfuscating {name}: {ex.Message}");
      return LuaObfuscationResult.Failure($"混淆异常（{name}）：{ex.Message}");
    }
    finally
    {
      TryDeleteDirectory(workDir);
    }
  }

  /// <summary>返回已释放并校验通过的工具路径；失败时返回 false 并给出原因（同一实例只解析一次）。</summary>
  private bool EnsureBundle(out string? error)
  {
    lock (_bundleLock)
    {
      if (!_bundleResolved)
      {
        _bundleResolved = true;
        _bundleError = ResolveBundle(out _luaExe, out _cliScript, out _configFile);
      }
      error = _bundleError;
      return error == null;
    }
  }

  /// <summary>
  /// 读清单 → 校验 res:// 内逐文件 SHA256 → 释放到临时目录（校验通过则复用）。
  /// 返回 <c>null</c> 表示成功，否则为失败原因。
  /// </summary>
  private string? ResolveBundle(out string luaExe, out string cliScript, out string configFile)
  {
    luaExe = string.Empty;
    cliScript = string.Empty;
    configFile = string.Empty;

    var manifestResourcePath = CombineResourcePath(_bundleResourceRoot, ManifestFileName);
    if (!TryReadAllBytes(manifestResourcePath, out var manifestBytes))
      return $"未找到混淆工具清单：{manifestResourcePath}";

    List<BundleFile> bundleFiles;
    string declaredBundleHash;
    string luaExeRel;
    string cliRel;
    string configRel;
    try
    {
      ParseManifest(
        manifestBytes,
        out bundleFiles,
        out declaredBundleHash,
        out luaExeRel,
        out cliRel,
        out configRel
      );
    }
    catch (Exception ex)
    {
      return $"混淆工具清单解析失败：{ex.Message}";
    }

    if (bundleFiles.Count == 0)
      return "混淆工具清单为空";

    // 先读 res:// 全部文件并逐文件校验（编辑器运行与导出 PCK 都走这条路径）。
    var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    foreach (var file in bundleFiles)
    {
      var resourcePath = CombineResourcePath(_bundleResourceRoot, file.Path);
      if (!TryReadAllBytes(resourcePath, out var data))
        return $"混淆工具文件缺失：{file.Path}";
      if (!Sha256Hex(data).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
        return $"混淆工具文件校验失败：{file.Path}";
      contents[file.Path] = data;
    }

    // 三重锚定：重算值 == manifest 声明值 == 代码内 ExpectedBundleHash，任一不符即 fail-closed。
    if (string.IsNullOrEmpty(declaredBundleHash))
      return "混淆工具清单缺少 bundleHash，无法校验工具完整性";

    var computedBundleHash = ComputeBundleHash(bundleFiles);
    if (!computedBundleHash.Equals(declaredBundleHash, StringComparison.OrdinalIgnoreCase))
    {
      return "混淆工具清单 bundleHash 与实际文件不一致"
        + $"（manifest 声明 {declaredBundleHash}，实际 {computedBundleHash}；工具文件可能被修改或清单未同步）";
    }
    if (!computedBundleHash.Equals(ExpectedBundleHash, StringComparison.OrdinalIgnoreCase))
    {
      return "混淆工具包版本不符，已 fail-closed"
        + $"（期望锚 {ExpectedBundleHash}，实际 {computedBundleHash}；"
        + "升级工具需同时更新 manifest.json 与 PrometheusLuaObfuscator.ExpectedBundleHash）";
    }

    var bundleRoot = Path.Combine(Path.GetTempPath(), "AutoCMEX", "tools", computedBundleHash);
    if (!IsExtractedBundleValid(bundleRoot, bundleFiles))
    {
      try
      {
        if (Directory.Exists(bundleRoot))
          Directory.Delete(bundleRoot, true);
      }
      catch
      { /* 旧目录被占用时仍尝试覆盖写入 */
      }

      try
      {
        foreach (var file in bundleFiles)
        {
          var dest = Path.Combine(bundleRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
          var destDir = Path.GetDirectoryName(dest);
          if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);
          File.WriteAllBytes(dest, contents[file.Path]);
        }
      }
      catch (Exception ex)
      {
        return $"释放混淆工具失败：{ex.Message}";
      }

      if (!IsExtractedBundleValid(bundleRoot, bundleFiles))
        return "释放后混淆工具校验失败";
    }

    luaExe = ToLocalPath(bundleRoot, luaExeRel);
    cliScript = ToLocalPath(bundleRoot, cliRel);
    configFile = ToLocalPath(bundleRoot, configRel);
    foreach (var path in new[] { luaExe, cliScript, configFile })
    {
      if (!File.Exists(path))
        return $"混淆工具可执行路径不存在：{path}";
    }

    _log.Print($"PrometheusLuaObfuscator: tool bundle ready at {bundleRoot}.");
    return null;
  }

  /// <summary>
  /// 用同一份 lua5.1.exe 对输出做 <c>loadfile</c> 校验（不可加载即 fail-closed）。
  /// 校验脚本写入本次调用的独立工作目录（<paramref name="workDir"/>），不写共享工具包目录。
  /// </summary>
  private bool VerifyLoadable(string luaPath, string workDir, out string error)
  {
    error = string.Empty;
    var checkScriptPath = Path.Combine(workDir, "check.lua");
    try
    {
      File.WriteAllText(checkScriptPath, LoadCheckScript, new UTF8Encoding(false));
    }
    catch (Exception ex)
    {
      error = $"无法写入校验脚本：{ex.Message}";
      return false;
    }

    var psi = CreateStartInfo(_luaExe);
    psi.ArgumentList.Add(checkScriptPath);
    psi.ArgumentList.Add(luaPath);

    var run = RunProcess(psi, _verifyTimeoutMs);
    if (run.TimedOut)
    {
      error = $"Lua 5.1 加载校验超时（超过 {_verifyTimeoutMs}ms）";
      return false;
    }
    if (run.ExitCode != 0)
    {
      error = "输出无法被 Lua 5.1 loadfile 加载：" + JoinSummary(run.Stderr, run.Stdout);
      return false;
    }
    return true;
  }

  /// <summary>构造统一的重定向、无窗口子进程启动信息。</summary>
  private static ProcessStartInfo CreateStartInfo(string fileName) =>
    new()
    {
      FileName = fileName,
      WorkingDirectory = Path.GetDirectoryName(fileName) ?? string.Empty,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };

  /// <summary>运行子进程并按超时终止；返回退出码与输出（超时以 <see cref="ProcessRunResult.TimedOut"/> 标记）。</summary>
  private static ProcessRunResult RunProcess(ProcessStartInfo psi, int timeoutMs)
  {
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException($"无法启动子进程：{psi.FileName}");

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();

    bool exited = process.WaitForExit(timeoutMs);
    if (!exited)
    {
      try
      {
        process.Kill(entireProcessTree: true);
      }
      catch
      { /* 进程可能恰好自行退出 */
      }
      try
      {
        process.WaitForExit(5000);
      }
      catch
      { /* 忽略终止等待异常 */
      }
      return new ProcessRunResult(true, -1, SafeGet(stdoutTask), SafeGet(stderrTask));
    }

    return new ProcessRunResult(false, process.ExitCode, SafeGet(stdoutTask), SafeGet(stderrTask));
  }

  /// <summary>把失败输出整理成面向用户的原因（识别 goto/label 与 BOM 两类高频失败）。</summary>
  private static string DescribeFailure(string entryName, ProcessRunResult run)
  {
    var detail = JoinSummary(run.Stderr, run.Stdout);
    if (detail.Contains("Parsing Error", StringComparison.OrdinalIgnoreCase))
    {
      return $"Lua 解析失败（含 goto/label 等 LuaJIT 扩展语法时 Prometheus 无法解析）："
        + $"{entryName}：{detail}";
    }
    if (detail.Contains("Lexing Error", StringComparison.OrdinalIgnoreCase))
      return $"Lua 词法解析失败（如 UTF-8 BOM 等）：{entryName}：{detail}";
    return $"Prometheus 混淆失败（exit {run.ExitCode}）：{entryName}：{detail}";
  }

  /// <summary>合并 stderr/stdout 摘要为单行短文本。</summary>
  private static string JoinSummary(string stderr, string stdout)
  {
    var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
    if (string.IsNullOrWhiteSpace(text))
      return "<无输出>";
    var lines = text.Replace("\r\n", "\n")
      .Split('\n', StringSplitOptions.RemoveEmptyEntries)
      .Select(line => line.Trim())
      .Where(line => line.Length > 0)
      .TakeLast(6)
      .ToList();
    var summary = string.Join(" | ", lines);
    return summary.Length <= 500 ? summary : summary[..500] + "…";
  }

  /// <summary>解析清单：文件列表（path+sha256）、bundleHash 与三个关键入口相对路径。</summary>
  private static void ParseManifest(
    byte[] json,
    out List<BundleFile> files,
    out string bundleHash,
    out string luaExecutable,
    out string cliScript,
    out string config
  )
  {
    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    bundleHash = root.TryGetProperty("bundleHash", out var hashElement)
      ? hashElement.GetString() ?? string.Empty
      : string.Empty;
    luaExecutable = GetRequiredString(root, "luaExecutable");
    cliScript = GetRequiredString(root, "cliScript");
    config = GetRequiredString(root, "config");
    // 三个入口路径同样按清单文件路径规则校验，防止清单指向绝对路径或临时目录之外。
    ValidateRelativePath(luaExecutable);
    ValidateRelativePath(cliScript);
    ValidateRelativePath(config);

    files = new List<BundleFile>();
    if (
      !root.TryGetProperty("files", out var filesElement)
      || filesElement.ValueKind != JsonValueKind.Array
    )
      throw new InvalidDataException("缺少 files 数组");

    foreach (var item in filesElement.EnumerateArray())
    {
      var path = GetRequiredString(item, "path");
      var sha256 = GetRequiredString(item, "sha256");
      ValidateRelativePath(path);
      if (sha256.Length != 64)
        throw new InvalidDataException($"sha256 长度非法：{path}");
      files.Add(new BundleFile(path, sha256));
    }

    files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
  }

  private static string GetRequiredString(JsonElement element, string propertyName)
  {
    if (
      !element.TryGetProperty(propertyName, out var value)
      || value.ValueKind != JsonValueKind.String
    )
      throw new InvalidDataException($"缺少字符串字段：{propertyName}");
    return value.GetString() ?? throw new InvalidDataException($"字段为空：{propertyName}");
  }

  /// <summary>拒绝绝对路径与 <c>..</c> 越界，防止清单指向临时目录之外。</summary>
  private static void ValidateRelativePath(string path)
  {
    if (
      string.IsNullOrWhiteSpace(path)
      || path.StartsWith("/", StringComparison.Ordinal)
      || path.Contains('\\')
      || path.Contains(':')
    )
      throw new InvalidDataException($"非法工具文件路径：{path}");
    foreach (var segment in path.Split('/'))
    {
      if (segment.Length == 0 || segment == "." || segment == "..")
        throw new InvalidDataException($"非法工具文件路径：{path}");
    }
  }

  /// <summary>计算 bundle hash：按路径排序后对 <c>path\nsha256\n</c> 拼接串做 SHA256（小写十六进制）。</summary>
  private static string ComputeBundleHash(IReadOnlyList<BundleFile> files)
  {
    var builder = new StringBuilder();
    foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
      builder.Append(file.Path).Append('\n').Append(file.Sha256.ToLowerInvariant()).Append('\n');
    return Sha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
  }

  /// <summary>校验已释放目录：每个文件存在且 SHA256 与清单一致（用于缓存复用判定）。</summary>
  private static bool IsExtractedBundleValid(string bundleRoot, IReadOnlyList<BundleFile> files)
  {
    try
    {
      if (!Directory.Exists(bundleRoot))
        return false;
      foreach (var file in files)
      {
        var path = Path.Combine(bundleRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
          return false;
        if (
          !Sha256Hex(File.ReadAllBytes(path))
            .Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)
        )
          return false;
      }
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static string ToLocalPath(string bundleRoot, string relativePath) =>
    Path.Combine(bundleRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

  /// <summary>拼接资源路径（<c>res://</c> 用正斜杠；物理路径同样可用）。</summary>
  private static string CombineResourcePath(string root, string relativePath) =>
    root.TrimEnd('/') + "/" + relativePath.TrimStart('/');

  /// <summary>
  /// 读取 <c>res://</c>/<c>user://</c>/物理文件字节：Godot PCK 与编辑器运行都必须经 Godot.FileAccess。
  /// </summary>
  private static bool TryReadAllBytes(string path, out byte[] data)
  {
    data = Array.Empty<byte>();
    try
    {
      if (
        path.StartsWith("res://", StringComparison.Ordinal)
        || path.StartsWith("user://", StringComparison.Ordinal)
      )
      {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
          return false;
        data = file.GetBuffer((long)file.GetLength());
        return true;
      }
      if (!File.Exists(path))
        return false;
      data = File.ReadAllBytes(path);
      return true;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>去除 UTF-8 BOM（<c>EF BB BF</c>）。</summary>
  private static byte[] StripUtf8Bom(byte[] data)
  {
    if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
      return data[3..];
    return data;
  }

  /// <summary>计算 SHA256 小写十六进制（与 manifest 的 sha256/bundleHash 表示一致）。</summary>
  private static string Sha256Hex(byte[] data) =>
    Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

  private static string SafeGet(Task<string> task)
  {
    try
    {
      return task.GetAwaiter().GetResult();
    }
    catch
    {
      return string.Empty;
    }
  }

  private static void TryDeleteDirectory(string directory)
  {
    try
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, true);
    }
    catch
    { /* 临时目录清理失败不影响结果 */
    }
  }

  /// <summary>清单中的一条工具文件（相对路径 + 期望 SHA256）。</summary>
  private sealed record BundleFile(string Path, string Sha256);

  /// <summary>子进程运行结果。</summary>
  private sealed record ProcessRunResult(bool TimedOut, int ExitCode, string Stdout, string Stderr);
}
