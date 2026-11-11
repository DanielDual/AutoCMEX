namespace AutoCMEX.UI;

/// <summary>
/// UI 布局记忆的语义键：落盘到 <c>ui_layout.json</c> 的 <c>splits[].key</c>，运行期用于把
/// 「数据里的某条分割条记录」与「场景里的某个 <c>SplitContainer</c>」对上。
/// </summary>
/// <remarks>
/// <para>
/// 键由 UI 层定义、模型层只存取不解释（见 <see cref="AutoCMEX.Models.SplitLayoutRecord"/>）；
/// UI 找不到对应分割条时忽略该记录，因此旧键不会让新版本启动失败。
/// </para>
/// <para>
/// 键名是持久化契约的一部分：改名等于丢弃用户已保存的对应布局，必须保持稳定。
/// </para>
/// </remarks>
public static class UiLayoutKeys
{
  /// <summary>主窗口导航栏：<c>MainWindow.tscn</c> 的 <c>MainContainer</c>（HSplitContainer，左导航 / 右内容）。</summary>
  public const string MainNav = "main.nav";

  /// <summary>整合板块根分割条：<c>MergePanel.tscn</c> 的 <c>RootSplit</c>（左 / 右两栏）。</summary>
  public const string MergeRoot = "merge.root";

  /// <summary>整合板块左栏分割条：<c>MergePanel.tscn</c> 的 <c>RootSplit/LeftSplit</c>（左上 / 左下）。</summary>
  public const string MergeLeft = "merge.left";

  /// <summary>整合板块右栏分割条：<c>MergePanel.tscn</c> 的 <c>RootSplit/RightSplit</c>（右上 / 右下）。</summary>
  public const string MergeRight = "merge.right";

  /// <summary>猜测板块内容分割条：<c>GuessingPanel.tscn</c> 的 <c>MainContainer/ContentArea</c>（左 / 右两栏）。</summary>
  public const string GuessingContent = "guessing.content";

  /// <summary>猜测板块左栏分割条：<c>GuessingPanel.tscn</c> 的 <c>MainContainer/ContentArea/LeftSplit</c>（符卡 / 别名）。</summary>
  public const string GuessingLeft = "guessing.left";

  /// <summary>设置板块内容分割条：<c>SettingsPanel.tscn</c> 的 <c>MainContainer/ContentArea</c>（类别 / 配置两栏）。</summary>
  public const string SettingsContent = "settings.content";
}
