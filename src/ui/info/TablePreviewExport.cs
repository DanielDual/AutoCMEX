namespace AutoCMEX.UI.Info;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.Helpers.Windows;
using Chickensoft.GodotNodeInterfaces;
using Godot;

/// <summary>预览导出后端：把图片写入系统剪贴板，或发起一次系统拖放。</summary>
/// <remarks>抽成接口是为了让 <see cref="TablePreviewExport"/> 可在单测中替换原生实现。</remarks>
public interface ITableImageExportBackend
{
  /// <summary>把图片写入系统剪贴板。</summary>
  /// <param name="image">要复制的图片。</param>
  /// <param name="error">失败原因；成功时为空串。</param>
  /// <returns>是否成功。</returns>
  bool TryCopy(Image image, out string error);

  /// <summary>发起系统拖放。</summary>
  /// <param name="paths">要拖出的文件绝对路径。</param>
  /// <param name="dragImage">拖影缩略图（可空）。</param>
  /// <param name="error">失败原因；成功时为空串。</param>
  /// <returns>是否成功完成拖放。</returns>
  bool TryDragFiles(IReadOnlyList<string> paths, Image? dragImage, out string error);
}

/// <summary>Windows 默认导出后端：调用 Win32 剪贴板与 OLE 拖放封装。</summary>
public sealed class WindowsTableImageExportBackend : ITableImageExportBackend
{
  /// <inheritdoc/>
  public bool TryCopy(Image image, out string error) =>
    Win32ClipboardImage.TryCopy(image, out error);

  /// <inheritdoc/>
  public bool TryDragFiles(IReadOnlyList<string> paths, Image? dragImage, out string error) =>
    Win32DragOut.TryDragFiles(paths, dragImage, out error);
}

/// <summary>
/// 表格预览的导出交互：缓存当前表格出图，提供右键「复制」与按住拖拽导出。
/// </summary>
/// <remarks>
/// <para>
/// <b>出图惰性：</b><see cref="Update"/> 只记录最新模型与内容指纹（并作废在飞的旧渲染），
/// 不立刻渲染——两张表栏每次数据变化都会调用它，若在这里出图，一次猜测连改 K 张符卡就要离屏渲染
/// K 次。真正的出图改到用户交互时：右键弹菜单前、拖拽启动前、鼠标进入表格时调用
/// <see cref="EnsureImage"/>；首次触发时图还没好，菜单项保持禁用、拖拽不启动（与原先失败的语义一致）。
/// </para>
/// <para>
/// 缓存图与发布图同源（都走 <see cref="TableImageRenderer"/>），内容指纹不变时不重复渲染。
/// 表为空时清空缓存。渲染失败后缓存为空，下一次交互（鼠标进入/右键/拖拽）会自然重试；
/// 内容变化会作废在飞渲染，旧图不会被写回新签名下。
/// </para>
/// <para>
/// 拖拽阈值取 <see cref="Viewport.GetDragThreshold"/>，避免与系统手感不一致；
/// 拖拽由 Windows OLE 完成，期间主线程阻塞属已知行为。
/// </para>
/// <para>
/// 右键菜单与失败提示框由栏场景（<c>CreatorRemainingPanel.tscn</c> / <c>GuessingTablePanel.tscn</c>）
/// 提供并经构造函数注入：本组件只负责填充内容与弹出，不创建、不释放节点（生命周期归栏面板），
/// 因此面板重新解析后可复用同一对节点。
/// </para>
/// </remarks>
public sealed class TablePreviewExport : IDisposable
{
  private const string CopyMenuText = "复制";
  private const int FallbackDragThreshold = 10;

  private readonly Node _owner;
  private readonly IControl _target;

  /// <summary>右键菜单（由栏场景提供，本组件只负责填充与弹出，不负责生命周期）。</summary>
  private readonly IPopupMenu _menu;

  /// <summary>导出失败提示框（由栏场景提供，同样不归本组件释放）。</summary>
  private readonly IAcceptDialog _messageDialog;

  private readonly Func<TableModel, Task<TableImageRenderResult>> _render;
  private readonly ITableImageExportBackend _backend;

  private Image? _image;
  private TableModel? _model;
  private string _signature = string.Empty;
  private string _imageSignature = string.Empty;
  private int _renderVersion;
  private bool _isRendering;
  private bool _renderPending;
  private bool _isPressed;
  private Vector2 _pressPosition;
  private bool _disposed;
  private Window? _rootWindow;

