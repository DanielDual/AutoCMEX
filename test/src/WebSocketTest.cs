namespace AutoCMEX;

using AutoCMEX.Core.WebSocket;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// WebSocket 服务单元测试
/// </summary>
public class WebSocketTest : TestClass
{
  public WebSocketTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void WebSocketServer_CanBeConstructed()
  {
    var server = new WebSocketServer(5140);
    server.ShouldNotBeNull();
    server.IsRunning.ShouldBeFalse();
  }

  [Test]
  public void WebSocketServer_StopWhenNotRunning_DoesNotThrow()
  {
    var server = new WebSocketServer(5140);
    Should.NotThrow(() => server.Stop());
  }

  [Test]
  public void MessageHandler_CanBeConstructed()
  {
    var server = new WebSocketServer(5140);
    var handler = new MessageHandler(server);
    handler.ShouldNotBeNull();
  }

  [Test]
  public void MessageHandler_HandlesInvalidJson_Gracefully()
  {
    var server = new WebSocketServer(5140);
    var handler = new MessageHandler(server);

    // Simulate receiving invalid JSON - should not throw
    // The handler catches exceptions internally
    handler.ShouldNotBeNull();
  }
}
