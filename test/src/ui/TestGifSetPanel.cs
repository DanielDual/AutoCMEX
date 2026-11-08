namespace AutoCMEX;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using AutoCMEX.UI;
using AutoCMEX.UI.Info;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// GIF 集栏面板级测试：卡片由清单驱动、集切换、导入（文件夹/压缩包）成败反馈，
/// 以及"只解码可见卡片、滑出视野即释放"的懒加载接线。
/// </summary>
/// <remarks>
/// 卡片可见性用<b>手工指定几何</b>（<c>Position</c>/<c>Size</c>/<c>ScrollVertical</c>）后同步调用
/// <c>RefreshPlayback()</c> 来断言：这样不依赖窗口布局结果，<c>SourcePath</c> 在 <c>Play()</c>/<c>Release()</c>
/// 里同步赋值，断言是确定性的；真实解码与出帧由 <c>GifFramePlayerTest</c> 覆盖。
/// 布局/显隐信号路径则相反：手工改几何后只 <c>EmitSignal</c>（不调用 <c>RefreshPlayback()</c>），
/// 等待数帧再断言，以验证「信号 → 调度 → 刷新」整链。
/// </remarks>
public class TestGifSetPanel : TestClass
{
  private const string InfoPanelScenePath = "res://src/ui/info/InfoPanel.tscn";

