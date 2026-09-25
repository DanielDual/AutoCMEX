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
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 发布编排单测：发送前预检、固定顺序串行、失败即停与失败明细可定位、只重试失败项。
/// </summary>
/// <remarks>
/// 出图与 WebSocket 都是注入的假实现：本层只验证编排与预检逻辑，真实出图由面板级测试覆盖。
/// </remarks>
public class PublishServiceTest : TestClass
{
  private static readonly string[] Targets = { "10001", "10002" };

  private string _root = string.Empty;
  private string _dataDir = string.Empty;
  private string _workDir = string.Empty;
  private DataManager _dataManager = default!;
  private InfoDataService _dataService = default!;
  private GifSetService _gifSets = default!;
  private Node _host = default!;
  private Mock<IWebSocketServer> _server = default!;
  private FakeTableImageFactory _tableImages = default!;
  private PublishService _service = default!;

  private readonly List<SentForward> _sent = new();
  private Func<JsonElement, string?>? _ackBuilder;
  private int _connectionCount = 1;

  public PublishServiceTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _root = Path.Combine(Path.GetTempPath(), "AutoCMEX_Test_" + Guid.NewGuid().ToString("N")[..8]);
    _dataDir = Path.Combine(_root, "data");
    _workDir = Path.Combine(_root, "work");
    Directory.CreateDirectory(_dataDir);
    Directory.CreateDirectory(_workDir);

