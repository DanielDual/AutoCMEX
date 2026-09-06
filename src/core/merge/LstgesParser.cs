namespace AutoCMEX.Core.Merge;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// 解析 LuaSTG Editor Sharp 工程文件（<c>.lstges</c>/<c>.lstgproj</c>）。
/// 文件为每行 <c>{level},{JSON}</c>，第一列是层级数字，其余为 JSON 节点对象。
/// </summary>
public static class LstgesParser
{
  /// <summary>
  /// 从文本解析为节点列表。<c>null</c> 表示解析失败（并返回错误信息）。
  /// </summary>
  /// <param name="text">文件文本。</param>
  /// <param name="error">解析失败时的错误信息。</param>
  public static List<LstgesNode>? Parse(string text, out string? error)
  {
    error = null;
    var nodes = new List<LstgesNode>();

    // 空/空白输入视为非法的工程文件
    if (string.IsNullOrWhiteSpace(text))
    {
      error = "文件内容为空，无法解析为工程文件";
      return null;
    }

    using var reader = new StringReader(text);
    int lineNumber = 0;
    string? raw;
    while ((raw = reader.ReadLine()) != null)
    {
      lineNumber++;
      var line = raw.Trim();
      if (string.IsNullOrEmpty(line))
        continue;

      int comma = line.IndexOf(',');
      if (comma <= 0)
      {
        error = $"第 {lineNumber} 行缺少层级逗号：{Truncate(line)}";
        return null;
      }

      if (!int.TryParse(line.AsSpan(0, comma), out var level))
      {
        error = $"第 {lineNumber} 行层级不是数字：{Truncate(line[..comma])}";
        return null;
      }

      var jsonText = line[(comma + 1)..];
      JsonNode? json;
      try
      {
        json = JsonNode.Parse(jsonText);
      }
      catch (JsonException ex)
      {
        error = $"第 {lineNumber} 行 JSON 解析失败：{ex.Message}";
        return null;
      }

      nodes.Add(new LstgesNode { Level = level, Line = json });
    }

    return nodes;
  }

  /// <summary>
  /// 从文本解析为 LstgesDocument。
  /// </summary>
  /// <param name="text">文件文本。</param>
  /// <param name="error">解析失败时的错误信息。</param>
  public static LstgesDocument? ParseDocument(string text, out string? error)
  {
    var nodes = Parse(text, out error);
    return error != null || nodes == null ? null : new LstgesDocument(nodes);
  }

  /// <summary>
  /// 从文件解析为 LstgesDocument。
  /// </summary>
  /// <param name="path">文件路径。</param>
  /// <param name="error">解析失败时的错误信息。</param>
  public static LstgesDocument? LoadFile(string path, out string? error)
  {
    if (!File.Exists(path))
    {
      error = $"文件不存在：{path}";
      return null;
    }
    return ParseDocument(File.ReadAllText(path), out error);
  }

  private static string Truncate(string s, int max = 40) => s.Length <= max ? s : s[..max] + "…";
}
