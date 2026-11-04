namespace AutoCMEX;

using AutoCMEX.UI;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// <see cref="FileDialogSizing"/> 的尺寸策略与弹出行为测试。
/// </summary>
/// <remarks>
/// 首次唤出 = 主窗口 80%，再次唤出沿用当前尺寸；拿不到树信息时回落到兜底尺寸。
/// 弹出尺寸的真实落点由面板级测试（<c>TestGifSetPanel</c>）覆盖，这里只验证策略与调用契约。
/// </remarks>
public class FileDialogSizingTest : TestClass
{
  public FileDialogSizingTest(Node testScene)
    : base(testScene) { }

  // ==================== Resolve Tests ====================

  [Test]
  public void Resolve_DefaultWindow_TakesEightyPercent() =>
    FileDialogSizing.Resolve(new Vector2I(1280, 720)).ShouldBe(new Vector2I(1024, 576));

  [Test]
  public void Resolve_LargerWindow_ScalesWithWindow() =>
    FileDialogSizing.Resolve(new Vector2I(1600, 900)).ShouldBe(new Vector2I(1280, 720));

  [Test]
  public void Resolve_SmallWindow_KeepsEightyPercent() =>
    FileDialogSizing.Resolve(new Vector2I(900, 600)).ShouldBe(new Vector2I(720, 480));

  [Test]
  public void Resolve_RoundsToNearestPixel() =>
    FileDialogSizing.Resolve(new Vector2I(1001, 701)).ShouldBe(new Vector2I(801, 561));

  [Test]
  public void Resolve_NoWindowInfo_FallsBackToDefaultParentSize()
  {
    var expected = FileDialogSizing.Resolve(FileDialogSizing.FallbackParentSize);
    expected.ShouldBe(new Vector2I(1024, 576));

    FileDialogSizing.Resolve(Vector2I.Zero).ShouldBe(expected);
    FileDialogSizing.Resolve(new Vector2I(0, 720)).ShouldBe(expected);
  }

  // ==================== Popup Tests ====================

  [Test]
  public void Popup_FirstOpen_AppliesPolicySize()
  {
    var dialog = new Mock<IFileDialog>();

    FileDialogSizing.Popup(dialog.Object);

    var expected = FileDialogSizing.Resolve(FileDialogSizing.FallbackParentSize);
    dialog.Verify(d => d.PopupCentered(It.Is<Vector2I?>(size => size == expected)), Times.Once);
  }

  [Test]
  public void Popup_SecondOpen_ReusesCurrentSize()
  {
    var dialog = new Mock<IFileDialog>();
    dialog.SetupProperty(d => d.Size, new Vector2I(1200, 700));

    FileDialogSizing.Popup(dialog.Object);
    FileDialogSizing.Popup(dialog.Object);

    // 第二次不带尺寸：PopupCentered() 等价于沿用对话框当前 Size
    dialog.Verify(d => d.PopupCentered(It.Is<Vector2I?>(size => size == null)), Times.Once);
  }

  [Test]
  public void Popup_FirstOpen_RestoresSizeAfterPopupClamp()
  {
    // popup 时引擎会把嵌入子窗口钳到屏幕可用区域的像素值（内容缩放 < 1 时会钳小），
    // helper 必须在弹完补回目标尺寸
    var dialog = new Mock<IFileDialog>();
    dialog.SetupProperty(d => d.Size, new Vector2I(640, 360));

    FileDialogSizing.Popup(dialog.Object);

    var expected = FileDialogSizing.Resolve(FileDialogSizing.FallbackParentSize);
    dialog.Object.Size.ShouldBe(expected);
  }

  [Test]
  public void Popup_WithoutTree_UsesFallbackParentSizeAndDoesNotThrow()
  {
    // 未入树的 Mock：GetTree() 为 null，弹窗必须回落到兜底尺寸而不是抛异常
    var dialog = new Mock<IFileDialog>();

    Should.NotThrow(() => FileDialogSizing.Popup(dialog.Object));

    var expected = FileDialogSizing.Resolve(FileDialogSizing.FallbackParentSize);
    dialog.Verify(d => d.PopupCentered(It.Is<Vector2I?>(size => size == expected)), Times.Once);
  }
}
