namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 阶段2 模型 + DataManager 持久化单元测试。
/// </summary>
public class MergeModelsTest : TestClass
{
  private string _tempDir = string.Empty;

  public MergeModelsTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _tempDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_MergeModels_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_tempDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  [Test]
  public void DataManager_LoadAll_MergeEmptyWhenNoFiles()
  {
    var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_tempDir));
    var dm = new DataManager(_tempDir, encryptor);

    dm.LoadAll();

    dm.CreatorPackages.ShouldBeEmpty();
    dm.MergeConfig.TemplatePath.Value.ShouldBe(string.Empty);
    dm.MergeConfig.Mapping.ShouldBeEmpty();
  }

  [Test]
  public void DataManager_SaveLoad_CreatorPackagesAndMergeConfigRoundTrip()
  {
    var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_tempDir));
    var dm = new DataManager(_tempDir, encryptor);

    dm.CreatorPackages.Add(
      new CreatorPackage
      {
        PackageName = "CMEX22_A",
        CreatorName = new("Alice"),
        SourcePath = new("C:/packages/CMEX22_A.zip"),
        IsDeleted = new(false),
      }
    );
    dm.MergeConfig.TemplatePath.Value = "C:/templates/cmex22_template";
    dm.MergeConfig.SharpEditorPath.Value = "D:/LuaSTG/LuaSTG Editor Sharp";
    dm.MergeConfig.PluginDll.Value = "LuaSTGPlusLib.dll";
    dm.MergeConfig.IncludeLstges.Value = false; // 不提供工程文件选项
    dm.MergeConfig.ObfuscateLua.Value = true;
    dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "结界「境界」",
        IsNonSpell = new(false),
        Creator = new("Alice"),
        PackageName = "CMEX22_A",
        SourceCardIndex = 0,
      }
    );

    dm.SaveAll();

    var dm2 = new DataManager(_tempDir, encryptor);
    dm2.LoadAll();

    dm2.CreatorPackages.Count.ShouldBe(1);
    dm2.CreatorPackages[0].PackageName.ShouldBe("CMEX22_A");
    dm2.CreatorPackages[0].CreatorName.Value.ShouldBe("Alice");
    dm2.CreatorPackages[0].SourcePath.Value.ShouldBe("C:/packages/CMEX22_A.zip");

    dm2.MergeConfig.TemplatePath.Value.ShouldBe("C:/templates/cmex22_template");
    dm2.MergeConfig.SharpEditorPath.Value.ShouldBe("D:/LuaSTG/LuaSTG Editor Sharp");
    dm2.MergeConfig.PluginDll.Value.ShouldBe("LuaSTGPlusLib.dll");
    dm2.MergeConfig.IncludeLstges.Value.ShouldBeFalse();
    dm2.MergeConfig.ObfuscateLua.Value.ShouldBeTrue();
    dm2.MergeConfig.Mapping.Count.ShouldBe(1);
    dm2.MergeConfig.Mapping[0].Name.ShouldBe("结界「境界」");
    dm2.MergeConfig.Mapping[0].Creator.Value.ShouldBe("Alice");
    dm2.MergeConfig.Mapping[0].SourceCardIndex.ShouldBe(0);
  }

  [Test]
  public void DataManager_Save_ProducesSeparateMergeJsonFiles()
  {
    var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_tempDir));
    var dm = new DataManager(_tempDir, encryptor);

    dm.CreatorPackages.Add(
      new CreatorPackage { PackageName = "CMEX22_A", CreatorName = new("Alice") }
    );
    dm.MergeConfig.OutputDir.Value = "C:/out";
    dm.SaveAll();

    var pkgJsonPath = Path.Combine(_tempDir, "creator_packages.json");
    var cfgJsonPath = Path.Combine(_tempDir, "merge_config.json");

    File.Exists(pkgJsonPath).ShouldBeTrue();
    File.Exists(cfgJsonPath).ShouldBeTrue();

    // 序列化后 CreatorName 应保存其值（AutoValue），而非对象类型
    File.ReadAllText(pkgJsonPath).ShouldContain("Alice");
    File.ReadAllText(cfgJsonPath).ShouldContain("C:/out");
  }
}
