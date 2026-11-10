namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoCMEX.Core.Info;
using AutoCMEX.UI.Info;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="TablePreviewExport"/> 单测：惰性出图、缓存去重、右键复制、拖动阈值与失败提示。
/// </summary>
/// <remarks>
/// 后端与出图都注入 Fake，不触发原生剪贴板/拖放；真实原生调用由 Windows 真机验收覆盖。
/// Fake 的出图默认同步完成，因此「交互触发渲染」的用例可以在同一调用栈里断言缓存；
/// 需要验证「渲染未完成」语义时用 <see cref="_pendingRender"/> 把手动完成的 Task 交给出图回调。
/// </remarks>
public class TestTablePreviewExport : TestClass
{
  private sealed class FakeBackend : ITableImageExportBackend
  {
    public List<Image> Copied { get; } = new();
    public List<string> DraggedFiles { get; } = new();
    public bool FailCopy { get; set; }
    public bool FailDrag { get; set; }
    public string ErrorMessage { get; set; } = "失败原因";

    public bool TryCopy(Image image, out string error)
    {
      Copied.Add(image);
      error = FailCopy ? ErrorMessage : string.Empty;
      return !FailCopy;
    }

    public bool TryDragFiles(IReadOnlyList<string> paths, Image? dragImage, out string error)
    {
      DraggedFiles.AddRange(paths);
      error = FailDrag ? ErrorMessage : string.Empty;
      return !FailDrag;
    }
  }

  private Node _owner = default!;
  private Control _target = default!;

  /// <summary>右键菜单：与生产一致，由宿主场景提供后注入（组件不再运行期 AddChild）。</summary>
  private PopupMenu _menu = default!;

  /// <summary>导出失败提示框：同样由宿主场景提供后注入。</summary>
  private AcceptDialog _messageDialog = default!;

  private FakeBackend _backend = default!;
  private TablePreviewExport _export = default!;
  private int _renderCount;
  private bool _failRender;

  /// <summary>非空时出图回调返回这个待完成的 Task（用于模拟「渲染还没好」）。</summary>
  private TaskCompletionSource<TableImageRenderResult>? _pendingRender;

  private readonly List<string> _tempFiles = new();

  public TestTablePreviewExport(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _owner = new Panel();
    _target = new Control { Size = new Vector2(400, 300) };
    TestScene.AddChild(_owner);
    _owner.AddChild(_target);

    // 与生产同构：菜单/提示框先在场景里建好，再注入组件（所有权归场景，组件只填内容）
    _menu = new PopupMenu { Name = "TableImageMenu" };
    _messageDialog = new AcceptDialog
    {
      Name = "TableImageMessage",
      Title = "表格图片导出",
      Exclusive = false,
    };
    _owner.AddChild(_menu);
    _owner.AddChild(_messageDialog);

    _backend = new FakeBackend();
    _renderCount = 0;
    _failRender = false;
    _pendingRender = null;
    _export = new TablePreviewExport(
      _owner,
      new ControlAdapter(_target),
      new PopupMenuAdapter(_menu),
      new AcceptDialogAdapter(_messageDialog),
      _ => Render(),
      _backend
    );
  }

  [Cleanup]
  public void Cleanup()
  {
    _export?.Dispose();

    // Free 而不是 QueueFree：立即释放挂在上面的菜单/对话框，避免上一个用例的
    // 独占子窗口残留到下一个用例（会打印 exclusive child 引擎 ERROR）。
    if (_owner != null && GodotObject.IsInstanceValid(_owner))
      _owner.Free();

    foreach (var file in _tempFiles)
    {
      if (File.Exists(file))
        File.Delete(file);
    }

    _tempFiles.Clear();
  }

  private Task<TableImageRenderResult> Render()
  {
    _renderCount++;

    if (_pendingRender is not null)
      return _pendingRender.Task;

    if (_failRender)
      return Task.FromResult(TableImageRenderResult.Error("渲染失败"));

    var image = Image.CreateEmpty(20, 10, false, Image.Format.Rgba8);
    image.Fill(Colors.White);
    return Task.FromResult(TableImageRenderResult.Success(image));
  }

  private static TableModel CreateModel(string cell) =>
    new()
    {
      Title = "表",
      Columns = new[] { new TableColumn { Header = "列" } },
      Rows = new[] { new TableRow { Cells = new[] { cell } } },
    };

  private static TableModel CreateEmptyModel() =>
    new() { Title = "空表", Columns = new[] { new TableColumn { Header = "列" } } };

  private void EmitGui(InputEvent @event) =>
    _target.EmitSignal(Control.SignalName.GuiInput, @event);

  /// <summary>模拟鼠标进入表格：惰性出图的主要交互入口。</summary>
  private void EnterTarget() => _target.EmitSignal(Control.SignalName.MouseEntered);

  private static InputEventMouseButton MakeMouseButton(
    MouseButton button,
    bool pressed,
    Vector2 position
  ) =>
    new()
    {
      ButtonIndex = button,
      Pressed = pressed,
      Position = position,
    };

