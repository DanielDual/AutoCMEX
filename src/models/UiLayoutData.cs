namespace AutoCMEX.Models;

using System;
using System.Collections.Generic;
using Chickensoft.Sync.Primitives;

/// <summary>
/// UI 布局记忆数据（落盘 <c>ui_layout.json</c>，是 <c>DataManager</c> 的第 8 段数据）。
/// </summary>
/// <remarks>
/// <para>
/// 记录主窗口几何、文件选择窗口尺寸与各板块分割条的 offset / 折叠状态，供下次启动还原。
/// 由 <c>DataManager</c> 统一加载与保存，复用其防抖自动保存链路，不另起持久化路径。
/// </para>
/// <para>
/// 形态约定（重要）：本模型是「启动 / 弹窗拉取的一次性快照」，运行期的事实源是节点几何
/// （<c>SplitContainer.SplitOffset</c>、<c>Window</c> 的 <c>Size</c> / <c>Position</c>）。
/// 有意不做 Sync 绑定——没有「模型 → UI」的推送：UI 只在启动或弹窗弹出时读一次模型并应用到
/// 节点，用户调整后由 UI 回写模型再落盘。因此不要在本模型上 <c>Bind().OnValue()</c> 驱动刷新，
/// 否则会与用户拖拽 / 缩放互相覆盖。其余配置类（如 <see cref="AppSettings"/>）是双向数据源，
/// 本类不是：规则 14-23 的数据同步约定在此只有「UI → 模型」一个方向。
/// </para>
/// </remarks>
public class UiLayoutData
{
  /// <summary>当前数据结构版本；加载到更高版本时 <c>DataManager</c> 会放弃读写该文件（只读保护）。</summary>
  public const int CurrentVersion = 1;

  /// <summary>窗口单边尺寸上限（像素）：超过它视为非法，窗口几何整体回落默认。</summary>
  private const int MaxWindowExtent = 32767;

  /// <summary>窗口坐标绝对值上限（像素）：超过它视为非法，窗口几何整体回落默认。</summary>
  private const int MaxWindowCoordinate = 32768;

  /// <summary>分割条 offset 的下限（像素）。</summary>
  private const int MinSplitOffset = -100000;

  /// <summary>分割条 offset 的上限（像素）。</summary>
  private const int MaxSplitOffset = 100000;

  /// <summary>数据版本，正常等于 <see cref="CurrentVersion"/>；缺失或非正数由 <see cref="Sanitize"/> 补正。</summary>
  public AutoValue<int> Version { get; set; } = new(CurrentVersion);

  /// <summary>各板块分割条的布局记录（按语义键去重；空键与重复键由 <see cref="Sanitize"/> 收敛）。</summary>
  public AutoList<SplitLayoutRecord> Splits { get; set; } = new();

  /// <summary>主窗口宽度（像素）；<c>null</c> 表示从未记录过，启动时保持默认尺寸。</summary>
  public AutoValue<int?> WindowWidth { get; set; } = new((int?)null);

  /// <summary>主窗口高度（像素）；<c>null</c> 表示从未记录过。</summary>
  public AutoValue<int?> WindowHeight { get; set; } = new((int?)null);

  /// <summary>主窗口左上角 X 坐标（像素）；<c>null</c> 表示从未记录过。</summary>
  public AutoValue<int?> WindowX { get; set; } = new((int?)null);

  /// <summary>主窗口左上角 Y 坐标（像素）；<c>null</c> 表示从未记录过。</summary>
  public AutoValue<int?> WindowY { get; set; } = new((int?)null);

  /// <summary>主窗口上次关闭时是否处于最大化。</summary>
  public AutoValue<bool> WindowMaximized { get; set; } = new(false);

  /// <summary>文件选择窗口宽度（像素）；<c>null</c> 表示从未记录过。</summary>
  public AutoValue<int?> FileDialogWidth { get; set; } = new((int?)null);

  /// <summary>文件选择窗口高度（像素）；<c>null</c> 表示从未记录过。</summary>
  public AutoValue<int?> FileDialogHeight { get; set; } = new((int?)null);

