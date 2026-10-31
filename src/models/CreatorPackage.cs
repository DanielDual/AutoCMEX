namespace AutoCMEX.Models;

using System.Collections.Generic;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 创作者包数据模型：标识一个已导入的创作者提交（zip）。
/// 创作者名从包名推导（可由用户编辑）；源路径用于按需重新解析检测「符卡/资源/Object」。
/// 导入时同步缓存该包内抽出的「符卡/资源/对象」清单，供四栏联动 UI 直接展示，无需重复解压解析。
/// </summary>
public class CreatorPackage
{
  /// <summary>创作者包名（稳定标识）。</summary>
  public string PackageName { get; set; } = string.Empty;

  /// <summary>创作者名（从包名推导，可编辑）。</summary>
  public AutoValue<string> CreatorName { get; set; } = new(string.Empty);

  /// <summary>创作者包 zip 的源路径。</summary>
  public AutoValue<string> SourcePath { get; set; } = new(string.Empty);

  /// <summary>是否标记为删除（软删除，保存后在表中移除）。</summary>
  public AutoValue<bool> IsDeleted { get; set; } = new(false);

  /// <summary>包内抽出的符卡清单（含非符，按抽取顺序）。仅用于 UI 展示。</summary>
  public List<string> SpellCards { get; set; } = new();

  /// <summary>包内检测到的资源路径清单。仅用于 UI 展示。</summary>
  public List<string> Resources { get; set; } = new();

  /// <summary>包内检测到的对象/定义清单（"类型:名称"）。仅用于 UI 展示。</summary>
  public List<string> Objects { get; set; } = new();
}
