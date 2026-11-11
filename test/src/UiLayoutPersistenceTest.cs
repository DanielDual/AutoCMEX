namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// UI 布局记忆（<c>ui_layout.json</c>，<see cref="DataManager"/> 的第 8 段数据）持久化测试。
/// </summary>
/// <remarks>
/// <para>
/// 守住以下契约：无文件时全默认；模型 → 落盘 → 重载往返一致；JSON 显式 <c>null</c> 会被
/// <see cref="UiLayoutData.EnsureIntegrity"/> 回填而不是让依赖方拿到 null；
/// <see cref="UiLayoutData.Sanitize"/> 对非法值的收敛规则确定；盘上版本高于当前程序时整段只读
/// （不覆盖文件、其余段照常写）；坏 JSON 走既有备份机制且内存回落默认；版本缺失或为 0 视为当前版本。
/// </para>
/// <para>
/// 与 <see cref="DataManagerSaveTest"/> 相同的隔离方式：每个用例一份临时数据目录 + 独享日志 Mock，
/// 用例结束删目录。
/// </para>
/// </remarks>
public class UiLayoutPersistenceTest : TestClass
{
  /// <summary>布局文件名（数据目录内）。</summary>
  private const string LayoutFileName = "ui_layout.json";

  private string _root = string.Empty;
  private string _dataDir = string.Empty;
  private AesEncryptor _encryptor = default!;
  private Mock<ILog> _log = default!;

  /// <summary>
  /// 构造测试类实例（GoDotTest 注入测试场景节点）。
  /// </summary>
  /// <param name="testScene">测试场景节点。</param>
  public UiLayoutPersistenceTest(Node testScene)
    : base(testScene) { }

  /// <summary>
  /// 每个用例一份隔离的临时目录与日志 Mock，避免用例间互相污染。
  /// </summary>
  [Setup]
  public void Setup()
  {
    _root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_UiLayout_" + Guid.NewGuid().ToString("N")[..8]
    );
    _dataDir = Path.Combine(_root, "data");
    Directory.CreateDirectory(_dataDir);

