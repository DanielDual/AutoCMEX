namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Chickensoft.Log;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 录制配置单测：默认值、被 JSON 显式 <c>null</c> 覆盖后的回填，以及经 <c>DataManager</c>
/// 的落盘往返（录制配置必须与其它配置同链路持久化）。
/// </summary>
public class RecordingConfigTest : TestClass
{
  private string _dataDir = string.Empty;

  public RecordingConfigTest(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dataDir = Path.Combine(
      Path.GetTempPath(),
      "AutoCMEX_RecordingConfig_" + Guid.NewGuid().ToString("N")[..8]
    );
    Directory.CreateDirectory(_dataDir);
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_dataDir))
    {
      Directory.Delete(_dataDir, recursive: true);
    }
  }

  /// <summary>用临时目录造一个 DataManager。</summary>
  /// <returns>配置管理器。</returns>
  private DataManager CreateDataManager() =>
    new(
      _dataDir,
      new AesEncryptor(AesEncryptor.GetDefaultKeyPath(_dataDir)),
      new Mock<ILog>().Object
    );

  [Test]
  public void Defaults_NewConfig_HasProductDefaults()
  {
    var config = new RecordingConfig();

    config.EngineDir.Value.ShouldBeEmpty();
    config.MaxFrame.Value.ShouldBe(350);
    config.FirstInterval.Value.ShouldBe(3);
    config.SecondInterval.Value.ShouldBe(5);
    config.LastOutputDir.Value.ShouldBeEmpty();
    config.Parallelism.Value.ShouldBe(RecordingConfig.DefaultParallelism);
    config.SandboxRoot.Value.ShouldBeEmpty();
  }

  [Test]
  public void EnsureIntegrity_ExplicitNulls_RestoresDefaults()
  {
    var config = new RecordingConfig
    {
      EngineDir = null!,
      MaxFrame = null!,
      FirstInterval = null!,
      SecondInterval = null!,
      LastOutputDir = null!,
      Parallelism = null!,
      SandboxRoot = null!,
    };

    config.EnsureIntegrity();

    config.EngineDir.Value.ShouldBeEmpty();
    config.MaxFrame.Value.ShouldBe(350);
    config.FirstInterval.Value.ShouldBe(3);
    config.SecondInterval.Value.ShouldBe(5);
    config.LastOutputDir.Value.ShouldBeEmpty();
    config.Parallelism.Value.ShouldBe(RecordingConfig.DefaultParallelism);
    config.SandboxRoot.Value.ShouldBeEmpty();
  }

  [Test]
  public void ClampParallelism_OutOfRange_ConvergesIntoBounds()
  {
    RecordingConfig.ClampParallelism(0).ShouldBe(RecordingConfig.MinParallelism);
    RecordingConfig.ClampParallelism(-3).ShouldBe(RecordingConfig.MinParallelism);
    RecordingConfig.ClampParallelism(2).ShouldBe(2);
    RecordingConfig.ClampParallelism(9999).ShouldBe(RecordingConfig.MaxParallelism);
  }

  [Test]
  public void SaveAllAndLoadAll_RecordingConfig_RoundTrips()
  {
    var manager = CreateDataManager();
    manager.LoadAll();
    manager.RecordingConfig.EngineDir.Value = @"D:\LuaSTG\LuaSTGSub";
    manager.RecordingConfig.MaxFrame.Value = 420;
    manager.RecordingConfig.FirstInterval.Value = 2;
    manager.RecordingConfig.SecondInterval.Value = 4;
    manager.RecordingConfig.LastOutputDir.Value = @"D:\out";
    manager.RecordingConfig.Parallelism.Value = 4;
    manager.RecordingConfig.SandboxRoot.Value = @"D:\sb";
    manager.SaveAll();

    var reloaded = CreateDataManager();
    reloaded.LoadAll();

    reloaded.RecordingConfig.EngineDir.Value.ShouldBe(@"D:\LuaSTG\LuaSTGSub");
    reloaded.RecordingConfig.MaxFrame.Value.ShouldBe(420);
    reloaded.RecordingConfig.FirstInterval.Value.ShouldBe(2);
    reloaded.RecordingConfig.SecondInterval.Value.ShouldBe(4);
    reloaded.RecordingConfig.LastOutputDir.Value.ShouldBe(@"D:\out");
    reloaded.RecordingConfig.Parallelism.Value.ShouldBe(4);
    reloaded.RecordingConfig.SandboxRoot.Value.ShouldBe(@"D:\sb");
  }
}
