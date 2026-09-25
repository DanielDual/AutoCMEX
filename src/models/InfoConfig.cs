namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// 信息板块的持久化配置（落盘 <c>info_config.json</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>merge_config.json</c> 同构：由 <c>DataManager</c> 统一加载与保存，复用其 1500ms
/// 防抖自动保存链路，不另起持久化路径。目标群聊列表属标量/列表类配置，落在
/// <see cref="AppSettings"/>，不在此处。
/// </para>
/// <para>
/// 属性用 <see cref="AutoValue{T}"/> / <see cref="AutoList{T}"/> 包装，便于 UI 以
/// <c>Bind().OnValue()</c> / <c>Bind().OnModify()</c> 驱动刷新，并由 <c>DataManager</c> 的
/// <c>AutoValueJsonConverterFactory</c> 与 <c>AutoListConverter</c> 正确序列化。
/// </para>
/// </remarks>
public class InfoConfig
{
  /// <summary>活动规则（纯文本，原样展示与发布）。</summary>
  public AutoValue<string> ActivityRule { get; set; } = new(string.Empty);

  /// <summary>已导入的符卡 GIF 集注册表（按导入顺序）。</summary>
  public AutoList<GifSetRecord> GifSets { get; set; } = new();

  /// <summary>当前选中的 GIF 集 Id；为空表示未选中任何集。</summary>
  public AutoValue<string> ActiveGifSetId { get; set; } = new(string.Empty);

  /// <summary>
  /// 恢复被 JSON 显式 <c>null</c> 覆盖的自动同步属性。
  /// </summary>
  /// <remarks>
  /// 与 <see cref="AppSettings.EnsureIntegrity"/> 同理：System.Text.Json 反序列化会把显式
  /// <c>null</c> 写入 AutoValue/AutoList 属性，令依赖方调用 <c>Bind()</c> 时抛
  /// <see cref="System.NullReferenceException"/>。加载后必须回填默认值。
  /// </remarks>
  public void EnsureIntegrity()
  {
    ActivityRule ??= new(string.Empty);
    GifSets ??= new();
    ActiveGifSetId ??= new(string.Empty);

    foreach (var set in GifSets)
    {
      set?.EnsureIntegrity();
    }
  }
}

/// <summary>
/// 一个已导入的符卡 GIF 集的注册记录。
/// </summary>
public class GifSetRecord
{
  /// <summary>集唯一标识（导入时生成，用于切换当前集与幂等更新）。</summary>
  public AutoValue<string> Id { get; set; } = new(string.Empty);

  /// <summary>集显示名（取清单 <c>setName</c>，为空时回退集目录名）。</summary>
  public AutoValue<string> SetName { get; set; } = new(string.Empty);

  /// <summary>集根目录绝对路径（GIF 与清单所在目录）。</summary>
  public AutoValue<string> RootPath { get; set; } = new(string.Empty);

  /// <summary>导入时间（本地时间字符串，仅展示）。</summary>
  public AutoValue<string> ImportedAt { get; set; } = new(string.Empty);

  /// <summary>导入时的条目数（= 清单条目数，用于列表摘要展示）。</summary>
  public AutoValue<int> EntryCount { get; set; } = new(0);

  /// <summary>
  /// 导入时校验通过的清单快照。
  /// </summary>
  /// <remarks>
  /// 快照用于列表展示与整条发布编排，避免每次重新读盘解析；严格导入校验已保证快照与集内
  /// 文件一一对应。文件在导入后被外部移动/删除属异常情形，由发布前预检拦截并明确报错。
  /// </remarks>
  public GifSetManifest Manifest { get; set; } = new();

  /// <summary>
  /// 恢复被 JSON 显式 <c>null</c> 覆盖的自动同步属性。
  /// </summary>
  public void EnsureIntegrity()
  {
    Id ??= new(string.Empty);
    SetName ??= new(string.Empty);
    RootPath ??= new(string.Empty);
    ImportedAt ??= new(string.Empty);
    EntryCount ??= new(0);
    Manifest ??= new GifSetManifest();
    Manifest.Entries ??= new();
  }
}