  private static readonly JsonSerializerOptions ManifestWriteOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
  };

  private string _dir = string.Empty;
  private Node _host = default!;
  private DataManager _dm = default!;
  private Mock<IWebSocketServer> _server = default!;

  public TestGifSetPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    // 尺寸记忆是进程内全局的：每个用例开始前清空，避免用例间互相影响
    FileDialogSizing.ResetMemoryForTests();
    _dir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_GifPanelTest_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_dir);

    _dm = new DataManager(Path.Combine(_dir, "data"), new AesEncryptor("test-key"));
    _dm.LoadAll();

    _server = new Mock<IWebSocketServer>();
    _server.SetupGet(s => s.IsRunning).Returns(true);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();

    _dm?.Dispose();

    if (Directory.Exists(_dir))
      Directory.Delete(_dir, true);
  }

  private InfoPanel InstantiatePanel()
  {
    _host = new Node();
    TestScene.AddChild(_host);

    var panel = GD.Load<PackedScene>(InfoPanelScenePath).Instantiate<InfoPanel>();
    panel.FakeDependency<DataManager>(_dm);
    panel.FakeDependency<IWebSocketServer>(_server.Object);
    panel.FakeDependency<InfoEventBus>(new InfoEventBus());
    _host.AddChild(panel);
    return panel;
  }

  private static async Task SettleAsync(Node node, int frames = 2)
  {
    for (var i = 0; i < frames; i++)
      await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  private static async Task<bool> WaitUntil(Node node, Func<bool> condition, int maxFrames = 600)
  {
    for (var i = 0; i < maxFrames; i++)
    {
      if (condition())
        return true;

      await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    return condition();
  }

  private static GifSetPanel GetGifColumn(InfoPanel panel) =>
    panel.ColumnPanels[0].ShouldBeOfType<GifSetPanel>();

  private static VBoxContainer GetCardList(Node column) =>
    (VBoxContainer)column.FindChild("CardList", owned: false, recursive: true)!;

  private static ScrollContainer GetCardScroll(Node column) =>
    (ScrollContainer)column.FindChild("CardScroll", owned: false, recursive: true)!;

  /// <summary>按用途取导入用的文件选择框（录制入口另有两个且模式相同，不能只按模式取）。</summary>
  private static FileDialog GetFileDialog(Node column, FileDialog.FileModeEnum mode) =>
    column.GetChildren().OfType<FileDialog>().Single(d => d.FileMode == mode && !IsRecordDialog(d));

  /// <summary>取录制入口的选择框（与导入用同模式，靠节点名区分）。</summary>
  private static FileDialog GetRecordDialog(Node column, FileDialog.FileModeEnum mode) =>
    column.GetChildren().OfType<FileDialog>().Single(d => d.FileMode == mode && IsRecordDialog(d));

  /// <summary>是否为录制入口的选择框。</summary>
  private static bool IsRecordDialog(FileDialog dialog) =>
    dialog.Name.ToString().StartsWith("Record", StringComparison.Ordinal);

  /// <summary>取错误明细弹窗（<see cref="FileDialog"/> 也是 <see cref="AcceptDialog"/>，必须排除）。</summary>
  private static AcceptDialog? GetMessageDialog(Node column) =>
    column.GetChildren().OfType<AcceptDialog>().LastOrDefault(d => d is not FileDialog);

  private static OptionButton GetSetSelector(Node column) =>
    (OptionButton)column.FindChild("SetSelector", owned: false, recursive: true)!;

  private static Label GetSetInfoLabel(Node column) =>
    (Label)column.FindChild("SetInfoLabel", owned: false, recursive: true)!;

  private static GifFramePlayer GetPlayer(Control card) =>
    card.GetChild(0).GetChild<GifFramePlayer>(2);

  /// <summary>现场生成一张多帧 GIF。</summary>
  private static void WriteGif(string path, int frameCount = 2)
  {
    using var image = new Image<Rgba32>(4, 4);
    while (image.Frames.Count < frameCount)
      image.Frames.CreateFrame();

    for (var i = 0; i < frameCount; i++)
    {
      image.Frames[i].Metadata.GetGifMetadata().FrameDelay = 10;
      image.Frames[i][0, 0] = new Rgba32((byte)(20 * (i + 1)), 0, 0, 255);
    }

    image.Save(path);
  }

  /// <summary>造一个自洽的 GIF 集目录（清单 + 同名 GIF）。</summary>
  private string CreateSetFolder(
    string folderName,
    string setName,
    int entryCount,
    bool firstGifBroken = false
  )
  {
    var root = Path.Combine(_dir, folderName);
    Directory.CreateDirectory(root);

    var manifest = new GifSetManifest
    {
      SetName = setName,
      BossLabel = "SampleBoss",
      GeneratedAt = "2026-09-25 12:00:00",
    };

    for (var i = 1; i <= entryCount; i++)
    {
      var fileName = $"sample_{i}.gif";
      var path = Path.Combine(root, fileName);

      if (firstGifBroken && i == 1)
        File.WriteAllText(path, "not a gif");
      else
        WriteGif(path);

      manifest.Entries.Add(
        new GifSetEntry
        {
          Index = i,
          SpellCardName = $"符卡 {i}",
          FileName = fileName,
          Width = 4,
          Height = 4,
        }
      );
    }

    File.WriteAllText(
      Path.Combine(root, GifSetManifest.FileName),
      JsonSerializer.Serialize(manifest, ManifestWriteOptions)
    );

    return root;
  }

  private string CreateSetZip(string zipName, int entryCount)
  {
    var setRoot = CreateSetFolder(
      Path.GetFileNameWithoutExtension(zipName),
      "SamplePkg",
      entryCount
    );
    var zipPath = Path.Combine(_dir, zipName);
    ZipFile.CreateFromDirectory(setRoot, zipPath, CompressionLevel.NoCompression, false);
    return zipPath;
  }

  [Test]
  public async Task NoSet_ShowsPlaceholderAndLocksPublish()
  {
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);

    GetSetInfoLabel(column).Text.ShouldBe("(未导入任何集)");
    GetCardList(column).GetChildCount().ShouldBe(0);
    GetSetSelector(column).Disabled.ShouldBeTrue("没有集时不应能切换");
    column.PublishButton.Disabled.ShouldBeTrue("没有内容可发布");

    // 录制入口不再占位禁用（代码再确认一次，防止场景被误改回 disabled 后按钮看似不可用）
    var record = (Button)column.FindChild("RecordButton", owned: false, recursive: true)!;
    record.Disabled.ShouldBeFalse();
    record.TooltipText.ShouldNotBeEmpty();
  }

  [Test]
  public async Task RecordPressed_UnconfiguredEngine_ShowsBlockerWithoutOpeningPickers()
  {
    var panel = InstantiatePanel();
    await SettleAsync(panel);
    var column = GetGifColumn(panel);

    ((Button)column.FindChild("RecordButton", owned: false, recursive: true)!).EmitSignal(
      BaseButton.SignalName.Pressed
    );
    await SettleAsync(panel);

    // 引擎目录未设：直接给可执行原因，连工程包选择框都不该弹（省得让人白选一轮）
    GetRecordDialog(column, FileDialog.FileModeEnum.OpenFile).Visible.ShouldBeFalse();

    var message = GetMessageDialog(column);
    message.ShouldNotBeNull("前置不通过必须说明原因，而不是静默无反应");
    message!.Visible.ShouldBeTrue();
    message.Title.ShouldBe("暂时无法录制");
    message.DialogText.ShouldContain("引擎目录");
  }

  [Test]
  public async Task RecordTwoStepPicking_StartsRecordingAndShowsFailure()
  {
    var panel = InstantiatePanel();
    await SettleAsync(panel);
    var column = GetGifColumn(panel);

    // 引擎目录为空：本轮必定停在前置检查；这里验的是「两步手选 → 起录 → 失败反馈」的接线
    _dm.RecordingConfig.SandboxRoot.Value = Path.Combine(_dir, "sandbox");
    var outputDir = Path.Combine(_dir, "record_out");

    GetRecordDialog(column, FileDialog.FileModeEnum.OpenFile)
      .EmitSignal(FileDialog.SignalName.FileSelected, Path.Combine(_dir, "sample.zip"));
    await SettleAsync(panel);

    var dirDialog = GetRecordDialog(column, FileDialog.FileModeEnum.OpenDir);
    dirDialog.Visible.ShouldBeTrue("选定工程包之后必须接着让你选输出目录");

    dirDialog.EmitSignal(FileDialog.SignalName.DirSelected, outputDir);

    // 起录是同步的：先把输出目录写进状态行，再进入录制态
    GetSetInfoLabel(column).Text.ShouldContain(outputDir);
    await SettleAsync(panel);

    (await WaitUntil(panel, () => GetMessageDialog(column)?.Visible == true)).ShouldBeTrue(
      "本轮失败必须给可读提示"
    );
    GetMessageDialog(column)!.Title.ShouldBe("录制失败");
  }

  [Test]
  public async Task ImportedSet_BuildsOneCardPerManifestEntry()
  {
    var root = CreateSetFolder("sample_set", "SamplePkg", 2);
    new GifSetService(_dm, _dm.DataDir).ImportFolder(root).IsSuccess.ShouldBeTrue();

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    var cards = GetCardList(column).GetChildren().OfType<Control>().ToList();

    cards.Count.ShouldBe(2);

    // 卡片 = 「序号. 符卡名」+ 尺寸副标题 + GIF 播放区，全部取自清单
    var firstBody = cards[0].GetChild(0);
    ((Label)firstBody.GetChild(0)).Text.ShouldBe("1. 符卡 1");
    ((Label)firstBody.GetChild(1)).Text.ShouldBe("sample_1.gif　4×4");
    GetPlayer(cards[0]).ShouldNotBeNull();

    GetSetInfoLabel(column).Text.ShouldContain("SamplePkg");
    GetSetInfoLabel(column).Text.ShouldContain("2 张");
    GetSetInfoLabel(column).Text.ShouldContain(root);

    var selector = GetSetSelector(column);
    selector.ItemCount.ShouldBe(1);
    selector.GetItemText(0).ShouldBe("SamplePkg");
    selector.Disabled.ShouldBeFalse();
    selector.Selected.ShouldBe(0);

    column.PublishButton.Disabled.ShouldBeFalse();

    // 面板里的卡片路径与发布取图路径同源（都来自集清单）
    var service = new GifSetService(_dm, _dm.DataDir);
    var set = service.GetActiveSet()!;
    GetPlayer(cards[0]).SourcePath.ShouldBe(service.GetEntryPath(set, set.Manifest.Entries[0]));
  }

  [Test]
  public async Task SetSelector_SwitchingActiveSetRebuildsCards()
  {
    var sets = new GifSetService(_dm, _dm.DataDir);
    sets.ImportFolder(CreateSetFolder("sample_a", "集A", 1)).IsSuccess.ShouldBeTrue();
    sets.ImportFolder(CreateSetFolder("sample_b", "集B", 3)).IsSuccess.ShouldBeTrue();

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    var selector = GetSetSelector(column);

    selector.ItemCount.ShouldBe(2);
    selector.GetItemText(0).ShouldBe("集A");
    selector.GetItemText(1).ShouldBe("集B");
    _dm.InfoConfig.ActiveGifSetId.Value.ShouldBe(sets.Sets[0].Id.Value);

    selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
    await SettleAsync(panel);

    _dm.InfoConfig.ActiveGifSetId.Value.ShouldBe(sets.Sets[1].Id.Value);
    selector.Selected.ShouldBe(1);
    GetCardList(column).GetChildCount().ShouldBe(3);
    ((Label)GetCardList(column).GetChild(0).GetChild(0).GetChild(0)).Text.ShouldBe("1. 符卡 1");

    // 再选回第一集：切换会重建卡片列表，不残留上一集的卡片
    selector.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
    await SettleAsync(panel);

    GetCardList(column).GetChildCount().ShouldBe(1);
    GetSetInfoLabel(column).Text.ShouldContain("集A");
  }

  [Test]
  public async Task RefreshPlayback_DecodesOnlyVisibleCardsAndReleasesHidden()
  {
    var root = CreateSetFolder("sample_set", "SamplePkg", 2);
    var sets = new GifSetService(_dm, _dm.DataDir);
    sets.ImportFolder(root).IsSuccess.ShouldBeTrue();

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    var cards = GetCardList(column).GetChildren().OfType<Control>().ToList();
    var scroll = GetCardScroll(column);
    var players = cards.Select(GetPlayer).ToList();

    // 手工指定几何：视口 [0, 300)，第一张在 [0, 300)，第二张在 [400, 700)（明确不相交，
    // 避免贴着视口下沿而命中"相邻即算可见"的边界规则）
    cards[0].Position = new Vector2(0, 0);
    cards[0].Size = new Vector2(260, 300);
    cards[1].Position = new Vector2(0, 400);
    cards[1].Size = new Vector2(260, 300);

    scroll.ScrollVertical = 0;
    scroll.Size = new Vector2(280, 300);
    column.RefreshPlayback();

    var set = sets.GetActiveSet()!;
    players[0].SourcePath.ShouldBe(sets.GetEntryPath(set, set.Manifest.Entries[0]));
    players[1].SourcePath.ShouldBe(string.Empty, "视口外的卡片不应驻留解码");

    // 视图不动、卡片上移：原本驻留的释放、新进入视野的开始解码
    cards[0].Position = new Vector2(0, 600);
    cards[1].Position = new Vector2(0, 0);
    column.RefreshPlayback();

    players[0].SourcePath.ShouldBe(string.Empty);
    players[1].SourcePath.ShouldBe(sets.GetEntryPath(set, set.Manifest.Entries[1]));

    // 滚动信号必须接到同一套可见性刷新
    scroll.GetVScrollBar().EmitSignal(Godot.Range.SignalName.ValueChanged, 0.0);
    players[1].SourcePath.ShouldBe(sets.GetEntryPath(set, set.Manifest.Entries[1]));
  }

  /// <summary>回归（信息页 GIF 空白）：视口内相交的卡超过旧的 3 张上限时，每一张可见卡都要驻留解码。</summary>
  [Test]
  public async Task RefreshPlayback_KeepsEveryVisibleCardBeyondOldLimit()
  {
    var root = CreateSetFolder("sample_many", "SamplePkg", 5);
    var sets = new GifSetService(_dm, _dm.DataDir);
    sets.ImportFolder(root).IsSuccess.ShouldBeTrue();

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    var cards = GetCardList(column).GetChildren().OfType<Control>().ToList();
    var scroll = GetCardScroll(column);
    var players = cards.Select(GetPlayer).ToList();

    cards.Count.ShouldBe(5, "回归夹具需要 5 张卡，否则断言会随清单条数静默变弱");

    // 5 张卡全部落在同一视口内（每张 90 高、步进 100）：旧实现只会给前 3 张派发解码
    for (var i = 0; i < cards.Count; i++)
    {
      cards[i].Position = new Vector2(0, i * 100);
      cards[i].Size = new Vector2(260, 90);
    }
    scroll.ScrollVertical = 0;
    scroll.Size = new Vector2(280, 500);
    column.RefreshPlayback();

    var set = sets.GetActiveSet()!;
    for (var i = 0; i < players.Count; i++)
      players[i].SourcePath.ShouldBe(sets.GetEntryPath(set, set.Manifest.Entries[i]));
  }

  /// <summary>回归（根因一）：视口尺寸/布局变化必须自己触发驻留刷新，不能等滚动。</summary>
  [Test]
  public async Task ViewportResize_TriggersRefreshWithoutScrolling()
  {
    var root = CreateSetFolder("sample_resize", "SamplePkg", 2);
    var sets = new GifSetService(_dm, _dm.DataDir);
    sets.ImportFolder(root).IsSuccess.ShouldBeTrue();

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    var list = GetCardList(column);
    var cards = list.GetChildren().OfType<Control>().ToList();
    var scroll = GetCardScroll(column);
    var players = cards.Select(GetPlayer).ToList();

    var set = sets.GetActiveSet()!;
    var firstPath = sets.GetEntryPath(set, set.Manifest.Entries[0]);
    var secondPath = sets.GetEntryPath(set, set.Manifest.Entries[1]);

    cards[0].Position = new Vector2(0, 0);
    cards[0].Size = new Vector2(260, 290);
    cards[1].Position = new Vector2(0, 300);
    cards[1].Size = new Vector2(260, 290);

    void AssertSecondCardInViewport(string step)
    {
      var viewTop = scroll.ScrollVertical;
      var viewBottom = viewTop + scroll.Size.Y;
      cards[1]
        .Position.Y.ShouldBeLessThanOrEqualTo(
          viewBottom,
          step + "：用例前提是第二张仍在视口下沿之上（容器重排后的真实几何）"
        );
      (cards[1].Position.Y + cards[1].Size.Y).ShouldBeGreaterThanOrEqualTo(
        viewTop,
        step + "：用例前提是第二张仍在视口上沿之下（容器重排后的真实几何）"
      );
    }

    scroll.ScrollVertical = 0;
    scroll.Size = new Vector2(280, 200);
    column.RefreshPlayback(); // 初始：只有第一张可见
    players[0].SourcePath.ShouldBe(firstPath);
    players[1].SourcePath.ShouldBe(string.Empty);

    // (1) 视口尺寸变化（窗口拉高）：第二张已移入视口，只发 Resized 信号，不滚动、不手动刷新
    cards[1].Position = new Vector2(0, 100);
    scroll.EmitSignal(Control.SignalName.Resized);
    await SettleAsync(panel, 4);

    // 几何前提与业务断言分开：容器重排后第二张必须真的落在视口内，
    // 这条若失败说明"真实布局高度变了"，而不是接线坏了
    AssertSecondCardInViewport("视口变化后");

    players[0].SourcePath.ShouldBe(firstPath, "视口变化不应把仍可见的第一张挤掉");
    players[1]
      .SourcePath.ShouldBe(
        secondPath,
        "视口尺寸变化后新进入视口的卡必须自动派发解码，不能再等滚动"
      );

    // 卡片列表尺寸与面板显隐共用同一处理器，这里只验证信号→调度不漏（触发后再刷新一次，
    // 已可见的卡不应被挤掉；三种信号同源，代表性用例由上面的视口尺寸路径覆盖）
    list.EmitSignal(Control.SignalName.Resized);
    column.EmitSignal(Control.SignalName.VisibilityChanged);
    await SettleAsync(panel, 4);

    AssertSecondCardInViewport("二次信号后");

    players[0].SourcePath.ShouldBe(firstPath, "卡片列表/显隐信号不应把仍可见的卡挤掉");
    players[1].SourcePath.ShouldBe(secondPath, "卡片列表/显隐信号不应把仍可见的卡挤掉");
  }

  [Test]
  public async Task BrokenGif_SurfacesReasonOnCardInsteadOfSilentBlank()
  {
    var root = CreateSetFolder("sample_broken", "SamplePkg", 2, firstGifBroken: true);
    new GifSetService(_dm, _dm.DataDir).ImportFolder(root).IsSuccess.ShouldBeTrue();

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    var cards = GetCardList(column).GetChildren().OfType<Control>().ToList();
    var broken = GetPlayer(cards[0]);

    // 卡片一进入视野就会尝试解码，失败必须挂到卡片提示上而不是留白
    (await WaitUntil(panel, () => !string.IsNullOrEmpty(broken.TooltipText))).ShouldBeTrue(
      "解码失败必须回报原因"
    );

    broken.TooltipText.ShouldContain("GIF 加载失败");
    broken.TooltipText.ShouldContain("符卡 1");
    broken.Texture.ShouldBeNull();

    // 另一张正常卡片不受影响
    GetPlayer(cards[1]).TooltipText.ShouldBeNullOrEmpty();
  }

  [Test]
  public async Task ImportFolderPicker_OpensWithViewportShareAndReusesDialog()
  {
    var panel = InstantiatePanel();
    await SettleAsync(panel);
    var column = GetGifColumn(panel);
    var tree = panel.GetTree();

    // 固定内容缩放，让用例与本机 DPI 解耦：只有视口（UI 坐标）与窗口像素不同，
    // 才能同时拦住「按窗口像素算尺寸」与「popup 后未补设尺寸」这两种回退。
    var originalScale = tree.Root.ContentScaleFactor;
    tree.Root.ContentScaleFactor = 0.5f;
    try
    {
      await SettleAsync(panel);
      tree.Root.GetVisibleRect()
        .Size.ShouldNotBe(
          new Vector2(tree.Root.Size.X, tree.Root.Size.Y),
          "内容缩放 0.5 时视口 UI 坐标应是窗口像素的两倍"
        );

      ((Button)column.FindChild("ImportFolderButton", owned: false, recursive: true)!).EmitSignal(
        BaseButton.SignalName.Pressed
      );
      await SettleAsync(panel);

      var dialog = GetFileDialog(column, FileDialog.FileModeEnum.OpenDir);
      dialog.Visible.ShouldBeTrue("点导入文件夹后必须弹出选择器");
      dialog.Unresizable.ShouldBeFalse("选择文件窗口要能拖大");

      // 首次唤出：精确等于视口（内容缩放后的 UI 坐标）的八成
      var visible = tree.Root.GetVisibleRect().Size;
      var expected = FileDialogSizing.Resolve(
        new Vector2I(Mathf.RoundToInt(visible.X), Mathf.RoundToInt(visible.Y))
      );
      dialog.Size.ShouldBe(expected);

      // 再次唤出：复用同一个对话框实例，而不是每次新建
      ((Button)column.FindChild("ImportFolderButton", owned: false, recursive: true)!).EmitSignal(
        BaseButton.SignalName.Pressed
      );
      await SettleAsync(panel);
      ReferenceEquals(GetFileDialog(column, FileDialog.FileModeEnum.OpenDir), dialog)
        .ShouldBeTrue("同一选择器应复用，不应每次唤起都新建 FileDialog");
    }
    finally
    {
      tree.Root.ContentScaleFactor = originalScale;
    }
  }

  [Test]
  public async Task ImportZip_ViaDialog_RegistersSetAndSelectsIt()
  {
    var zipPath = CreateSetZip("sample_pkg.zip", 2);
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    GetSetSelector(column).Disabled.ShouldBeTrue();

    ((Button)column.FindChild("ImportZipButton", owned: false, recursive: true)!).EmitSignal(
      BaseButton.SignalName.Pressed
    );
    await SettleAsync(panel);

    var dialog = GetFileDialog(column, FileDialog.FileModeEnum.OpenFile);

    dialog.EmitSignal(FileDialog.SignalName.FileSelected, zipPath);
    await SettleAsync(panel);

    _dm.InfoConfig.GifSets.Count.ShouldBe(1);
    var record = _dm.InfoConfig.GifSets[0];
    record.SetName.Value.ShouldBe("SamplePkg");
    // 压缩包解压到托管目录并引用该目录（校验失败才回滚删除）
    record.RootPath.Value.ShouldStartWith(
      Path.Combine(_dm.DataDir, GifSetService.ManagedSetsDirName)
    );
    _dm.InfoConfig.ActiveGifSetId.Value.ShouldBe(record.Id.Value);

    var status = GetSetInfoLabel(column).Text;
    status.ShouldContain("导入成功");
    status.ShouldContain("SamplePkg");
    status.ShouldContain("2 张");
    GetCardList(column).GetChildCount().ShouldBe(2);
    column.PublishButton.Disabled.ShouldBeFalse();

    // 多等几帧：集列表与当前集各会触发一次重建，成功提示不能被后一次冲掉
    await SettleAsync(panel, 4);
    GetSetInfoLabel(column).Text.ShouldContain("导入成功");
  }

  [Test]
  public async Task Import_BadInput_ReportsDetailsAndKeepsPreviousState()
  {
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);

    // 先成功导入一集，作为"失败不得破坏既有状态"的基线
    var good = CreateSetFolder("sample_ok", "SamplePkg", 1);
    ((Button)column.FindChild("ImportFolderButton", owned: false, recursive: true)!).EmitSignal(
      BaseButton.SignalName.Pressed
    );
    await SettleAsync(panel);
    GetFileDialog(column, FileDialog.FileModeEnum.OpenDir)
      .EmitSignal(FileDialog.SignalName.DirSelected, good);
    await SettleAsync(panel);

    _dm.InfoConfig.GifSets.Count.ShouldBe(1);
    GetSetInfoLabel(column).Text.ShouldContain("导入成功");

    // 压缩包不存在 → 报错、弹明细、不落库，且上一次的成功提示被失败结果替换
    ((Button)column.FindChild("ImportZipButton", owned: false, recursive: true)!).EmitSignal(
      BaseButton.SignalName.Pressed
    );
    await SettleAsync(panel);

    GetFileDialog(column, FileDialog.FileModeEnum.OpenFile)
      .EmitSignal(FileDialog.SignalName.FileSelected, Path.Combine(_dir, "missing.zip"));
    await SettleAsync(panel);

    var failed = GetSetInfoLabel(column).Text;
    failed.ShouldContain("压缩包不存在");
    failed.ShouldNotContain("导入成功");
    _dm.InfoConfig.GifSets.Count.ShouldBe(1, "失败不得落库");

    var message = GetMessageDialog(column);
    message.ShouldNotBeNull("导入失败必须弹出明细，而不是只写一行状态");
    message!.DialogText.ShouldContain("压缩包不存在");

    // 文件夹缺少 manifest.json → 逐条列出原因，且不落库
    var dirtyFolder = Path.Combine(_dir, "sample_dirty");
    Directory.CreateDirectory(dirtyFolder);
    File.WriteAllText(Path.Combine(dirtyFolder, "loose.gif"), "not a gif");

    ((Button)column.FindChild("ImportFolderButton", owned: false, recursive: true)!).EmitSignal(
      BaseButton.SignalName.Pressed
    );
    await SettleAsync(panel);

    GetFileDialog(column, FileDialog.FileModeEnum.OpenDir)
      .EmitSignal(FileDialog.SignalName.DirSelected, dirtyFolder);
    await SettleAsync(panel);

    GetSetInfoLabel(column).Text.ShouldContain(GifSetManifest.FileName);
    _dm.InfoConfig.GifSets.Count.ShouldBe(1);

    // 基线集仍在展示、仍可发布：失败不该把界面清空
    GetCardList(column).GetChildCount().ShouldBe(1);
    column.PublishButton.Disabled.ShouldBeFalse();
  }

  [Test]
  public async Task Publish_WithoutSelectedTargets_SendsNothing()
  {
    var root = CreateSetFolder("sample_set", "SamplePkg", 1);
    new GifSetService(_dm, _dm.DataDir).ImportFolder(root).IsSuccess.ShouldBeTrue();

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = GetGifColumn(panel);
    column.PublishButton.Disabled.ShouldBeFalse();

    ((Button)column.FindChild("PublishButton", owned: false, recursive: true)!).EmitSignal(
      BaseButton.SignalName.Pressed
    );
    await SettleAsync(panel);

    // 目标群未勾选时预检拦在出站之前
    _server.Verify(s => s.BroadcastAsync(It.IsAny<WebSocketMessage>()), Times.Never);
  }

  /// <summary>
  /// 回归（S4）：导入选择器会复用，但信号订阅只能在创建分支里挂一次。
  /// 旧写法把 <c>+=</c> 放在 <c>??=</c> 外面，第 N 次按下导入按钮就会重复连接同一条信号
  /// （Godot 4 对同一 Callable 的重复连接会持续报 "already connected" 引擎错误）。
  /// </summary>
  [Test]
  public async Task ImportPickers_RepeatedPress_SubscribeHandlerOnlyOnce()
  {
    var panel = InstantiatePanel();
    await SettleAsync(panel);
    var column = GetGifColumn(panel);

    var folderButton = (Button)
      column.FindChild("ImportFolderButton", owned: false, recursive: true)!;
    var zipButton = (Button)column.FindChild("ImportZipButton", owned: false, recursive: true)!;

    folderButton.EmitSignal(BaseButton.SignalName.Pressed);
    zipButton.EmitSignal(BaseButton.SignalName.Pressed);
    await SettleAsync(panel);

    var folderDialog = GetFileDialog(column, FileDialog.FileModeEnum.OpenDir);
    var zipDialog = GetFileDialog(column, FileDialog.FileModeEnum.OpenFile);

    var folderConnections = folderDialog.GetSignalConnectionList(FileDialog.SignalName.DirSelected);
    var zipConnections = zipDialog.GetSignalConnectionList(FileDialog.SignalName.FileSelected);
    folderConnections.Count.ShouldBe(1, "文件夹选择器的 DirSelected 只应连一次");
    zipConnections.Count.ShouldBe(1, "压缩包选择器的 FileSelected 只应连一次");

    // 复用同一个对话框再点一次：对话框实例不变、连接数也不得增长
    folderButton.EmitSignal(BaseButton.SignalName.Pressed);
    zipButton.EmitSignal(BaseButton.SignalName.Pressed);
    await SettleAsync(panel);

    ReferenceEquals(GetFileDialog(column, FileDialog.FileModeEnum.OpenDir), folderDialog)
      .ShouldBeTrue("同一选择器应复用");
    ReferenceEquals(GetFileDialog(column, FileDialog.FileModeEnum.OpenFile), zipDialog)
      .ShouldBeTrue("同一选择器应复用");
    folderDialog
      .GetSignalConnectionList(FileDialog.SignalName.DirSelected)
      .Count.ShouldBe(folderConnections.Count);
    zipDialog
      .GetSignalConnectionList(FileDialog.SignalName.FileSelected)
      .Count.ShouldBe(zipConnections.Count);
  }

  /// <summary>
  /// 回归（S4）：连点两次导入后只做一次选择，只能导入一个集、只能解压一份。
  /// 压缩包导入每次都会新建 GUID 托管目录，重复执行会留下重复集与重复解压目录。
  /// </summary>
  [Test]
  public async Task ImportPicker_SingleSelectionAfterRepeatedPress_ImportsExactlyOnce()
  {
    var zipPath = CreateSetZip("repeat_pkg.zip", 2);
    var panel = InstantiatePanel();
    await SettleAsync(panel);
    var column = GetGifColumn(panel);

    var zipButton = (Button)column.FindChild("ImportZipButton", owned: false, recursive: true)!;
    zipButton.EmitSignal(BaseButton.SignalName.Pressed);
    zipButton.EmitSignal(BaseButton.SignalName.Pressed);
    await SettleAsync(panel);

    GetFileDialog(column, FileDialog.FileModeEnum.OpenFile)
      .EmitSignal(FileDialog.SignalName.FileSelected, zipPath);
    await SettleAsync(panel);

    _dm.InfoConfig.GifSets.Count.ShouldBe(1, "一次选择只能落一个集");
    GetCardList(column).GetChildCount().ShouldBe(2);
    GetSetInfoLabel(column).Text.ShouldContain("导入成功");

    var managedDir = Path.Combine(_dm.DataDir, GifSetService.ManagedSetsDirName);
    Directory.Exists(managedDir).ShouldBeTrue("压缩包导入应解压到托管集目录");
    Directory
      .GetDirectories(managedDir)
      .Length.ShouldBe(1, "一次选择只能解压出一份集目录，不能重复解压");
  }
}
