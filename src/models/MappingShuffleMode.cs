namespace AutoCMEX.Models;

/// <summary>
/// 对应表（<see cref="MergeConfig.Mapping"/>）的「打乱模式」：点击「按模式重排」时按此模式重排注入顺序。
/// </summary>
public enum MappingShuffleMode
{
  /// <summary>完全随机（Fisher-Yates 洗牌）。</summary>
  Random,

  /// <summary>
  /// 非符/符卡交替插花：**形态由两类数量决定**（非符块与符卡块轮转、多者块长非降、首块恒为非符），
  /// 非符池与符卡池则各自随机洗牌 —— 即「形态固定、内容每次随机」。
  /// </summary>
  Interleave,

  /// <summary>
  /// 按创作者聚集：**分组固定**（组间顺序取创作者首次出现的先后），组内元素随机洗牌。
  /// </summary>
  GroupByCreator,
}
