namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 存储层单元测试
/// </summary>
public class StorageTest : TestClass
{
  private string _tempDir = string.Empty;

  public StorageTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_Test_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_tempDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  // ==================== AesEncryptor Tests ====================

  [Test]
  public void AesEncryptor_EncryptDecrypt_RoundTrip()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);

    var plainText = "my-secret-api-key-12345";
    var encrypted = encryptor.Encrypt(plainText);

    encrypted.ShouldNotBe(plainText);
    encrypted.ShouldNotBeNullOrEmpty();

    var decrypted = encryptor.Decrypt(encrypted);
    decrypted.ShouldBe(plainText);
  }

  [Test]
  public void AesEncryptor_EncryptEmpty_ReturnsEmpty()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);

    var encrypted = encryptor.Encrypt(string.Empty);
    encrypted.ShouldBe(string.Empty);
  }

  [Test]
  public void AesEncryptor_DecryptEmpty_ReturnsEmpty()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);

    var decrypted = encryptor.Decrypt(string.Empty);
    decrypted.ShouldBe(string.Empty);
  }

  [Test]
  public void AesEncryptor_DecryptInvalidData_ReturnsEmpty()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);

    var decrypted = encryptor.Decrypt("not-valid-base64!!!");
    decrypted.ShouldBe(string.Empty);
  }

  [Test]
  public void AesEncryptor_GeneratesKeyFile()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);

    File.Exists(keyPath).ShouldBeTrue();
  }

  [Test]
  public void AesEncryptor_ReusesExistingKeyFile()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor1 = new AesEncryptor(keyPath);
    var encrypted = encryptor1.Encrypt("test");

    // Create a new encryptor with the same key file
    var encryptor2 = new AesEncryptor(keyPath);
    var decrypted = encryptor2.Decrypt(encrypted);

    decrypted.ShouldBe("test");
  }

  // ==================== CsvImporter Tests ====================

  [Test]
  public void CsvImporter_SpellCardTable_ValidCsv()
  {
    var csvPath = Path.Combine(_tempDir, "spellcard.csv");
    var csv = "Boss,符卡名,创作者\nBoss1,Card1,Alice\nBoss1,Card2,Bob\nBoss2,Card3,Charlie";
    File.WriteAllText(csvPath, csv, Encoding.UTF8);

    var result = new CsvImporter().ImportSpellCardTable(csvPath);

    result.IsSuccess.ShouldBeTrue();
    result.Data!.Count.ShouldBe(2);
    result.Data[0].Name.ShouldBe("Boss1");
    result.Data[0].SpellCards.Count.ShouldBe(2);
    result.Data[0].SpellCards[0].Name.ShouldBe("Card1");
    result.Data[0].SpellCards[0].Creator.ShouldBe("Alice");
    result.Data[1].Name.ShouldBe("Boss2");
    result.Data[1].SpellCards.Count.ShouldBe(1);
  }

  [Test]
  public void CsvImporter_SpellCardTable_MissingColumns()
  {
    var csvPath = Path.Combine(_tempDir, "spellcard.csv");
    var csv = "Boss,符卡名\nBoss1,Card1";
    File.WriteAllText(csvPath, csv, Encoding.UTF8);

    var result = new CsvImporter().ImportSpellCardTable(csvPath);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("列缺失");
  }

  [Test]
  public void CsvImporter_AliasTable_ValidCsv()
  {
    var csvPath = Path.Combine(_tempDir, "alias.csv");
    var csv = "主名,别名1,别名2\nAlice,Ali,A\nBob,B,Bo";
    File.WriteAllText(csvPath, csv, Encoding.UTF8);

    var result = new CsvImporter().ImportAliasTable(csvPath);

    result.IsSuccess.ShouldBeTrue();
    result.Data!.Count.ShouldBe(2);
    result.Data[0].MainName.ShouldBe("Alice");
    result.Data[0].Aliases.Count.ShouldBe(2);
    result.Data[0].Aliases.ShouldContain("Ali");
    result.Data[0].Aliases.ShouldContain("A");
    result.Data[1].MainName.ShouldBe("Bob");
    result.Data[1].Aliases.Count.ShouldBe(2);
  }

  [Test]
  public void CsvImporter_AliasTable_MissingColumns()
  {
    var csvPath = Path.Combine(_tempDir, "alias.csv");
    var csv = "主名\nAlice";
    File.WriteAllText(csvPath, csv, Encoding.UTF8);

    var result = new CsvImporter().ImportAliasTable(csvPath);

    result.IsSuccess.ShouldBeFalse();
    result.ErrorMessage.ShouldContain("列缺失");
  }

  [Test]
  public void CsvImporter_FileNotFound_ReturnsError()
  {
    var result = new CsvImporter().ImportSpellCardTable(Path.Combine(_tempDir, "nonexistent.csv"));

    result.IsSuccess.ShouldBeFalse();
  }

  // ==================== DataManager Tests ====================

  [Test]
  public void DataManager_LoadAll_EmptyWhenNoFiles()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);
    var dm = new DataManager(_tempDir, encryptor);

    dm.LoadAll();

    dm.Bosses.ShouldBeEmpty();
    dm.Aliases.ShouldBeEmpty();
    dm.Settings.AiModels.ShouldBeEmpty();
  }

  [Test]
  public void DataManager_SaveAndLoad_RoundTrip()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);
    var dm = new DataManager(_tempDir, encryptor);

    dm.Bosses.Add(new Boss { Name = "TestBoss" });
    dm.Aliases.Add(new CreatorAlias { MainName = "Alice" });
    dm.Settings.WebSocketPort = 9999;

    dm.SaveAll();

    // Load with a new DataManager
    var dm2 = new DataManager(_tempDir, encryptor);
    dm2.LoadAll();

    dm2.Bosses.Count.ShouldBe(1);
    dm2.Bosses[0].Name.ShouldBe("TestBoss");
    dm2.Aliases.Count.ShouldBe(1);
    dm2.Aliases[0].MainName.ShouldBe("Alice");
    dm2.Settings.WebSocketPort.ShouldBe(9999);
  }

  [Test]
  public void DataManager_AiModelApiKey_EncryptedOnSave()
  {
    var keyPath = AesEncryptor.GetDefaultKeyPath(_tempDir);
    var encryptor = new AesEncryptor(keyPath);
    var dm = new DataManager(_tempDir, encryptor);

    dm.Settings.AiModels.Add(new AiModelConfig { Id = "test", EncryptedApiKey = "sk-secret-key" });

    dm.SaveAll();

    // Read the raw JSON to verify encryption
    var jsonPath = Path.Combine(_tempDir, "app_settings.json");
    var json = File.ReadAllText(jsonPath);
    json.ShouldNotContain("sk-secret-key");
  }
}
