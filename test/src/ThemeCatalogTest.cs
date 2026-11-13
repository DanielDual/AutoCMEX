namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX.UI;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="ThemeCatalog"/> 单元测试：清单契约、ID 解析、资源加载、窗口底色解析，
/// 以及主题资源的条目合法性 / 必备条目 / 浅色图标映射。
/// </summary>
/// <remarks>
/// 主题条目拼写错误不会报错、只会静默失效，因此必须由「默认主题条目白名单 + 必备条目 + 图标映射」
/// 三组用例兜住；这些用例只读取主题资源，不碰场景树。
/// </remarks>
public class ThemeCatalogTest : TestClass
{
  /// <summary>每套主题必须定义的 7 个业务语义色（<see cref="ThemeCatalog.AppColorsType"/> 下）。</summary>
  private static readonly string[] AppColorNames =
  {
    ThemeCatalog.WindowBackground,
    ThemeCatalog.StatusOk,
    ThemeCatalog.StatusWarn,
    ThemeCatalog.StatusError,
    ThemeCatalog.MutedText,
    ThemeCatalog.Accent,
    ThemeCatalog.LogText,
  };

  /// <summary>每套主题必须覆盖的 Control 条目（类型, 名称）；缺失会导致控件退回引擎默认观感。</summary>
  private static readonly (string Kind, string Type, string Name)[] RequiredItems =
  {
    ("stylebox", "Button", "normal"),
    ("stylebox", "Button", "hover"),
    ("stylebox", "Button", "pressed"),
    ("stylebox", "Button", "disabled"),
    ("stylebox", "Button", "focus"),
    ("color", "Button", "font_color"),
    ("stylebox", "LineEdit", "normal"),
    ("stylebox", "LineEdit", "focus"),
    ("stylebox", "Panel", "panel"),
    ("stylebox", "PanelContainer", "panel"),
    ("color", "Label", "font_color"),
    ("stylebox", "ItemList", "panel"),
    ("stylebox", "Tree", "panel"),
    ("stylebox", "PopupMenu", "panel"),
    ("stylebox", "VScrollBar", "grabber"),
    ("stylebox", "HScrollBar", "grabber"),
    ("stylebox", "Window", "embedded_border"),
    ("stylebox", "AcceptDialog", "panel"),
  };

  /// <summary>浅色主题必须覆盖的深色图标（类型, 名称）；缺失时图标在浅底上几乎不可见。</summary>
  private static readonly (string Type, string Name)[] LightThemeIcons =
  {
    ("OptionButton", "arrow"),
    ("Tree", "arrow"),
    ("Tree", "arrow_collapsed"),
    ("Tree", "arrow_collapsed_mirrored"),
    ("Tree", "checked"),
    ("Tree", "unchecked"),
    ("Tree", "indeterminate"),
    ("HSplitContainer", "grabber"),
    ("VSplitContainer", "grabber"),
    ("Window", "close"),
    ("Window", "close_pressed"),
    // FileDialog 的 menu_* 图标在运行时默认主题里是空纹理（Godot 上游行为），
    // 主题无法从引擎取图，浅色主题不引用它们；缩略图图标是真实存在的。
    ("FileDialog", "folder_thumbnail"),
    ("FileDialog", "file_thumbnail"),
  };

  /// <summary>兜底底色：刻意避开所有主题色，用于区分「取到主题色」与「用了兜底」。</summary>
  private static readonly Color FallbackBackground = new(0.25f, 0.5f, 0.75f);

  public ThemeCatalogTest(Node testScene)
    : base(testScene) { }

  // ==================== 清单契约 ====================

  /// <summary>清单固定 5 项，首项是默认项（无资源路径），顺序即下拉顺序。</summary>
  [Test]
  public void Options_HasFiveEntriesWithDefaultFirst()
  {
    ThemeCatalog.Options.Count.ShouldBe(5);

    var first = ThemeCatalog.Options[0];
    first.Id.ShouldBe(ThemeCatalog.DefaultId);
    first.ResourcePath.ShouldBeNull();

    var expectedIds = new[]
    {
      "default",
      "dark-default",
      "light-default",
      "indigo-night",
      "high-contrast",
    };
    ThemeCatalog.Options.Select(option => option.Id).ShouldBe(expectedIds);
  }