  /// <summary>
  /// 恢复被 JSON 显式 <c>null</c> 覆盖的自动同步属性。
  /// </summary>
  /// <remarks>
  /// 与 <see cref="AppSettings.EnsureIntegrity"/> 同理：System.Text.Json 反序列化会把显式
  /// <c>null</c> 写入 <see cref="AutoValue{T}"/> / <see cref="AutoList{T}"/> 属性（转换器不参与
  /// 顶层 null），令后续访问抛 <see cref="System.NullReferenceException"/>。加载后必须调用，
  /// 且顺序上要先于 <see cref="Sanitize"/>（后者直接解引用这些容器）。
  /// </remarks>
  public void EnsureIntegrity()
  {
    Version ??= new(CurrentVersion);
    Splits ??= new();
    WindowWidth ??= new((int?)null);
    WindowHeight ??= new((int?)null);
    WindowX ??= new((int?)null);
    WindowY ??= new((int?)null);
    WindowMaximized ??= new(false);
    FileDialogWidth ??= new((int?)null);
    FileDialogHeight ??= new((int?)null);

    // 规则 14/15：集合元素同样是 AutoValue 容器，逐条回填；JSON 里的 null 元素在此跳过
    foreach (var split in Splits)
    {
      split?.EnsureIntegrity();
    }
  }

  /// <summary>
  /// 把加载到的布局数据收敛到确定性的合法状态（加载后调用，保证 UI 拿到的值可直接应用）。
  /// </summary>
  /// <remarks>
  /// <para>确定性规则：</para>
  /// <list type="bullet">
  /// <item><description>版本 <c>&lt;= 0</c>：视为 <see cref="CurrentVersion"/>（旧文件或手改文件）。</description></item>
  /// <item><description>窗口宽或高 <c>&lt;= 0</c>、或任一边大于 32767：四个几何字段全置 <c>null</c>，
  /// 最大化标志一并置 <c>false</c>。</description></item>
  /// <item><description>窗口坐标任一绝对值大于 32768：同样把整个窗口几何记录置 <c>null</c>
  /// （最大化标志一并复位），避免恢复出屏幕外的窗口。</description></item>
  /// <item><description>文件选择窗口宽或高 <c>&lt;= 0</c>、或任一边大于 32767：两个字段一起置
  /// <c>null</c>，避免只恢复一半的宽高。</description></item>
  /// <item><description>分割条：丢弃 Key 为 <c>null</c> / 空白的记录；重复 Key 保留最后一条；
  /// Offset 收敛到 [-100000, 100000]。</description></item>
  /// </list>
  /// <para>
  /// 调用前必须已执行 <see cref="EnsureIntegrity"/>（<c>DataManager.LoadAll</c> 的顺序是
  /// EnsureIntegrity → Sanitize → 版本迁移），否则这里直接解引用容器字段会抛异常。
  /// </para>
  /// </remarks>
  public void Sanitize()
  {
    // 规则 16：AutoValue 的读取与赋值一律经 .Value
    if (Version.Value <= 0)
      Version.Value = CurrentVersion;

    SanitizeWindowGeometry();
    SanitizeSplits();
  }

  /// <summary>
  /// 收敛主窗口与文件选择窗口的几何字段。
  /// </summary>
  private void SanitizeWindowGeometry()
  {
    // 尺寸与坐标分开判非法，但结果一致：任一非法都说明这份记录整体不可信，
    // 一律回落成「从未记录」，避免出现「尺寸有效、位置却不可用」的半套数据
    if (
      IsInvalidExtent(WindowWidth.Value)
      || IsInvalidExtent(WindowHeight.Value)
      || IsInvalidCoordinate(WindowX.Value)
      || IsInvalidCoordinate(WindowY.Value)
    )
    {
      ResetWindowGeometry();
    }

    if (IsInvalidExtent(FileDialogWidth.Value) || IsInvalidExtent(FileDialogHeight.Value))
    {
      FileDialogWidth.Value = null;
      FileDialogHeight.Value = null;
    }
  }

  /// <summary>
  /// 把主窗口几何记录整体置为「从未记录」。
  /// </summary>
  /// <remarks>布局数据已不可信时最大化标志也一并复位，否则下次启动会以最大化的形式落在默认位置。</remarks>
  private void ResetWindowGeometry()
  {
    WindowWidth.Value = null;
    WindowHeight.Value = null;
    WindowX.Value = null;
    WindowY.Value = null;
    WindowMaximized.Value = false;
  }

