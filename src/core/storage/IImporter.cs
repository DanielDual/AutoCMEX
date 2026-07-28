namespace AutoCMEX.Core.Storage;

using System.Collections.Generic;
using AutoCMEX.Models;

/// <summary>
/// 数据导入器抽象接口，定义符卡表和别名表的统一导入契约。
/// </summary>
public interface IImporter
{
  /// <summary>
  /// 导入符卡—创作者对应表。
  /// </summary>
  /// <param name="filePath">文件路径。</param>
  /// <returns>导入结果，包含 Boss 列表。</returns>
  ImportResult<List<Boss>> ImportSpellCardTable(string filePath);

  /// <summary>
  /// 导入创作者别名表。
  /// </summary>
  /// <param name="filePath">文件路径。</param>
  /// <returns>导入结果，包含别名列表。</returns>
  ImportResult<List<CreatorAlias>> ImportAliasTable(string filePath);
}
