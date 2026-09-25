namespace AutoCMEX.Test.Drivers;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AutoCMEX.Core.Recording;

/// <summary>
/// 假引擎进程：在不真启游戏的前提下驱动 <c>GameProcessRunner</c> 的各分支。
/// </summary>
/// <remarks>
/// 真启游戏需要引擎与 ffmpeg、且同一引擎目录不可并发，故所有涉及进程的测试
/// 一律注入本替身：可切换「立即退出 / 永不退出」、自定退出码、在启动瞬间写出结果
/// 文件（模拟游戏运行期落盘），并记录启动信息与被杀次数供断言。
/// </remarks>
public sealed class FakeEngineProcess : IEngineProcess
{
  private readonly TaskCompletionSource _exit = new(
    TaskCreationOptions.RunContinuationsAsynchronously
  );

  /// <summary>是否立即退出；置 <c>false</c> 表示永不退出（用于验证超时杀进程）。</summary>
  public bool ExitsImmediately { get; set; } = true;

  /// <summary>退出码。</summary>
  public int ExitCodeValue { get; set; }

  /// <summary>启动信息，由进程工厂回填，供断言参数串与工作目录。</summary>
  public ProcessStartInfo? StartInfo { get; set; }

  /// <summary>启动瞬间的回调，用于模拟「游戏在运行中写出结果文件」。</summary>
  public Action? OnStart { get; set; }

  /// <summary>标准输出文本（默认空）。</summary>
  public string StandardOutputText { get; set; } = string.Empty;

  /// <summary>标准错误文本（默认空）。</summary>
  public string StandardErrorText { get; set; } = string.Empty;

  /// <summary>被杀次数。</summary>
  public int KillCount { get; private set; }

  /// <inheritdoc />
  public int Id => 4242;

  /// <inheritdoc />
  public bool HasExited { get; private set; }

  /// <inheritdoc />
  public int ExitCode => ExitCodeValue;

  /// <inheritdoc />
  public bool Start()
  {
    OnStart?.Invoke();
    if (ExitsImmediately)
    {
      HasExited = true;
      _exit.TrySetResult();
    }

    return true;
  }

  /// <inheritdoc />
  public Task<string> ReadStandardOutputAsync() => Task.FromResult(StandardOutputText);

  /// <inheritdoc />
  public Task<string> ReadStandardErrorAsync() => Task.FromResult(StandardErrorText);

  /// <inheritdoc />
  public Task WaitForExitAsync(CancellationToken cancellationToken)
  {
    if (ExitsImmediately)
    {
      return Task.CompletedTask;
    }

    // 永不退出的进程只在被取消（或被杀）时结束等待
    return _exit.Task.WaitAsync(cancellationToken);
  }

  /// <inheritdoc />
  public void KillTree()
  {
    KillCount++;
    HasExited = true;
    _exit.TrySetResult();
  }

  /// <inheritdoc />
  public void Dispose()
  {
    // 假进程无需释放。
  }
}