  /// <summary>
  /// 判断单边尺寸是否非法（非空且 <c>&lt;= 0</c> 或超过上限）。
  /// </summary>
  /// <param name="extent">窗口 / 弹窗的单边尺寸。</param>
  /// <returns>非法返回 <c>true</c>；<c>null</c> 表示「未记录」，不算非法。</returns>
  private static bool IsInvalidExtent(int? extent) =>
    extent is { } value && (value <= 0 || value > MaxWindowExtent);

  /// <summary>
  /// 判断窗口坐标是否非法（绝对值超过 32768）。
  /// </summary>
  /// <param name="coordinate">窗口左上角坐标。</param>
  /// <returns>非法返回 <c>true</c>；<c>null</c> 表示「未记录」，不算非法。</returns>
  private static bool IsInvalidCoordinate(int? coordinate) =>
    coordinate is { } value && (value < -MaxWindowCoordinate || value > MaxWindowCoordinate);

  /// <summary>
  /// 丢弃无效的分割条记录、按 Key 去重并收敛 offset。
  /// </summary>
  private void SanitizeSplits()
  {
    // 规则 22：下面会增删集合，先对快照遍历，遍历期间不改动活着的 AutoList
    var snapshot = new List<SplitLayoutRecord>(Splits);
    var kept = new List<SplitLayoutRecord>(snapshot.Count);
    var seenKeys = new HashSet<string>(StringComparer.Ordinal);

    // 从后往前扫：重复 Key 保留最后一条，并保持它最后出现的位置（结果唯一且确定）
    for (var index = snapshot.Count - 1; index >= 0; index--)
    {
      var split = snapshot[index];
      if (split is null)
        continue;

      var key = split.Key.Value;
      if (string.IsNullOrWhiteSpace(key) || !seenKeys.Add(key))
        continue;

      split.Offset.Value = Math.Clamp(split.Offset.Value, MinSplitOffset, MaxSplitOffset);
      kept.Add(split);
    }

    // 没有丢弃或去重时保持原集合不动，避免无意义的增删通知
    if (kept.Count == snapshot.Count)
      return;

    kept.Reverse();
    Splits.Clear();
    foreach (var split in kept)
      Splits.Add(split);
  }
}

/// <summary>
/// 单条分割条布局记录：语义键 + offset（<c>SplitContainer.SplitOffset</c> 原始值）+ 折叠状态。
/// </summary>
/// <remarks>
/// <para>
/// 语义键由 UI 层定义（主窗口导航栏、整合板块的根 / 左 / 右栏、猜测板块内容 / 左栏、设置右栏），
/// 本模型只存取不解释；未知键原样保留（UI 找不到对应分割条时忽略），便于新旧版本共存与回滚。
/// </para>
/// <para>
/// offset 原样存取 Godot 4.7.1 <c>SplitContainer.SplitOffset</c> 的原始值：它是以引擎按子节点
/// 展开标志与最小尺寸算出的<b>默认分隔位置</b>为 0 的像素偏移，不是窗口内固定的像素坐标——
/// 窗口尺寸或子节点展开标志变化时默认位置会移动，实际分隔条位置也随之移动。这里原样存取、
/// 不做坐标换算：引擎在空间不足时只夹紧实际布局、不改属性值，恢复语义与用户当时拖拽的一致。
/// </para>
/// <para>
/// 不存比例：比例要在读写两侧换算、存在取整漂移；且默认位置本身随窗口与展开标志变化，
/// 存比例会破坏引擎的默认位置语义。
/// </para>
/// </remarks>
public class SplitLayoutRecord
{
  /// <summary>分割条的语义键（如 <c>main.nav</c>）；空键在加载后会被丢弃。</summary>
  public AutoValue<string> Key { get; set; } = new(string.Empty);

  /// <summary>
  /// 分割条位置（<c>SplitContainer.SplitOffset</c> 原始值：相对引擎按子节点展开标志与最小尺寸
  /// 算出的默认分隔位置的像素偏移；原样存取、不做换算）。
  /// </summary>
  public AutoValue<int> Offset { get; set; } = new(0);

  /// <summary>分割条是否处于折叠状态。</summary>
  public AutoValue<bool> Collapsed { get; set; } = new(false);

  /// <summary>
  /// 恢复被 JSON 显式 <c>null</c> 覆盖的自动同步属性（与 <see cref="UiLayoutData.EnsureIntegrity"/> 同理）。
  /// </summary>
  public void EnsureIntegrity()
  {
    Key ??= new(string.Empty);
    Offset ??= new(0);
    Collapsed ??= new(false);
  }
}
