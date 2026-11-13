namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCMEX.Core.Merge;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="PrometheusLuaObfuscator"/> 的 fail-closed 前置分支单元测试：用物理目录 bundle
/// （临时 <c>manifest.json</c> + 假工具文件）覆盖「清单缺失 / 清单非法 / 工具文件缺失 / 校验失败」。
/// 不启动真实 Prometheus 子进程，也不依赖仓库内 798KB 工具二进制（真机端到端由导出链路验收）。
/// </summary>
public class PrometheusLuaObfuscatorTest : TestClass
{
  private string _tempDir = string.Empty;

  public PrometheusLuaObfuscatorTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Prometheus_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_tempDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  private void WriteBundleFile(string relativePath, byte[] content)
  {
    var path = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);
    File.WriteAllBytes(path, content);
  }

  /// <summary>写入一份合法结构清单（入口字段齐全，files 由调用方给出，便于构造缺失/篡改场景）。</summary>
  private void WriteManifest(IReadOnlyList<(string Path, string Sha256)> files)
  {
    var manifest = new
    {
      luaExecutable = "runtime/lua5.1.exe",
      cliScript = "prometheus/cli.lua",
      config = "configs/cfg.lua",
      files = files.Select(file => new { path = file.Path, sha256 = file.Sha256 }).ToArray(),
    };
    File.WriteAllText(
      Path.Combine(_tempDir, "manifest.json"),
      JsonSerializer.Serialize(manifest),
      new UTF8Encoding(false)
    );
  }

  private PrometheusLuaObfuscator CreateObfuscator() =>
    new(log: null, bundleResourceRoot: _tempDir);

  private static byte[] Source() => Encoding.UTF8.GetBytes("print('hello')");

  /// <summary>构造清单 JSON（入口路径与 files 可覆盖，用于恶意清单变体）。</summary>
  private static string BuildManifestJson(
    string luaExecutable,
    string cliScript,
    string config,
    IReadOnlyList<(string Path, string Sha256)>? files = null
  ) =>
    JsonSerializer.Serialize(
      new
      {
        luaExecutable,
        cliScript,
        config,
        files = (files ?? Array.Empty<(string Path, string Sha256)>())
          .Select(file => new { path = file.Path, sha256 = file.Sha256 })
          .ToArray(),
      }
    );

  /// <summary>在独立子目录写入恶意清单，返回该目录（作为独立的 bundleResourceRoot）。</summary>
  private string WriteVariantManifest(string variantName, string json)
  {
    var directory = Path.Combine(_tempDir, variantName);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "manifest.json"), json, new UTF8Encoding(false));
    return directory;
  }

  [Test]
  public void Obfuscate_EmptySource_ReturnsFailure()
  {
    var result = new PrometheusLuaObfuscator().Obfuscate(Array.Empty<byte>(), "empty.lua");

    result.IsSuccess.ShouldBeFalse();
    (result.Error ?? string.Empty).ShouldContain("Lua 源码为空");
  }

  [Test]
  public void Obfuscate_MissingManifest_ReturnsFailure()
  {
    var result = CreateObfuscator().Obfuscate(Source(), "a.lua");

    result.IsSuccess.ShouldBeFalse();
    (result.Error ?? string.Empty).ShouldContain("未找到混淆工具清单");
  }

  [Test]
  public void Obfuscate_InvalidManifest_ReturnsFailure()
  {
    File.WriteAllText(
      Path.Combine(_tempDir, "manifest.json"),
      """{"files":[]}""",
      new UTF8Encoding(false)
    );

    var result = CreateObfuscator().Obfuscate(Source(), "a.lua");

    result.IsSuccess.ShouldBeFalse();
    (result.Error ?? string.Empty).ShouldContain("混淆工具清单解析失败");
  }

  [Test]
  public void Obfuscate_ManifestListingMissingFile_ReturnsFailure()
  {
    WriteManifest(new[] { ("runtime/lua5.1.exe", new string('0', 64)) });

    var result = CreateObfuscator().Obfuscate(Source(), "a.lua");

    result.IsSuccess.ShouldBeFalse();
    (result.Error ?? string.Empty).ShouldContain("混淆工具文件缺失");
  }

  [Test]
  public void Obfuscate_TamperedBundleFile_ReturnsFailure()
  {
    var content = Encoding.UTF8.GetBytes("not-a-real-lua-runtime");
    WriteBundleFile("runtime/lua5.1.exe", content);
    WriteManifest(new[] { ("runtime/lua5.1.exe", new string('A', 64)) });

    var result = CreateObfuscator().Obfuscate(Source(), "a.lua");

    result.IsSuccess.ShouldBeFalse();
    (result.Error ?? string.Empty).ShouldContain("混淆工具文件校验失败");
  }

  [Test]
  public void Obfuscate_MaliciousManifestPaths_ReturnsFailureWithoutSpawningTool()
  {
    const string validLua = "runtime/lua5.1.exe";
    const string validCli = "prometheus/cli.lua";
    const string validConfig = "configs/cfg.lua";
    var sha = new string('a', 64);
    // 运行时构造绝对路径，避免源码里出现盘符字面量（敏感词守卫会误报）。
    var absoluteCli = Path.Combine(Path.GetTempPath(), "abs", "cli.lua");
    var absoluteEvil = Path.Combine(Path.GetTempPath(), "evil.lua");

    var variants = new (string Name, string Json)[]
    {
      // 入口路径为绝对路径 / 含 .. / 盘符 → 清单非法
      ("lua-absolute", BuildManifestJson("/abs/lua5.1.exe", validCli, validConfig)),
      ("lua-dotdot", BuildManifestJson("../runtime/lua5.1.exe", validCli, validConfig)),
      ("cli-absolute", BuildManifestJson(validLua, absoluteCli, validConfig)),
      ("config-absolute", BuildManifestJson(validLua, validCli, "/etc/cfg.lua")),
      // files[].path 穿越 / 反斜杠 / 盘符 → 清单非法
      (
        "file-dotdot",
        BuildManifestJson(validLua, validCli, validConfig, new[] { ("../evil.lua", sha) })
      ),
      (
        "file-backslash",
        BuildManifestJson(validLua, validCli, validConfig, new[] { ("prometheus\\cli.lua", sha) })
      ),
      (
        "file-drive",
        BuildManifestJson(validLua, validCli, validConfig, new[] { (absoluteEvil, sha) })
      ),
    };

    foreach (var (name, json) in variants)
    {
      var bundleRoot = WriteVariantManifest(name, json);
      // 每个变体独立实例：bundle 解析结果按实例缓存，避免前一个失败被复用
      var result = new PrometheusLuaObfuscator(log: null, bundleResourceRoot: bundleRoot).Obfuscate(
        Source(),
        "a.lua"
      );

      result.IsSuccess.ShouldBeFalse($"{name} 应 fail-closed");
      var error = result.Error ?? string.Empty;
      error
        .Contains("混淆工具清单解析失败", StringComparison.Ordinal)
        .ShouldBeTrue($"{name} 应报清单解析失败：{error}");
      error
        .Contains("非法工具文件路径", StringComparison.Ordinal)
        .ShouldBeTrue($"{name} 应报非法路径：{error}");
      // 路径校验在读取工具文件 / 启动子进程之前完成：失败即返回，不会执行任何工具进程
      result.Output.ShouldBeNull();
    }
  }
}
