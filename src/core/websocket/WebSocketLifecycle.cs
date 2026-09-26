namespace AutoCMEX.Core.WebSocket;

using System;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Models;
using Chickensoft.Log;

/// <summary>
/// WebSocket 实例的生命周期收敛点：实例的创建、启动、停止与「配置变更后的重启」都经由此处，
/// 保证同一时刻只有一个实例在跑、同一实例只有一条链路。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是两个真实故障：① 配置绑定的 <c>OnValue</c> 是「订阅即回调」，
/// 启动时三个绑定会连打三次重启；② 重启若并发执行，会各自新建实例并互相覆盖引用，
/// 先建的实例成为无人回收的孤儿并继续连接，对端「新连接踢旧连接」把它变成互踢的跷跷板（连接震颤）。
/// </para>
/// <para>
/// 因此这里做两件事：<b>串行化</b>（同一时刻只允许一次重启在跑）与 <b>校验式重启</b>
/// （实例的有效配置已满足设置就不重建、不启停）。调用方（<c>MainWindow</c>）只负责把设置递进来、
/// 把返回的实例交给面板显示。
/// </para>
/// </remarks>
public class WebSocketLifecycle
{
  private readonly Func<AppSettings, IWebSocketServer> _factory;
  private readonly ILog _log;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private IWebSocketServer? _current;
  private bool _disposed;

  /// <summary>
  /// 创建生命周期管理器。
  /// </summary>
  /// <param name="factory">按设置创建实例的工厂。</param>
  /// <param name="log">日志。</param>
  public WebSocketLifecycle(Func<AppSettings, IWebSocketServer> factory, ILog log)
  {
    _factory = factory;
    _log = log;
  }

  /// <summary>当前实例；尚未创建时为 <c>null</c>。</summary>
  public IWebSocketServer? Current => _current;

  /// <summary>
  /// 按设置创建实例并记为当前实例（**不启动**）。
  /// </summary>
  /// <param name="settings">应用设置。</param>
  /// <returns>新实例。</returns>
  /// <remarks>
  /// 供启动流程在 UI 就绪前先拿到实例（<c>IProvide&lt;IWebSocketServer&gt;</c> 要求同步可用），
  /// 启动时机仍由调用方决定。
  /// </remarks>
  public IWebSocketServer Create(AppSettings settings)
  {
    var server = _factory(settings);
    _current = server;
    return server;
  }

  /// <summary>
  /// 启动当前实例；尚未创建时先按设置创建。
  /// </summary>
  /// <param name="settings">应用设置。</param>
  public async Task StartAsync(AppSettings settings)
  {
    await _gate.WaitAsync();
    try
    {
      if (_disposed)
        return;

      var server = _current ?? Create(settings);
      await server.StartAsync();
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// 按新设置重启：设置与当前实例的有效配置一致时**不重建、不启停**，直接返回当前实例；
  /// 不一致时执行「停止旧实例 → 重建 → 启动新实例 → 释放旧实例」；尚无实例时创建并启动。
  /// </summary>
  /// <param name="settings">应用设置。</param>
  /// <returns>重启后应对外展示的实例。</returns>
  public async Task<IWebSocketServer> RestartAsync(AppSettings settings)
  {
    await _gate.WaitAsync();
    try
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(WebSocketLifecycle));

      var previous = _current;
      if (previous is not null && MatchesSettings(previous, settings))
        return previous;

      if (previous is not null)
      {
        await previous.StopAsync();
      }

      var server = Create(settings);

      if (previous is not null)
      {
        // 旧实例已停止：释放它的监听器/令牌源等资源，否则每次重启都留一份（孤儿实例）
        (previous as IDisposable)?.Dispose();
        _log.Print("WebSocketLifecycle: previous instance stopped and disposed.");
      }

      await server.StartAsync();
      _log.Print("WebSocketLifecycle: instance rebuilt for changed settings.");
      return server;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// 停止当前实例（保留引用，之后仍可再次 <see cref="StartAsync"/>）。
  /// </summary>
  public async Task StopAsync()
  {
    await _gate.WaitAsync();
    try
    {
      if (_current is null || _disposed)
        return;

      await _current.StopAsync();
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// 启停当前实例：它在工作（<see cref="IWebSocketServer.IsActive"/>）则停止，否则启动。
  /// </summary>
  /// <remarks>
  /// 方向在锁内按**当前实例**判定，而不是按界面上的显示状态。界面（状态面板）拿的是上次推送的实例引用，
  /// 配置变更重启期间它可能已指向被替换掉的旧实例；若由界面自行判断方向并对自己的引用下手，
  /// 就会出现「旧实例被重新启动 + 新实例同时在跑」的多实例场景——正是连接震颤的成因。
  /// 启停与重启共用这把锁，因此点击要么完整地发生在重启之前，要么作用在重启后的实例上。
  /// </remarks>
  public async Task ToggleAsync()
  {
    await _gate.WaitAsync();
    try
    {
      if (_current is null || _disposed)
        return;

      if (_current.IsActive)
        await _current.StopAsync();
      else
        await _current.StartAsync();
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// 判断实例的有效配置是否已满足设置：比较「模式 + 该模式对应的端口 / 地址」。
  /// </summary>
  /// <param name="server">已存在的实例。</param>
  /// <param name="settings">应用设置。</param>
  /// <returns>一致返回 <c>true</c>（调用方应跳过重建）。</returns>
  /// <remarks>
  /// 只比配置，**不把运行时状态（是否已连接、最近错误、连接数）纳入比较**：
  /// 否则会出现「改了设置却判定相同 → 不重启 → 设置不生效」。
  /// </remarks>
  public static bool MatchesSettings(IWebSocketServer server, AppSettings settings)
  {
    var desiredClient = string.Equals(
      settings.WebSocketMode.Value,
      "Client",
      StringComparison.OrdinalIgnoreCase
    );

    if (desiredClient)
    {
      // Client 的地址可能带 Token，直接与实例实际使用的地址比较
      return string.Equals(server.Mode, "Client", StringComparison.OrdinalIgnoreCase)
        && string.Equals(
          server.Url,
          WebSocketClient.BuildClientUrl(settings),
          StringComparison.Ordinal
        );
    }

    return string.Equals(server.Mode, "Server", StringComparison.OrdinalIgnoreCase)
      && server.Port == settings.WebSocketPort.Value;
  }

  /// <summary>
  /// 释放当前实例与本管理器。
  /// </summary>
  /// <remarks>
  /// 不释放内部的信号量：退出时可能仍有重启在途，释放它会让对方抛
  /// <see cref="ObjectDisposedException"/>，而信号量本身无需如此收尾。
  /// </remarks>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    (_current as IDisposable)?.Dispose();
    _current = null;
    GC.SuppressFinalize(this);
  }
}
