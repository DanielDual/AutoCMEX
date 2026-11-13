namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.UI;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="AppPalette"/> 单元测试：主题定义了语义色时取主题色，主题缺失 / 缺项 / 节点为空时回退兜底色，
/// 以及 Node 重载对真实控件、非控件节点的归一化。
/// </summary>
/// <remarks>
/// 纯逻辑用例：只建真实 <see cref="Theme"/> 与 <see cref="Label"/>（挂进测试场景，主题链才生效），
/// 不实例化任何项目场景，也不依赖运行时主题切换。
/// </remarks>
public class AppPaletteTest : TestClass
{
  /// <summary>兜底色：刻意与主题色不同，用来区分「读到主题色」与「用了兜底」。</summary>
  private static readonly Color Fallback = new(0.25f, 0.5f, 0.75f);

  /// <summary>主题里定义的语义色。</summary>
  private static readonly Color Themed = new(0.62f, 0.86f, 0.52f);

  private readonly List<Node> _toCleanup = new();

  public AppPaletteTest(Node testScene)
    : base(testScene) { }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
        node.Free();
    }

    _toCleanup.Clear();
  }

  /// <summary>建一个只定义 <see cref="ThemeCatalog.AppColorsType"/> 下某色的主题。</summary>
  /// <param name="colorName">颜色项名。</param>
  /// <param name="color">颜色值。</param>
  /// <returns>新主题。</returns>
  private static Theme CreateThemeWith(string colorName, Color color)
  {
    var theme = new Theme();
    theme.SetColor(colorName, ThemeCatalog.AppColorsType, color);
    return theme;
  }

  /// <summary>建一个挂了指定主题的 Label 并接进测试场景（主题链只在树内生效）。</summary>
  /// <param name="theme">要挂的主题；<c>null</c> 表示不挂主题。</param>
  /// <returns>已入树的 Label。</returns>
  private Label CreateLabel(Theme? theme)
  {
    var label = new Label();
    if (theme is not null)
      label.Theme = theme;

    TestScene.AddChild(label);
    _toCleanup.Add(label);
    return label;
  }

  /// <summary>建一个普通节点（非 Control）并接进测试场景。</summary>
  /// <returns>已入树的节点。</returns>
  private Node CreatePlainNode()
  {
    var node = new Node();
    TestScene.AddChild(node);
    _toCleanup.Add(node);
    return node;
  }

  // ==================== Control 重载 ====================

  /// <summary>主题定义了该语义色：返回主题色，而不是兜底色。</summary>
  [Test]
  public void Get_ControlWithAppColors_ReturnsThemeColor()
  {
    var label = CreateLabel(CreateThemeWith(ThemeCatalog.StatusOk, Themed));

    var color = AppPalette.Get(label, ThemeCatalog.StatusOk, Fallback);

    color.IsEqualApprox(Themed).ShouldBeTrue($"{color} != {Themed}");
  }

  /// <summary>主题定义了别的语义色、但没有这一个：回退兜底色。</summary>
  [Test]
  public void Get_ControlMissingThatColor_ReturnsFallback()
  {
    var label = CreateLabel(CreateThemeWith(ThemeCatalog.StatusWarn, Themed));

    var color = AppPalette.Get(label, ThemeCatalog.StatusOk, Fallback);

    color.IsEqualApprox(Fallback).ShouldBeTrue($"{color} != {Fallback}");
  }

  /// <summary>没挂任何主题（走引擎默认主题，不含 AppColors）：回退兜底色。</summary>
  [Test]
  public void Get_ControlWithoutTheme_ReturnsFallback()
  {
    var label = CreateLabel(null);

    var color = AppPalette.Get(label, ThemeCatalog.StatusOk, Fallback);

    color.IsEqualApprox(Fallback).ShouldBeTrue($"{color} != {Fallback}");
  }

  /// <summary><c>null</c> 控件：直接回退兜底色，不抛异常。</summary>
  [Test]
  public void Get_NullControl_ReturnsFallback()
  {
    var color = AppPalette.Get((Control)null!, ThemeCatalog.StatusOk, Fallback);

    color.IsEqualApprox(Fallback).ShouldBeTrue($"{color} != {Fallback}");
  }

  // ==================== Node 重载 ====================

  /// <summary>真实 Label 走 Node 重载：与 Control 重载读到同一个主题色。</summary>
  [Test]
  public void Get_NodeOverloadWithLabel_ReturnsThemeColor()
  {
    var label = CreateLabel(CreateThemeWith(ThemeCatalog.Accent, Themed));

    // 显式转成 Node 才会命中 Node 重载（Label 直接传会优先匹配更具体的 Control 重载）
    var color = AppPalette.Get((Node)label, ThemeCatalog.Accent, Fallback);

    color.IsEqualApprox(Themed).ShouldBeTrue($"{color} != {Themed}");
  }

  /// <summary>非 Control 的节点（没有主题链上的颜色容器）：回退兜底色。</summary>
  [Test]
  public void Get_NodeOverloadWithNonControlNode_ReturnsFallback()
  {
    var node = CreatePlainNode();

    var color = AppPalette.Get(node, ThemeCatalog.Accent, Fallback);

    color.IsEqualApprox(Fallback).ShouldBeTrue($"{color} != {Fallback}");
  }

  /// <summary><c>null</c> 节点：直接回退兜底色，不抛异常。</summary>
  [Test]
  public void Get_NullNode_ReturnsFallback()
  {
    var color = AppPalette.Get((Node)null!, ThemeCatalog.Accent, Fallback);

    color.IsEqualApprox(Fallback).ShouldBeTrue($"{color} != {Fallback}");
  }

  /// <summary>主题里没有 AppColors 但定义了常规控件色：语义色仍回退兜底色（不会误取 font_color）。</summary>
  [Test]
  public void Get_ControlWithOnlyControlColors_ReturnsFallback()
  {
    var theme = new Theme();
    theme.SetColor("font_color", "Label", Themed);
    var label = CreateLabel(theme);

    var color = AppPalette.Get(label, ThemeCatalog.StatusOk, Fallback);

    color.IsEqualApprox(Fallback).ShouldBeTrue($"{color} != {Fallback}");
  }

  /// <summary>子控件继承父容器上的主题：语义色沿主题链向上解析。</summary>
  [Test]
  public void Get_ChildControl_ResolvesColorFromAncestorTheme()
  {
    var container = new PanelContainer { Theme = CreateThemeWith(ThemeCatalog.MutedText, Themed) };
    TestScene.AddChild(container);
    _toCleanup.Add(container);

    var label = new Label();
    container.AddChild(label);

    var color = AppPalette.Get(label, ThemeCatalog.MutedText, Fallback);

    color.IsEqualApprox(Themed).ShouldBeTrue($"{color} != {Themed}");
  }
}
