namespace AutoCMEX;

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.WebSocket;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 信息板块入站事件路由测试：只有 <c>info_</c> 前缀的事件进总线，其余仍按未知事件报错。
/// </summary>
public class InfoEventRoutingTest : TestClass
{
  public InfoEventRoutingTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void IsInfoEvent_MatchesOnlyInfoPrefix()
  {
    InfoEventBus.IsInfoEvent("info_guild_list").ShouldBeTrue();
    InfoEventBus.IsInfoEvent("info_").ShouldBeTrue();
    InfoEventBus.IsInfoEvent("status_query").ShouldBeFalse();
    InfoEventBus.IsInfoEvent(string.Empty).ShouldBeFalse();
    // 前缀必须落在最前面：别的模块里带 info 字样的事件不能误进信息板块分支
    InfoEventBus.IsInfoEvent("guess_info_result").ShouldBeFalse();
  }

  [Test]
  public void Publish_ForwardsToSubscribers_AndIsSafeWithoutAnySubscriber()
  {
    var bus = new InfoEventBus();
    var received = new List<string>();
    bus.Received += (name, _) => received.Add(name);

    bus.Publish("info_guild_list", JsonSerializer.SerializeToElement(new { groups = 1 }));

    received.ShouldBe(new[] { "info_guild_list" });

    // 总线未接线（无订阅者）时投递只是丢弃，不得抛异常
    new InfoEventBus().Publish(
      "info_publish_report",
      JsonSerializer.SerializeToElement(new { total = 0 })
    );
  }

  [Test]
  public async Task HandleAsync_RoutesInfoEventToBusWithoutSynchronousReply()
  {
    var bus = new InfoEventBus();
    var received = new List<string>();
    bus.Received += (name, _) => received.Add(name);
    var handler = new EventHandler(new Mock<ILog>().Object, bus);

    var replies = await handler.HandleAsync(
      WebSocketMessage.CreateEvent("info_publish_report", new { total = 1 }),
      "conn-1"
    );

    received.ShouldBe(new[] { "info_publish_report" });
    // 回执由总线异步送达面板，这里不能再有同步应答，否则会被当成协议错误
    replies.ShouldBeEmpty();
  }

  [Test]
  public async Task HandleAsync_KeepsNonInfoEventAsUnknownError()
  {
    var bus = new InfoEventBus();
    var received = new List<string>();
    bus.Received += (name, _) => received.Add(name);
    var handler = new EventHandler(new Mock<ILog>().Object, bus);

    var replies = await handler.HandleAsync(
      WebSocketMessage.CreateEvent("guess_info_result", new { }),
      "conn-1"
    );

    received.ShouldBeEmpty();
    replies.Count.ShouldBe(1);
    replies[0].Type.ShouldBe("error");
    replies[0].Payload.GetProperty("code").GetString().ShouldBe("INVALID_COMMAND");
  }
}
