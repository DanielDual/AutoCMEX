namespace AutoCMEX.UI;

using System.Runtime.CompilerServices;
using Chickensoft.GodotNodeInterfaces;
using Godot;

/// <summary>
/// 文件选择窗口的统一尺寸策略：首次唤出按主窗口的固定比例给尺寸，再次唤出沿用上次尺寸。
/// </summary>
/// <remarks>
/// <para>
/// 全项目所有 <see cref="IFileDialog"/> 的弹出都经这里，避免各面板各写一套尺寸或写死像素值
/// （写死的像素值不随主窗口与主题缩放变化）。
/// </para>
/// <para>
/// 首次唤出的目标尺寸 = 主窗口<b>视口尺寸</b> × <see cref="DefaultParentRatio"/>；同一对话框在同一次运行内
/// 再次唤出时沿用其当前尺寸，用户拖拽过的结果不回弹。
/// </para>
/// <para>
/// 父尺寸必须取 <c>Root.GetVisibleRect().Size</c>（内容缩放后的 UI 坐标），不能用 <c>Root.Size</c>（窗口像素尺寸）：
/// 主窗口会由 <c>Main.LookGood</c> 设置 <c>content_scale_factor</c>（4K 设计在 1080p 上为 0.5），此时两者差一倍，
/// 用窗口像素尺寸算出的弹窗只有预期的 content_scale 倍大。嵌入子窗口的 <c>Size</c> 与 <c>PopupCentered</c>
/// 都使用同一套 UI 坐标，故两者必须同源。
/// </para>
/// <para>
/// 另需在 popup 之后再补设一次 <c>Size</c>：Godot 弹嵌入子窗口时会把尺寸钳到「屏幕可用区域」的<b>像素值</b>
/// （不按 content_scale_factor 换算），内容缩放 0.5 时会把弹窗钳到屏幕像素大小、只有预期的一半；
/// 位置已由 <c>PopupCentered</c> 按目标尺寸算好，补设尺寸不会破坏居中。
/// </para>
/// <para>
/// 不额外设置上下限：尺寸下限由 Godot 引擎按对话框内容最小尺寸（随主题缩放、字体、DPI 变化）自行抬升；
/// 主窗口被缩到极小时，抬升后的内容最小尺寸可能超出主窗口，属已知边界，这里不截断。
/// </para>
/// </remarks>
public static class FileDialogSizing
{
  /// <summary>首次唤出时，弹窗宽高各占主窗口对应边的比例。</summary>
  public const float DefaultParentRatio = 0.8f;

  /// <summary>拿不到主窗口信息（未入树、单元测试的 Mock）时的兜底主窗口尺寸。</summary>
  public static readonly Vector2I FallbackParentSize = new(1280, 720);

  /// <summary>已按策略唤出过的对话框实例（弱引用，不延长节点生命周期）。</summary>
  private static readonly ConditionalWeakTable<object, object> _opened = new();

  /// <summary>
  /// 按主窗口尺寸计算首次唤出的目标尺寸。
  /// </summary>
  /// <param name="parentSize">主窗口视口尺寸（内容缩放后的 UI 坐标）；任一分量不为正时使用 <see cref="FallbackParentSize"/>。</param>
  /// <returns>目标尺寸：宽高各取父视口的 <see cref="DefaultParentRatio"/>，四舍五入取整。</returns>
  public static Vector2I Resolve(Vector2I parentSize)
  {
    var parent = parentSize.X > 0 && parentSize.Y > 0 ? parentSize : FallbackParentSize;
    return new Vector2I(
      Mathf.RoundToInt(parent.X * DefaultParentRatio),
      Mathf.RoundToInt(parent.Y * DefaultParentRatio)
    );
  }

  /// <summary>
  /// 统一弹出入口（节点接口形式，场景内注入的对话框）。
  /// </summary>
  /// <param name="dialog">要弹出的文件对话框；可见性与信号连接仍由调用方负责。</param>
  public static void Popup(IFileDialog dialog) => Open(dialog, dialog);

  /// <summary>
  /// 统一弹出入口（具体节点形式，代码创建的 <see cref="FileDialog"/>）。
  /// </summary>
  /// <param name="dialog">要弹出的文件对话框；可见性与信号连接仍由调用方负责。</param>
  public static void Popup(FileDialog dialog)
  {
    if (GodotInterfaces.AdaptNode(dialog) is not IFileDialog adapted)
      return;

    // 以具体节点为记忆键：同一节点每次唤出都复用同一个尺寸，用户拖拽结果才留得住
    Open(adapted, dialog);
  }

  /// <summary>首次唤出按策略给尺寸，之后沿用对话框当前尺寸。</summary>
  /// <param name="dialog">节点接口形式的对话框（适配器或 Mock）。</param>
  /// <param name="instanceKey">区分实例的记忆键：场景节点用适配器本身，代码创建的节点用具体节点。</param>
  private static void Open(IFileDialog dialog, object instanceKey)
  {
    if (_opened.TryGetValue(instanceKey, out _))
    {
      // 再次唤出：沿用上次尺寸。popup 会被引擎钳小，故弹完再补回来（位置已按该尺寸居中）
      var current = dialog.Size;
      dialog.PopupCentered();
      dialog.Size = current;
      return;
    }

    // 取视口可见区域（内容缩放后的 UI 坐标，不是窗口像素），与嵌入子窗口的 Size / PopupCentered 同源
    var viewport = dialog.GetTree()?.Root?.GetVisibleRect().Size ?? Vector2.Zero;
    var target = Resolve(new Vector2I(Mathf.RoundToInt(viewport.X), Mathf.RoundToInt(viewport.Y)));
    _opened.AddOrUpdate(instanceKey, dialog);
    dialog.PopupCentered(target);
    // popup 会把尺寸钳到屏幕可用区域的像素值，这里补回目标尺寸（位置已按 target 居中）
    dialog.Size = target;
  }
}
