namespace AutoCMEX.Models;

using Chickensoft.Sync.Primitives;

/// <summary>
/// 创作者别名数据模型
/// </summary>
public class CreatorAlias
{
  /// <summary>创作者主名</summary>
  public string MainName { get; set; } = string.Empty;

  /// <summary>别名列表</summary>
  public AutoList<string> Aliases { get; set; } = new();
}
