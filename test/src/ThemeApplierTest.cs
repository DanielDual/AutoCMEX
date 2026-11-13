namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.UI;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="ThemeApplier"/> 单元测试：默认 / 成功 / 未知 ID / 资源缺失四条分支，
/// 以及「失败后能恢复」和「重复调用不叠加副作用」。
/// </summary>
/// <remarks>
/// 用委托注入收集实际应用的主题与底色，不创建任何窗口或场景节点。
/// </remarks>
public class ThemeApplierTest : TestClass
{
  /// <summary>默认底色：与所有主题的主窗口底色刻意不同，方便区分。</summary>
  private static readonly Color DefaultBackground = new(0.5f, 0.25f, 0.75f);

  public ThemeApplierTest(Node testScene)
    : base(testScene) { }

  /// <summary>默认主题：应用 <c>null</c> 主题 + 默认底色，结果成功且错误为空。</summary>
  [Test]
  public void Apply_Default_AppliesNullThemeAndDefaultBackground()
  {
    var themes = new List<Theme?>();
    var backgrounds = new List<Color>();
    var applier = CreateApplier(themes, backgrounds);

    var result = applier.Apply(ThemeCatalog.DefaultId);

    result.Ok.ShouldBeTrue();
    result.AppliedId.ShouldBe(ThemeCatalog.DefaultId);
    result.Error.ShouldBeNull();
    themes.Count.ShouldBe(1);
    themes[0].ShouldBeNull();
    backgrounds.Count.ShouldBe(1);
    backgrounds[0].ShouldBe(DefaultBackground);
  }

  /// <summary><c>null</c> 主题 ID 按默认主题处理（启动时存档缺失即走此分支）。</summary>
  [Test]
  public void Apply_NullId_AppliesDefaultWithoutError()
  {
    var themes = new List<Theme?>();
    var backgrounds = new List<Color>();
    var applier = CreateApplier(themes, backgrounds);

    var result = applier.Apply(null);

    result.Ok.ShouldBeTrue();
    result.AppliedId.ShouldBe(ThemeCatalog.DefaultId);
    result.Error.ShouldBeNull();
    themes.Count.ShouldBe(1);
    themes[0].ShouldBeNull();
    backgrounds[0].ShouldBe(DefaultBackground);
  }

  /// <summary>暗夜主题：应用非空 <see cref="Theme"/>，底色取自主题的 <c>AppColors/window_background</c>。</summary>
  [Test]
  public void Apply_DarkDefault_AppliesThemeAndThemeBackground()
  {
    var themes = new List<Theme?>();
    var backgrounds = new List<Color>();
    var applier = CreateApplier(themes, backgrounds);
    var option = RequireOption("dark-default");

    var result = applier.Apply("dark-default");

    result.Ok.ShouldBeTrue();
    result.AppliedId.ShouldBe("dark-default");
    result.Error.ShouldBeNull();
    themes.Count.ShouldBe(1);
    themes[0].ShouldNotBeNull();
    themes[0]!.ResourcePath.ShouldBe(option.ResourcePath);
    backgrounds.Count.ShouldBe(1);
    backgrounds[0]
      .ShouldBe(themes[0]!.GetColor(ThemeCatalog.WindowBackground, ThemeCatalog.AppColorsType));
    backgrounds[0].ShouldNotBe(DefaultBackground);
  }

  /// <summary>未知 ID：回退默认外观，<c>Ok = false</c> 且带非空原因。</summary>
  [Test]
  public void Apply_UnknownId_FallsBackToDefaultWithError()
  {
    var themes = new List<Theme?>();
    var backgrounds = new List<Color>();
    var applier = CreateApplier(themes, backgrounds);

    var result = applier.Apply("no-such-theme");

    result.Ok.ShouldBeFalse();
    result.AppliedId.ShouldBe(ThemeCatalog.DefaultId);
    result.Error.ShouldNotBeNullOrEmpty();
    themes.Count.ShouldBe(1);
    themes[0].ShouldBeNull();
    backgrounds[0].ShouldBe(DefaultBackground);
  }

