namespace AutoCMEX;

using System;
using System.IO;
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
/// 群目录服务单测：群列表查询的发送前置、回执解析（两种 payload 形状）、去重合并与
/// "刷新不得改动用户勾选"的约束。
/// </summary>
public class GroupDirectoryServiceTest : TestClass
{
  private string _dataDir = string.Empty;
  private DataManager _dm = default!;
  private Mock<IWebSocketServer> _server = default!;
  private GroupDirectoryService _service = default!;

  public GroupDirectoryServiceTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dataDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_GroupDir_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_dataDir);

    _dm = new DataManager(_dataDir, new AesEncryptor("test-key"));
    _dm.LoadAll();

    _server = new Mock<IWebSocketServer>();
    _server.SetupGet(s => s.ConnectionCount).Returns(1);

    _service = new GroupDirectoryService(_dm, _server.Object, new Mock<ILog>().Object);
  }

  [Cleanup]
  public void Cleanup()
  {
    _dm?.Dispose();

    if (Directory.Exists(_dataDir))
      Directory.Delete(_dataDir, true);
  }

  private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

  [Test]
  public async Task RequestAsync_WithoutConnection_ReturnsFalseAndSendsNothing()
  {
    _server.SetupGet(s => s.ConnectionCount).Returns(0);

    // 无连接时必须明确回报"没发出去"，而不是静默丢弃
    (await _service.RequestAsync()).ShouldBeFalse();
    _server.Verify(s => s.BroadcastAsync(It.IsAny<WebSocketMessage>()), Times.Never);
  }

  [Test]
  public async Task RequestAsync_WithConnection_SendsGroupListQueryEvent()
  {
    (await _service.RequestAsync()).ShouldBeTrue();

    _server.Verify(
      s =>
        s.BroadcastAsync(
          It.Is<WebSocketMessage>(m =>
            m.Type == "event"
            && m.Payload.GetProperty("event").GetString() == InfoProtocol.QueryGroupListEvent
          )
        ),
      Times.Once
    );
  }

  [Test]
  public void HandleGroupListResult_AddsGroupsUncheckedWithDisplayName()
  {
    var added = _service.HandleGroupListResult(
      Parse(
        """
        { "event": "info_group_list_result",
          "data": { "groups": [
            { "channelId": "10001", "guildId": "g1", "name": "示例群" },
            { "channelId": "10002" }
          ] } }
        """
      )
    );

    added.ShouldBe(2);
    var targets = _dm.Settings.TargetGroups;
    targets.Count.ShouldBe(2);

    targets[0].ChannelId.Value.ShouldBe("10001");
    targets[0].GuildId.Value.ShouldBe("g1");
    targets[0].DisplayName.Value.ShouldBe("示例群");
    // 刷新一次群列表不得等于群发内容
    targets[0].Enabled.Value.ShouldBeFalse();

    // 无名群回落到 channelId，界面上不会出现空白项
    targets[1].DisplayName.Value.ShouldBe("10002");
    targets[1].GuildId.Value.ShouldBe(string.Empty);
    targets[1].Enabled.Value.ShouldBeFalse();
  }

  [Test]
  public void HandleGroupListResult_MalformedPayload_ReturnsMinusOne()
  {
    _service.HandleGroupListResult(Parse("""{ "event": "info_group_list_result" }""")).ShouldBe(-1);
    _service.HandleGroupListResult(Parse("""{ "data": "not-an-array" }""")).ShouldBe(-1);

    _dm.Settings.TargetGroups.ShouldBeEmpty();
  }

  [Test]
  public void ApplyGroupList_DeduplicatesAndRenamesWithoutTouchingCheckedState()
  {
    _dm.Settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = "10001" },
        GuildId = { Value = "g1" },
        DisplayName = { Value = "旧群名" },
        Enabled = { Value = true },
      }
    );

    var added = _service.ApplyGroupList(
      new[]
      {
        new GroupListEntry("10001", "g2", "新群名"),
        new GroupListEntry("10002", "g1", "新增群"),
      }
    );

    added.ShouldBe(1);
    var targets = _dm.Settings.TargetGroups;
    targets.Count.ShouldBe(2);

    // 已存在的只更新展示信息，勾选状态保持用户设置
    targets[0].DisplayName.Value.ShouldBe("新群名");
    targets[0].GuildId.Value.ShouldBe("g2");
    targets[0].Enabled.Value.ShouldBeTrue();

    targets[1].ChannelId.Value.ShouldBe("10002");
    targets[1].Enabled.Value.ShouldBeFalse();
  }

  [Test]
  public void ApplyGroupList_SkipsBlankChannelIdAndNullEntries()
  {
    var added = _service.ApplyGroupList(
      new[]
      {
        new GroupListEntry("   ", string.Empty, "脏数据"),
        new GroupListEntry(string.Empty, string.Empty, string.Empty),
        new GroupListEntry("10001", string.Empty, "有效群"),
      }
    );

    added.ShouldBe(1);
    _dm.Settings.TargetGroups.Count.ShouldBe(1);
    _dm.Settings.TargetGroups[0].ChannelId.Value.ShouldBe("10001");

    Should.Throw<ArgumentNullException>(() => _service.ApplyGroupList(null!));
  }

  [Test]
  public void TryParseGroups_AcceptsBothArrayShapes()
  {
    var flat = GroupDirectoryService.TryParseGroups(
      Parse("""{ "data": [ { "channelId": "10001", "name": "平铺形状" } ] }"""),
      out var flatGroups
    );

    flat.ShouldBeTrue();
    flatGroups!.Count.ShouldBe(1);
    flatGroups[0].Name.ShouldBe("平铺形状");

    var nested = GroupDirectoryService.TryParseGroups(
      Parse("""{ "data": { "groups": [ { "channelId": "10002" } ] } }"""),
      out var nestedGroups
    );

    nested.ShouldBeTrue();
    nestedGroups![0].ChannelId.ShouldBe("10002");
  }

  [Test]
  public void TryParseGroups_RejectsUnexpectedShapes()
  {
    GroupDirectoryService.TryParseGroups(Parse("""[1, 2]"""), out var fromArray).ShouldBeFalse();
    fromArray.ShouldBeNull();

    GroupDirectoryService.TryParseGroups(Parse("""{ "data": null }"""), out _).ShouldBeFalse();
    GroupDirectoryService
      .TryParseGroups(Parse("""{ "data": { "groups": 5 } }"""), out _)
      .ShouldBeFalse();
    GroupDirectoryService.TryParseGroups(Parse("{}"), out _).ShouldBeFalse();

    // 形状合法但条目全不可用：解析成功、合并为空
    GroupDirectoryService
      .TryParseGroups(Parse("""{ "data": [ { "name": "无 ID" }, 7 ] }"""), out var empty)
      .ShouldBeTrue();
    empty.ShouldBeEmpty();
  }
}