  /// <summary>创建预览导出组件并接管目标控件的鼠标输入。</summary>
  /// <param name="owner">宿主节点（面板），用于取视口与判断是否在树内。</param>
  /// <param name="target">接收右键与拖动输入的表格控件。</param>
  /// <param name="menu">右键菜单（栏场景提供）。</param>
  /// <param name="messageDialog">导出失败提示框（栏场景提供）。</param>
  /// <param name="render">表格内存出图回调（生产环境走 <see cref="TableImageRenderer"/>）。</param>
  /// <param name="backend">剪贴板/拖放后端。</param>
  public TablePreviewExport(
    Node owner,
    IControl target,
    IPopupMenu menu,
    IAcceptDialog messageDialog,
    Func<TableModel, Task<TableImageRenderResult>> render,
    ITableImageExportBackend backend
  )
  {
    _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    _target = target ?? throw new ArgumentNullException(nameof(target));
    _menu = menu ?? throw new ArgumentNullException(nameof(menu));
    _messageDialog = messageDialog ?? throw new ArgumentNullException(nameof(messageDialog));
    _render = render ?? throw new ArgumentNullException(nameof(render));
    _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    // Pass：自身接收右键/拖动，滚轮事件继续传给外层 ScrollContainer
    _target.MouseFilter = Control.MouseFilterEnum.Pass;
    _target.GuiInput += OnGuiInput;
    _target.MouseEntered += OnMouseEntered;

    // 菜单是场景节点：信号在构造时挂一次，Dispose 时退订
    _menu.IdPressed += OnMenuPressed;

    _rootWindow = _owner.GetTree()?.Root;
    if (_rootWindow is not null)
      _rootWindow.FocusExited += OnWindowFocusExited;
  }

  /// <summary>缓存图是否与当前表格内容一致且已就绪。</summary>
  public bool HasImage =>
    _image is not null && string.Equals(_imageSignature, _signature, StringComparison.Ordinal);

  /// <summary>
  /// 记录最新表格模型并作废旧缓存；内容指纹未变时什么都不做。
  /// </summary>
  /// <param name="model">表格模型。</param>
  /// <remarks>
  /// 本方法不渲染（惰性出图，见类型注释）：只把模型与指纹记下来并作废在飞的旧渲染，
  /// 真正出图留到右键/拖拽/鼠标进入这些交互入口。
  /// </remarks>
  public void Update(TableModel model)
  {
    if (_disposed)
      return;

    _model = model;
    var signature = TableModelSignature.Build(model);
    if (string.Equals(signature, _signature, StringComparison.Ordinal))
      return;

    _signature = signature;

    // 内容变化即作废旧图与在飞渲染，避免旧图在新签名下被写回；新图等交互时再出。
    // 若确有在飞渲染，就在它结束后按新内容补一次，否则这次内容变化会一直等到下次交互才出图。
    _renderVersion++;
    _renderPending = _isRendering;
    _image = null;
    _imageSignature = string.Empty;
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _target.GuiInput -= OnGuiInput;
    _target.MouseEntered -= OnMouseEntered;

    if (_rootWindow is not null)
    {
      _rootWindow.FocusExited -= OnWindowFocusExited;
      _rootWindow = null;
    }

    // 菜单与提示框是场景节点（归栏面板所有）：这里只退订信号并隐藏，不释放节点。
    // 节点可能先于本对象被释放（栏面板先退出），退订前必须确认底层对象仍有效。
    var menu = _menu.GetTargetObj<GodotObject>();
    if (menu is not null && GodotObject.IsInstanceValid(menu))
    {
      _menu.IdPressed -= OnMenuPressed;
      _menu.Hide();
    }

    var message = _messageDialog.GetTargetObj<GodotObject>();
    if (message is not null && GodotObject.IsInstanceValid(message))
      _messageDialog.Hide();

    _image = null;
    _imageSignature = string.Empty;
    _model = null;
  }

  private void QueueRender()
  {
    if (_isRendering)
    {
      _renderPending = true;
      return;
    }

    _ = RenderAsync(_signature);
  }

  private async Task RenderAsync(string signature)
  {
    _isRendering = true;
    var version = _renderVersion;

    try
    {
      var model = _model;
      if (model is null || !model.HasContent)
        return;

      var result = await _render(model);

      // 期间内容又变了：丢弃过期结果（不能把旧图写到新签名下）
      if (_disposed || version != _renderVersion)
        return;

      if (!result.IsSuccess)
      {
        GD.PushWarning($"TablePreviewExport: 表格出图失败：{result.ErrorMessage}");
        _image = null;
        _imageSignature = string.Empty;
        return;
      }

      _image = result.Image;
      _imageSignature = signature;
    }
    catch (Exception ex)
    {
      GD.PushWarning($"TablePreviewExport: 表格出图异常：{ex.Message}");
      if (!_disposed && version == _renderVersion)
      {
        _image = null;
        _imageSignature = string.Empty;
      }
    }
    finally
    {
      _isRendering = false;
      if (_renderPending && !_disposed)
      {
        _renderPending = false;
        QueueRender();
      }
    }
  }

