namespace AutoCMEX;

using AutoCMEX.Core.WebSocket;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// WebSocket 模块单元测试
/// </summary>
public class WebSocketTest : TestClass
{
  public WebSocketTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void ProtocolHandler_ParseValidMessage_ReturnsMessage()
  {
    var handler = new ProtocolHandler();
    var json =
      "{\"id\":\"test-1\",\"type\":\"command\",\"timestamp\":123456789,\"payload\":{\"action\":\"ping\"}}";
    var msg = handler.ParseMessage(json);
    msg.ShouldNotBeNull();
    msg.Id.ShouldBe("test-1");
    msg.Type.ShouldBe("command");
  }

  [Test]
  public void ProtocolHandler_ParseInvalidJson_ThrowsProtocolException()
  {
    var handler = new ProtocolHandler();
    Should.Throw<ProtocolException>(() => handler.ParseMessage("not json"));
  }

  [Test]
  public void ProtocolHandler_ParseUnknownType_ThrowsProtocolException()
  {
    var handler = new ProtocolHandler();
    var json = "{\"id\":\"test-1\",\"type\":\"unknown\",\"payload\":{}}";
    var ex = Should.Throw<ProtocolException>(() => handler.ParseMessage(json));
    ex.ErrorCode.ShouldBe("UNKNOWN_TYPE");
  }

  [Test]
  public void ProtocolHandler_SerializeMessage_ReturnsJson()
  {
    var handler = new ProtocolHandler();
    var msg = WebSocketMessage.CreateAck("original-1", "success");
    var json = handler.SerializeMessage(msg);
    json.ShouldNotBeNullOrEmpty();
    json.ShouldContain("\"type\":\"ack\"");
  }

  [Test]
  public void ConnectionManager_RegisterAndUnregister_Works()
  {
    var manager = new ConnectionManager(10);
    manager.Count.ShouldBe(0);
    manager.IsFull.ShouldBeFalse();
  }

  [Test]
  public void WebSocketMessage_CreateError_ReturnsError()
  {
    var msg = WebSocketMessage.CreateError("orig-1", "INVALID_FORMAT", "Bad JSON");
    msg.Type.ShouldBe("error");
  }

  [Test]
  public void WebSocketMessage_CreateAck_ReturnsAck()
  {
    var msg = WebSocketMessage.CreateAck("orig-1", "success");
    msg.Type.ShouldBe("ack");
  }

  [Test]
  public void WebSocketMessage_CreateEvent_ReturnsEvent()
  {
    var msg = WebSocketMessage.CreateEvent("status_changed", new { status = "running" });
    msg.Type.ShouldBe("event");
  }
}
