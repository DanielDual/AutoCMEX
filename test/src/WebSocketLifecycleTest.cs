namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// <see cref="WebSocketLifecycle"/> 的测试：串行化重启与「配置未变不重建」的校验式重启。
/// </summary>
/// <remarks>
/// 这些用例守的是真机上出现过的连接震颤：配置绑定的 OnValue 是「订阅即回调」，
/// 启动时三个绑定连打三次重启；只要有一次重启留下仍在连接的旧实例，对端「新连接踢旧连接」
/// 就会把它变成互踢的跷跷板。
/// </remarks>
public class WebSocketLifecycleTest : TestClass
{
  public WebSocketLifecycleTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void Create_DoesNotStartInstance()
  {
    // Arrange
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);

    // Act
    var server = (FakeWebSocketServer)lifecycle.Create(BuildSettings(mode: "Server", port: 5140));

    // Assert：启动时机由调用方决定（UI 就绪后才启动），创建不能顺带启动
    factory.Created.Count.ShouldBe(1);
    server.StartCount.ShouldBe(0);
    lifecycle.Current.ShouldBeSameAs(server);
  }

  [Test]
  public async Task RestartAsync_ThreeSubscriptionCallbacksWithUnchangedSettings_DoesNotRebuild()
  {
    // Arrange：模拟 OnValue 订阅时连打三次重启（真机启动路径）
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    var settings = BuildSettings(mode: "Server", port: 5140);
    var first = (FakeWebSocketServer)lifecycle.Create(settings);
    await lifecycle.StartAsync(settings);

    // Act
    var a = await lifecycle.RestartAsync(settings);
    var b = await lifecycle.RestartAsync(settings);
    var c = await lifecycle.RestartAsync(settings);

    // Assert：三次都命中「配置没变」，实例不变、无启停动作、无孤儿
    a.ShouldBeSameAs(first);
    b.ShouldBeSameAs(first);
    c.ShouldBeSameAs(first);
    factory.Created.Count.ShouldBe(1);
    first.StartCount.ShouldBe(1);
    first.StopCount.ShouldBe(0);
    factory.LiveCount.ShouldBe(1);
  }

  [Test]
  public async Task RestartAsync_ChangedPort_StopsRebuildsStartsAndDisposesPrevious()
  {
    // Arrange
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    await lifecycle.StartAsync(BuildSettings(mode: "Server", port: 5140));

    // Act
    var second = (FakeWebSocketServer)
      await lifecycle.RestartAsync(BuildSettings(mode: "Server", port: 5141));

    // Assert：顺序必须是「旧停 → 新建 → 旧释放 → 新启」：旧实例在启动新实例之前就已停掉并释放，
    // 否则它会变成仍在连接却无人引用的孤儿（震颤的根因）
    factory.Events.ShouldBe(
      new[]
      {
        "create:5140",
        "start:5140",
        "stop:5140",
        "create:5141",
        "dispose:5140",
        "start:5141",
      }
    );
    factory.LiveCount.ShouldBe(1);
    second.IsActive.ShouldBeTrue();
    lifecycle.Current.ShouldBeSameAs(second);
  }

  [Test]
  public async Task RestartAsync_ConcurrentRequests_SerializeAndLeaveExactlyOneLiveInstance()
  {
    // Arrange
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    await lifecycle.StartAsync(BuildSettings(mode: "Server", port: 5140));

    // Act：并发请求（用户快速改动端口时的形态）
    var tasks = new List<Task<IWebSocketServer>>();
    for (var port = 5141; port <= 5144; port++)
    {
      tasks.Add(lifecycle.RestartAsync(BuildSettings(mode: "Server", port: port)));
    }

    await Task.WhenAll(tasks);

    // Assert：全部串行完成，最终只留一个活实例，且没有任何实例被启动两次
    factory.Created.Count.ShouldBe(5);
    factory.LiveCount.ShouldBe(1);
    factory.Created.ShouldAllBe(server => server.StartCount <= 1);
    ((FakeWebSocketServer)lifecycle.Current!).Disposed.ShouldBeFalse();
    factory.Created.Count(server => !server.Disposed).ShouldBe(1);
  }

  [Test]
  public async Task StopAsync_KeepsInstanceForLaterStart()
  {
    // Arrange
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    var settings = BuildSettings(mode: "Server", port: 5140);
    var server = (FakeWebSocketServer)lifecycle.Create(settings);
    await lifecycle.StartAsync(settings);

    // Act
    await lifecycle.StopAsync();

    // Assert：停止不销毁实例，之后仍能再次启动同实例
    server.StopCount.ShouldBe(1);
    lifecycle.Current.ShouldBeSameAs(server);
    factory.LiveCount.ShouldBe(1);

    await lifecycle.StartAsync(settings);
    server.StartCount.ShouldBe(2);
  }

  [Test]
  public void MatchesSettings_ServerMode_ComparesPortOnly()
  {
    // Arrange
    var settings = BuildSettings(mode: "Server", port: 5141);
    var matching = new FakeWebSocketServer(5141, new RecordingFactory()) { Mode = "Server" };
    var otherPort = new FakeWebSocketServer(5140, new RecordingFactory()) { Mode = "Server" };

    // Act & Assert
    WebSocketLifecycle.MatchesSettings(matching, settings).ShouldBeTrue();
    WebSocketLifecycle.MatchesSettings(otherPort, settings).ShouldBeFalse();
  }

  [Test]
  public void MatchesSettings_ClientMode_ComparesUrlIncludingToken()
  {
    // Arrange
    var settings = BuildSettings(mode: "Client", port: 5140);
    settings.KoishiWebSocketUrl.Value = "ws://127.0.0.1:5141";
    var url = WebSocketClient.BuildClientUrl(settings);
    var matching = new FakeWebSocketServer(0, new RecordingFactory())
    {
      Mode = "Client",
      Url = url,
    };

    // Act & Assert：同地址同 Token → 一致
    WebSocketLifecycle.MatchesSettings(matching, settings).ShouldBeTrue();

    // Token 变化会改变实际连接地址，必须判定为不一致（否则改 Token 不生效）
    settings.WebSocketEnableAuth.Value = true;
    settings.WebSocketAuthToken.Value = "token-1";
    WebSocketLifecycle
      .MatchesSettings(matching, settings)
      .ShouldBeFalse(WebSocketClient.BuildClientUrl(settings));
  }

  [Test]
  public void MatchesSettings_ModeChanged_IsNotMatching()
  {
    // Arrange：实例还在 Server 模式，设置已切到 Client
    var settings = BuildSettings(mode: "Client", port: 5140);
    settings.KoishiWebSocketUrl.Value = "ws://127.0.0.1:5141";
    var server = new FakeWebSocketServer(5140, new RecordingFactory()) { Mode = "Server" };

    // Act & Assert
    WebSocketLifecycle.MatchesSettings(server, settings).ShouldBeFalse();
  }

  [Test]
  public void Dispose_DisposesCurrentInstance()
  {
    // Arrange
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    var server = (FakeWebSocketServer)lifecycle.Create(BuildSettings(mode: "Server", port: 5140));

    // Act
    lifecycle.Dispose();

    // Assert：退出时释放实例，避免留下监听器/令牌源
    server.Disposed.ShouldBeTrue();
    factory.LiveCount.ShouldBe(0);
    lifecycle.Current.ShouldBeNull();
  }

  [Test]
  public async Task AfterDispose_RestartThrowsAndStartStopBecomeNoOps()
  {
    // Arrange
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    var settings = BuildSettings(mode: "Server", port: 5140);
    lifecycle.Create(settings);
    lifecycle.Dispose();

    // Act & Assert：退出后不再新建实例，重启明确失败而不是静默返回
    await Should.ThrowAsync<ObjectDisposedException>(() => lifecycle.RestartAsync(settings));
    await Should.NotThrowAsync(() => lifecycle.StartAsync(settings));
    await Should.NotThrowAsync(lifecycle.StopAsync);
    await Should.NotThrowAsync(lifecycle.ToggleAsync);
    factory.Created.Count.ShouldBe(1);
  }

  [Test]
  public async Task Toggle_WhenInstanceIsWorking_StopsIt()
  {
    // Arrange：实例已在工作
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    var settings = BuildSettings(mode: "Server", port: 5140);
    var server = (FakeWebSocketServer)lifecycle.Create(settings);
    await lifecycle.StartAsync(settings);

    // Act
    await lifecycle.ToggleAsync();

    // Assert：方向由控制器按当前实例判定——界面拿不到「重连等待中仍在工作」，
    // 客户端此刻 IsRunning 为 false，若按它判方向会把正在重连的客户端再启动一次（空操作，停不掉）
    server.StopCount.ShouldBe(1);
    server.StartCount.ShouldBe(1);
    server.IsActive.ShouldBeFalse();
  }

  [Test]
  public async Task Toggle_WhenInstanceIsStopped_StartsIt()
  {
    // Arrange：实例已创建但未启动
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);
    var server = (FakeWebSocketServer)lifecycle.Create(BuildSettings(mode: "Server", port: 5140));

    // Act
    await lifecycle.ToggleAsync();

    // Assert
    server.StartCount.ShouldBe(1);
    server.StopCount.ShouldBe(0);
    server.IsActive.ShouldBeTrue();
  }

  [Test]
  public async Task Toggle_WithoutInstance_DoesNotCreateOne()
  {
    // Arrange：尚未创建实例（启动流程早期的形态）
    var factory = new RecordingFactory();
    var lifecycle = new WebSocketLifecycle(factory.Create, new Mock<ILog>().Object);

    // Act & Assert：点按钮不应凭空造出一个实例
    await Should.NotThrowAsync(lifecycle.ToggleAsync);
    factory.Created.ShouldBeEmpty();
  }

  private static AppSettings BuildSettings(string mode, int port)
  {
    var settings = new AppSettings();
    settings.WebSocketMode.Value = mode;
    settings.WebSocketPort.Value = port;
    settings.KoishiWebSocketUrl.Value = "ws://127.0.0.1:5140";
    settings.WebSocketEnableAuth.Value = false;
    return settings;
  }

  /// <summary>记录创建顺序与每次启停/释放的工厂（含存活实例计数）。</summary>
  private sealed class RecordingFactory
  {
    /// <summary>按创建顺序记录的实例。</summary>
    public List<FakeWebSocketServer> Created { get; } = new();

    /// <summary>按发生顺序记录的「动作:端口」。</summary>
    public List<string> Events { get; } = new();

    /// <summary>尚未释放的实例数。</summary>
    public int LiveCount => Created.Count(server => !server.Disposed);

    /// <summary>按设置创建实例并记录。</summary>
    public IWebSocketServer Create(AppSettings settings)
    {
      var server = new FakeWebSocketServer(settings.WebSocketPort.Value, this);
      Created.Add(server);
      Events.Add($"create:{server.Port}");
      return server;
    }

    /// <summary>由假实例回调，记录动作。</summary>
    public void Record(FakeWebSocketServer server, string action) =>
      Events.Add($"{action}:{server.Port}");
  }

  /// <summary>可观测启停/释放的 WebSocket 实例替身。</summary>
  private sealed class FakeWebSocketServer : IWebSocketServer, IDisposable
  {
    private readonly RecordingFactory _factory;

    public FakeWebSocketServer(int port, RecordingFactory factory)
    {
      Port = port;
      _factory = factory;
    }

    public bool IsRunning { get; private set; }

    public bool IsActive { get; private set; }

    public bool Disposed { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int ConnectionCount => 0;

    public string Mode { get; set; } = "Server";

    public int Port { get; }

    public string Url { get; set; } = string.Empty;

    public string LastError => string.Empty;

    /// <summary>替身不模拟连接事件（接口要求存在，用例只关心启停与释放）。</summary>
    public event Action<string>? OnClientConnected
    {
      add { }
      remove { }
    }

    /// <summary>替身不模拟断开事件（接口要求存在，用例只关心启停与释放）。</summary>
    public event Action<string>? OnClientDisconnected
    {
      add { }
      remove { }
    }

    public Task StartAsync()
    {
      StartCount++;
      IsRunning = true;
      IsActive = true;
      _factory.Record(this, "start");
      return Task.CompletedTask;
    }

    public Task StopAsync()
    {
      StopCount++;
      IsRunning = false;
      IsActive = false;
      _factory.Record(this, "stop");
      return Task.CompletedTask;
    }

    public Task BroadcastAsync(WebSocketMessage message) => Task.CompletedTask;

    public void Dispose()
    {
      Disposed = true;
      _factory.Record(this, "dispose");
    }
  }
}
