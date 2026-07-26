namespace AutoCMEX.Core.Logging;

using System;
using Chickensoft.Log;

/// <summary>
/// 应用日志服务接口。提供按模块获取 <see cref="ILog"/> 的能力，
/// 以及对内存写入器、刷新与关闭的统一管理。
/// </summary>
public interface ILogService : IDisposable
{
  /// <summary>当前配置。</summary>
  LogConfig Config { get; }

  /// <summary>用于 UI 面板实时显示的内存写入器。</summary>
  InMemoryLogWriter InMemoryWriter { get; }

  /// <summary>
  /// 获取（或创建）指定模块的 <see cref="ILog"/> 实例。
  /// </summary>
  /// <param name="moduleName">模块名（一般为类名）。</param>
  ILog GetLogger(string moduleName);

  /// <summary>触发日志文件轮转检查（删除超过保留数量的旧文件）。</summary>
  void RotateIfNeeded();

  /// <summary>关闭日志服务（释放写入器、刷新缓存）。</summary>
  void Shutdown();
}
