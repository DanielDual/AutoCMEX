namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AutoCMEX.Core.Merge;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// mod zip 后处理（<see cref="ModPackageFinalizer"/>）单元测试：用 fake <see cref="ILuaObfuscator"/>
/// 覆盖「混淆开/关 × 工程文件条目保留/移除」组合、条目顺序 / FullName / 外部属性 / 字节保真，
/// 以及混淆失败时的 fail-closed 备份语义。不依赖真实 Prometheus 二进制。
/// </summary>
public class ModPackageFinalizerTest : TestClass
{
  private string _tempDir = string.Empty;

  public ModPackageFinalizerTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_ModFinalizer_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_tempDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  // ==================== 测试夹具 ====================

  /// <summary>记录调用并可注入失败行为的 fake 混淆器；默认输出可辨识的十六进制标记，便于断言替换生效。</summary>
  private sealed class FakeLuaObfuscator : ILuaObfuscator
  {
    private readonly Func<byte[], string, LuaObfuscationResult> _behavior;

    public FakeLuaObfuscator(Func<byte[], string, LuaObfuscationResult>? behavior = null) =>
      _behavior = behavior ?? ((source, _) => ReplaceWithMarker(source));

    public List<(string EntryName, byte[] Source)> Calls { get; } = new();

    /// <summary>确定性替换：OBF[&lt;原文字节十六进制&gt;]，用作混淆结果的替身。</summary>
    public static LuaObfuscationResult ReplaceWithMarker(byte[] source) =>
      LuaObfuscationResult.Success(ExpectedObfuscated(source));

    public LuaObfuscationResult Obfuscate(byte[] source, string entryName)
    {
      Calls.Add((entryName, source));
      return _behavior(source, entryName);
    }
  }

  /// <summary>Unix 文件模式外部属性（zip 条目常见值），用于验证属性保真。</summary>
  private const int LuaFileExternalAttributes = unchecked((int)0x81A40000);

  /// <summary>写入 zip 的单个条目规格（名称 + 内容 + 外部属性）。</summary>
  private readonly record struct ZipEntrySpec(
    string Name,
    byte[] Content,
    int ExternalAttributes = 0
  );

  private static ZipEntrySpec TextEntry(string name, string text, int externalAttributes = 0) =>
    new(name, Encoding.UTF8.GetBytes(text), externalAttributes);

  private static ZipEntrySpec BinaryEntry(
    string name,
    byte[] content,
    int externalAttributes = 0
  ) => new(name, content, externalAttributes);

  /// <summary>按给定条目顺序写 zip，返回完整路径。</summary>
  private string CreateZip(string fileName, params ZipEntrySpec[] entries)
  {
    var path = Path.Combine(_tempDir, fileName);
    using var stream = File.Create(path);
    using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
    foreach (var spec in entries)
    {
      var entry = zip.CreateEntry(spec.Name, CompressionLevel.Optimal);
      entry.ExternalAttributes = spec.ExternalAttributes;
      using var entryStream = entry.Open();
      entryStream.Write(spec.Content, 0, spec.Content.Length);
    }
    return path;
  }

  /// <summary>解压读取 zip 全部条目（保持条目顺序），用于断言名称 / 内容 / 外部属性。</summary>
  private static List<(string FullName, byte[] Content, int ExternalAttributes)> ReadZip(
    string zipPath
  )
  {
    var entries = new List<(string, byte[], int)>();
    using var zip = ZipFile.OpenRead(zipPath);
    foreach (var entry in zip.Entries)
    {
      using var source = entry.Open();
      using var memory = new MemoryStream();
      source.CopyTo(memory);
      entries.Add((entry.FullName, memory.ToArray(), entry.ExternalAttributes));
    }
    return entries;
  }

  private static byte[] ExpectedObfuscated(byte[] source) =>
    Encoding.UTF8.GetBytes("OBF[" + Convert.ToHexString(source) + "]");

  private static void ShouldEqualBytes(byte[] actual, byte[] expected, string label) =>
    actual.SequenceEqual(expected).ShouldBeTrue(label);

  // ==================== 混淆开 × 工程文件条目 ====================