    _dataManager = new DataManager(
      _dataDir,
      new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_dataDir)),
      new Mock<ILog>().Object
    );
    _dataService = new InfoDataService(_dataManager, new Mock<ILog>().Object);
    _gifSets = new GifSetService(_dataManager, _dataDir, new Mock<ILog>().Object);

    _host = new Node { Name = "PublishTestHost" };
    TestScene.AddChild(_host);

    _sent.Clear();
    _ackBuilder = null;
    _connectionCount = 1;

    _server = new Mock<IWebSocketServer>();
    _server.SetupGet(server => server.ConnectionCount).Returns(() => _connectionCount);
    _server
      .Setup(server => server.BroadcastAsync(It.IsAny<WebSocketMessage>()))
      .Callback((WebSocketMessage message) => OnBroadcast(message))
      .Returns(Task.CompletedTask);

    _tableImages = new FakeTableImageFactory(Path.Combine(_root, "images"));
    _service = new PublishService(
      _dataManager,
      _dataService,
      _gifSets,
      _server.Object,
      _tableImages,
      new Mock<ILog>().Object
    );
  }

  [Cleanup]
  public void Cleanup()
  {
    if (_host is not null && !_host.IsQueuedForDeletion())
    {
      TestScene.RemoveChild(_host);
      _host.QueueFree();
    }

    _dataManager?.Dispose();

    if (Directory.Exists(_root))
      Directory.Delete(_root, true);
  }

  // ==================== 预检：未通过时不发出任何请求 ====================

  [Test]
  public async Task PublishAll_NoTargets_BlockedWithoutSending()
  {
    ImportGifSet();
    SetActivityRule("活动规则文本");
    AddBoss();
    AckSuccess();

    var report = await _service.PublishAllAsync(Array.Empty<string>(), _host);

    report.Items.Count.ShouldBe(1);
    report.Items[0].Kind.ShouldBe(PublishItemKind.GifSet);
    report.Items[0].ErrorMessage.ShouldContain("未选择目标群");
    report.Items[0].TargetTotal.ShouldBe(0);
    _sent.ShouldBeEmpty();
  }

  [Test]
  public async Task Publish_GifSetNotImported_Blocked()
  {
    var report = await _service.PublishAsync(PublishItemKind.GifSet, Targets, _host);

    report.Items[0].ErrorMessage.ShouldContain("尚未导入任何符卡 GIF 集");
    _sent.ShouldBeEmpty();
  }

  [Test]
  public async Task Publish_GifSetFileRemovedAfterImport_BlockedWithEntryName()
  {
    ImportGifSet();

    var set = _gifSets.GetActiveSet()!;
    File.Delete(_gifSets.GetEntryPath(set, set.Manifest.Entries[0]));

    var report = await _service.PublishAsync(PublishItemKind.GifSet, Targets, _host);

    report.Items[0].IsSuccess.ShouldBeFalse();
    report.Items[0].ErrorMessage.ShouldContain("预检失败");
    report.Items[0].ErrorMessage.ShouldContain("1.gif");
    report.Items[0].TargetTotal.ShouldBe(Targets.Length);
    report.Items[0].TargetSucceeded.ShouldBe(0);
    _sent.ShouldBeEmpty();
  }

  [Test]
  public async Task Publish_TableWithoutBoss_Blocked()
  {
    var report = await _service.PublishAsync(PublishItemKind.GuessingTable, Targets, _host);

    report.Items[0].ErrorMessage.ShouldContain("没有可用的 Boss 数据");
    _sent.ShouldBeEmpty();
  }

  [Test]
  public async Task Publish_TableWithoutRenderHost_Blocked()
  {
    AddBoss();

    var report = await _service.PublishAsync(PublishItemKind.GuessingTable, Targets, null);

    report.Items[0].ErrorMessage.ShouldContain("出图宿主节点");
    _tableImages.RenderedTitles.ShouldBeEmpty();
    _sent.ShouldBeEmpty();
  }

  [Test]
  public async Task Publish_ActivityRuleEmpty_Blocked()
  {
    var report = await _service.PublishAsync(PublishItemKind.ActivityRule, Targets, _host);

    report.Items[0].ErrorMessage.ShouldContain("活动规则为空");
    _sent.ShouldBeEmpty();
  }

  // ==================== 串行发送：固定顺序 + 逐群回执 ====================

  [Test]
  public async Task PublishAll_HappyPath_SendsFourKindsInFixedOrder()
  {
    ImportGifSet();
    SetActivityRule("活动规则文本");
    AddBoss();
    AckSuccess();

    var report = await _service.PublishAllAsync(Targets, _host);

    report.IsSuccess.ShouldBeTrue();
    report.Items.Select(item => item.Kind).ShouldBe(PublishOrder.Fixed);

    // 逐群串行：4 项 × 2 个目标群 = 8 次请求
    _sent.Count.ShouldBe(8);
    _sent
      .Select(sent => sent.Kind)
      .ShouldBe(
        new[]
        {
          "GifSet",
          "GifSet",
          "GuessingTable",
          "GuessingTable",
          "CreatorRemainingTable",
          "CreatorRemainingTable",
          "ActivityRule",
          "ActivityRule",
        }
      );

    _sent[0].ChannelId.ShouldBe("10001");
    _sent[1].ChannelId.ShouldBe("10002");
    _sent.Select(sent => sent.RequestId).Distinct().Count().ShouldBe(8);

    // GIF 集：逐条 = 序号. 符卡名 + 图片
    var gifSent = _sent[0];
    gifSent.Nodes.Count.ShouldBe(2);
    gifSent.Nodes[0].Index.ShouldBe(1);
    gifSent.Nodes[0].Title.ShouldBe("1. 非符");
    gifSent.Nodes[0].ImagePath.ShouldEndWith("1.gif");
    gifSent.Nodes[1].Title.ShouldBe("2. 符卡 A");

    // 两张表：各 1 个图片节点，标题即表格标题
    _tableImages.RenderedTitles.ShouldBe(
      new[] { TableModelBuilder.GuessingTableTitle, TableModelBuilder.CreatorRemainingTableTitle }
    );
    var tableSent = _sent.Where(sent => sent.Kind == "GuessingTable").ToArray();
    tableSent[0].Nodes.Count.ShouldBe(1);
    tableSent[0].Nodes[0].Title.ShouldBe(TableModelBuilder.GuessingTableTitle);
    tableSent[0].Nodes[0].ImagePath.ShouldEndWith(".png");

    // 活动规则：纯文本节点，无图片
    var ruleSent = _sent.First(sent => sent.Kind == "ActivityRule");
    ruleSent.Nodes[0].ImagePath.ShouldBeNull();
    ruleSent.Nodes[0].Title.ShouldBe("活动规则文本");

    report.Items.ShouldAllBe(item => item.TargetSucceeded == Targets.Length);
  }

  [Test]
  public async Task PublishAll_OnlyKinds_PublishesRequestedKindsOnly()
  {
    ImportGifSet();
    SetActivityRule("活动规则文本");
    AddBoss();
    AckSuccess();

    var report = await _service.PublishAllAsync(
      Targets,
      _host,
      new[] { PublishItemKind.ActivityRule }
    );

    report.Items.Count.ShouldBe(1);
    report.Items[0].Kind.ShouldBe(PublishItemKind.ActivityRule);
    _sent.Select(sent => sent.Kind).Distinct().ShouldBe(new[] { "ActivityRule" });
    _tableImages.RenderedTitles.ShouldBeEmpty();
  }

  // ==================== 失败路径：失败即停 + 明细可定位 ====================

  [Test]
  public async Task PublishAll_FirstItemFails_StopsSubsequentKinds()
  {
    ImportGifSet();
    SetActivityRule("活动规则文本");
    AddBoss();
    AckFailure("预检失败：1 个附件的图片不可读，未发送。", (2, "2. 符卡 A", "图片不存在或为空"));

    var report = await _service.PublishAllAsync(Targets, _host);

    report.Items.Count.ShouldBe(1);
    report.FailedKinds.ShouldBe(new[] { PublishItemKind.GifSet });
    report.IsSuccess.ShouldBeFalse();

    var item = report.Items[0];
    item.Failures.ShouldContain(failure => failure.Index == 2);
    item.Failures.First(failure => failure.Index == 2).ToDisplayText().ShouldContain("第 2 条");
    item.Failures.ShouldContain(failure => failure.Title == "目标群 10001");
    item.TargetSucceeded.ShouldBe(0);
    item.TargetTotal.ShouldBe(Targets.Length);
    report.ToDisplayText().ShouldContain("第 2 条");

    // 第一个目标群失败后不再尝试第二个群，也不再发后续项
    _sent.Count.ShouldBe(1);
  }

  [Test]
  public async Task PublishAll_AckTimeout_FailsAndStops()
  {
    _service.AckTimeoutMs = 40;
    ImportGifSet();
    SetActivityRule("活动规则文本");
    AddBoss();
    _ackBuilder = null;

    var report = await _service.PublishAllAsync(Targets, _host);

    report.Items.Count.ShouldBe(1);
    report.Items[0].IsSuccess.ShouldBeFalse();
    report.Items[0].Failures.ShouldContain(failure => failure.Reason.Contains("超时"));
    _sent.Count.ShouldBe(1);
  }

  [Test]
  public async Task PublishAll_NoConnection_FailsWithoutBroadcast()
  {
    _connectionCount = 0;
    ImportGifSet();
    AckSuccess();

    var report = await _service.PublishAllAsync(Targets, _host);

    report.Items.Count.ShouldBe(1);
    report
      .Items[0]
      .Failures.ShouldContain(failure => failure.Reason.Contains("没有可用的 Koishi 连接"));
    _sent.ShouldBeEmpty();
  }

  [Test]
  public async Task PublishAll_BroadcastThrows_ReportsVisibleFailure()
  {
    ImportGifSet();
    _server
      .Setup(server => server.BroadcastAsync(It.IsAny<WebSocketMessage>()))
      .ThrowsAsync(new IOException("socket aborted"));

    // 发送阶段抛异常不得穿过编排层（那把报告整个丢掉，用户什么也看不到）
    var report = await _service.PublishAllAsync(Targets, _host);

    report.Items.Count.ShouldBe(1);
    report.Items[0].IsSuccess.ShouldBeFalse();
    report.Items[0].TargetSucceeded.ShouldBe(0);
    report.Items[0].Failures.ShouldContain(failure => failure.Reason.Contains("socket aborted"));
  }

  [Test]
  public void HandlePublishResult_UnknownRequestId_IsIgnored()
  {
    using var document = JsonDocument.Parse("{\"requestId\":\"unknown\",\"success\":true}");

    Should.NotThrow(() => _service.HandlePublishResult(document.RootElement));
  }

  // ==================== 夹具工具 ====================

  private void OnBroadcast(WebSocketMessage message)
  {
    var data = message.Payload.GetProperty("data");
    var nodes = new List<SentNode>();

    foreach (var node in data.GetProperty("nodes").EnumerateArray())
    {
      var imagePath =
        node.TryGetProperty("imagePath", out var image) && image.ValueKind == JsonValueKind.String
          ? image.GetString()
          : null;

      nodes.Add(
        new SentNode(
          node.GetProperty("index").GetInt32(),
          node.TryGetProperty("title", out var title)
            ? title.GetString() ?? string.Empty
            : string.Empty,
          imagePath
        )
      );
    }

    _sent.Add(
      new SentForward(
        data.GetProperty("kind").GetString() ?? string.Empty,
        data.GetProperty("channelId").GetString() ?? string.Empty,
        data.GetProperty("requestId").GetString() ?? string.Empty,
        nodes
      )
    );

    var ack = _ackBuilder?.Invoke(message.Payload);
    if (ack is not null)
      _service.HandlePublishResult(JsonDocument.Parse(ack).RootElement);
  }

  private void AckSuccess() =>
    _ackBuilder = payload => $"{{\"requestId\":\"{ReadRequestId(payload)}\",\"success\":true}}";

  private void AckFailure(
    string message,
    params (int Index, string Title, string Reason)[] failedNodes
  ) =>
    _ackBuilder = payload =>
      JsonSerializer.Serialize(
        new
        {
          requestId = ReadRequestId(payload),
          success = false,
          message,
          failedNodes = failedNodes
            .Select(node => new
            {
              index = node.Index,
              title = node.Title,
              reason = node.Reason,
            })
            .ToArray(),
        }
      );

  private static string ReadRequestId(JsonElement payload) =>
    payload.GetProperty("data").GetProperty("requestId").GetString() ?? string.Empty;

  private void ImportGifSet()
  {
    var folder = Path.Combine(_workDir, "sample_exp");
    Directory.CreateDirectory(folder);

    var entries = new[]
    {
      (Index: 1, Name: "非符", FileName: "1.gif"),
      (Index: 2, Name: "符卡 A", FileName: "2.gif"),
    };
    foreach (var entry in entries)
      File.WriteAllBytes(
        Path.Combine(folder, entry.FileName),
        new byte[] { 0x47, 0x49, 0x46, 0x38 }
      );

    File.WriteAllText(
      Path.Combine(folder, GifSetManifest.FileName),
      JsonSerializer.Serialize(
        new GifSetManifest
        {
          SetName = "SamplePkg",
          BossLabel = "SampleBoss",
          GeneratedAt = "2026-09-25 12:00:00",
          Entries = entries
            .Select(entry => new GifSetEntry
            {
              Index = entry.Index,
              SpellCardName = entry.Name,
              FileName = entry.FileName,
              Width = 640,
              Height = 480,
            })
            .ToList(),
        },
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
      )
    );

    var result = _gifSets.ImportFolder(folder);
    result.IsSuccess.ShouldBeTrue(result.ErrorMessage);
  }

  private void SetActivityRule(string text) => _dataManager.InfoConfig.ActivityRule.Value = text;

  private void AddBoss()
  {
    var boss = new Boss { Name = "SampleBoss" };
    boss.SpellCards.Add(Card("非符", "Alpha", guessedOut: false));
    boss.SpellCards.Add(Card("符卡 A", "Alpha", guessedOut: true));
    boss.SpellCards.Add(Card("符卡 B", "Beta", guessedOut: false));
    _dataManager.Bosses.Add(boss);
  }

  private static SpellCard Card(string name, string creator, bool guessedOut) =>
    new()
    {
      Name = { Value = name },
      Creator = { Value = creator },
      IsGuessedOut = { Value = guessedOut },
    };

  private sealed record SentNode(int Index, string Title, string? ImagePath);

  private sealed record SentForward(
    string Kind,
    string ChannelId,
    string RequestId,
    IReadOnlyList<SentNode> Nodes
  );

  /// <summary>假出图实现：写出一个真实非空文件，用于验证「出图产物非空」预检。</summary>
  private sealed class FakeTableImageFactory : ITableImageFactory
  {
    private readonly string _outputDir;
    private int _counter;

    public FakeTableImageFactory(string outputDir)
    {
      _outputDir = outputDir;
      Directory.CreateDirectory(outputDir);
    }

    public List<string> RenderedTitles { get; } = new();

    public Task<TableImageResult> RenderAsync(TableModel model, Node host)
    {
      RenderedTitles.Add(model.Title);

      var path = Path.Combine(_outputDir, $"table_{++_counter}.png");
      File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

      return Task.FromResult(
        TableImageResult.Success(path, new Vector2I(320, 180), new FileInfo(path).Length)
      );
    }
  }
}
