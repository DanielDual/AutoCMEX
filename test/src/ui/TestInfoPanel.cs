namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using AutoCMEX.UI.Info;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;
// 注意：本文件里 Image/Color 指 Godot 类型（出图断言用），ImageSharp 只能全限定引用
using SharpImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

/// <summary>
/// 信息板块面板级测试：实例化真实 <c>InfoPanel.tscn</c>，断言四栏装配顺序、两表预览与出图同源、
/// 下栏目标群勾选与发布前置守卫。
/// </summary>
/// <remarks>
/// <para>
/// 出图用例走真实的窗口渲染（<see cref="TableImageRenderer"/> 依赖 <see cref="SubViewport"/>），
/// 因此本测试类不适用于 headless 运行；断言像素而非"文件存在"是为了真正守住"行染色落到了图上"。
/// </para>
/// <para>
/// WebSocket 用 Moq 打桩：面板级只关心"该不该发"，实际发送编排由 <c>PublishServiceTest</c> 覆盖。
/// </para>
/// </remarks>
public class TestInfoPanel : TestClass
{
  private const string InfoPanelScenePath = "res://src/ui/info/InfoPanel.tscn";

  private Node _host = default!;
  private DataManager _dm = default!;
  private Mock<IWebSocketServer> _server = default!;
  private InfoEventBus _bus = default!;

  /// <summary>面板实际发出的出站消息（按顺序），用于断言发布顺序与内容。</summary>
  private readonly List<WebSocketMessage> _sent = new();

  private readonly List<string> _tempFiles = new();
  private readonly List<string> _tempDirs = new();

  public TestInfoPanel(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();

    _dm?.Dispose();

    foreach (var path in _tempFiles)
    {
      if (File.Exists(path))
        File.Delete(path);
    }

    _tempFiles.Clear();

    foreach (var dir in _tempDirs)
    {
      if (Directory.Exists(dir))
        Directory.Delete(dir, true);
    }

    _tempDirs.Clear();
    _sent.Clear();
  }

  private DataManager CreateDataManager(IEnumerable<Boss>? bosses = null)
  {
    _dm = new DataManager(
      Path.Combine(Path.GetTempPath(), $"AutoCMEX_InfoPanelTest_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    if (bosses is not null)
    {
      foreach (var boss in bosses)
        _dm.Bosses.Add(boss);
    }

    return _dm;
  }

  private InfoPanel InstantiatePanel()
  {
    _sent.Clear();

    _server = new Mock<IWebSocketServer>();
    _server.SetupGet(s => s.IsRunning).Returns(true);
    _server.SetupGet(s => s.ConnectionCount).Returns(1);
    _server
      .Setup(s => s.BroadcastAsync(It.IsAny<WebSocketMessage>()))
      .Callback<WebSocketMessage>(_sent.Add)
      .Returns(Task.CompletedTask);

    _bus = new InfoEventBus();

    _host = new Node();
    TestScene.AddChild(_host);

    var panel = GD.Load<PackedScene>(InfoPanelScenePath).Instantiate<InfoPanel>();
    panel.FakeDependency<DataManager>(_dm);
    panel.FakeDependency<IWebSocketServer>(_server.Object);
    panel.FakeDependency<InfoEventBus>(_bus);
    _host.AddChild(panel);
    return panel;
  }

  /// <summary>在临时目录造一个「1 条清单 + 同名 GIF」的集并注册为当前集。</summary>
  private void ImportGifSet(int entryCount = 1)
  {
    var root = Path.Combine(Path.GetTempPath(), $"AutoCMEX_InfoPanelSet_{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    _tempDirs.Add(root);

    var manifest = new GifSetManifest { SetName = "SamplePkg", BossLabel = "SampleBoss" };
    for (var i = 1; i <= entryCount; i++)
    {
      var fileName = $"sample_{i}.gif";
      using (var image = new SharpImage(4, 4))
        SixLabors.ImageSharp.ImageExtensions.Save(image, Path.Combine(root, fileName));

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
      JsonSerializer.Serialize(
        manifest,
        new JsonSerializerOptions
        {
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
          WriteIndented = true,
        }
      )
    );

    new GifSetService(_dm, _dm.DataDir).ImportFolder(root).IsSuccess.ShouldBeTrue();
  }

  /// <summary>加一个已勾选的目标群。</summary>
  private void EnableGroup(string channelId) =>
    _dm.Settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = channelId },
        DisplayName = { Value = "示例群" },
        Enabled = { Value = true },
      }
    );