  [Test]
  public void Finalize_ObfuscateTrueIncludeTrue_ReplacesLuaKeepsOrderFullNameAndOtherBytes()
  {
    var luaSourceA = Encoding.UTF8.GetBytes("print('a')");
    var luaSourceB = Encoding.UTF8.GetBytes("print('b')");
    var projectBytes = Encoding.UTF8.GetBytes("project-content");
    var binary = new byte[] { 0x00, 0xFF, 0x10, 0x7F, 0x00 };
    var zipPath = CreateZip(
      "mod.zip",
      BinaryEntry("root.lua", luaSourceA, LuaFileExternalAttributes),
      TextEntry("mod.lstgproj", "project-content", 0x20),
      TextEntry("readme.txt", "readme"),
      BinaryEntry("sub\\nested.lua", luaSourceB, LuaFileExternalAttributes),
      BinaryEntry("data/tile.bin", binary)
    );

    var fake = new FakeLuaObfuscator();
    var result = new ModPackageFinalizer(fake).Finalize(
      zipPath,
      obfuscateLua: true,
      includeLstges: true
    );

    result.IsSuccess.ShouldBeTrue(result.Error);
    result.Processed.ShouldBeTrue();
    result.ObfuscatedCount.ShouldBe(2);
    result.RemovedProjectFile.ShouldBeFalse();
    result.BackupPath.ShouldBeNull();
    result.Warnings.ShouldBeEmpty();

    // 条目 FullName / 顺序不变；.lua 被替换；其余条目（含工程文件）字节完全一致
    var entries = ReadZip(zipPath);
    entries
      .Select(entry => entry.FullName)
      .ShouldBe(
        new List<string>
        {
          "root.lua",
          "mod.lstgproj",
          "readme.txt",
          "sub\\nested.lua",
          "data/tile.bin",
        }
      );
    ShouldEqualBytes(entries[0].Content, ExpectedObfuscated(luaSourceA), ".lua 应被替换");
    ShouldEqualBytes(entries[1].Content, projectBytes, "工程文件条目字节不应变化");
    ShouldEqualBytes(
      entries[2].Content,
      Encoding.UTF8.GetBytes("readme"),
      "其他文本条目字节不应变化"
    );
    ShouldEqualBytes(entries[3].Content, ExpectedObfuscated(luaSourceB), "反斜杠 .lua 应被替换");
    ShouldEqualBytes(entries[4].Content, binary, "二进制条目字节不应变化");

    // 反斜杠 FullName 与外部属性保留
    entries[3].FullName.ShouldBe("sub\\nested.lua");
    entries[0].ExternalAttributes.ShouldBe(LuaFileExternalAttributes);
    entries[1].ExternalAttributes.ShouldBe(0x20);

    // fake 收到的正是原 .lua 字节与条目名（按 zip 顺序）
    fake.Calls.Select(call => call.EntryName)
      .ShouldBe(new List<string> { "root.lua", "sub\\nested.lua" });
    ShouldEqualBytes(fake.Calls[0].Source, luaSourceA, "混淆输入应为原始 .lua 字节");
    ShouldEqualBytes(fake.Calls[1].Source, luaSourceB, "混淆输入应为原始 .lua 字节");

    // 成功后就地替换，不残留临时/备份文件
    File.Exists(zipPath).ShouldBeTrue();
    File.Exists(zipPath + ".tmp").ShouldBeFalse();
    File.Exists(zipPath + ".unobfuscated").ShouldBeFalse();
  }