  /// <summary>鼠标进入表格：惰性出图的主要入口（也是渲染失败后的天然重试时机）。</summary>
  private void OnMouseEntered()
  {
    EnsureImage();
  }

  /// <summary>
  /// 按需触发一次出图：仅在缓存缺失/失效、没有在飞渲染且表有内容时排队。
  /// </summary>
  /// <remarks>右键弹菜单前、拖拽启动前、鼠标进入时调用；渲染尚未完成时缓存仍为空。</remarks>
  private void EnsureImage()
  {
    if (_disposed || _isRendering || HasImage)
      return;

    if (_model is { HasContent: true })
      QueueRender();
  }

  /// <summary>窗口失焦时复位按下状态，避免下次拖动误判。</summary>
  private void OnWindowFocusExited()
  {
    _isPressed = false;
  }

  private static bool IsPlatformSupported => OperatingSystem.IsWindows();

  private void OnGuiInput(InputEvent @event)
  {
    if (_disposed)
      return;

    switch (@event)
    {
      case InputEventMouseButton button when button.ButtonIndex == MouseButton.Right:
        if (button.Pressed && IsPlatformSupported)
        {
          ShowMenu();
          _target.AcceptEvent();
        }
        break;

      case InputEventMouseButton button when button.ButtonIndex == MouseButton.Left:
        if (button.Pressed)
        {
          _isPressed = true;
          _pressPosition = button.Position;
        }
        else
        {
          _isPressed = false;
        }
        break;

      case InputEventMouseMotion motion:
        if (_isPressed && (motion.ButtonMask & MouseButtonMask.Left) != 0)
          MaybeStartDrag(motion.Position);
        break;
    }
  }

  private void MaybeStartDrag(Vector2 position)
  {
    if (!IsPlatformSupported)
      return;

    var threshold = _owner.GetViewport()?.GetDragThreshold() ?? FallbackDragThreshold;
    if (position.DistanceTo(_pressPosition) < threshold)
      return;

    _isPressed = false;
    StartDrag();
  }

  private void ShowMenu()
  {
    if (!IsPlatformSupported || !_owner.IsInsideTree())
      return;

    // 右键是惰性出图的交互入口：首次触发时图还没好，菜单项保持禁用
    EnsureImage();

    _menu.Clear();
    _menu.AddItem(CopyMenuText, 0);
    _menu.SetItemDisabled(0, !HasImage);

    // 项目使用嵌入子窗口（embed_subwindows 默认开启）：PopupMenu 的 Position 按
    // 父视口坐标解释；只有显式关闭嵌入时才用屏幕坐标。
    var viewport = _owner.GetViewport();
    _menu.Position =
      viewport is not null && viewport.GuiEmbedSubwindows
        ? (Vector2I)viewport.GetMousePosition()
        : DisplayServer.MouseGetPosition();
    _menu.Popup();
  }

  private void OnMenuPressed(long id)
  {
    if (id != 0)
      return;

    CopyImage();
  }

  private void CopyImage()
  {
    if (!IsPlatformSupported || !HasImage || _image is null)
      return;

    if (!_backend.TryCopy(_image, out var error))
      ShowMessage("复制失败", error);
  }

  private void StartDrag()
  {
    if (!IsPlatformSupported || !_owner.IsInsideTree())
      return;

    // 拖拽是惰性出图的交互入口：图未就绪时本次不启动，等图好了再拖一次
    EnsureImage();

    if (!HasImage || _image is null)
      return;

    var image = _image;
    _menu.Hide();

    var fileName =
      $"drag_{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}_"
      + $"{Guid.NewGuid().ToString("N").Substring(0, 4)}.png";
    var path = TableImageRenderer.BuildTempOutputPath(fileName);

    try
    {
      var directory = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

      var error = image.SavePng(path);
      if (error != Error.Ok)
      {
        ShowMessage("拖拽失败", $"无法创建拖拽文件（{error}）。");
        return;
      }
    }
    catch (Exception ex)
    {
      ShowMessage("拖拽失败", $"无法创建拖拽文件：{ex.Message}");
      return;
    }

    if (!_backend.TryDragFiles(new[] { path }, image, out var dragError))
      ShowMessage("拖拽失败", dragError);
  }

  private void ShowMessage(string title, string text)
  {
    _messageDialog.Title = title;
    _messageDialog.DialogText = text;

    if (_owner.IsInsideTree())
      _messageDialog.PopupCentered(new Vector2I(560, 320));
  }
}