  /// <summary>ID 唯一：重复 ID 会让下拉选中项与持久化值无法一一对应。</summary>
  [Test]
  public void Options_IdsAreUnique()
  {
    var ids = ThemeCatalog.Options.Select(option => option.Id).ToList();

    ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count);
  }

  /// <summary>只有浅色主题标记 <c>IsLight</c>：它决定是否换浅色 Logo。</summary>
  [Test]
  public void Options_MarkOnlyLightDefaultAsLight()
  {
    ThemeCatalog
      .Options.Where(option => option.IsLight)
      .Select(option => option.Id)
      .ShouldBe(new[] { "light-default" });
  }

  // ==================== TryResolve ====================

  /// <summary><c>null</c> 主题 ID 回退默认项并给出错误说明，不抛异常。</summary>
  [Test]
  public void TryResolve_NullId_FallsBackToDefaultWithError()
  {
    var resolved = ThemeCatalog.TryResolve(null, out var option, out var error);

    resolved.ShouldBeFalse();
    option.Id.ShouldBe(ThemeCatalog.DefaultId);
    error.ShouldNotBeNullOrEmpty();
  }

  /// <summary>空白主题 ID（空串 / 全空格）同样回退默认项并给出错误说明。</summary>
  [Test]
  public void TryResolve_BlankId_FallsBackToDefaultWithError()
  {
    foreach (var blank in new[] { string.Empty, "   " })
    {
      var resolved = ThemeCatalog.TryResolve(blank, out var option, out var error);

      resolved.ShouldBeFalse();
      option.Id.ShouldBe(ThemeCatalog.DefaultId);
      error.ShouldNotBeNullOrEmpty();
    }
  }

  /// <summary>未知 ID 回退默认项并给出错误说明（调用方据此提示存档主题不可用）。</summary>
  [Test]
  public void TryResolve_UnknownId_FallsBackToDefaultWithError()
  {
    var resolved = ThemeCatalog.TryResolve("no-such-theme", out var option, out var error);

    resolved.ShouldBeFalse();
    option.Id.ShouldBe(ThemeCatalog.DefaultId);
    error.ShouldNotBeNullOrEmpty();
  }

  /// <summary>清单内每个 ID 都能解析回同一项，且无错误。</summary>
  [Test]
  public void TryResolve_KnownIds_ResolveToCatalogEntry()
  {
    foreach (var expected in ThemeCatalog.Options)
    {
      var resolved = ThemeCatalog.TryResolve(expected.Id, out var option, out var error);

      resolved.ShouldBeTrue();
      option.ShouldBe(expected);
      error.ShouldBeNull();
    }
  }

  /// <summary>首尾空白不影响解析：存档值手工编辑后仍可用。</summary>
  [Test]
  public void TryResolve_IdWithSurroundingWhitespace_Resolves()
  {
    var resolved = ThemeCatalog.TryResolve("  indigo-night  ", out var option, out var error);

    resolved.ShouldBeTrue();
    option.Id.ShouldBe("indigo-night");
    error.ShouldBeNull();
  }

  // ==================== TryLoad ====================

  /// <summary>默认项不加载资源：返回成功、主题为 <c>null</c>、无错误。</summary>
  [Test]
  public void TryLoad_DefaultOption_ReturnsNullThemeWithoutError()
  {
    var result = ThemeCatalog.TryLoad(ThemeCatalog.Options[0], out var theme, out var error);

    result.ShouldBeTrue();
    theme.ShouldBeNull();
    error.ShouldBeNull();
  }

  /// <summary>4 套主题都能加载成 <see cref="Theme"/>，且资源路径与清单一致。</summary>
  [Test]
  public void TryLoad_NonDefaultThemes_SucceedWithMatchingResourcePath()
  {
    foreach (var option in NonDefaultOptions)
    {
      var result = ThemeCatalog.TryLoad(option, out var theme, out var error);

      result.ShouldBeTrue($"{option.Id}: {error}");
      theme.ShouldNotBeNull(error);
      theme!.ResourcePath.ShouldBe(option.ResourcePath);
    }
  }

  /// <summary>路径不存在的主题项：加载失败、主题为 <c>null</c>，错误信息带上路径便于定位。</summary>
  [Test]
  public void TryLoad_MissingResource_FailsWithPathInError()
  {
    var option = new ThemeOption(
      "missing",
      "缺失主题",
      false,
      "res://src/assets/themes/does-not-exist.tres"
    );

    var result = ThemeCatalog.TryLoad(option, out var theme, out var error);

    result.ShouldBeFalse();
    theme.ShouldBeNull();
    error.ShouldNotBeNullOrEmpty();
    error!.ShouldContain(option.ResourcePath!);
  }

  /// <summary>资源路径为空白：加载失败并给出非空错误，不抛异常。</summary>
  [Test]
  public void TryLoad_BlankResourcePath_FailsWithError()
  {
    var option = new ThemeOption("blank", "空路径", false, "   ");

    var result = ThemeCatalog.TryLoad(option, out var theme, out var error);

    result.ShouldBeFalse();
    theme.ShouldBeNull();
    error.ShouldNotBeNullOrEmpty();
  }

  // ==================== 语义色 ====================

  /// <summary>每套主题都定义 7 个 <see cref="ThemeCatalog.AppColorsType"/> 颜色。</summary>
  [Test]
  public void NonDefaultThemes_DefineAllSevenAppColors()
  {
    foreach (var option in NonDefaultOptions)
    {
      var theme = LoadTheme(option);

      foreach (var colorName in AppColorNames)
        theme
          .HasColor(colorName, ThemeCatalog.AppColorsType)
          .ShouldBeTrue($"{option.Id} 缺少 {ThemeCatalog.AppColorsType}/{colorName}");
    }
  }

  /// <summary>有主题时窗口底色取主题的 <c>AppColors/window_background</c>。</summary>
  [Test]
  public void ResolveWindowBackground_WithTheme_ReturnsThemeColor()
  {
    var theme = LoadTheme(RequireOption("dark-default"));
    var expected = theme.GetColor(ThemeCatalog.WindowBackground, ThemeCatalog.AppColorsType);

    var actual = ThemeCatalog.ResolveWindowBackground(theme, FallbackBackground);

    actual.ShouldBe(expected);
    actual.ShouldNotBe(FallbackBackground);
  }

  /// <summary>无主题（默认主题）或主题未定义该颜色时回退兜底色。</summary>
  [Test]
  public void ResolveWindowBackground_WithoutTheme_ReturnsFallback()
  {
    ThemeCatalog.ResolveWindowBackground(null, FallbackBackground).ShouldBe(FallbackBackground);

    using var themeWithoutAppColors = new Theme();
    ThemeCatalog
      .ResolveWindowBackground(themeWithoutAppColors, FallbackBackground)
      .ShouldBe(FallbackBackground);
  }

  // ==================== 条目合法性 ====================

  /// <summary>
  /// 主题里的每个自定义条目都必须是 <see cref="ThemeDB.GetDefaultTheme"/> 中同名同类型的条目，
  /// 唯一例外是 <see cref="ThemeCatalog.AppColorsType"/>；拼写错误在此处直接失败。
  /// </summary>
  [Test]
  public void Themes_OnlyUseEntriesDeclaredInDefaultTheme()
  {
    var defaultTheme = ThemeDB.GetDefaultTheme();
    defaultTheme.ShouldNotBeNull();

    foreach (var option in NonDefaultOptions)
    {
      var violations = new List<string>();
      foreach (var entry in EnumerateEntries(LoadTheme(option)))
      {
        if (entry.Type == ThemeCatalog.AppColorsType)
          continue;

        if (!HasEntry(defaultTheme, entry))
          violations.Add($"{option.Id}: {entry.Kind}/{entry.Type}/{entry.Name}");
      }

      violations.ShouldBeEmpty(string.Join(System.Environment.NewLine, violations));
    }
  }

  /// <summary>每套主题都必须覆盖关键控件的必备条目（样式 / 字体色 / 面板 / 滚动条抓握 / 窗口边框）。</summary>
  [Test]
  public void Themes_DefineRequiredThemeItems()
  {
    foreach (var option in NonDefaultOptions)
    {
      var theme = LoadTheme(option);

      var missing = RequiredItems
        .Where(item => !HasEntry(theme, item))
        .Select(item => $"{option.Id}: {item.Kind}/{item.Type}/{item.Name}")
        .ToList();

      missing.ShouldBeEmpty(string.Join(System.Environment.NewLine, missing));
    }
  }

  /// <summary>不得覆盖字体与字号：只改颜色 / 边框 / 圆角，才能保证主题间布局零漂移。</summary>
  [Test]
  public void Themes_DoNotOverrideButtonOrLabelFont()
  {
    foreach (var option in NonDefaultOptions)
    {
      var theme = LoadTheme(option);

      theme.HasFont("font", "Button").ShouldBeFalse($"{option.Id} 不得覆盖 Button/font");
      theme
        .HasFontSize("font_size", "Button")
        .ShouldBeFalse($"{option.Id} 不得覆盖 Button/font_size");
      theme.HasFont("font", "Label").ShouldBeFalse($"{option.Id} 不得覆盖 Label/font");
      theme
        .HasFontSize("font_size", "Label")
        .ShouldBeFalse($"{option.Id} 不得覆盖 Label/font_size");
    }
  }

  // ==================== 浅色图标 ====================

  /// <summary>浅色主题必须映射 13 个实际存在的默认图标，且每个图标都有有效尺寸。</summary>
  [Test]
  public void LightTheme_MapsExpectedIcons()
  {
    var theme = LoadTheme(RequireOption("light-default"));

    foreach (var (type, name) in LightThemeIcons)
    {
      theme.HasIcon(name, type).ShouldBeTrue($"light-default 缺少图标 {type}/icons/{name}");

      var icon = theme.GetIcon(name, type);
      icon.ShouldNotBeNull($"light-default 的 {type}/icons/{name} 为空");
      (icon.GetWidth() > 0 || icon.GetHeight() > 0).ShouldBeTrue(
        $"light-default 的 {type}/icons/{name} 尺寸无效"
      );
    }
  }

  // ==================== 辅助 ====================

  /// <summary>清单中所有需要加载资源的主题项。</summary>
  private static IEnumerable<ThemeOption> NonDefaultOptions =>
    ThemeCatalog.Options.Where(option => option.ResourcePath != null);

  /// <summary>按 ID 取清单项，取不到直接失败（清单契约由前面的用例保证）。</summary>
  /// <param name="id">主题 ID。</param>
  private static ThemeOption RequireOption(string id) =>
    ThemeCatalog.Options.Single(option => option.Id == id);

  /// <summary>加载主题并在失败时让用例失败。</summary>
  /// <param name="option">待加载的主题项。</param>
  private static Theme LoadTheme(ThemeOption option)
  {
    var loaded = ThemeCatalog.TryLoad(option, out var theme, out var error);
    loaded.ShouldBeTrue($"{option.Id}: {error}");
    theme.ShouldNotBeNull(error);
    return theme!;
  }

  /// <summary>枚举主题里全部自定义条目（颜色 / 常量 / 字体 / 字号 / 图标 / StyleBox）。</summary>
  /// <param name="theme">待检查的主题。</param>
  private static IEnumerable<(string Kind, string Type, string Name)> EnumerateEntries(Theme theme)
  {
    foreach (var type in theme.GetColorTypeList())
    foreach (var name in theme.GetColorList(type.ToString()))
      yield return ("color", type.ToString(), name.ToString());

    foreach (var type in theme.GetConstantTypeList())
    foreach (var name in theme.GetConstantList(type.ToString()))
      yield return ("constant", type.ToString(), name.ToString());

    foreach (var type in theme.GetFontTypeList())
    foreach (var name in theme.GetFontList(type.ToString()))
      yield return ("font", type.ToString(), name.ToString());

    foreach (var type in theme.GetFontSizeTypeList())
    foreach (var name in theme.GetFontSizeList(type.ToString()))
      yield return ("font_size", type.ToString(), name.ToString());

    foreach (var type in theme.GetIconTypeList())
    foreach (var name in theme.GetIconList(type.ToString()))
      yield return ("icon", type.ToString(), name.ToString());

    foreach (var type in theme.GetStyleboxTypeList())
    foreach (var name in theme.GetStyleboxList(type.ToString()))
      yield return ("stylebox", type.ToString(), name.ToString());
  }

  /// <summary>判断主题中是否存在指定数据类型的条目。</summary>
  /// <param name="theme">待检查的主题（自定义主题或默认主题）。</param>
  /// <param name="entry">条目描述。</param>
  private static bool HasEntry(Theme theme, (string Kind, string Type, string Name) entry) =>
    entry.Kind switch
    {
      "color" => theme.HasColor(entry.Name, entry.Type),
      "constant" => theme.HasConstant(entry.Name, entry.Type),
      "font" => theme.HasFont(entry.Name, entry.Type),
      "font_size" => theme.HasFontSize(entry.Name, entry.Type),
      "icon" => theme.HasIcon(entry.Name, entry.Type),
      "stylebox" => theme.HasStylebox(entry.Name, entry.Type),
      _ => false,
    };
}
