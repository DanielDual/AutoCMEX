namespace AutoCMEX.UI;

using System.Linq;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GodotNodeInterfaces;

/// <summary>
/// 把一条场景分割条（<see cref="ISplitContainer"/>）与 <c>ui_layout.json</c> 里的一条
/// <see cref="SplitLayoutRecord"/> 双向接起来：启动时把记忆值应用到场景，用户拖拽 / 折叠时回写模型。
/// </summary>
/// <remarks>
/// <para>
/// 普通类而非 <c>Node</c>：它没有自己的生命周期，也不出现在场景树里，只是面板 / 主窗口持有的
/// 「接线器」（规则 6：由宿主在 <c>OnResolved</c> 创建、<c>_ExitTree</c> 收尾）。
/// </para>
/// <para>
/// 数据方向只有「UI → 模型」：模型是启动快照，不做 Sync 绑定（见 <see cref="UiLayoutData"/> 类注释）。
/// 拖拽过程中只更新内存里的模型（不落盘，避免每拖一像素触发一次防抖写盘），拖拽结束时才
/// <see cref="DataManager.TriggerAutoSave"/>；面板退出时由 <see cref="Capture"/> 补一笔，
/// 最终由 <see cref="DataManager"/> 的末次 <c>SaveAll</c>/<c>Dispose</c> 落盘。
/// </para>
/// <para>
/// offset 存 <c>SplitContainer.SplitOffset</c> 的原始值：它是以引擎按子节点展开标志与最小尺寸
/// 算出的默认分隔位置为 0 的像素偏移，不是窗口内固定像素坐标。这里原样写回、不做换算，
/// 恢复语义与用户当时拖拽的一致；引擎在空间不足时只夹紧实际布局、不改属性值。
/// </para>
/// </remarks>
public sealed class SplitLayoutLink
{
  /// <summary>被接线的场景分割条。</summary>
  private readonly ISplitContainer _split;

  /// <summary>本分割条在模型里的语义键（见 <see cref="UiLayoutKeys"/>）。</summary>
  private readonly string _key;

  /// <summary>布局记忆模型（由构造参数的 <see cref="DataManager"/> 取出，避免长期持有可释放的管理器）。</summary>
  private readonly UiLayoutData _uiLayout;

  /// <summary>触发防抖自动保存的委托（同上，从 <see cref="DataManager"/> 取出）。</summary>
  private readonly System.Action _triggerAutoSave;

  /// <summary>应用记忆值之后（或场景默认值）的 offset 快照，用于 <see cref="Capture"/> 判定用户是否改过。</summary>
  private int _initialOffset;

  /// <summary>应用记忆值之后的折叠状态快照。</summary>
  private bool _initialCollapsed;

  /// <summary>是否已建立初值快照（未 <see cref="Apply"/> 过时 <see cref="Capture"/> 不写模型）。</summary>
  private bool _snapshotTaken;

  /// <summary>是否已订阅拖拽信号（防止重复 <see cref="Apply"/> 导致重复回写）。</summary>
  private bool _subscribed;

  /// <summary>
  /// 创建接线器；初值在 <see cref="Apply"/> 中建立。
  /// </summary>
  /// <param name="split">要接线的分割条（场景节点适配器）。</param>
  /// <param name="key">该分割条的语义键。</param>
  /// <param name="dataManager">布局数据的持有者；本类只取它的模型与保存入口。</param>
  public SplitLayoutLink(ISplitContainer split, string key, DataManager dataManager)
  {
    _split = split;
    _key = key;
    _uiLayout = dataManager.UiLayout;
    _triggerAutoSave = dataManager.TriggerAutoSave;
  }

  /// <summary>
  /// 应用记忆值并建立初值快照；同时订阅拖拽信号。
  /// </summary>
  /// <remarks>
  /// 模型里没有该键的记录时保持场景当前值（不写 0，避免首次启动把场景布局改成 0）；
  /// 有记录时先设 offset 再设 collapsed，两值都来自同一条记录。
  /// </remarks>
  public void Apply()
  {
    var record = FindRecord();
    if (record is not null)
    {
      _split.SplitOffset = record.Offset.Value;
      _split.Collapsed = record.Collapsed.Value;
    }

    _initialOffset = _split.SplitOffset;
    _initialCollapsed = _split.Collapsed;
    _snapshotTaken = true;

    if (_subscribed)
      return;

    _split.Dragged += OnDragged;
    _split.DragEnded += OnDragEnded;
    _subscribed = true;
  }

  /// <summary>
  /// 把当前场景值与初值比较，有变化则回写模型（不触发保存，交给宿主退出时统一落盘）。
  /// </summary>
  public void Capture()
  {
    if (!_snapshotTaken)
      return;

    var offset = _split.SplitOffset;
    var collapsed = _split.Collapsed;
    if (offset == _initialOffset && collapsed == _initialCollapsed)
      return;

    Upsert(offset, collapsed);
  }

  /// <summary>
  /// 拖拽中：只把新 offset 写进内存模型，不落盘（每像素一次写盘既浪费又会让磁盘抖动）。
  /// </summary>
  /// <param name="offset">引擎给出的新分割位置（<c>SplitOffset</c> 原始值）。</param>
  private void OnDragged(long offset) => Upsert((int)offset, _split.Collapsed);

  /// <summary>
  /// 拖拽结束：补一次 upsert（覆盖折叠态等拖拽中未变的值）并触发防抖保存。
  /// </summary>
  private void OnDragEnded()
  {
    Upsert(_split.SplitOffset, _split.Collapsed);
    _triggerAutoSave();
  }

  /// <summary>
  /// 按语义键查找模型记录。
  /// </summary>
  /// <returns>找到的记录；没有时返回 <c>null</c>。</returns>
  private SplitLayoutRecord? FindRecord() =>
    _uiLayout.Splits.FirstOrDefault(record => record is not null && record.Key.Value == _key);

  /// <summary>
  /// 按语义键插入或更新一条记录（模型里 Key 唯一由加载时的 <c>Sanitize</c> 保证）。
  /// </summary>
  /// <param name="offset">要写入的分割位置（<c>SplitOffset</c> 原始值）。</param>
  /// <param name="collapsed">要写入的折叠状态。</param>
  /// <remarks>规则 16：<c>AutoValue</c> 读写一律经 <c>.Value</c>；本方法只改模型，不触发保存。</remarks>
  private void Upsert(int offset, bool collapsed)
  {
    var record = FindRecord();
    if (record is null)
    {
      _uiLayout.Splits.Add(
        new SplitLayoutRecord
        {
          Key = new(_key),
          Offset = new(offset),
          Collapsed = new(collapsed),
        }
      );
      return;
    }

    record.Offset.Value = offset;
    record.Collapsed.Value = collapsed;
  }
}
