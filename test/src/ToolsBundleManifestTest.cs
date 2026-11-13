namespace AutoCMEX;

using System;
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
/// 随包 Lua 混淆工具清单（<c>res://src/tools/lua-obfuscator/manifest.json</c>）与磁盘实际文件的一致性测试：
/// 逐文件 size/sha256、bundleHash 三重锚定（重算值 == manifest 声明值 ==
/// <see cref="PrometheusLuaObfuscator.ExpectedBundleHash"/>），以及三个入口文件存在且被清单覆盖。
/// 该测试直接校验仓库内真实工具包，防止「改了工具文件/清单但没同步锚值」的运行期 fail-closed。
/// </summary>
public class ToolsBundleManifestTest : TestClass
{
  /// <summary>与 <see cref="PrometheusLuaObfuscator.DefaultBundleResourceRoot"/> 对应的工具包资源根。</summary>
  private const string BundleResourceRoot = "res://src/tools/lua-obfuscator";

  public ToolsBundleManifestTest(Node testScene)
    : base(testScene) { }

  /// <summary>工具包物理目录（测试在编辑器内运行，res:// 映射到仓库根）。</summary>
  private static string BundleDir => ProjectSettings.GlobalizePath(BundleResourceRoot);

  /// <summary>读取并解析 manifest.json（返回可独立使用的 JsonElement）。</summary>
  private static JsonElement LoadManifest()
  {
    var manifestPath = Path.Combine(BundleDir, "manifest.json");
    File.Exists(manifestPath).ShouldBeTrue($"缺少工具清单：{manifestPath}");
    using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
    return document.RootElement.Clone();
  }

  private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

  /// <summary>按运行期同算法重算 bundleHash：path 排序后拼 path + 换行 + 小写 sha256 + 换行，再取 SHA256。</summary>
  private static string RecomputeBundleHash(JsonElement manifest)
  {
    var builder = new StringBuilder();
    var entries = manifest
      .GetProperty("files")
      .EnumerateArray()
      .Select(entry =>
        (
          Path: entry.GetProperty("path").GetString() ?? string.Empty,
          Sha256: entry.GetProperty("sha256").GetString() ?? string.Empty
        )
      )
      .OrderBy(entry => entry.Path, StringComparer.Ordinal);
    foreach (var entry in entries)
      builder.Append(entry.Path).Append('\n').Append(entry.Sha256.ToLowerInvariant()).Append('\n');
    return Convert
      .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
      .ToLowerInvariant();
  }

  private static string ToLocalPath(string relativePath) =>
    Path.Combine(BundleDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

  [Test]
  public void Manifest_FilesMatchDisk_AndReadmeIsNotIncluded()
  {
    var manifest = LoadManifest();
    var files = manifest.GetProperty("files").EnumerateArray().ToList();
    files.Count.ShouldBeGreaterThan(0);

    var paths = files
      .Select(entry => entry.GetProperty("path").GetString() ?? string.Empty)
      .ToList();
    // README.md 只是仓库文档，不参与运行期工具包哈希与释放
    paths
      .Any(path => path.Contains("README", StringComparison.OrdinalIgnoreCase))
      .ShouldBeFalse("README.md 不应出现在 manifest.files 中");

    foreach (var entry in files)
    {
      var relative = entry.GetProperty("path").GetString() ?? string.Empty;
      var sha256 = entry.GetProperty("sha256").GetString() ?? string.Empty;
      entry.GetProperty("size").ValueKind.ShouldBe(JsonValueKind.Number);
      var size = entry.GetProperty("size").GetInt64();
      var absolute = ToLocalPath(relative);
      File.Exists(absolute).ShouldBeTrue($"清单列出的文件缺失：{relative}");
      new FileInfo(absolute).Length.ShouldBe(size, $"size 与磁盘不一致：{relative}");
      Sha256Hex(File.ReadAllBytes(absolute))
        .Equals(sha256, StringComparison.OrdinalIgnoreCase)
        .ShouldBeTrue($"sha256 与磁盘不一致：{relative}");
    }
  }

  [Test]
  public void Manifest_BundleHash_MatchesRecomputedAndExpectedAnchor()
  {
    var manifest = LoadManifest();
    var declared = manifest.GetProperty("bundleHash").GetString() ?? string.Empty;
    declared.Length.ShouldBe(64, "manifest.bundleHash 应为 64 位十六进制");
    var recomputed = RecomputeBundleHash(manifest);

    // 三重锚定：重算值 == manifest 声明值 == 代码内 ExpectedBundleHash
    recomputed
      .Equals(declared, StringComparison.OrdinalIgnoreCase)
      .ShouldBeTrue($"重算 bundleHash 与 manifest 不一致：{recomputed} vs {declared}");
    recomputed
      .Equals(PrometheusLuaObfuscator.ExpectedBundleHash, StringComparison.OrdinalIgnoreCase)
      .ShouldBeTrue(
        "工具包与代码锚 ExpectedBundleHash 不一致；升级工具需同步 manifest.json 与该常量"
      );
  }

  [Test]
  public void Manifest_EntryPoints_ExistOnDiskAndAreCoveredByFiles()
  {
    var manifest = LoadManifest();
    var listedPaths = manifest
      .GetProperty("files")
      .EnumerateArray()
      .Select(entry => entry.GetProperty("path").GetString() ?? string.Empty)
      .ToHashSet(StringComparer.Ordinal);

    foreach (var propertyName in new[] { "luaExecutable", "cliScript", "config" })
    {
      var relative = manifest.GetProperty(propertyName).GetString();
      relative.ShouldNotBeNullOrEmpty($"{propertyName} 不能为空");
      relative!.ShouldNotContain("\\");
      File.Exists(ToLocalPath(relative!))
        .ShouldBeTrue($"{propertyName} 指向的文件不存在：{relative}");
      listedPaths
        .Contains(relative)
        .ShouldBeTrue($"{propertyName} 未列入 manifest.files，无法被完整性校验覆盖：{relative}");
    }
  }
}