  [Test]
  public void Finalize_ObfuscateTrueIncludeFalse_RemovesProjectEntryAndObfuscatesLua()
  {
    var luaSource = Encoding.UTF8.GetBytes("print('a')");
    var zipPath = CreateZip(
      "mod.zip",
      BinaryEntry("root.lua", luaSource),
      TextEntry("mod.lstgproj", "project-content"),
      TextEntry("keep.txt", "keep")
    );

    var result = new ModPackageFinalizer(new FakeLuaObfuscator()).Finalize(
      zipPath,
      obfuscateLua: true,
      includeLstges: false
    );

    result.IsSuccess.ShouldBeTrue(result.Error);
    result.Processed.ShouldBeTrue();
    result.RemovedProjectFile.ShouldBeTrue();
    result.ObfuscatedCount.ShouldBe(1);
    result.Warnings.ShouldBeEmpty(); // 工程条目实际被移除 → 不报「未找到」

    var entries = ReadZip(zipPath);
    entries.Select(entry => entry.FullName).ShouldBe(new List<string> { "root.lua", "keep.txt" });
    ShouldEqualBytes(entries[0].Content, ExpectedObfuscated(luaSource), ".lua 应仍被替换");
    ShouldEqualBytes(entries[1].Content, Encoding.UTF8.GetBytes("keep"), "其他条目字节不变");
  }

  // ==================== 混淆关 × 工程文件条目 ====================

  [Test]
  public void Finalize_ObfuscateFalseIncludeTrue_IsNoOpAndZipBytesUnchanged()
  {
    var zipPath = CreateZip(
      "mod.zip",
      TextEntry("root.lua", "print('a')"),
      TextEntry("mod.lstgproj", "project-content")
    );
    var before = File.ReadAllBytes(zipPath);
    var fake = new FakeLuaObfuscator();

    var result = new ModPackageFinalizer(fake).Finalize(
      zipPath,
      obfuscateLua: false,
      includeLstges: true
    );

    result.IsSuccess.ShouldBeTrue(result.Error);
    result.Processed.ShouldBeFalse();
    result.ObfuscatedCount.ShouldBe(0);
    result.RemovedProjectFile.ShouldBeFalse();
    fake.Calls.ShouldBeEmpty(); // 不做后处理
    ShouldEqualBytes(File.ReadAllBytes(zipPath), before, "no-op 场景原 zip 字节不应变化");
    File.Exists(zipPath + ".tmp").ShouldBeFalse();
  }

  [Test]
  public void Finalize_ObfuscateFalseIncludeFalse_RemovesOnlyProjectEntry()
  {
    var luaSource = Encoding.UTF8.GetBytes("print('a')");
    var binary = new byte[] { 0x01, 0x02, 0x00, 0xFE };
    var zipPath = CreateZip(
      "mod.zip",
      BinaryEntry("root.lua", luaSource),
      TextEntry("mod.lstgproj", "project-content"),
      BinaryEntry("data.bin", binary)
    );
    var fake = new FakeLuaObfuscator();

    var result = new ModPackageFinalizer(fake).Finalize(
      zipPath,
      obfuscateLua: false,
      includeLstges: false
    );

    result.IsSuccess.ShouldBeTrue(result.Error);
    result.Processed.ShouldBeTrue();
    result.RemovedProjectFile.ShouldBeTrue();
    result.ObfuscatedCount.ShouldBe(0);
    fake.Calls.ShouldBeEmpty(); // 混淆关：不调用混淆器

    var entries = ReadZip(zipPath);
    entries.Select(entry => entry.FullName).ShouldBe(new List<string> { "root.lua", "data.bin" });
    ShouldEqualBytes(entries[0].Content, luaSource, "混淆关时 .lua 应原样保留");
    ShouldEqualBytes(entries[1].Content, binary, "其他条目字节不变");
  }

  [Test]
  public void Finalize_ProjectEntryNestedWithBackslash_RemovedWhenIncludeFalse()
  {
    // zip 条目可用反斜杠；工程文件条目归一后按「根名或任意目录下同名」识别
    var zipPath = CreateZip(
      "mod.zip",
      TextEntry("assets\\mod.lstgproj", "project-content"),
      TextEntry("keep.txt", "keep")
    );

    var result = new ModPackageFinalizer(new FakeLuaObfuscator()).Finalize(
      zipPath,
      obfuscateLua: false,
      includeLstges: false
    );

    result.IsSuccess.ShouldBeTrue(result.Error);
    result.Processed.ShouldBeTrue();
    result.RemovedProjectFile.ShouldBeTrue();
    result.Warnings.ShouldBeEmpty();
    ReadZip(zipPath).Select(entry => entry.FullName).ShouldBe(new List<string> { "keep.txt" });
  }

