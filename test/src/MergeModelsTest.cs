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

  /// <summary>
  /// 向后兼容：本次改动删除了 <c>MergeConfig.GroupMappingByCreator</c> 字段。
  /// 旧 <c>merge_config.json</c> 里的 <c>groupMappingByCreator</c> 键必须被宽容忽略
  /// （而不是整份配置反序列化抛错、被 <c>LoadJson</c> 静默重置为默认值 —— 那会丢用户全部设置），
  /// 且缺失的 <c>shuffleMode</c> 回落默认 <see cref="MappingShuffleMode.Random"/>。
  /// </summary>
  [Test]
  public void DataManager_LoadMergeConfig_IgnoresRemovedLegacyKey()
  {
    File.WriteAllText(
      Path.Combine(_tempDir, "merge_config.json"),
      """
      {
        "templatePath": "C:/templates/legacy",
        "groupMappingByCreator": true,
        "algorithm": 1
      }
      """
    );

    var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_tempDir));
    var dm = new DataManager(_tempDir, encryptor);

    dm.LoadAll();

    // 非默认值被读入 ⇒ 整份配置确实加载成功（未因未知键回退到 new MergeConfig()）。
    dm.MergeConfig.TemplatePath.Value.ShouldBe("C:/templates/legacy");
    dm.MergeConfig.Algorithm.Value.ShouldBe(MergeAlgorithm.TopFolderCarry);
    // 新增字段在旧文件里缺失 ⇒ 取默认值。
    dm.MergeConfig.ShuffleMode.Value.ShouldBe(MappingShuffleMode.Random);
  }

  [Test]
  public void DataManager_SaveLoad_CreatorPackageInventoryCacheRoundTrip()
  {
    var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_tempDir));
    var dm = new DataManager(_tempDir, encryptor);

    dm.CreatorPackages.Add(
      new CreatorPackage
      {
        PackageName = "SamplePkg_A",
        CreatorName = new("Alice"),
        SourcePath = new("C:/packages/SamplePkg_A.zip"),
        IsDeleted = new(false),
        SpellCards = new() { "（非符）", "结界「真名的境界」" },
        Resources = new() { "LoadImage: res/boss.png", "LoadBGM: bgm.ogg" },
        Objects = new() { "BossDefine: pkg_enm1", "ObjectDefine: bullet_a" },
      }
    );

    dm.SaveAll();

    var dm2 = new DataManager(_tempDir, encryptor);
    dm2.LoadAll();

    var pkg = dm2.CreatorPackages.ShouldHaveSingleItem();
    pkg.SpellCards.ShouldBe(new[] { "（非符）", "结界「真名的境界」" });
    pkg.Resources.ShouldBe(new[] { "LoadImage: res/boss.png", "LoadBGM: bgm.ogg" });
    pkg.Objects.ShouldBe(new[] { "BossDefine: pkg_enm1", "ObjectDefine: bullet_a" });
  }

  [Test]
  public void DataManager_SaveLoad_CreatorPackagesAndMergeConfigRoundTrip()
  {
    var encryptor = new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_tempDir));
    var dm = new DataManager(_tempDir, encryptor);

    dm.CreatorPackages.Add(
      new CreatorPackage
      {
        PackageName = "SamplePkg_A",
        CreatorName = new("Alice"),
        SourcePath = new("C:/packages/SamplePkg_A.zip"),
        IsDeleted = new(false),
      }
    );
    dm.MergeConfig.TemplatePath.Value = "C:/templates/samp_template";
    dm.MergeConfig.SharpEditorPath.Value = "D:/LuaSTG/LuaSTG Editor Sharp";
    dm.MergeConfig.PluginDll.Value = "LuaSTGPlusLib.dll";
    dm.MergeConfig.IncludeLstges.Value = false; // 不提供工程文件选项
    dm.MergeConfig.ObfuscateLua.Value = true;
    dm.MergeConfig.ForcePerformAction.Value = true;
    dm.MergeConfig.GroupByCreatorFolders.Value = true;
    dm.MergeConfig.ShuffleMode.Value = AutoCMEX.Models.MappingShuffleMode.Interleave;
    dm.MergeConfig.Algorithm.Value = AutoCMEX.Models.MergeAlgorithm.TopFolderCarry;
    dm.MergeConfig.Mapping.Add(
      new SpellCardMappingEntry
      {
        Name = "结界「境界」",
        IsNonSpell = new(false),
        Creator = new("Alice"),
        PackageName = "SamplePkg_A",
        SourceCardIndex = 0,
      }
    );

    dm.SaveAll();

    var dm2 = new DataManager(_tempDir, encryptor);
    dm2.LoadAll();

    dm2.CreatorPackages.Count.ShouldBe(1);
    dm2.CreatorPackages[0].PackageName.ShouldBe("SamplePkg_A");
    dm2.CreatorPackages[0].CreatorName.Value.ShouldBe("Alice");
    dm2.CreatorPackages[0].SourcePath.Value.ShouldBe("C:/packages/SamplePkg_A.zip");

    dm2.MergeConfig.TemplatePath.Value.ShouldBe("C:/templates/samp_template");
    dm2.MergeConfig.SharpEditorPath.Value.ShouldBe("D:/LuaSTG/LuaSTG Editor Sharp");
    dm2.MergeConfig.PluginDll.Value.ShouldBe("LuaSTGPlusLib.dll");
    dm2.MergeConfig.IncludeLstges.Value.ShouldBeFalse();
    dm2.MergeConfig.ObfuscateLua.Value.ShouldBeTrue();
    dm2.MergeConfig.ForcePerformAction.Value.ShouldBeTrue();
    dm2.MergeConfig.GroupByCreatorFolders.Value.ShouldBeTrue();
    dm2.MergeConfig.ShuffleMode.Value.ShouldBe(AutoCMEX.Models.MappingShuffleMode.Interleave);
    dm2.MergeConfig.Algorithm.Value.ShouldBe(AutoCMEX.Models.MergeAlgorithm.TopFolderCarry);
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
      new CreatorPackage { PackageName = "SamplePkg_A", CreatorName = new("Alice") }
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