  private static InputEventMouseMotion MouseMotion(Vector2 position, MouseButtonMask mask) =>
    new() { Position = position, ButtonMask = mask };

  private void RightClick()
  {
    EmitGui(MakeMouseButton(MouseButton.Right, true, new Vector2(10, 10)));
    EmitGui(MakeMouseButton(MouseButton.Right, false, new Vector2(10, 10)));
  }

  private PopupMenu FindMenu() => _menu;

  [Test]
  public void Update_DefersRenderUntilInteractionAndSkipsUnchangedContent()
  {
    // 惰性出图：Update 只记内容，不渲染（两表栏每次数据变化都会调它）
    _export.Update(CreateModel("A"));
    _renderCount.ShouldBe(0, "Update 不得立即出图");
    _export.HasImage.ShouldBeFalse();

    // 鼠标进入（交互）时才真正出图
    EnterTarget();
    _renderCount.ShouldBe(1);
    _export.HasImage.ShouldBeTrue();

    // 内容未变：再次 Update 不标脏，交互也不重复渲染
    _export.Update(CreateModel("A"));
    EnterTarget();
    _renderCount.ShouldBe(1);
    _export.HasImage.ShouldBeTrue();

    // 内容变化：旧图立即作废，下次交互才出新图
    _export.Update(CreateModel("B"));
    _export.HasImage.ShouldBeFalse("内容变化后旧图不得继续算作就绪");
    EnterTarget();
    _renderCount.ShouldBe(2);
    _export.HasImage.ShouldBeTrue();
  }

  [Test]
  public void Update_EmptyModel_ClearsImage()
  {
    _export.Update(CreateModel("A"));
    EnterTarget();
    _export.HasImage.ShouldBeTrue();

    _export.Update(CreateEmptyModel());
    _export.HasImage.ShouldBeFalse();
  }

  [Test]
  public void RightClick_ShowsCopyMenuAndCopiesImage()
  {
    // 没有先经过鼠标进入：右键本身也是出图入口，Fake 同步完成时菜单项即可用
    _export.Update(CreateModel("A"));
    RightClick();

    var menu = FindMenu();
    menu.ItemCount.ShouldBe(1);
    menu.GetItemText(0).ShouldBe("复制");
    menu.IsItemDisabled(0).ShouldBeFalse();

    menu.EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
    _backend.Copied.Count.ShouldBe(1);
    _backend.Copied[0].GetWidth().ShouldBe(20);
  }

  [Test]
  public void RightClick_WithoutImage_DisablesCopy()
  {
    _export.Update(CreateEmptyModel());
    RightClick();

    FindMenu().IsItemDisabled(0).ShouldBeTrue();
    FindMenu().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
    _backend.Copied.ShouldBeEmpty();
  }

  [Test]
  public void CopyFailure_ShowsMessageDialog()
  {
    _backend.FailCopy = true;
    _export.Update(CreateModel("A"));
    RightClick();

    FindMenu().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);

