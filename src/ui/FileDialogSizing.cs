namespace AutoCMEX.UI;

using System.Runtime.CompilerServices;
using Chickensoft.GodotNodeInterfaces;
using Godot;

/// <summary>
/// 文件选择窗口的统一尺寸策略：所有文件对话框共用一份「上次尺寸」，没被用户改过时按主窗口视口尺寸的固定比例打开。
/// </summary>
/// <remarks>
/// <para>
/// 全项目所有 <see cref="IFileDialog"/> 的弹出都经这里，避免各面板各写一套尺寸或写死像素值
/// （写死的像素值不随主窗口与主题缩放变化）。
/// </para>
/// <para>
/// 尺寸记忆是<b>进程内全局</b>的，且只在用户真的把某个对话框拖成别的尺寸时才更新：之后所有文件对话框都用这个尺寸。
/// 用户没改过尺寸时不会写记忆，仍按主窗口<b>视口尺寸</b> × <see cref="DefaultParentRatio"/> 打开
/// （主窗口尺寸变化时跟着重算）。记忆尺寸在显示时按当前视口收敛，但不会把收敛后的值写回记忆
/// （主窗口临时缩小后恢复，弹窗仍回到用户当初的尺寸）。跨重启不记忆。
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

  /// <summary>所有文件对话框共用的上次尺寸（视口 UI 坐标，未经视口收敛）；<c>null</c> 表示还没被用户改过。</summary>
  private static Vector2I? _remembered;

  /// <summary>已挂过「隐藏时记住尺寸」的对话框实例（弱引用，避免重复订阅）。</summary>
  private static readonly ConditionalWeakTable<object, object> _hooked = new();

  /// <summary>每个对话框实例本次弹出时采用的尺寸，用于判断隐藏时用户是否真的改过尺寸。</summary>
  private static readonly ConditionalWeakTable<object, object> _applied = new();

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
    {
      // 适配失败时静默返回会表现成「点了按钮没反应」，这里留下可诊断的错误
      GD.PushError(
        $"FileDialogSizing: 节点 {dialog.GetPath()} 无法适配成 IFileDialog，文件选择窗口不会弹出。"
      );
      return;
    }

    Open(adapted, dialog);
  }

  /// <summary>按全局记忆尺寸（或视口 80%）弹出，并在 popup 后补回被引擎钳小的尺寸。</summary>
  /// <param name="dialog">节点接口形式的对话框（适配器或 Mock）。</param>
  /// <param name="instanceKey">对话框实例键：用于保证同一实例只挂一次「隐藏时记住尺寸」。</param>
  private static void Open(IFileDialog dialog, object instanceKey)
  {
    Hook(dialog, instanceKey);

    var viewport = ViewportSize(dialog);
    var target = Clamp(_remembered ?? Resolve(viewport), viewport);
    _applied.AddOrUpdate(instanceKey, target);
    dialog.PopupCentered(target);
    // popup 会把尺寸钳到屏幕可用区域的像素值，这里补回目标尺寸（位置已按 target 居中）
    dialog.Size = target;
  }

  /// <summary>给对话框挂「隐藏时回写尺寸」，同一实例只挂一次。</summary>
  /// <param name="dialog">要挂的对话框。</param>
  /// <param name="instanceKey">区分实例的键：场景节点用适配器本身，代码创建的节点用具体节点。</param>
  private static void Hook(IFileDialog dialog, object instanceKey)
  {
    if (_hooked.TryGetValue(instanceKey, out _))
      return;

    _hooked.AddOrUpdate(instanceKey, dialog);
    dialog.VisibilityChanged += () => RememberSizeOnHide(dialog, instanceKey);
  }

  /// <summary>
  /// 对话框隐藏时：只有用户真的把尺寸改成了别的值才更新全局记忆；
  /// 与本次弹出设定的尺寸一致时视为「用户没改过」，不写记忆（保持 80% 动态策略）。
  /// </summary>
  /// <param name="dialog">发生可见性变化的对话框。</param>
  /// <param name="instanceKey">该对话框的实例键。</param>
  private static void RememberSizeOnHide(IFileDialog dialog, object instanceKey)
  {
    if (dialog.Visible)
      return;

    var size = dialog.Size;
    if (size.X <= 0 || size.Y <= 0)
      return;

    if (_applied.TryGetValue(instanceKey, out var applied) && size == (Vector2I)applied)
      return;

    _remembered = size;
  }

  /// <summary>取主窗口视口尺寸（内容缩放后的 UI 坐标）；拿不到时回落兜底尺寸。</summary>
  /// <param name="dialog">用于定位场景树/主窗口的对话框。</param>
  /// <returns>视口尺寸（UI 坐标）。</returns>
  private static Vector2I ViewportSize(IFileDialog dialog)
  {
    var viewport = dialog.GetTree()?.Root?.GetVisibleRect().Size ?? Vector2.Zero;
    if (viewport.X <= 0 || viewport.Y <= 0)
      return FallbackParentSize;

    return new Vector2I(Mathf.RoundToInt(viewport.X), Mathf.RoundToInt(viewport.Y));
  }

  /// <summary>把目标尺寸收敛到视口以内，避免窗口缩小后弹窗超出主窗口。</summary>
  /// <param name="size">目标尺寸。</param>
  /// <param name="limit">视口尺寸。</param>
  /// <returns>收敛后的尺寸。</returns>
  private static Vector2I Clamp(Vector2I size, Vector2I limit) =>
    new(Mathf.Min(size.X, limit.X), Mathf.Min(size.Y, limit.Y));

  /// <summary>当前全局记忆尺寸（仅测试使用）。</summary>
  internal static Vector2I? RememberedForTests => _remembered;

  /// <summary>
  /// 清空全局尺寸记忆与实例记录（仅测试使用，保证用例之间互不影响）。
  /// </summary>
  /// <remarks>
  /// 只清表、不解绑已经挂上的 <c>VisibilityChanged</c>；测试都是在弹出前清空、且每个用例用新实例，
  /// 所以不会出现重复订阅。生产运行中不调用本方法。
  /// </remarks>
  internal static void ResetMemoryForTests()
  {
    _remembered = null;
    _hooked.Clear();
    _applied.Clear();
  }
}
