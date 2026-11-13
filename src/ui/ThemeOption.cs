namespace AutoCMEX.UI;

/// <summary>
/// 一套可切换界面主题的静态描述：稳定 ID、下拉显示名、明暗标记与主题资源路径。
/// </summary>
/// <remarks>
/// ID 是持久化契约的一部分（写入 <c>app_settings.json</c> 的 <c>themeId</c>），改名等于丢弃用户已选主题，
/// 必须保持稳定。<see cref="ResourcePath"/> 为 <c>null</c> 表示「默认（Godot 原生）」：不加载任何资源，
/// 界面保持引擎默认外观。
/// </remarks>
/// <param name="Id">主题稳定 ID，对应 <see cref="ThemeCatalog.Options"/> 中唯一的一项。</param>
/// <param name="DisplayName">设置页下拉中展示的中文名。</param>
/// <param name="IsLight">是否为浅色主题；浅色主题需要配套替换浅色 Logo 等贴图。</param>
/// <param name="ResourcePath">主题 <c>.tres</c> 的 <c>res://</c> 路径；<c>null</c> 表示不加载资源的默认项。</param>
public sealed record ThemeOption(string Id, string DisplayName, bool IsLight, string? ResourcePath);
