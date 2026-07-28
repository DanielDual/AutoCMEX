namespace AutoCMEX;

using System;
using AutoCMEX.Core.Storage;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// Tests for <see cref="ImporterFactory"/>.
/// </summary>
public class ImporterFactoryTest : TestClass
{
  public ImporterFactoryTest(Node testScene)
    : base(testScene) { }

  [Test]
  public void Create_CsvPath_ReturnsCsvImporter() =>
    ImporterFactory.Create("data.csv").ShouldBeOfType<CsvImporter>();

  [Test]
  public void Create_CsvPathUpperCase_ReturnsCsvImporter() =>
    ImporterFactory.Create("DATA.CSV").ShouldBeOfType<CsvImporter>();

  [Test]
  public void Create_XlsxPath_ReturnsExcelImporter() =>
    ImporterFactory.Create("data.xlsx").ShouldBeOfType<ExcelImporter>();

  [Test]
  public void Create_XlsPath_ReturnsExcelImporter() =>
    ImporterFactory.Create("data.xls").ShouldBeOfType<ExcelImporter>();

  [Test]
  public void Create_XlsxPathUpperCase_ReturnsExcelImporter() =>
    ImporterFactory.Create("DATA.XLSX").ShouldBeOfType<ExcelImporter>();

  [Test]
  public void Create_UnsupportedFormat_ThrowsNotSupportedException() =>
    Should.Throw<NotSupportedException>(() => ImporterFactory.Create("data.json"));

  [Test]
  public void Create_NoExtension_ThrowsNotSupportedException() =>
    Should.Throw<NotSupportedException>(() => ImporterFactory.Create("data"));

  [Test]
  public void Create_PathWithDirectory_CorrectlyIdentifiesExtension() =>
    ImporterFactory.Create("/path/to/file.csv").ShouldBeOfType<CsvImporter>();
}
