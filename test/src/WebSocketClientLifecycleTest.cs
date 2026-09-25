namespace AutoCMEX.Core.WebSocket;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// <see cref="WebSocketClient"/> 的启停测试：一个实例在任一时刻只能有一条连接链路。
/// </summary>
/// <remarks>
/// 这些用例守的是真机上的连接震颤：客户端在「重连等待中」既不能被再次启动、也不能停不掉，
/// 否则同一实例会并行两条链路，被对端「新连接踢旧连接」变成互踢的跷跷板。
/// 这里用本机 HttpListener 如实统计连接次数与存活连接数。
/// </remarks>
public class WebSocketClientLifecycleTest : TestClass
{
  private const int ReconnectIntervalMs = 200;

  public WebSocketClientLifecycleTest(Node testScene)
    : base(testScene) { }

  [Test]
  public async Task StartAsync_CalledTwice_KeepsSingleConnection()
  {
    // Arrange：连点两次启停按钮的形态（第二次发生在第一次尚未连上时）
    var port = FindFreePort();
    using var server = new LocalWebSocketServer(port);
    server.Start();
    using var client = CreateClient(port);

    // Act
    await client.StartAsync();
    await client.StartAsync();

    // Assert：只允许一条链路
    (await WaitUntilAsync(() => client.IsRunning)).ShouldBeTrue();
    await Task.Delay(ReconnectIntervalMs * 4);
    server.AcceptedCount.ShouldBe(1);
    server.LiveCount.ShouldBe(1);
  }

  [Test]
  public async Task StartAsync_WithoutUrl_DoesNotConnectButReportsReason()
  {
    // Arrange：Client 模式但没填 Koishi 地址
    using var client = new WebSocketClient(
      string.Empty,
      new ProtocolHandler(),
      new MessageRouter(new Mock<ILog>().Object),
      ReconnectIntervalMs,
      30000,
      new Mock<ILog>().Object
    );

    // Act
    await client.StartAsync();

    // Assert：既不起链路，也不静默——失败原因留给面板显示
    client.IsActive.ShouldBeFalse();
    client.LastError.ShouldNotBeEmpty();
  }

  [Test]
  public async Task StopAsync_DuringReconnectWait_PreventsFurtherConnects()
  {
    // Arrange：服务端接受后立即关闭，逼客户端进入重连等待
    var port = FindFreePort();
    using var server = new LocalWebSocketServer(port, closeImmediatelyOnAccept: true);
    server.Start();
    using var client = CreateClient(port);

    await client.StartAsync();
    (await WaitUntilAsync(() => server.AcceptedCount >= 1)).ShouldBeTrue();

    // 客户端此刻处于「未连接但仍在重连」的状态：按钮必须还能停掉它
    (await WaitUntilAsync(() => client.IsActive && !client.IsRunning)).ShouldBeTrue();

    // Act
    await client.StopAsync();
    var acceptedAtStop = server.AcceptedCount;

    // Assert：停止后不再有任何新连接（旧实现会因 IsRunning 提前返回而继续重连）
    await Task.Delay(ReconnectIntervalMs * 8);
    server.AcceptedCount.ShouldBe(acceptedAtStop);
    client.IsActive.ShouldBeFalse();
    client.IsRunning.ShouldBeFalse();
  }

  [Test]
  public async Task StartStopInterleaved_LeavesExactlyOneLiveConnection()
  {
    // Arrange
    var port = FindFreePort();
    using var server = new LocalWebSocketServer(port);
    server.Start();
    using var client = CreateClient(port);

    // Act：启动与停止交叉调用
    var start = client.StartAsync();
    var stop = client.StopAsync();
    await Task.WhenAll(start, stop);

    // 收尾再启动一次，模拟用户随后重新连接
    await client.StartAsync();
    (await WaitUntilAsync(() => client.IsRunning)).ShouldBeTrue();
    await Task.Delay(ReconnectIntervalMs * 4);

    // Assert：无论中间怎么交叉，最终只剩一条存活连接
    server.LiveCount.ShouldBe(1);
  }

