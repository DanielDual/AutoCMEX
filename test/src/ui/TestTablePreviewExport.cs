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
/// <see cref="TablePreviewExport"/> 单测：缓存去重、右键复制、拖动阈值与失败提示。
/// </summary>
/// <remarks>
/// 后端与出图都注入 Fake，不触发原生剪贴板/拖放；真实原生调用由 Windows 真机验收覆盖。
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
  private FakeBackend _backend = default!;
  private TablePreviewExport _export = default!;
  private int _renderCount;
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

    _backend = new FakeBackend();
    _renderCount = 0;
    _export = new TablePreviewExport(_owner, new ControlAdapter(_target), _ => Render(), _backend);
  }

  [Cleanup]
  public void Cleanup()
  {
    _export?.Dispose();

    if (_owner != null && !_owner.IsQueuedForDeletion())
      _owner.QueueFree();

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

  private PopupMenu FindMenu() => _owner.GetChildren().OfType<PopupMenu>().Single();

  [Test]
  public void Update_RendersOnceAndSkipsUnchangedContent()
  {
    _export.Update(CreateModel("A"));
    _export.HasImage.ShouldBeTrue();
    _renderCount.ShouldBe(1);

    _export.Update(CreateModel("A"));
    _renderCount.ShouldBe(1);

    _export.Update(CreateModel("B"));
    _renderCount.ShouldBe(2);
  }

  [Test]
  public void Update_EmptyModel_ClearsImage()
  {
    _export.Update(CreateModel("A"));
    _export.HasImage.ShouldBeTrue();

    _export.Update(CreateEmptyModel());
    _export.HasImage.ShouldBeFalse();
  }

  [Test]
  public void RightClick_ShowsCopyMenuAndCopiesImage()
  {
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

    var dialog = _owner.GetChildren().OfType<AcceptDialog>().Single();
    dialog.DialogText.ShouldBe("失败原因");
  }

  [Test]
  public void Drag_StartsOnlyAboveThresholdAndWritesTempPng()
  {
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
  public void DragFailure_ShowsMessageDialog()
  {
    _backend.FailDrag = true;
    _export.Update(CreateModel("A"));

    EmitGui(MakeMouseButton(MouseButton.Left, true, new Vector2(20, 20)));
    EmitGui(MouseMotion(new Vector2(160, 160), MouseButtonMask.Left));

    var dialog = _owner.GetChildren().OfType<AcceptDialog>().Single();
    dialog.DialogText.ShouldBe("失败原因");
  }
}