  // ==================== fail-closed 与边界 ====================

  [Test]
  public void Finalize_ObfuscationFailure_BacksUpOriginalZipAndFailsClosed()
  {
    var zipPath = CreateZip(
      "mod.zip",
      TextEntry("ok.lua", "print('ok')"),
      TextEntry("boom.lua", "print('boom')"),
      TextEntry("mod.lstgproj", "project-content"),
      TextEntry("keep.txt", "keep")
    );
    var originalBytes = File.ReadAllBytes(zipPath);
    var fake = new FakeLuaObfuscator(
      (source, entryName) =>
        entryName.EndsWith("boom.lua", StringComparison.Ordinal)
          ? LuaObfuscationResult.Failure("mock prometheus exploded")
          : FakeLuaObfuscator.ReplaceWithMarker(source)
    );

    var result = new ModPackageFinalizer(fake).Finalize(
      zipPath,
      obfuscateLua: true,
      includeLstges: true
    );

    result.IsSuccess.ShouldBeFalse();
    result.Processed.ShouldBeTrue();
    var error = result.Error ?? string.Empty;
    error.ShouldContain("混淆失败");
    error.ShouldContain("boom.lua");
    error.ShouldContain("mock prometheus exploded");

    // fail-closed：不交付半成品 → 预期文件名不存在，原 zip 已改名备份且原始字节完整
    File.Exists(zipPath).ShouldBeFalse();
    var backupPath = zipPath + ".unobfuscated";
    result.BackupPath.ShouldBe(backupPath);
    File.Exists(backupPath).ShouldBeTrue();
    ShouldEqualBytes(File.ReadAllBytes(backupPath), originalBytes, "备份应是 Sharp 原始 zip");
    File.Exists(zipPath + ".tmp").ShouldBeFalse();
  }

  [Test]
  public void Finalize_EmptyOrMissingZipPath_ReturnsFailure()
  {
    var finalizer = new ModPackageFinalizer(new FakeLuaObfuscator());

    var empty = finalizer.Finalize("  ", obfuscateLua: true, includeLstges: true);
    empty.IsSuccess.ShouldBeFalse();
    empty.Processed.ShouldBeFalse();
    (empty.Error ?? string.Empty).ShouldContain("路径为空");

    var missing = finalizer.Finalize(
      Path.Combine(_tempDir, "nope.zip"),
      obfuscateLua: true,
      includeLstges: true
    );
    missing.IsSuccess.ShouldBeFalse();
    missing.Processed.ShouldBeFalse();
    (missing.Error ?? string.Empty).ShouldContain("未找到 mod zip");
  }

  [Test]
  public void Finalize_ObfuscateTrueWithoutLuaEntries_WarnsNothingObfuscated()
  {
    var zipPath = CreateZip(
      "mod.zip",
      TextEntry("mod.lstgproj", "project-content"),
      TextEntry("keep.txt", "keep")
    );

    var result = new ModPackageFinalizer(new FakeLuaObfuscator()).Finalize(
      zipPath,
      obfuscateLua: true,
      includeLstges: true
    );

    result.IsSuccess.ShouldBeTrue(result.Error);
    result.Processed.ShouldBeTrue();
    result.ObfuscatedCount.ShouldBe(0);
    result.Warnings.ShouldContain(warning => warning.Contains("没有找到 .lua 条目"));
  }

  [Test]
  public void Finalize_IncludeFalseWithoutProjectEntry_WarnsMissingProjectEntry()
  {
    var zipPath = CreateZip("mod.zip", TextEntry("keep.txt", "keep"));

    var result = new ModPackageFinalizer(new FakeLuaObfuscator()).Finalize(
      zipPath,
      obfuscateLua: false,
      includeLstges: false
    );

    result.IsSuccess.ShouldBeTrue(result.Error);
    result.Processed.ShouldBeTrue();
    result.RemovedProjectFile.ShouldBeFalse();
    result.Warnings.ShouldContain(warning => warning.Contains("未在 mod zip 中找到工程文件条目"));
  }
}