  private static string SentKind(WebSocketMessage message) =>
    message.Payload.GetProperty("data").GetProperty("kind").GetString()!;

  private static string RequestId(WebSocketMessage message) =>
    message.Payload.GetProperty("data").GetProperty("requestId").GetString()!;

  /// <summary>取面板的报告对话框（面板只建一个，标题随用途变化）。</summary>
  private static AcceptDialog FindDialog(Node panel)
  {
    var dialog = FindDialogOrNull(panel);
    dialog.ShouldNotBeNull("面板应弹出报告对话框");
    return dialog!;
  }

  private static AcceptDialog? FindDialogOrNull(Node panel) =>
    panel.GetChildren().OfType<AcceptDialog>().LastOrDefault(d => d is not FileDialog);

  /// <summary>取报告对话框上的「只重试失败项」按钮（由 <c>AcceptDialog.AddButton</c> 追加）。</summary>
  private static Button FindRetryButton(Node panel)
  {
    var dialog = FindDialog(panel);
    var button = Descendants(dialog).OfType<Button>().FirstOrDefault(b => b.Text == "只重试失败项");
    button.ShouldNotBeNull("报告对话框应带「只重试失败项」按钮");
    return button!;
  }

  private static IEnumerable<Node> Descendants(Node root)
  {
    // includeInternal：AcceptDialog 的自定义按钮挂在内部子节点上，普通遍历看不到
    foreach (var child in root.GetChildren(includeInternal: true))
    {
      yield return child;
      foreach (var nested in Descendants(child))
        yield return nested;
    }
  }