  /// <summary>注入自定义清单里的坏路径主题项：加载失败，回退默认外观并带路径错误。</summary>
  [Test]
  public void Apply_OptionsWithMissingResource_FallsBackToDefaultWithError()
  {
    var themes = new List<Theme?>();
    var backgrounds = new List<Color>();
    var brokenPath = "res://src/assets/themes/__no_such_theme__.tres";
    var options = new List<ThemeOption>
    {
      new(ThemeCatalog.DefaultId, "默认（Godot 原生）", false, null),
      new("broken", "坏主题", false, brokenPath),
    };
    var applier = CreateApplier(themes, backgrounds, options);

    var result = applier.Apply("broken");

    result.Ok.ShouldBeFalse();
    result.AppliedId.ShouldBe(ThemeCatalog.DefaultId);
    result.Error.ShouldNotBeNullOrEmpty();
    result.Error!.ShouldContain(brokenPath);
    themes.Count.ShouldBe(1);
    themes[0].ShouldBeNull();
    backgrounds[0].ShouldBe(DefaultBackground);
  }

  /// <summary>失败后再应用合法主题：结果恢复为成功，界面使用新主题与新底色。</summary>
  [Test]
  public void Apply_AfterFailure_ValidThemeRecovers()
  {
    var themes = new List<Theme?>();
    var backgrounds = new List<Color>();
    var applier = CreateApplier(themes, backgrounds);

    var failed = applier.Apply("no-such-theme");
    var recovered = applier.Apply("light-default");

    failed.Ok.ShouldBeFalse();
    recovered.Ok.ShouldBeTrue();
    recovered.AppliedId.ShouldBe("light-default");
    recovered.Error.ShouldBeNull();

    themes.Count.ShouldBe(2);
    themes[0].ShouldBeNull();
    themes[1].ShouldNotBeNull();
    themes[1]!.ResourcePath.ShouldBe(RequireOption("light-default").ResourcePath);
    backgrounds.Count.ShouldBe(2);
    backgrounds[0].ShouldBe(DefaultBackground);
    backgrounds[1]
      .ShouldBe(themes[1]!.GetColor(ThemeCatalog.WindowBackground, ThemeCatalog.AppColorsType));
  }

  /// <summary>重复应用同一主题：每次只应用一次，不累积额外调用。</summary>
  [Test]
  public void Apply_SameThemeTwice_DoesNotAccumulateSideEffects()
  {
    var themes = new List<Theme?>();
    var backgrounds = new List<Color>();
    var applier = CreateApplier(themes, backgrounds);

    var first = applier.Apply("indigo-night");
    var second = applier.Apply("indigo-night");

    first.Ok.ShouldBeTrue();
    second.Ok.ShouldBeTrue();
    themes.Count.ShouldBe(2);
    backgrounds.Count.ShouldBe(2);
    themes[0]!.ResourcePath.ShouldBe(themes[1]!.ResourcePath);
    backgrounds[0].ShouldBe(backgrounds[1]);
  }

  // ==================== 辅助 ====================

  /// <summary>创建注入收集委托的应用器。</summary>
  /// <param name="themes">收集每次应用的主题（<c>null</c> 表示默认外观）。</param>
  /// <param name="backgrounds">收集每次应用的窗口底色。</param>
  /// <param name="options">可选的自定义主题清单；省略时用 <see cref="ThemeCatalog.Options"/>。</param>
  private static ThemeApplier CreateApplier(
    List<Theme?> themes,
    List<Color> backgrounds,
    IReadOnlyList<ThemeOption>? options = null
  ) => new(DefaultBackground, themes.Add, backgrounds.Add, options);

  /// <summary>按 ID 取清单项，取不到直接失败。</summary>
  /// <param name="id">主题 ID。</param>
  private static ThemeOption RequireOption(string id)
  {
    ThemeCatalog.TryResolve(id, out var option, out var error).ShouldBeTrue(error);
    return option;
  }
}
