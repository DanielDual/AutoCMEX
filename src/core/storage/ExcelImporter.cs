namespace AutoCMEX.Core.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoCMEX.Models;
using ClosedXML.Excel;

/// <summary>
/// Excel 导入工具
/// </summary>
public static class ExcelImporter
{
  /// <summary>
  /// 导入符卡—创作者对应表
  /// </summary>
  /// <param name="filePath">Excel 文件路径</param>
  /// <returns>导入结果</returns>
  public static ImportResult<List<Boss>> ImportSpellCardTable(string filePath)
  {
    try
    {
      using var workbook = new XLWorkbook(filePath);
      var worksheet = workbook.Worksheet(1);
      var range = worksheet.RangeUsed();
      if (range == null)
        return ImportResult<List<Boss>>.Error("文件为空");

      var rows = range.RowsUsed().ToList();

      if (rows.Count < 2)
        return ImportResult<List<Boss>>.Error("文件为空或仅包含表头");

      // 检查列数
      var headerRow = rows[0];
      var colCount = headerRow.CellsUsed().Count();
      if (colCount < 3)
        return ImportResult<List<Boss>>.Error("列缺失：对应表必须包含 Boss、符卡名、创作者三列");

      var bossMap = new Dictionary<string, Boss>();
      var bossOrder = new List<string>();

      for (int i = 1; i < rows.Count; i++)
      {
        var row = rows[i];
        var bossName = row.Cell(1).GetString().Trim();
        var cardName = row.Cell(2).GetString().Trim();
        var creator = row.Cell(3).GetString().Trim();

        if (string.IsNullOrEmpty(bossName) || string.IsNullOrEmpty(cardName))
          continue;

        if (!bossMap.TryGetValue(bossName, out var boss))
        {
          boss = new Boss { Name = bossName };
          bossMap[bossName] = boss;
          bossOrder.Add(bossName);
        }

        boss.SpellCards.Add(new SpellCard { Name = cardName, Creator = creator });
      }

      var bosses = new List<Boss>();
      foreach (var name in bossOrder)
        bosses.Add(bossMap[name]);

      return ImportResult<List<Boss>>.Success(bosses);
    }
    catch (Exception ex)
    {
      return ImportResult<List<Boss>>.Error($"导入失败：{ex.Message}");
    }
  }

  /// <summary>
  /// 导入创作者别名表
  /// </summary>
  /// <param name="filePath">Excel 文件路径</param>
  /// <returns>导入结果</returns>
  public static ImportResult<List<CreatorAlias>> ImportAliasTable(string filePath)
  {
    try
    {
      using var workbook = new XLWorkbook(filePath);
      var worksheet = workbook.Worksheet(1);
      var range = worksheet.RangeUsed();
      if (range == null)
        return ImportResult<List<CreatorAlias>>.Error("文件为空");

      var rows = range.RowsUsed().ToList();

      if (rows.Count < 2)
        return ImportResult<List<CreatorAlias>>.Error("文件为空或仅包含表头");

      var headerRow = rows[0];
      var colCount = headerRow.CellsUsed().Count();
      if (colCount < 2)
        return ImportResult<List<CreatorAlias>>.Error("列缺失：别名表必须包含主名和至少一列别名");

      var aliases = new List<CreatorAlias>();

      for (int i = 1; i < rows.Count; i++)
      {
        var row = rows[i];
        var mainName = row.Cell(1).GetString().Trim();
        if (string.IsNullOrEmpty(mainName))
          continue;

        var alias = new CreatorAlias { MainName = mainName };
        for (int col = 2; col <= colCount; col++)
        {
          var aliasName = row.Cell(col).GetString().Trim();
          if (!string.IsNullOrEmpty(aliasName))
            alias.Aliases.Add(aliasName);
        }

        aliases.Add(alias);
      }

      return ImportResult<List<CreatorAlias>>.Success(aliases);
    }
    catch (Exception ex)
    {
      return ImportResult<List<CreatorAlias>>.Error($"导入失败：{ex.Message}");
    }
  }
}
