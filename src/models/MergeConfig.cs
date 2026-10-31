namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>整合所采用的合并算法。</summary>
public enum MergeAlgorithm
{
  /// <summary>提取式：把资源/Obj 节点按类型提取后分别重放到注入点（打散作者文件夹结构）。</summary>
  Extractive,

  /// <summary>整文件夹搬运：以「最靠近根目录的顶层文件夹」为单位整棵搬入对象注入点下。</summary>
  TopFolderCarry,
}

/// <summary>
/// 整合模块配置 + 编辑中的对应表。
/// 「不提供工程文件」是导出选项（含 .lstges 开关），非硬约束；「加密 Lua」= 对 Lua 脚本做混淆。
/// </summary>
public class MergeConfig
{
  /// <summary>工程模板路径（作为完整项目包基底，含贴图/音乐）。</summary>
  public AutoValue<string> TemplatePath { get; set; } = new(string.Empty);

  /// <summary>LuaSTG Editor Sharp 安装目录（对应 Cli.exe 与插件）。</summary>
  public AutoValue<string> SharpEditorPath { get; set; } = new(string.Empty);

  /// <summary>编译插件 dll 文件名（如 LuaSTGPlusLib.dll）。</summary>
  public AutoValue<string> PluginDll { get; set; } = new(string.Empty);

  /// <summary>完整项目包导出输出目录。</summary>
  public AutoValue<string> OutputDir { get; set; } = new(string.Empty);

  /// <summary>导出是否包含 .lstges 工程文件（默认包含；取消则「不提供工程文件」）。</summary>
  public AutoValue<bool> IncludeLstges { get; set; } = new(true);

  /// <summary>导出是否对 Lua 脚本混淆（默认不混淆）。</summary>
  public AutoValue<bool> ObfuscateLua { get; set; } = new(false);

  /// <summary>冲突处理是否自动改名资源（默认 false，保留原名）。</summary>
  public AutoValue<bool> AutoRenameConflicts { get; set; } = new(false);

  /// <summary>
  /// 是否强制把「紧邻前序为对话/出场移动」的符卡按 Perform Action 方式整合
  /// （携带其紧邻前序节点一起注入；默认 false，只按符卡自身 Performing action 属性）。
  /// 紧邻前序为前一张符卡或无前序时不触发，按普通符卡整合。
  /// </summary>
  public AutoValue<bool> ForcePerformAction { get; set; } = new(false);

  /// <summary>
  /// 注入资源/Object 节点时是否按创作者分组：为每个有贡献的包建立一个专属
  /// <c>.General.Folder</c> 文件夹（Name=创作者名），把该包节点放回各自文件夹；
  /// 默认 false 时平铺到注入点旁。符卡注入点不受此开关影响。
  /// </summary>
  public AutoValue<bool> GroupByCreatorFolders { get; set; } = new(false);

  /// <summary>
  /// 整合采用的合并算法（默认 <see cref="MergeAlgorithm.Extractive"/> 提取式）。
  /// 切换为 <see cref="MergeAlgorithm.TopFolderCarry"/> 时按顶层文件夹整棵搬运。
  /// </summary>
  public AutoValue<MergeAlgorithm> Algorithm { get; set; } = new(MergeAlgorithm.Extractive);

  /// <summary>导出完整项目包时使用的输出名（不含 .zip 后缀）。</summary>
  public AutoValue<string> OutputName { get; set; } = new("mod");

  /// <summary>当前选中的创作者包索引（-1 表示未选中）。事件只写此模型，三栏清单由绑定驱动。</summary>
  public AutoValue<int> SelectedPackageIndex { get; set; } = new(-1);

  /// <summary>
  /// 排除归档空间的额外清单（自动推导「模板已有 ArchiveSpace」之外的兜底）。
  /// 源包资源命中此清单（或其最内层归属空间在模板已有集合中）时，不检测、不导入、不复制。
  /// </summary>
  public AutoList<string> ExcludedArchiveSpaces { get; set; } = new();

  /// <summary>
  /// 右上「对应表」的「打乱模式」（默认 <see cref="MappingShuffleMode.Random"/> 完全随机）。
  /// 点击「按模式重排」按钮时按此模式重排 <see cref="Mapping"/>；下拉选择会持久化（重启回显）。
  /// 注意与 <see cref="GroupByCreatorFolders"/>（整合注入时按创作者建专属文件夹）语义无关，
  /// 此模式只描述对应表本身的重排方式。
  /// </summary>
  public AutoValue<MappingShuffleMode> ShuffleMode { get; set; } = new(MappingShuffleMode.Random);

  /// <summary>
  /// 编辑中的「符卡—创作者对应表」（顺序即注入顺序；符卡/非符分开标注）。
  /// </summary>
  public AutoList<SpellCardMappingEntry> Mapping { get; set; } = new();
}
