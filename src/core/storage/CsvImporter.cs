namespace AutoCMEX.Core.Storage;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AutoCMEX.Models;
using CsvHelper;
using CsvHelper.Configuration;

/// <summary>
/// CSV 导入工具
/// </summary>
public static class CsvImporter
{
  /// <summary>
  /// 导入符卡—创作者对应表
  /// </summary>
  /// <param name="filePath">CSV 文件路径</param>
  /// <returns>导入结果</returns>
  public static ImportResult<List<Boss>> ImportSpellCardTable(string filePath)
  {
    try
    {
      using var reader = new StreamReader(filePath, Encoding.UTF8);
      using var csv = new CsvReader(
        reader,
        new CsvConfiguration(CultureInfo.InvariantCulture)
        {
          HasHeaderRecord = true,
          MissingFieldFound = null,
          HeaderValidated = null,
          TrimOptions = TrimOptions.Trim,
        }
      );

      csv.Read();
      csv.ReadHeader();
      var headers = csv.HeaderRecord;

      if (headers == null || headers.Length < 3)
        return ImportResult<List<Boss>>.Error("列缺失：对应表必须包含 Boss、符卡名、创作者三列");

      var bossMap = new Dictionary<string, Boss>();
      var bossOrder = new List<string>();

      while (csv.Read())
      {
        var bossName = csv.GetField(0)?.Trim() ?? string.Empty;
        var cardName = csv.GetField(1)?.Trim() ?? string.Empty;
        var creator = csv.GetField(2)?.Trim() ?? string.Empty;

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
  /// <param name="filePath">CSV 文件路径</param>
  /// <returns>导入结果</returns>
  public static ImportResult<List<CreatorAlias>> ImportAliasTable(string filePath)
  {
    try
    {
      using var reader = new StreamReader(filePath, Encoding.UTF8);
      using var csv = new CsvReader(
        reader,
        new CsvConfiguration(CultureInfo.InvariantCulture)
        {
          HasHeaderRecord = true,
          MissingFieldFound = null,
          HeaderValidated = null,
          TrimOptions = TrimOptions.Trim,
        }
      );

      csv.Read();
      csv.ReadHeader();
      var headers = csv.HeaderRecord;

      if (headers == null || headers.Length < 2)
        return ImportResult<List<CreatorAlias>>.Error("列缺失：别名表必须包含主名和至少一列别名");

      var aliases = new List<CreatorAlias>();

      while (csv.Read())
      {
        var mainName = csv.GetField(0)?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(mainName))
          continue;

        var alias = new CreatorAlias { MainName = mainName };
        for (int i = 1; i < headers.Length; i++)
        {
          var aliasName = csv.GetField(i)?.Trim();
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
