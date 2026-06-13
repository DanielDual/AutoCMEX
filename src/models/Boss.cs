namespace AutoCMEX.Models;

using System.Collections.Generic;

/// <summary>
/// Boss 数据模型，包含符卡列表
/// </summary>
public class Boss
{
  /// <summary>Boss 名称</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>该 Boss 下的符卡列表</summary>
  public List<SpellCard> SpellCards { get; set; } = new();
}
