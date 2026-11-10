namespace AutoCMEX.UI.Info;

using System;
using AutoCMEX.Models;

/// <summary>
/// 录制过程对话框接口：供 GIF 集栏以接口类型声明 <c>[Node]</c>，并收口「跑一轮录制」的输入与回调。
/// </summary>
/// <remarks>
/// <para>
/// AutoInject 的节点适配只在 <c>GodotInterfaces</c> 内置适配器字典里按类型查表，自定义脚本类
/// 不能直接用作 <c>[Node]</c> 字段类型；面板脚本实现本接口后，节点实例本身即满足接口类型
/// （先例：<c>IGifSetPanel</c>、<c>IInfoPanel</c>）。
/// </para>
/// <para>
/// 不继承 <c>IAcceptDialog</c>：那是 <c>GodotNodeInterfaces</c> 给内置控件用的适配器接口，
/// <see cref="RecordingRunPanel"/> 派生自具体类 <see cref="Godot.AcceptDialog"/>，无法实现整套
/// 适配器成员；GIF 集栏唯一需要的窗口操作（把进度窗拉回前台）由 <see cref="BringToFront"/> 收口，
/// 语义与 <c>PopupCentered(760, 460)</c> 相同。
/// </para>
/// </remarks>
public interface IRecordingRunPanel
{
  /// <summary>跑一轮录制的委托（由 GIF 集栏装配时注入；未设置时 <see cref="Start"/> 抛错）。</summary>
  RecordingRunner? Runner { get; set; }

  /// <summary>一轮录制结束（成功、取消或失败都触发）。</summary>
  event Action<RecordingRunResult>? Finished;

  /// <summary>本轮是否还在跑。</summary>
  bool IsRunning { get; }

  /// <summary>起录一轮：弹出对话框并开始上报进度。</summary>
  /// <param name="request">本轮输入。</param>
  void Start(RecordingRequest request);

  /// <summary>请求取消本轮（已结束则忽略）。</summary>
  void Cancel();

  /// <summary>回填结束汇总（由调用方在自动导入之后调用）。</summary>
  /// <param name="summary">汇总文本。</param>
  void AppendSummary(string summary);

  /// <summary>把已在跑的对话框拉回前台（重复点录制时）。</summary>
  void BringToFront();
}