  /// <summary>
  /// 驱动一次真实往返：面板每发出一项，就按 <paramref name="answers"/> 依次回一条发布结果，
  /// 直到累计发出 <paramref name="expectedSends"/> 项。
  /// </summary>
  /// <remarks>
  /// 回执走 <see cref="InfoEventBus"/>（真实入站路径：总线 → 延后一帧 → 主线程分发），
  /// 因此这里必须逐帧推进，不能用固定的等待帧数（发布项数越多需要等越久）。
  /// </remarks>
  private async Task<bool> PumpPublishAsync(Node panel, int expectedSends, params bool[] answers)
  {
    var answered = 0;
    for (var frame = 0; frame < 600; frame++)
    {
      while (answered < _sent.Count)
      {
        var success = answered >= answers.Length || answers[answered];
        _bus.Publish(
          InfoProtocol.PublishResultEvent,
          JsonSerializer.SerializeToElement(new { requestId = RequestId(_sent[answered]), success })
        );
        answered++;
      }

      if (_sent.Count >= expectedSends && answered >= expectedSends)
        return true;

      await panel.ToSignal(panel.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    return false;
  }

  /// <summary>造一个「2 张已猜出、1 张未猜出、2 个创作者」的 Boss。</summary>
  private static Boss CreateBoss() =>
    new()
    {
      Name = "SampleBoss",
      SpellCards = new()
      {
        new SpellCard
        {
          Name = { Value = "符卡 1" },
          Creator = { Value = "Alpha" },
          IsGuessedOut = { Value = true },
        },
        new SpellCard
        {
          Name = { Value = "符卡 2" },
          Creator = { Value = "Alpha" },
          IsGuessedOut = { Value = false },
        },
        new SpellCard
        {
          Name = { Value = "符卡 3" },
          Creator = { Value = "Beta" },
          IsGuessedOut = { Value = true },
        },
      },
    };

  private static async Task SettleAsync(Node node)
  {
    await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  private static GridContainer FindGrid(Node root, string name)
  {
    var grid = root.FindChild(name, owned: false, recursive: true) as GridContainer;
    grid.ShouldNotBeNull($"子面板内应有名为 {name} 的真实网格节点");
    return grid;
  }

  private static Color CellBackground(Node grid, int index) =>
    ((StyleBoxFlat)grid.GetChild<PanelContainer>(index).GetThemeStylebox("panel")).BgColor;

  private static Button FindButton(Node root, string name)
  {
    var button = root.FindChild(name, owned: false, recursive: true) as Button;
    button.ShouldNotBeNull($"子面板内应有名为 {name} 的真实按钮节点");
    return button;
  }

  private static int CountPixels(Image image, Func<Color, bool> predicate)
  {
    var count = 0;
    for (var y = 0; y < image.GetHeight(); y++)
    {
      for (var x = 0; x < image.GetWidth(); x++)
      {
        if (predicate(image.GetPixel(x, y)))
          count++;
      }
    }

    return count;
  }

  /// <summary>把「栏位场景内的某个节点」折算成该栏位的直接子节点（标题可能包在 HeaderRow 之类的行容器里）。</summary>
  private static Control ColumnChild(Node column, string name)
  {
    var node = column.FindChild(name, owned: false, recursive: true);
    node.ShouldNotBeNull($"{column.Name} 内应有名为 {name} 的节点");

    while (node!.GetParent() != column)
      node = node.GetParent();

    return (Control)node!;
  }

  /// <summary>断言这些直接子节点按给定顺序自上而下排列（VBoxContainer 的直接子节点顺序即视觉顺序）。</summary>
  private static void AssertTopToBottom(Node column, params string[] names)
  {
    for (var i = 1; i < names.Length; i++)
    {
      ColumnChild(column, names[i])
        .GetIndex()
        .ShouldBeGreaterThan(
          ColumnChild(column, names[i - 1]).GetIndex(),
          $"{column.Name} 内 {names[i - 1]} 应排在 {names[i]} 之上"
        );
    }
  }

  [Test]
  public async Task RealScene_InstantiatesFourColumnsInFixedOrder()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    // 上栏 = ScrollContainer（Godot 无 HScrollContainer）+ 内层 HBoxContainer
    panel.FindChild("TopScroll", owned: false).ShouldNotBeNull();

    var columns = panel.GetNode<HBoxContainer>("%Columns");
    columns.GetChildCount().ShouldBe(4);

    panel.ColumnPanels.Count.ShouldBe(4);
    panel.ColumnPanels[0].ShouldBeOfType<GifSetPanel>();
    panel.ColumnPanels[1].ShouldBeOfType<GuessingTablePanel>();
    panel.ColumnPanels[2].ShouldBeOfType<CreatorRemainingPanel>();
    panel.ColumnPanels[3].ShouldBeOfType<ActivityRulePanel>();

    foreach (var column in panel.ColumnPanels)
    {
      column.ShouldNotBeNull();
      // 每栏保底最小宽度，窗口变窄时交给上栏横向滚动，而不是把栏压变形
      column!.CustomMinimumSize.X.ShouldBe(320f);
    }
  }

  /// <summary>
  /// 四栏的排版契约：标题在最上、正文在中、发布按钮在最下，且四栏铺满上栏宽度。
  /// </summary>
  /// <remarks>
  /// 这两条都是「只有肉眼才发现」的缺陷的回归守卫：带 <c>size_flags_vertical = 3</c> 的滚动区若排在
  /// 标题之前，会把标题挤到整栏最底部；上栏容器缺 <c>size_flags_horizontal</c> 时不会被
  /// <c>ScrollContainer</c> 拉伸，四栏会停在各自最小宽度、全挤在左侧而右侧大片空白。
  /// </remarks>
  [Test]
  public async Task Columns_PlaceTitleOnTopPublishAtBottomAndFillTopScrollWidth()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    AssertTopToBottom(panel.ColumnPanels[0]!, "TitleLabel", "CardScroll", "PublishButton");
    AssertTopToBottom(panel.ColumnPanels[1]!, "TitleLabel", "TableScroll", "PublishButton");
    AssertTopToBottom(panel.ColumnPanels[2]!, "TitleLabel", "TableScroll", "PublishButton");
    AssertTopToBottom(panel.ColumnPanels[3]!, "TitleLabel", "RuleEdit", "PublishButton");

    var topScroll = (ScrollContainer)panel.FindChild("TopScroll", owned: false)!;
    var columns = panel.GetNode<HBoxContainer>("%Columns");

    topScroll.Size.X.ShouldBeGreaterThan(
      columns.GetCombinedMinimumSize().X,
      "本用例假定上栏比四栏最小宽度更宽，否则下面一条断言的区分度不成立"
    );
    columns.Size.X.ShouldBe(topScroll.Size.X, "四栏应被拉伸到铺满上栏，而不是停在最小宽度挤在左侧");
  }

  [Test]
  public async Task GuessingTableColumn_PreviewMatchesPublishModel()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = (GuessingTablePanel)panel.ColumnPanels[1]!;
    var snapshot = new InfoDataService(_dm).BuildSnapshot();
    var model = TableModelBuilder.BuildGuessingTable(snapshot);

    column.TitleLabel.Text.ShouldBe(model.Title);
    column.PublishButton.Disabled.ShouldBeFalse();

    var grid = FindGrid(column, "TableGrid");
    grid.Columns.ShouldBe(3);
    grid.GetChildCount().ShouldBe((model.Rows.Count + 1) * 3);

    // 表头 + 每行文本与出图模型逐格一致（预览与发布图同源）
    ((Label)grid.GetChild(0).GetChild(0)).Text.ShouldBe(model.Columns[0].Header);
    ((Label)grid.GetChild(3).GetChild(0)).Text.ShouldBe(model.Rows[0].Cells[0]);
    ((Label)grid.GetChild(6).GetChild(0)).Text.ShouldBe(model.Rows[1].Cells[0]);

    // 已猜出行整行染绿；未猜出行留白（第一栏为空串）
    CellBackground(grid, 3).ShouldBe(InfoTablePalette.HighlightBackground);
    CellBackground(grid, 6).ShouldBe(InfoTablePalette.Background);
    ((Label)grid.GetChild(6).GetChild(0)).Text.ShouldBe(string.Empty);

    // 单元格必须自身计入最小尺寸（PanelContainer 而非 Panel）：否则网格算出的列宽/行高全为 0，
    // 单元格塌成 0×0 并全部叠在同一坐标，预览变成一坨糊在一起的字
    foreach (var cell in grid.GetChildren().OfType<Control>())
    {
      cell.GetCombinedMinimumSize()
        .X.ShouldBeGreaterThan(
          TableModelView.CellPaddingX * 2,
          "单元格最小宽度应含左右内边距，否则网格算不出列宽"
        );

      cell.GetCombinedMinimumSize()
        .Y.ShouldBeGreaterThan(
          TableModelView.CellPaddingY * 2,
          "单元格最小高度应含上下内边距，否则网格算不出行高"
        );
    }
  }