    // 密钥文件放在数据目录之外，数据目录里就只会有 DataManager 写出的 JSON
    _encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_root));
    _log = new Mock<ILog>();
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_root))
      Directory.Delete(_root, recursive: true);
  }

  /// <summary>
  /// 无布局文件：加载后分割条为空、窗口与弹窗几何全部为 <c>null</c>、版本为当前版本。
  /// </summary>
  [Test]
  public void LoadAll_WithoutFile_UsesDefaults()
  {
    var dm = CreateManager();

    dm.UiLayout.Splits.ShouldBeEmpty();
    dm.UiLayout.Version.Value.ShouldBe(UiLayoutData.CurrentVersion);
    dm.UiLayout.WindowWidth.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowHeight.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowX.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowY.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowMaximized.Value.ShouldBeFalse();
    dm.UiLayout.FileDialogWidth.Value.ShouldBe((int?)null);
    dm.UiLayout.FileDialogHeight.Value.ShouldBe((int?)null);

    dm.Dispose();
  }

  /// <summary>
  /// 写模型 → <see cref="DataManager.SaveAll"/> → 新实例重载：窗口几何、弹窗尺寸与
  /// 分割条的 offset / 折叠状态必须原样回来。
  /// </summary>
  [Test]
  public void SaveAndLoad_RoundTripsLayoutSnapshot()
  {
    var dm = CreateManager();
    dm.UiLayout.WindowWidth.Value = 1600;
    dm.UiLayout.WindowHeight.Value = 900;
    dm.UiLayout.WindowX.Value = 120;
    dm.UiLayout.WindowY.Value = -40;
    dm.UiLayout.WindowMaximized.Value = true;
    dm.UiLayout.FileDialogWidth.Value = 1024;
    dm.UiLayout.FileDialogHeight.Value = 768;
    dm.UiLayout.Splits.Add(NewSplit("main.nav", 640, true));
    dm.UiLayout.Splits.Add(NewSplit("merge.left", 300, false));
    dm.SaveAll();
    dm.Dispose();

    var reloaded = CreateManager();

    reloaded.UiLayout.WindowWidth.Value.ShouldBe((int?)1600);
    reloaded.UiLayout.WindowHeight.Value.ShouldBe((int?)900);
    reloaded.UiLayout.WindowX.Value.ShouldBe((int?)120);
    reloaded.UiLayout.WindowY.Value.ShouldBe((int?)-40);
    reloaded.UiLayout.WindowMaximized.Value.ShouldBeTrue();
    reloaded.UiLayout.FileDialogWidth.Value.ShouldBe((int?)1024);
    reloaded.UiLayout.FileDialogHeight.Value.ShouldBe((int?)768);

    reloaded.UiLayout.Splits.Count.ShouldBe(2);
    var nav = reloaded.UiLayout.Splits.Single(split => split.Key.Value == "main.nav");
    nav.Offset.Value.ShouldBe(640);
    nav.Collapsed.Value.ShouldBeTrue();
    var mergeLeft = reloaded.UiLayout.Splits.Single(split => split.Key.Value == "merge.left");
    mergeLeft.Offset.Value.ShouldBe(300);
    mergeLeft.Collapsed.Value.ShouldBeFalse();

    reloaded.Dispose();
  }

  /// <summary>
  /// 手写含显式 <c>null</c> 的 JSON：加载不崩，容器字段被回填为默认值、几何字段仍为
  /// <c>null</c>（表示「从未记录」）；集合元素内部的显式 <c>null</c> 同样回填。
  /// </summary>
  [Test]
  public void LoadAll_ExplicitNullFields_BackfillsWithoutCrash()
  {
    WriteLayout(
      "{\"version\": null, \"splits\": null, \"windowWidth\": null, "
        + "\"windowHeight\": null, \"windowX\": null, \"windowY\": null, "
        + "\"windowMaximized\": null, \"fileDialogWidth\": null, \"fileDialogHeight\": null}"
    );

    var dm = CreateManager();

    dm.UiLayout.Version.ShouldNotBeNull();
    dm.UiLayout.Version.Value.ShouldBe(UiLayoutData.CurrentVersion);
    dm.UiLayout.Splits.ShouldNotBeNull();
    dm.UiLayout.Splits.ShouldBeEmpty();
    dm.UiLayout.WindowWidth.ShouldNotBeNull();
    dm.UiLayout.WindowWidth.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowHeight.ShouldNotBeNull();
    dm.UiLayout.WindowX.ShouldNotBeNull();
    dm.UiLayout.WindowY.ShouldNotBeNull();
    dm.UiLayout.WindowMaximized.ShouldNotBeNull();
    dm.UiLayout.WindowMaximized.Value.ShouldBeFalse();
    dm.UiLayout.FileDialogWidth.ShouldNotBeNull();
    dm.UiLayout.FileDialogHeight.ShouldNotBeNull();
    dm.Dispose();

    // 元素级显式 null：记录本身要能回填，否则 UI 读 Key/Offset/Collapsed 时会 NRE
    WriteLayout(
      "{\"version\": 1, \"splits\": [{\"key\": \"main.nav\", \"offset\": null, "
        + "\"collapsed\": null}]}"
    );

    var dm2 = CreateManager();
    dm2.UiLayout.Splits.Count.ShouldBe(1);
    var record = dm2.UiLayout.Splits[0];
    record.Key.ShouldNotBeNull();
    record.Key.Value.ShouldBe("main.nav");
    record.Offset.ShouldNotBeNull();
    record.Offset.Value.ShouldBe(0);
    record.Collapsed.ShouldNotBeNull();
    record.Collapsed.Value.ShouldBeFalse();
    dm2.Dispose();
  }

  /// <summary>
  /// 非法值经 <see cref="UiLayoutData.Sanitize"/> 收敛：窗口尺寸/坐标非法 → 整个窗口几何回落；
  /// 弹窗尺寸非法 → 两个字段一起清掉；分割条按 Key 去重、丢弃空键、offset 收敛到 ±100000。
  /// </summary>
  [Test]
  public void LoadAll_InvalidValues_AreSanitized()
  {
    WriteLayout(
      "{"
        + "\"version\": 1,"
        + "\"windowWidth\": -5,"
        + "\"windowHeight\": 720,"
        + "\"windowX\": 100,"
        + "\"windowY\": 100,"
        + "\"windowMaximized\": true,"
        + "\"fileDialogWidth\": 0,"
        + "\"fileDialogHeight\": 600,"
        + "\"splits\": ["
        + "{\"key\": \"main.nav\", \"offset\": 200000, \"collapsed\": false},"
        + "{\"key\": \"merge.left\", \"offset\": 100, \"collapsed\": false},"
        + "{\"key\": \"merge.left\", \"offset\": 400, \"collapsed\": true},"
        + "{\"key\": \"  \", \"offset\": 10, \"collapsed\": false},"
        + "{\"key\": null, \"offset\": 20, \"collapsed\": false},"
        + "{\"offset\": 30}"
        + "]"
        + "}"
    );

    var dm = CreateManager();

    // 窗口宽非法：四个几何字段全清、最大化复位
    dm.UiLayout.WindowWidth.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowHeight.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowX.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowY.Value.ShouldBe((int?)null);
    dm.UiLayout.WindowMaximized.Value.ShouldBeFalse();
    // 弹窗宽非法：两个字段一起清掉，避免只恢复一半宽高
    dm.UiLayout.FileDialogWidth.Value.ShouldBe((int?)null);
    dm.UiLayout.FileDialogHeight.Value.ShouldBe((int?)null);

    // 空白键 / 缺键记录被丢弃；重复 Key 保留最后一条（并按最后出现的位置保留顺序）
    dm.UiLayout.Splits.Count.ShouldBe(2);
    dm.UiLayout.Splits[0].Key.Value.ShouldBe("main.nav");
    dm.UiLayout.Splits[0].Offset.Value.ShouldBe(100000);
    dm.UiLayout.Splits[1].Key.Value.ShouldBe("merge.left");
    dm.UiLayout.Splits[1].Offset.Value.ShouldBe(400);
    dm.UiLayout.Splits[1].Collapsed.Value.ShouldBeTrue();
    dm.Dispose();

    // 坐标越界：同样整个窗口几何回落（否则会恢复出屏幕外的窗口）
    WriteLayout(
      "{\"version\": 1, \"windowWidth\": 800, \"windowHeight\": 600, "
        + "\"windowX\": 40000, \"windowY\": 0, \"windowMaximized\": true}"
    );

    var dm2 = CreateManager();
    dm2.UiLayout.WindowWidth.Value.ShouldBe((int?)null);
    dm2.UiLayout.WindowHeight.Value.ShouldBe((int?)null);
    dm2.UiLayout.WindowX.Value.ShouldBe((int?)null);
    dm2.UiLayout.WindowY.Value.ShouldBe((int?)null);
    dm2.UiLayout.WindowMaximized.Value.ShouldBeFalse();
    dm2.Dispose();

    // 弹窗尺寸超上限同样两个字段一起清；合法值原样保留
    WriteLayout("{\"version\": 1, \"fileDialogWidth\": 40000, \"fileDialogHeight\": 600}");
    var dm3 = CreateManager();
    dm3.UiLayout.FileDialogWidth.Value.ShouldBe((int?)null);
    dm3.UiLayout.FileDialogHeight.Value.ShouldBe((int?)null);
    dm3.Dispose();

    WriteLayout("{\"version\": 1, \"fileDialogWidth\": 1024, \"fileDialogHeight\": 768}");
    var dm4 = CreateManager();
    dm4.UiLayout.FileDialogWidth.Value.ShouldBe((int?)1024);
    dm4.UiLayout.FileDialogHeight.Value.ShouldBe((int?)768);
    dm4.Dispose();
  }

  /// <summary>
  /// 盘上版本高于当前程序：不崩、内存用默认值、记 WARN；<see cref="DataManager.SaveAll"/>
  /// 不覆盖该文件（只读保护），其余 7 段照常写入。
  /// </summary>
  [Test]
  public void LoadAll_FutureVersion_IsReadOnlyAndNotOverwritten()
  {
    const string tooNew =
      "{\"version\": 2, \"windowWidth\": 1280, "
      + "\"splits\": [{\"key\": \"main.nav\", \"offset\": 700, \"collapsed\": false}]}";
    WriteLayout(tooNew);
    var layoutPath = LayoutPath();
    var bytesBefore = HexBytesOf(layoutPath);

    var dm = CreateManager();

    dm.UiLayout.Version.Value.ShouldBe(UiLayoutData.CurrentVersion);
    dm.UiLayout.Splits.ShouldBeEmpty();
    dm.UiLayout.WindowWidth.Value.ShouldBe((int?)null);
    _log.Verify(log => log.Warn(It.Is<string>(msg => msg.Contains(LayoutFileName))), Times.Once);

    dm.SaveAll();
    dm.SaveAll();

    // 只读保护：两次保存后该文件一个字节都没变
    HexBytesOf(layoutPath).ShouldBe(bytesBefore);
    // 其余 7 段照常写盘且可解析
    foreach (
      var name in new[]
      {
        "spellcard_table.json",
        "alias_table.json",
        "app_settings.json",
        "creator_packages.json",
        "merge_config.json",
        "info_config.json",
        "recording_config.json",
      }
    )
    {
      var path = Path.Combine(_dataDir, name);
      File.Exists(path).ShouldBeTrue(name);
      ShouldParseAsJson(path);
    }

    dm.Dispose();
  }

  /// <summary>
  /// 坏 JSON：加载不崩、内存回落默认值、坏文件被既有机制备份为
  /// <c>ui_layout.json.corrupt-*</c> 且内容保持原样。
  /// </summary>
  [Test]
  public void LoadAll_CorruptJson_BacksUpFileAndUsesDefaults()
  {
    const string corrupt = "{oops";
    WriteLayout(corrupt);

    var dm = new DataManager(_dataDir, _encryptor, _log.Object);
    Should.NotThrow(() => dm.LoadAll());

    dm.UiLayout.Splits.ShouldBeEmpty();
    dm.UiLayout.WindowWidth.Value.ShouldBe((int?)null);
    dm.UiLayout.Version.Value.ShouldBe(UiLayoutData.CurrentVersion);

    var backups = Directory.GetFiles(_dataDir, LayoutFileName + ".corrupt-*");
    backups.Length.ShouldBe(1);
    File.ReadAllText(backups[0]).ShouldBe(corrupt);
    _log.Verify(
      log => log.Err(It.Is<string>(msg => msg.Contains("failed to load " + LayoutFileName))),
      Times.AtLeastOnce
    );

    dm.Dispose();
  }

  /// <summary>
  /// 版本字段缺失或为 0：视为当前版本，加载后可正常保存并重载。
  /// </summary>
  [Test]
  public void LoadAll_MissingOrZeroVersion_TreatedAsCurrent()
  {
    foreach (var payload in new[] { "{\"splits\": []}", "{\"version\": 0, \"splits\": []}" })
    {
      WriteLayout(payload);
      var dm = CreateManager();
      dm.UiLayout.Version.Value.ShouldBe(UiLayoutData.CurrentVersion);
      dm.SaveAll();
      dm.Dispose();

      // 已落盘的版本就是当前版本（下次启动不会再触发版本收敛）
      var reloaded = CreateManager();
      reloaded.UiLayout.Version.Value.ShouldBe(UiLayoutData.CurrentVersion);
      reloaded.Dispose();
    }
  }

  /// <summary>
  /// 新建并加载一份 <see cref="DataManager"/>（日志走 Mock，避免污染全局日志）。
  /// </summary>
  /// <returns>已加载的 DataManager。</returns>
  private DataManager CreateManager()
  {
    var dm = new DataManager(_dataDir, _encryptor, _log.Object);
    dm.LoadAll();
    return dm;
  }

  /// <summary>
  /// 布局文件绝对路径。
  /// </summary>
  /// <returns>数据目录下的 <c>ui_layout.json</c> 路径。</returns>
  private string LayoutPath() => Path.Combine(_dataDir, LayoutFileName);

  /// <summary>
  /// 写入一份手写的布局 JSON（用于构造非法 / 旧版本 / 损坏等现场）。
  /// </summary>
  /// <param name="json">完整 JSON 文本。</param>
  private void WriteLayout(string json) => File.WriteAllText(LayoutPath(), json);

  /// <summary>
  /// 构造一条分割条记录。
  /// </summary>
  /// <param name="key">语义键。</param>
  /// <param name="offset">分割条 offset（像素）。</param>
  /// <param name="collapsed">是否折叠。</param>
  /// <returns>可直接加入 <see cref="UiLayoutData.Splits"/> 的记录。</returns>
  private static SplitLayoutRecord NewSplit(string key, int offset, bool collapsed) =>
    new()
    {
      Key = { Value = key },
      Offset = { Value = offset },
      Collapsed = { Value = collapsed },
    };

  /// <summary>
  /// 取文件字节内容的十六进制串（用于断言「一个字节都没变」，避开 byte[] 与 Shouldly 的
  /// 引用相等重载歧义）。
  /// </summary>
  /// <param name="path">文件绝对路径。</param>
  /// <returns>小写十六进制字符串。</returns>
  private static string HexBytesOf(string path) => Convert.ToHexString(File.ReadAllBytes(path));

  /// <summary>
  /// 断言文件内容是一份完整、可解析的 JSON。
  /// </summary>
  /// <param name="path">文件绝对路径。</param>
  private static void ShouldParseAsJson(string path)
  {
    var json = File.ReadAllText(path);
    Should.NotThrow(() =>
    {
      using var document = JsonDocument.Parse(json);
    });
  }
}
