namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using AutoCMEX.Core.Storage;
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
/// 首次唤出 = 视口 80%；任一个对话框被拖动并关闭后，之后所有对话框共用这份尺寸（进程内全局）。
/// 拿不到树信息时回落到兜底尺寸。弹出尺寸的真实落点由面板级测试（<c>TestGifSetPanel</c>）覆盖，
/// 这里只验证策略与调用契约。
/// </remarks>
public class FileDialogSizingTest : TestClass
{
  /// <summary>待删除的临时数据根目录（跨重启用例使用）。</summary>
  private readonly List<string> _tempRoots = new();

  public FileDialogSizingTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup() => FileDialogSizing.ResetMemoryForTests();

  [Cleanup]
  public void Cleanup()
  {
    FileDialogSizing.ResetMemoryForTests();

    foreach (var root in _tempRoots)
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }

    _tempRoots.Clear();
  }

  /// <summary>建一份临时目录里的 <see cref="DataManager"/>（数据目录为 root/data）。</summary>
  private DataManager CreateDataManager()
  {
    var root = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_DialogSize_" + Guid.NewGuid().ToString("N")[..8]
    );
    _tempRoots.Add(root);

    var dataManager = new DataManager(Path.Combine(root, "data"), new AesEncryptor("test-key"));
    dataManager.LoadAll();
    return dataManager;
  }

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
  public void Popup_RememberedSizeIsSharedAcrossDialogs()
  {
    var first = new Mock<IFileDialog>();
    first.SetupProperty(d => d.Size, new Vector2I(800, 500));
    first.SetupGet(d => d.Visible).Returns(false);
    FileDialogSizing.Popup(first.Object);

    // 模拟用户拖成 1200×700 后关窗：helper 收到隐藏事件即记住该尺寸
    var resized = new Vector2I(1200, 700);
    first.Object.Size = resized;
    first.Raise(d => d.VisibilityChanged += null);
    FileDialogSizing.RememberedForTests.ShouldBe(resized);

    // 另一个按钮唤出的另一个对话框，直接沿用这份全局尺寸
    var second = new Mock<IFileDialog>();
    second.SetupProperty(d => d.Size, new Vector2I(640, 360));
    FileDialogSizing.Popup(second.Object);
    second.Verify(d => d.PopupCentered(It.Is<Vector2I?>(size => size == resized)), Times.Once);
    second.Object.Size.ShouldBe(resized);
  }

  [Test]
  public void Popup_HideWithoutResize_KeepsPolicyMemory()
  {
    // 用户没改尺寸就关窗：不能写记忆，否则主窗口之后变大，弹窗也不会跟着重算 80%
    var dialog = new Mock<IFileDialog>();
    dialog.SetupProperty(d => d.Size, new Vector2I(640, 360));
    dialog.SetupGet(d => d.Visible).Returns(false);

    FileDialogSizing.Popup(dialog.Object);
    dialog.Raise(d => d.VisibilityChanged += null);

    FileDialogSizing.RememberedForTests.ShouldBeNull("只隐藏、没拖动时不应写全局记忆");
  }

  [Test]
  public void Popup_ShowDirection_DoesNotWriteMemory()
  {
    // 显示方向的可见性变化不是「关闭」，不能写记忆
    var dialog = new Mock<IFileDialog>();
    dialog.SetupProperty(d => d.Size, new Vector2I(1200, 700));
    dialog.SetupGet(d => d.Visible).Returns(true);

    FileDialogSizing.Popup(dialog.Object);

    // 显示方向即使尺寸与本次设定不同（例如引擎修正过），也不能写记忆
    dialog.Object.Size = new Vector2I(900, 600);
    dialog.Raise(d => d.VisibilityChanged += null);

    FileDialogSizing.RememberedForTests.ShouldBeNull();
  }

  [Test]
  public void Popup_ContentMinBump_IsNotRemembered()
  {
    // 引擎把窗口抬到内容最小尺寸不是用户拖动，不能写记忆
    var dialog = new Mock<IFileDialog>();
    dialog.SetupProperty(d => d.Size, new Vector2I(640, 360));
    dialog.SetupGet(d => d.Visible).Returns(false);
    dialog.Setup(d => d.GetContentsMinimumSize()).Returns(new Vector2I(1200, 700));

    FileDialogSizing.Popup(dialog.Object);
    dialog.Object.Size = new Vector2I(1200, 700);
    dialog.Raise(d => d.VisibilityChanged += null);

    FileDialogSizing.RememberedForTests.ShouldBeNull("引擎内容最小尺寸抬升不算用户改动");
  }

  [Test]
  public void Popup_HideWithoutResize_NextDialogKeepsPolicy()
  {
    // 只关闭、没拖动：下一个对话框仍按视口 80%（80% 策略没有被冻结）
    var first = new Mock<IFileDialog>();
    first.SetupProperty(d => d.Size, new Vector2I(640, 360));
    first.SetupGet(d => d.Visible).Returns(false);
    FileDialogSizing.Popup(first.Object);
    first.Raise(d => d.VisibilityChanged += null);

    var second = new Mock<IFileDialog>();
    FileDialogSizing.Popup(second.Object);

    var expected = FileDialogSizing.Resolve(FileDialogSizing.FallbackParentSize);
    second.Verify(d => d.PopupCentered(It.Is<Vector2I?>(size => size == expected)), Times.Once);
  }

  [Test]
  public void Popup_RememberedSizeIsClampedToViewport()
  {
    // 记住一个超过视口的尺寸（Mock 无树 → 兜底视口 1280×720），再次唤出应收敛到视口
    var first = new Mock<IFileDialog>();
    first.SetupProperty(d => d.Size, new Vector2I(5000, 5000));
    first.SetupGet(d => d.Visible).Returns(false);
    FileDialogSizing.Popup(first.Object);
    first.Object.Size = new Vector2I(5000, 5000);
    first.Raise(d => d.VisibilityChanged += null);
    // 记忆保留用户原始尺寸（未被视口夹过）
    FileDialogSizing.RememberedForTests.ShouldBe(new Vector2I(5000, 5000));

    var second = new Mock<IFileDialog>();
    second.SetupProperty(d => d.Size, new Vector2I(100, 100));
    second.SetupGet(d => d.Visible).Returns(false);
    FileDialogSizing.Popup(second.Object);

    var fallback = FileDialogSizing.FallbackParentSize;
    second.Verify(d => d.PopupCentered(It.Is<Vector2I?>(size => size == fallback)), Times.Once);

    // 关掉这次被夹过的窗口，不能把「夹小后的值」写回记忆
    second.Raise(d => d.VisibilityChanged += null);
    FileDialogSizing.RememberedForTests.ShouldBe(new Vector2I(5000, 5000));
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
  public void Popup_SameDialogTwice_HooksVisibilityOnlyOnce()
  {
    // 同一实例重复弹出不能重复订阅：否则隐藏一次会回写多次、测试之间还会累积处理函数
    var dialog = new Mock<IFileDialog>();

    FileDialogSizing.Popup(dialog.Object);
    FileDialogSizing.Popup(dialog.Object);

    FileDialogSizing.HookedCountForTests.ShouldBe(1);
  }

  [Test]
  public void ResetMemoryForTests_DetachesVisibilityHandler()
  {
    var dialog = new Mock<IFileDialog>();
    dialog.SetupProperty(d => d.Size, new Vector2I(1200, 700));
    dialog.SetupGet(d => d.Visible).Returns(false);
    FileDialogSizing.Popup(dialog.Object);
    FileDialogSizing.HookedCountForTests.ShouldBe(1);

    FileDialogSizing.ResetMemoryForTests();

    FileDialogSizing.HookedCountForTests.ShouldBe(0);
    // 解绑后旧对话框再触发隐藏事件，不能写进新一轮的全局记忆
    dialog.Object.Size = new Vector2I(1200, 700);
    dialog.Raise(d => d.VisibilityChanged += null);
    FileDialogSizing.RememberedForTests.ShouldBeNull("复位后旧的隐藏回调必须已解绑");
  }

  [Test]
  public void Popup_AfterReset_HooksAgain()
  {
    // 解绑只发生在复位时；复位后再次弹出要重新挂上，不能因为「曾经挂过」就永久失效
    var dialog = new Mock<IFileDialog>();
    FileDialogSizing.Popup(dialog.Object);
    FileDialogSizing.ResetMemoryForTests();

    FileDialogSizing.Popup(dialog.Object);

    FileDialogSizing.HookedCountForTests.ShouldBe(1);
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

  // ==================== 跨重启记忆 ====================

  /// <summary>内存记忆为空时，首次弹出采用 ui_layout.json 里上次真改过的尺寸。</summary>
  [Test]
  public void Popup_WithData_UsesPersistedSizeOnFirstOpen()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.FileDialogWidth.Value = 900;
      dataManager.UiLayout.FileDialogHeight.Value = 600;

      var dialog = new Mock<IFileDialog>();
      dialog.SetupProperty(d => d.Size, Vector2I.Zero);

      FileDialogSizing.Popup(dialog.Object, dataManager);

      dialog.Verify(
        d => d.PopupCentered(It.Is<Vector2I?>(size => size == new Vector2I(900, 600))),
        Times.Once
      );
      dialog.Object.Size.ShouldBe(new Vector2I(900, 600));
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>内存记忆优先于落盘记忆（同一次运行里换过尺寸时不给旧尺寸盖回去）。</summary>
  [Test]
  public void Popup_WithData_MemoryTakesPrecedenceOverPersisted()
  {
    var dataManager = CreateDataManager();
    try
    {
      dataManager.UiLayout.FileDialogWidth.Value = 900;
      dataManager.UiLayout.FileDialogHeight.Value = 600;

      var first = new Mock<IFileDialog>();
      first.SetupProperty(d => d.Size, new Vector2I(640, 360));
      first.SetupGet(d => d.Visible).Returns(false);
      FileDialogSizing.Popup(first.Object, dataManager);

      // 用户这次拖成 1100×700 后关闭 → 内存与落盘都记成新值
      var resized = new Vector2I(1100, 700);
      first.Object.Size = resized;
      first.Raise(d => d.VisibilityChanged += null);

      var second = new Mock<IFileDialog>();
      second.SetupProperty(d => d.Size, Vector2I.Zero);
      FileDialogSizing.Popup(second.Object, dataManager);

      second.Verify(d => d.PopupCentered(It.Is<Vector2I?>(size => size == resized)), Times.Once);
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>用户真改尺寸后关闭：尺寸写回 ui_layout.json（跨重启生效）。</summary>
  [Test]
  public void Popup_WithData_HideAfterResize_WritesModel()
  {
    var dataManager = CreateDataManager();
    try
    {
      var dialog = new Mock<IFileDialog>();
      dialog.SetupProperty(d => d.Size, new Vector2I(640, 360));
      dialog.SetupGet(d => d.Visible).Returns(false);

      FileDialogSizing.Popup(dialog.Object, dataManager);

      dialog.Object.Size = new Vector2I(1111, 777);
      dialog.Raise(d => d.VisibilityChanged += null);

      dataManager.UiLayout.FileDialogWidth.Value.ShouldBe(1111);
      dataManager.UiLayout.FileDialogHeight.Value.ShouldBe(777);
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>只关闭、没改尺寸：不写落盘记忆（保持视口 80% 动态策略）。</summary>
  [Test]
  public void Popup_WithData_HideWithoutResize_DoesNotWriteModel()
  {
    var dataManager = CreateDataManager();
    try
    {
      var dialog = new Mock<IFileDialog>();
      dialog.SetupProperty(d => d.Size, new Vector2I(640, 360));
      dialog.SetupGet(d => d.Visible).Returns(false);

      FileDialogSizing.Popup(dialog.Object, dataManager);
      dialog.Raise(d => d.VisibilityChanged += null);

      dataManager.UiLayout.FileDialogWidth.Value.ShouldBeNull();
      dataManager.UiLayout.FileDialogHeight.Value.ShouldBeNull();
    }
    finally
    {
      dataManager.Dispose();
    }
  }

  /// <summary>先无参弹出、后经带数据入口补绑：补绑后隐藏才写落盘模型（不重复订阅）。</summary>
  [Test]
  public void Popup_WithoutDataThenWithData_BindsPersistedMemoryAndWritesModel()
  {
    var dataManager = CreateDataManager();
    try
    {
      var dialog = new Mock<IFileDialog>();
      dialog.SetupProperty(d => d.Size, new Vector2I(640, 360));
      dialog.SetupGet(d => d.Visible).Returns(false);

      // 无参重载没有数据来源，但同一实例随后经带数据入口弹出时应补上落盘来源
      FileDialogSizing.Popup(dialog.Object);
      FileDialogSizing.Popup(dialog.Object, dataManager);

      dialog.Object.Size = new Vector2I(1234, 567);
      dialog.Raise(d => d.VisibilityChanged += null);

      dataManager.UiLayout.FileDialogWidth.Value.ShouldBe(1234);
      dataManager.UiLayout.FileDialogHeight.Value.ShouldBe(567);
    }
    finally
    {
      dataManager.Dispose();
    }
  }
}