  [Test]
  public async Task IsActive_StaysTrueWhileRunning_AndFalseAfterStop()
  {
    // Arrange
    var port = FindFreePort();
    using var server = new LocalWebSocketServer(port);
    server.Start();
    using var client = CreateClient(port);
    client.IsActive.ShouldBeFalse();

    // Act
    await client.StartAsync();
    (await WaitUntilAsync(() => client.IsRunning)).ShouldBeTrue();

    // Assert：已连接时两个状态同为 true
    client.IsActive.ShouldBeTrue();

    // 停止后即使稍等也不应再活过来
    await client.StopAsync();
    await Task.Delay(ReconnectIntervalMs * 4);
    client.IsActive.ShouldBeFalse();
    client.IsRunning.ShouldBeFalse();
  }

  [Test]
  public async Task StartAsync_AfterDispose_DoesNotConnect()
  {
    // Arrange
    var client = CreateClient(FindFreePort());
    client.Dispose();

    // Act & Assert：已释放的实例不再起链路，停止也不抛
    await client.StartAsync();
    client.IsActive.ShouldBeFalse();
    await Should.NotThrowAsync(client.StopAsync);
  }

  private static WebSocketClient CreateClient(int port) =>
    new(
      $"ws://127.0.0.1:{port}",
      new ProtocolHandler(),
      new MessageRouter(new Mock<ILog>().Object),
      ReconnectIntervalMs,
      30000,
      new Mock<ILog>().Object
    );

  private static int FindFreePort()
  {
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
  }

  private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
  {
    var deadline = System.Environment.TickCount64 + timeoutMs;
    while (System.Environment.TickCount64 < deadline)
    {
      if (condition())
        return true;

      await Task.Delay(50);
    }

    return condition();
  }

  /// <summary>本机 WebSocket 服务器：统计接受过的连接数与当前存活连接数。</summary>
  private sealed class LocalWebSocketServer : IDisposable
  {
    private readonly System.Net.HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<WebSocket> _sockets = new();
    private int _acceptedCount;
    private int _liveCount;

    public LocalWebSocketServer(int port, bool closeImmediatelyOnAccept = false)
    {
      Port = port;
      CloseImmediatelyOnAccept = closeImmediatelyOnAccept;
      _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public int Port { get; }

    /// <summary>接受连接后立即正常关闭（用于逼出客户端重连等待）。</summary>
    public bool CloseImmediatelyOnAccept { get; }

    /// <summary>累计接受的连接数。</summary>
    public int AcceptedCount => Volatile.Read(ref _acceptedCount);

    /// <summary>当前存活的连接数。</summary>
    public int LiveCount => Volatile.Read(ref _liveCount);

    public void Start()
    {
      _listener.Start();
      _ = Task.Run(AcceptLoopAsync);
    }

    public void Dispose()
    {
      _cts.Cancel();

      lock (_sockets)
      {
        foreach (var socket in _sockets)
        {
          try
          {
            socket.Dispose();
          }
          catch (Exception)
          {
            // 关闭阶段的异常无需处理
          }
        }
      }

      try
      {
        _listener.Stop();
        _listener.Close();
      }
      catch (Exception)
      {
        // 监听器可能已被关闭
      }

      _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
      while (!_cts.IsCancellationRequested)
      {
        System.Net.HttpListenerContext context;
        try
        {
          context = await _listener.GetContextAsync();
        }
        catch (Exception)
        {
          break;
        }

        if (!context.Request.IsWebSocketRequest)
        {
          context.Response.StatusCode = 400;
          context.Response.Close();
          continue;
        }

        WebSocketContext socketContext;
        try
        {
          socketContext = await context.AcceptWebSocketAsync(null);
        }
        catch (Exception)
        {
          continue;
        }

        Interlocked.Increment(ref _acceptedCount);
        Interlocked.Increment(ref _liveCount);

        var socket = socketContext.WebSocket;
        lock (_sockets)
        {
          _sockets.Add(socket);
        }

        _ = Task.Run(() => DrainAsync(socket));
      }
    }

    private async Task DrainAsync(WebSocket socket)
    {
      var buffer = new byte[4096];
      try
      {
        if (CloseImmediatelyOnAccept)
        {
          await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "bye",
            CancellationToken.None
          );
          return;
        }

        while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
          var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
          if (result.MessageType == WebSocketMessageType.Close)
            break;
        }
      }
      catch (Exception)
      {
        // 连接被对端关闭或测试收尾，均属正常
      }
      finally
      {
        Interlocked.Decrement(ref _liveCount);
      }
    }
  }
}