    // 提示框是注入的场景节点：组件只负责填内容并弹出
    _messageDialog.DialogText.ShouldBe("失败原因");
    _messageDialog.Title.ShouldBe("复制失败");
    _messageDialog.Hide();
  }

  [Test]
  public void Drag_StartsOnlyAboveThresholdAndWritesTempPng()
  {
    // 没有先经过鼠标进入：拖拽本身也是出图入口，Fake 同步完成时拖拽可以启动
    _export.Update(CreateModel("A"));

    EmitGui(MakeMouseButton(MouseButton.Left, true, new Vector2(20, 20)));
    EmitGui(MouseMotion(new Vector2(22, 22), MouseButtonMask.Left));
    _backend.DraggedFiles.ShouldBeEmpty();

    EmitGui(MouseMotion(new Vector2(160, 160), MouseButtonMask.Left));
    _backend.DraggedFiles.Count.ShouldBe(1);

    var path = _backend.DraggedFiles[0];
    _tempFiles.Add(path);
    File.Exists(path).ShouldBeTrue();
    path.ShouldEndWith(".png");
  }

  [Test]
  public void Drag_WithoutImage_DoesNotStart()
  {
    _export.Update(CreateEmptyModel());

    EmitGui(MakeMouseButton(MouseButton.Left, true, new Vector2(20, 20)));
    EmitGui(MouseMotion(new Vector2(160, 160), MouseButtonMask.Left));

    _backend.DraggedFiles.ShouldBeEmpty();
  }

  [Test]
  public void Update_RenderFailure_ClearsImageAndDisablesCopy()
  {
    _failRender = true;
    _export.Update(CreateModel("A"));
    _export.HasImage.ShouldBeFalse();

    RightClick();
    FindMenu().IsItemDisabled(0).ShouldBeTrue();

    EmitGui(MakeMouseButton(MouseButton.Left, true, new Vector2(20, 20)));
    EmitGui(MouseMotion(new Vector2(160, 160), MouseButtonMask.Left));
    _backend.DraggedFiles.ShouldBeEmpty();
  }

  /// <summary>
  /// 渲染尚未完成时，右键菜单「复制」保持禁用、拖拽不得启动（惰性出图不改变这条语义）。
  /// </summary>
  [Test]
  public async Task Interaction_BeforeImageReady_KeepsCopyDisabledAndDragBlocked()
  {
    _pendingRender = new TaskCompletionSource<TableImageRenderResult>();
    _export.Update(CreateModel("A"));
    _export.HasImage.ShouldBeFalse();

    // 首次右键触发渲染，但出图尚未完成 → 菜单项禁用、复制不生效
    RightClick();
    FindMenu().IsItemDisabled(0).ShouldBeTrue("图未就绪时「复制」应禁用");
    FindMenu().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
    _backend.Copied.ShouldBeEmpty();

    // 拖拽同样不得启动
    EmitGui(MakeMouseButton(MouseButton.Left, true, new Vector2(20, 20)));
    EmitGui(MouseMotion(new Vector2(160, 160), MouseButtonMask.Left));
    _backend.DraggedFiles.ShouldBeEmpty("图未就绪时不得启动拖拽");

    // 渲染完成后再次交互即可用
    var image = Image.CreateEmpty(20, 10, false, Image.Format.Rgba8);
    image.Fill(Colors.White);
    _pendingRender.SetResult(TableImageRenderResult.Success(image));

    for (var frame = 0; frame < 300 && !_export.HasImage; frame++)
      await _owner.ToSignal(_owner.GetTree(), SceneTree.SignalName.ProcessFrame);

    _export.HasImage.ShouldBeTrue("渲染完成后缓存应就绪");

    RightClick();
    FindMenu().IsItemDisabled(0).ShouldBeFalse();
    FindMenu().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
    _backend.Copied.Count.ShouldBe(1);
  }

  [Test]
  public async Task Dispose_ClearsImageAndUnsubscribes()
  {
    _export.Update(CreateModel("A"));
    RightClick();
    FindMenu().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
    _backend.Copied.Count.ShouldBe(1);
    FindMenu().Hide();
    _backend.Copied.Clear();

    _export.Dispose();
    _export.HasImage.ShouldBeFalse();

    // 菜单/提示框归场景所有：Dispose 不得释放它们（重新解析后可复用），只退订交互
    GodotObject.IsInstanceValid(_menu).ShouldBeTrue("Dispose 不得释放场景承载的菜单");
    GodotObject.IsInstanceValid(_messageDialog).ShouldBeTrue("Dispose 不得释放场景承载的提示框");

    // 退订后再交互：右键不再填菜单、菜单回调也不再复制
    await _owner.ToSignal(_owner.GetTree(), SceneTree.SignalName.ProcessFrame);
    RightClick();
    FindMenu().Visible.ShouldBeFalse("Dispose 后不得再响应右键弹菜单");
    FindMenu().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
    _backend.Copied.ShouldBeEmpty("Dispose 后菜单回调应已退订");
  }

  [Test]
  public void DragFailure_ShowsMessageDialog()
  {
    _backend.FailDrag = true;
    _export.Update(CreateModel("A"));

    EmitGui(MakeMouseButton(MouseButton.Left, true, new Vector2(20, 20)));
    EmitGui(MouseMotion(new Vector2(160, 160), MouseButtonMask.Left));

    // 提示框是注入的场景节点：组件只负责填内容并弹出
    _messageDialog.DialogText.ShouldBe("失败原因");
    _messageDialog.Title.ShouldBe("拖拽失败");
    _messageDialog.Hide();
  }

  /// <summary>
  /// 装配契约：菜单与提示框由宿主场景提供后注入，组件只填充/弹出它们，不改变父子关系、
  /// 不在 <see cref="TablePreviewExport.Dispose"/> 里释放（生命周期归栏面板）。
  /// </summary>
  [Test]
  public void SceneProvidedMenuAndDialog_AreFilledAndNotReparented()
  {
    _menu.GetParent().ShouldBe(_owner, "菜单应仍是宿主提供的那个场景节点");
    _messageDialog.GetParent().ShouldBe(_owner, "提示框应仍是宿主提供的那个场景节点");

    _export.Update(CreateModel("A"));
    RightClick();
    FindMenu().ItemCount.ShouldBe(1, "右键应填充注入的菜单");
    FindMenu().GetItemText(0).ShouldBe("复制");
    FindMenu().GetParent().ShouldBe(_owner, "填充/弹出不得改挂菜单父节点");

    _menu.Hide();
    _backend.FailCopy = true;
    FindMenu().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
    _messageDialog.DialogText.ShouldBe("失败原因", "失败提示应写入注入的提示框");
    _messageDialog.Title.ShouldBe("复制失败");
    _messageDialog.GetParent().ShouldBe(_owner);
    _messageDialog.Hide();
  }
}