  [Test]
  public async Task CreatorRemainingColumn_CountsAndHighlightsFullyGuessedCreator()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = (CreatorRemainingPanel)panel.ColumnPanels[2]!;
    var model = TableModelBuilder.BuildCreatorRemainingTable(
      new InfoDataService(_dm).BuildSnapshot()
    );

    column.TitleLabel.Text.ShouldBe(model.Title);

    var grid = FindGrid(column, "TableGrid");
    grid.Columns.ShouldBe(2);
    grid.GetChildCount().ShouldBe((model.Rows.Count + 1) * 2);

    // Alpha 剩 1 张（未打完）→ 不染色；Beta 剩 0 张 → 整行染绿
    ((Label)grid.GetChild(2).GetChild(0)).Text.ShouldBe("Alpha");
    ((Label)grid.GetChild(3).GetChild(0)).Text.ShouldBe("1");
    CellBackground(grid, 2).ShouldBe(InfoTablePalette.Background);

    ((Label)grid.GetChild(4).GetChild(0)).Text.ShouldBe("Beta");
    ((Label)grid.GetChild(5).GetChild(0)).Text.ShouldBe("0");
    CellBackground(grid, 4).ShouldBe(InfoTablePalette.HighlightBackground);
  }

  [Test]
  public async Task ActivityRuleColumn_EditsWriteBackToModelAndGatePublish()
  {
    CreateDataManager();
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = (ActivityRulePanel)panel.ColumnPanels[3]!;
    var edit = (TextEdit)column.FindChild("RuleEdit", owned: false, recursive: true)!;

    // 规则为空 → 无可发布内容
    edit.Text.ShouldBe(string.Empty);
    column.PublishButton.Disabled.ShouldBeTrue();

    edit.Text = "活动规则正文";
    edit.EmitSignal(TextEdit.SignalName.TextChanged);
    await SettleAsync(panel);

    _dm.InfoConfig.ActivityRule.Value.ShouldBe("活动规则正文");
    column.PublishButton.Disabled.ShouldBeFalse();

    // 反向：模型 → 控件（绑定回灌不得再写成"用户输入"而循环触发）
    _dm.InfoConfig.ActivityRule.Value = "由模型更新";
    await SettleAsync(panel);

    edit.Text.ShouldBe("由模型更新");
    _dm.InfoConfig.ActivityRule.Value.ShouldBe("由模型更新");
  }

  [Test]
  public async Task GroupList_BuildsCheckBoxPerTargetGroupAndGatesForwardButton()
  {
    CreateDataManager(new[] { CreateBoss() });
    _dm.Settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = "10001" },
        DisplayName = { Value = "示例群" },
        Enabled = { Value = true },
      }
    );
    _dm.Settings.TargetGroups.Add(
      new TargetGroup { ChannelId = { Value = "10002" }, Enabled = { Value = false } }
    );

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var list = panel.GetNode<HBoxContainer>("%GroupList");
    list.GetChildCount().ShouldBe(2);
    list.GetChild<CheckBox>(0).Text.ShouldBe("示例群（10001）");
    list.GetChild<CheckBox>(0).ButtonPressed.ShouldBeTrue();
    list.GetChild<CheckBox>(1).Text.ShouldBe("10002");
    list.GetChild<CheckBox>(1).ButtonPressed.ShouldBeFalse();

    var forward = panel.GetNode<Button>("%ForwardAllButton");
    forward.Disabled.ShouldBeFalse("已有一个目标群被勾选时应可转发");

    // 取消唯一勾选的群 → 写回模型并立即禁用一键转发
    var first = list.GetChild<CheckBox>(0);
    first.SetPressedNoSignal(false);
    first.EmitSignal(BaseButton.SignalName.Toggled, false);
    await SettleAsync(panel);

    _dm.Settings.TargetGroups[0].Enabled.Value.ShouldBeFalse();
    forward.Disabled.ShouldBeTrue();

    // AutoList 变更驱动下栏重建：新增目标群后自动出现第 3 个勾选框
    _dm.Settings.TargetGroups.Add(
      new TargetGroup { ChannelId = { Value = "10003" }, Enabled = { Value = true } }
    );
    await SettleAsync(panel);

    list.GetChildCount().ShouldBe(3);
    forward.Disabled.ShouldBeFalse();
  }

  [Test]
  public async Task Publish_WithoutSelectedTargets_ShowsGuardAndSendsNothing()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = (GuessingTablePanel)panel.ColumnPanels[1]!;
    FindButton(column, "PublishButton").EmitSignal(BaseButton.SignalName.Pressed);
    await SettleAsync(panel);

    // 预检拦在出站之前：无目标群时不发任何消息
    _server.Verify(s => s.BroadcastAsync(It.IsAny<WebSocketMessage>()), Times.Never);
  }

  [Test]
  public async Task RenderGuessingTable_WithRealWindow_ProducesHighlightedPng()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var model = TableModelBuilder.BuildGuessingTable(new InfoDataService(_dm).BuildSnapshot());
    model.HasContent.ShouldBeTrue();

    var outputPath = TableImageRenderer.BuildTempOutputPath($"info_panel_{Guid.NewGuid():N}.png");
    _tempFiles.Add(outputPath);

    var result = await TableImageRenderer.RenderToPngAsync(panel, model, outputPath);

    result.IsSuccess.ShouldBeTrue(result.ErrorMessage);
    result.FileSizeBytes.ShouldBeGreaterThan(0);
    File.Exists(outputPath).ShouldBeTrue();
    result.Size.X.ShouldBeGreaterThan(200);
    result.Size.Y.ShouldBeGreaterThan(0);

    using var image = Image.LoadFromFile(outputPath);
    image.GetWidth().ShouldBe(result.Size.X);
    image.GetHeight().ShouldBe(result.Size.Y);

    // 画面必须真的按模型画：已猜出行染绿、未猜出行留白、表头为灰
    var green = CountPixels(image, c => c.G > c.R + 0.15f && c.G > c.B + 0.15f);
    var white = CountPixels(image, c => c.R > 0.9f && c.G > 0.9f && c.B > 0.9f);
    var header = CountPixels(
      image,
      c => Mathf.Abs(c.R - c.G) < 0.05f && Mathf.Abs(c.G - c.B) < 0.05f && c.R is > 0.6f and < 0.95f
    );

    green.ShouldBeGreaterThan(0, "至少一行已猜出，应为绿底");
    white.ShouldBeGreaterThan(0, "未猜出行与表体应为白底");
    header.ShouldBeGreaterThan(0, "表头应为灰底");
  }

  [Test]
  public async Task RefreshGroups_RequestsKoishiListAndMergesResult()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    panel.GetNode<Button>("%RefreshGroupsButton").EmitSignal(BaseButton.SignalName.Pressed);
    await SettleAsync(panel);

    _sent.Count.ShouldBe(1);
    _sent[0].Payload.GetProperty("event").GetString().ShouldBe(InfoProtocol.QueryGroupListEvent);
    FindDialog(panel).DialogText.ShouldContain("已向 Koishi 发起群列表查询");

    // 回执走总线（入站真实路径）→ 并入目标群列表并重建下栏
    _bus.Publish(
      InfoProtocol.GroupListResultEvent,
      JsonSerializer.SerializeToElement(
        new
        {
          data = new[]
          {
            new
            {
              channelId = "10001",
              guildId = "9001",
              name = "示例群",
            },
            new
            {
              channelId = "10002",
              guildId = "9001",
              name = "另一群",
            },
          },
        }
      )
    );
    await SettleAsync(panel);

    _dm.Settings.TargetGroups.Count.ShouldBe(2);
    _dm.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10001");
    _dm.Settings.TargetGroups[0].DisplayName.Value.ShouldBe("示例群");

    var list = panel.GetNode<HBoxContainer>("%GroupList");
    list.GetChildCount().ShouldBe(2);
    list.GetChild<CheckBox>(0).Text.ShouldBe("示例群（10001）");
    list.GetChild<CheckBox>(0).ButtonPressed.ShouldBeFalse("新并入的群默认不勾选");
    FindDialog(panel).Title.ShouldBe("群列表已更新");

    // 与 Koishi 无连接时不得静默：直接报失败
    _server.SetupGet(s => s.ConnectionCount).Returns(0);
    panel.GetNode<Button>("%RefreshGroupsButton").EmitSignal(BaseButton.SignalName.Pressed);
    await SettleAsync(panel);

    _sent.Count.ShouldBe(1, "无连接时不应发出查询");
    FindDialog(panel).Title.ShouldBe("群列表刷新失败");
  }

  [Test]
  public async Task ForwardAll_PublishesFourItemsInFixedOrderAndReportsSuccess()
  {
    CreateDataManager(new[] { CreateBoss() });
    ImportGifSet();
    EnableGroup("10001");
    _dm.InfoConfig.ActivityRule.Value = "活动规则正文";

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var forward = panel.GetNode<Button>("%ForwardAllButton");
    forward.Disabled.ShouldBeFalse();
    forward.EmitSignal(BaseButton.SignalName.Pressed);

    (await PumpPublishAsync(panel, 4)).ShouldBeTrue("四项都应拿到回执后结束");
    await SettleAsync(panel);

    // 固定顺序：GIF 集 → 猜测表 → 创作者表 → 活动规则
    _sent
      .Select(SentKind)
      .ShouldBe(new[] { "GifSet", "GuessingTable", "CreatorRemainingTable", "ActivityRule" });

    // GIF 集每条 = 序号.符卡名 + 原图路径
    var nodes = _sent[0].Payload.GetProperty("data").GetProperty("nodes");
    nodes.GetArrayLength().ShouldBe(1);
    nodes[0].GetProperty("title").GetString().ShouldBe("1. 符卡 1");
    nodes[0].GetProperty("imagePath").GetString().ShouldEndWith("sample_1.gif");

    // 两表发布的是当次渲染出的真实 PNG（出图薄壳在本链路里也必须真的落文件）
    foreach (var index in new[] { 1, 2 })
    {
      var imagePath = _sent[index]
        .Payload.GetProperty("data")
        .GetProperty("nodes")[0]
        .GetProperty("imagePath")
        .GetString();
      imagePath.ShouldNotBeNullOrWhiteSpace();
      File.Exists(imagePath).ShouldBeTrue();
      new FileInfo(imagePath!).Length.ShouldBeGreaterThan(0);
      _tempFiles.Add(imagePath!);
    }

    // 活动规则为纯文本：同一个节点槽位里只有文字，没有图片
    var ruleNode = _sent[3].Payload.GetProperty("data").GetProperty("nodes")[0];
    ruleNode.GetProperty("text").GetString().ShouldBe("活动规则正文");
    ruleNode.GetProperty("imagePath").ValueKind.ShouldBe(JsonValueKind.Null);

    var dialog = FindDialog(panel);
    dialog.Title.ShouldBe("发布结果");
    dialog.DialogText.ShouldContain("共 4 项，成功 4 项，失败 0 项");
    FindRetryButton(panel).Visible.ShouldBeFalse("没有失败项就不该提供重试");
  }

  [Test]
  public async Task ForwardAll_FailureSkipsRestAndRetrySendsOnlyFailedItem()
  {
    CreateDataManager(new[] { CreateBoss() });
    ImportGifSet();
    EnableGroup("10001");
    _dm.InfoConfig.ActivityRule.Value = "活动规则正文";

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    panel.GetNode<Button>("%ForwardAllButton").EmitSignal(BaseButton.SignalName.Pressed);

    // 首项回执失败：剩下的三项不再发送
    (await PumpPublishAsync(panel, 1, false)).ShouldBeTrue();
    await SettleAsync(panel);

    _sent.Count.ShouldBe(1, "失败即停，不得继续发后续项");
    SentKind(_sent[0]).ShouldBe("GifSet");

    var dialog = FindDialog(panel);
    dialog.DialogText.ShouldContain("共 1 项，成功 0 项，失败 1 项");

    // 报告只含已尝试的项，因此必须显式写明哪些压根没发出去（否则看起来像漏发）
    dialog.DialogText.ShouldContain("未发送（前一项失败即停）");
    dialog.DialogText.ShouldContain("符卡猜测情况表");
    dialog.DialogText.ShouldContain("活动规则");

    var retry = FindRetryButton(panel);
    retry.Visible.ShouldBeTrue("有失败项才提供重试");

    // 只重试失败项：这次只发 GIF 集，且成功后不再提供重试
    retry.EmitSignal(BaseButton.SignalName.Pressed);
    (await PumpPublishAsync(panel, 2)).ShouldBeTrue();
    await SettleAsync(panel);

    _sent.Count.ShouldBe(2);
    SentKind(_sent[1]).ShouldBe("GifSet");
    FindDialog(panel).DialogText.ShouldContain("成功 1 项");
    FindDialog(panel).DialogText.ShouldNotContain("未发送");
    FindRetryButton(panel).Visible.ShouldBeFalse();
  }

  [Test]
  public async Task ColumnPublish_SendsOnlyThatColumnItem()
  {
    CreateDataManager(new[] { CreateBoss() });
    ImportGifSet();
    EnableGroup("10001");
    _dm.InfoConfig.ActivityRule.Value = "活动规则正文";

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    var column = (GifSetPanel)panel.ColumnPanels[0]!;
    column.PublishButton.Disabled.ShouldBeFalse();
    FindButton(column, "PublishButton").EmitSignal(BaseButton.SignalName.Pressed);

    (await PumpPublishAsync(panel, 1)).ShouldBeTrue();
    await SettleAsync(panel);

    _sent.Count.ShouldBe(1, "单栏发布不得连带其它栏");
    SentKind(_sent[0]).ShouldBe("GifSet");
    FindDialog(panel).DialogText.ShouldContain("共 1 项，成功 1 项");
  }

  [Test]
  public async Task RemainingColumns_PublishTheirOwnItem()
  {
    CreateDataManager(new[] { CreateBoss() });
    ImportGifSet();
    EnableGroup("10001");
    _dm.InfoConfig.ActivityRule.Value = "活动规则正文";

    var panel = InstantiatePanel();
    await SettleAsync(panel);

    // 创作者剩余表栏
    var remaining = (CreatorRemainingPanel)panel.ColumnPanels[2]!;
    remaining.PublishButton.Disabled.ShouldBeFalse();
    FindButton(remaining, "PublishButton").EmitSignal(BaseButton.SignalName.Pressed);
    (await PumpPublishAsync(panel, 1)).ShouldBeTrue();
    await SettleAsync(panel);

    SentKind(_sent[0]).ShouldBe("CreatorRemainingTable");

    // 活动规则栏
    var rule = (ActivityRulePanel)panel.ColumnPanels[3]!;
    rule.PublishButton.Disabled.ShouldBeFalse();
    FindButton(rule, "PublishButton").EmitSignal(BaseButton.SignalName.Pressed);
    (await PumpPublishAsync(panel, 2)).ShouldBeTrue();
    await SettleAsync(panel);

    _sent.Select(SentKind).ShouldBe(new[] { "CreatorRemainingTable", "ActivityRule" });
    FindDialog(panel).DialogText.ShouldContain("共 1 项，成功 1 项");
  }

  [Test]
  public async Task DispatchInfoEvent_IgnoresMalformedAndUnknownPayload()
  {
    CreateDataManager(new[] { CreateBoss() });
    var panel = InstantiatePanel();
    await SettleAsync(panel);

    // 畸形 payload 与未知事件名都不得抛出、也不得弹报告
    panel.DispatchInfoEvent(InfoProtocol.PublishResultEvent, "{ not json");
    panel.DispatchInfoEvent("info_unknown_event", "{}");
    await SettleAsync(panel);

    FindDialogOrNull(panel).ShouldBeNull();
    _sent.ShouldBeEmpty();
  }
}
