namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

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

  /// <summary>
  /// 编辑中的「符卡—创作者对应表」（顺序即注入顺序；符卡/非符分开标注）。
  /// </summary>
  public AutoList<SpellCardMappingEntry> Mapping { get; set; } = new();
}
