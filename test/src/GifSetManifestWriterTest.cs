namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoCMEX.Core.Recording;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// GIF 集清单生成器单测：只收成功落盘的卡、键名写 camelCase、时间转成清单约定格式，
/// 且写出的清单能被读取侧的选项（<see cref="GifSetManifest.CreateReadOptions"/>）原样读回。
/// </summary>
public class GifSetManifestWriterTest : TestClass
{
  private string _root = string.Empty;
  private string _outputDir = string.Empty;

  public GifSetManifestWriterTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Manifest_" + Guid.NewGuid().ToString("N")[..8]
    );
    _outputDir = Path.Combine(_root, "gifset");
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
      Directory.Delete(_root, true);
  }

  [Test]
  public void Write_AllCardsOk_WritesCamelCaseManifest()
  {
    var report = CreateReport(
      Card(1, "普通攻击 1", "1.gif"),
      Card(2, "符卡·一", "2.gif"),
      Card(3, "符卡·二", "3.gif")
    );

    var path = GifSetManifestWriter.Write(_outputDir, report);

    path.ShouldBe(Path.Combine(_outputDir, GifSetManifest.FileName));
    Directory.Exists(_outputDir).ShouldBeTrue(); // 输出目录不存在时由写盘方创建
    File.Exists(path!).ShouldBeTrue();

    var text = File.ReadAllText(path!);

    // 契约是 camelCase：漏出 PascalCase 属性名会让外部工具读不到
    text.ShouldContain("\"setName\": \"sample_project\"");
    text.ShouldContain("\"bossLabel\": \"测试Boss\"");
    text.ShouldContain("\"generatedAt\": \"2026-09-25 12:34:56\"");
    text.ShouldContain("\"spellCardName\": \"符卡·一\"");
    text.ShouldContain("\"fileName\": \"2.gif\"");
    // 注意 Shouldly 的字符串比较默认忽略大小写，故必须显式要求区分大小写
    text.ShouldNotContain("\"SetName\"", Case.Sensitive);
    text.ShouldNotContain("\"SpellCardName\"", Case.Sensitive);
    // 中文符卡名保持可读，不转义成 \uXXXX
    text.ShouldNotContain("\\u7B26", Case.Sensitive);

    var manifest = ReadBack(text);
    manifest.SetName.ShouldBe("sample_project");
    manifest.BossLabel.ShouldBe("测试Boss");
    manifest.GeneratedAt.ShouldBe("2026-09-25 12:34:56");
    manifest.Entries.Count.ShouldBe(3);
    manifest.Entries.Select(entry => entry.Index).ShouldBe(new[] { 1, 2, 3 });
    manifest
      .Entries.Select(entry => entry.SpellCardName)
      .ShouldBe(new[] { "普通攻击 1", "符卡·一", "符卡·二" });
    manifest.Entries.Select(entry => entry.FileName).ShouldBe(new[] { "1.gif", "2.gif", "3.gif" });

    foreach (var entry in manifest.Entries)
    {
      entry.Width.ShouldBe(640);
      entry.Height.ShouldBe(480);
    }
  }

  [Test]
  public void Write_SomeCardsNotOk_KeepsOnlyOkEntriesWithHoles()
  {
    var report = CreateReport(
      Card(1, "普通攻击 1", string.Empty, RecordingCardStatus.Failed),
      Card(2, "符卡·一", "2.gif"),
      Card(3, "符卡·二", string.Empty, RecordingCardStatus.Unrecorded),
      Card(4, "符卡·三", "4.gif")
    );

    var manifest = ReadBack(File.ReadAllText(GifSetManifestWriter.Write(_outputDir, report)!));

    // 失败与未录制的卡没有产物，写进清单会立刻被判「声明的文件不存在」；序号允许有洞
    manifest.Entries.Select(entry => entry.Index).ShouldBe(new[] { 2, 4 });
    manifest.Entries.Select(entry => entry.FileName).ShouldBe(new[] { "2.gif", "4.gif" });
  }

  [Test]
  public void Write_NoOkCard_ReturnsNullWithoutCreatingOutputDir()
  {
    var report = CreateReport(
      Card(1, "普通攻击 1", string.Empty, RecordingCardStatus.Failed),
      Card(2, "符卡·一", string.Empty, RecordingCardStatus.Unrecorded)
    );

    GifSetManifestWriter.Write(_outputDir, report).ShouldBeNull();

    Directory.Exists(_outputDir).ShouldBeFalse();
  }

  [Test]
  public void Write_CancelledRunKeepsCollectedCards()
  {
    var report = CreateReport(Card(1, "普通攻击 1", "1.gif"));
    report.Cancelled = true;

    var manifest = ReadBack(File.ReadAllText(GifSetManifestWriter.Write(_outputDir, report)!));

    // 取消不是失败：已归集的产物照常可导入
    manifest.Entries.Count.ShouldBe(1);
    manifest.Entries[0].Index.ShouldBe(1);
  }

  [Test]
  public void Build_NoBossName_WritesEmptyLabel()
  {
    var report = CreateReport(Card(1, "普通攻击 1", "1.gif"));
    report.BossName = null;

    GifSetManifestWriter.Build(report)!.BossLabel.ShouldBeEmpty();
  }

  [Test]
  public void Build_UnparsableGeneratedAt_KeepsOriginalText()
  {
    var report = CreateReport(Card(1, "普通攻击 1", "1.gif"));
    report.GeneratedAt = "未知时间";

    // 认不出就不编造时间，原样带出便于排查
    GifSetManifestWriter.Build(report)!.GeneratedAt.ShouldBe("未知时间");
  }

  [Test]
  public void Build_NullReport_Throws() =>
    Should.Throw<ArgumentNullException>(() => GifSetManifestWriter.Build(null!));

  /// <summary>造一张已录成的卡（默认 640×480，状态 ok）。</summary>
  /// <param name="combatOrdinal">战斗阶段序号。</param>
  /// <param name="entryName">清单名。</param>
  /// <param name="fileName">集内文件名（未录成时为空串）。</param>
  /// <param name="status">卡状态。</param>
  /// <param name="width">产物宽。</param>
  /// <param name="height">产物高。</param>
  /// <returns>报告里的一条卡记录。</returns>
  private static RecordingCardReport Card(
    int combatOrdinal,
    string entryName,
    string fileName,
    string status = RecordingCardStatus.Ok,
    int width = 640,
    int height = 480
  ) =>
    new()
    {
      CombatOrdinal = combatOrdinal,
      AbsoluteIndex = combatOrdinal + 1,
      EntryName = entryName,
      FileName = fileName,
      Width = width,
      Height = height,
      Status = status,
    };

  /// <summary>造一份报告（只填清单用得到的字段与卡片，取固定时间便于断言格式）。</summary>
  /// <param name="cards">逐卡记录。</param>
  /// <returns>报告。</returns>
  private static RecordingReport CreateReport(params RecordingCardReport[] cards) =>
    new()
    {
      ModPackName = "sample_project",
      BossName = "测试Boss",
      BossClass = "sample_enm1",
      GeneratedAt = "2026-09-25T12:34:56",
      Parallelism = 2,
      WorkersStarted = 2,
      Cards = cards.ToList(),
    };

  /// <summary>按读取侧选项把清单文本读回模型。</summary>
  /// <param name="text">清单文本。</param>
  /// <returns>清单。</returns>
  private static GifSetManifest ReadBack(string text) =>
    JsonSerializer.Deserialize<GifSetManifest>(text, GifSetManifest.CreateReadOptions())!;
}
