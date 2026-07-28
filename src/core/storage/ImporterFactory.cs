namespace AutoCMEX.Core.Storage;

using System;
using System.IO;

/// <summary>
/// 导入器工厂，根据文件扩展名返回对应的 <see cref="IImporter"/> 实例。
/// </summary>
public static class ImporterFactory
{
  /// <summary>
  /// 根据文件路径创建对应的导入器。
  /// </summary>
  /// <param name="filePath">文件路径，用于判断格式。</param>
  /// <returns>对应格式的导入器实例。</returns>
  /// <exception cref="NotSupportedException">文件格式不支持时抛出。</exception>
  public static IImporter Create(string filePath)
  {
    var ext = Path.GetExtension(filePath).ToLowerInvariant();
    return ext switch
    {
      ".csv" => new CsvImporter(),
      ".xlsx" or ".xls" => new ExcelImporter(),
      _ => throw new NotSupportedException($"不支持的文件格式：{ext}"),
    };
  }
}
