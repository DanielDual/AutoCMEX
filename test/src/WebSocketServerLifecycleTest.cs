namespace AutoCMEX.Core.WebSocket;

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// <see cref="WebSocketServer"/> 的启停测试：监听器必须随停止真正释放，启动/停止幂等。
/// </summary>
/// <remarks>
/// 真机上端口变更后旧监听器仍占着端口，会让新端口「启动失败」或让重启后的实例对着空气发消息；
/// 这里用「同一端口能否被再次监听」作为可观测判据。
/// </remarks>
public class WebSocketServerLifecycleTest : TestClass
{
  public WebSocketServerLifecycleTest(Node testScene)
    : base(testScene) { }

  [Test]
  public async Task StartStop_ReleasesPort_SoItCanBeListenedAgain()
  {
    // Arrange
    var port = FindFreePort();
    using var first = CreateServer(port);

    // Act
    await first.StartAsync();
    first.IsRunning.ShouldBeTrue();
    first.IsActive.ShouldBeTrue();

    await first.StopAsync();
    first.IsRunning.ShouldBeFalse();
    first.IsActive.ShouldBeFalse();

    // Assert：端口已释放，另一个实例能立刻在同一端口监听
    using var second = CreateServer(port);
    await second.StartAsync();
    second.IsRunning.ShouldBeTrue();
    second.LastError.ShouldBeEmpty();
  }

  [Test]
  public async Task StartAsync_CalledTwice_KeepsSingleListener()
  {
    // Arrange
    var port = FindFreePort();
    using var server = CreateServer(port);

    // Act：连点两次启动
    await server.StartAsync();
    await server.StartAsync();

    // Assert：仍是同一个监听器（能正常停止并释放端口，未因重复创建而泄漏）
    server.IsRunning.ShouldBeTrue();
    await server.StopAsync();
    server.IsRunning.ShouldBeFalse();

    using var probe = CreateServer(port);
    await probe.StartAsync();
    probe.IsRunning.ShouldBeTrue();
  }

  [Test]
  public async Task StartAsync_PortAlreadyInUse_ReportsReasonWithoutRunning()
  {
    // Arrange：先占住端口
    var port = FindFreePort();
    using var holder = CreateServer(port);
    await holder.StartAsync();
    holder.IsRunning.ShouldBeTrue();

    // Act
    using var contender = CreateServer(port);
    await contender.StartAsync();

    // Assert：失败要落在 LastError（面板据此显示原因），而不是只留一个「已停止」
    contender.IsRunning.ShouldBeFalse();
    contender.IsActive.ShouldBeFalse();
    contender.LastError.ShouldNotBeEmpty();
  }

  [Test]
  public async Task StopAsync_WhenNeverStarted_IsNoOp()
  {
    // Arrange
    using var server = CreateServer(FindFreePort());

    // Act & Assert：未启动就停止不应抛异常，也不应变成「在运行」
    await Should.NotThrowAsync(server.StopAsync);
    server.IsRunning.ShouldBeFalse();
    server.IsActive.ShouldBeFalse();
  }

  [Test]
  public async Task StartAndStop_AfterDispose_AreNoOps()
  {
    // Arrange
    var server = CreateServer(FindFreePort());
    server.Dispose();

    // Act & Assert：已释放的实例不再起监听，停止也不抛
    await server.StartAsync();
    server.IsRunning.ShouldBeFalse();
    server.IsActive.ShouldBeFalse();
    await Should.NotThrowAsync(server.StopAsync);
  }

  private static WebSocketServer CreateServer(int port) =>
    new(
      port,
      new ConnectionManager(),
      new ProtocolHandler(),
      new MessageRouter(new Mock<ILog>().Object),
      new HeartbeatService(30000, 60000, new Mock<ILog>().Object),
      enableAuth: false,
      authToken: string.Empty,
      log: new Mock<ILog>().Object
    );

  private static int FindFreePort()
  {
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
  }
}
