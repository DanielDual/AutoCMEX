namespace AutoCMEX;

using System;
using System.IO;
using System.Linq;
using AutoCMEX.Core.Logging;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 日志核心模块单元测试
/// </summary>
public class LogCoreTest : TestClass
{
  public LogCoreTest(Node testScene)
    : base(testScene) { }

  private string _testLogDir = "";

  [Setup]
  public void Setup()
  {
    _testLogDir = Path.Combine(Path.GetTempPath(), "AutoCMEX-Tests", Guid.NewGuid().ToString("N"));
  }

  [Cleanup]
  public void Cleanup()
  {
    if (Directory.Exists(_testLogDir))
    {
      try
      {
        Directory.Delete(_testLogDir, recursive: true);
      }
      catch
      {
        // ignore
      }
    }
  }

  // ==================== LogEntry Tests ====================

  [Test]
  public void LogEntry_DefaultConstructor_HasDefaults()
  {
    var entry = new LogEntry
    {
      Level = LogLevel.Info,
      Module = "Test",
      Message = "hello",
    };

    entry.Timestamp.ShouldNotBe(default);
    entry.Level.ShouldBe(LogLevel.Info);
    entry.Module.ShouldBe("Test");
    entry.Message.ShouldBe("hello");
  }

  [Test]
  public void LogEntry_FactoryConstructor_PopulatesTimestamp()
  {
    var before = DateTime.UtcNow;
    var entry = new LogEntry(LogLevel.Warn, "Test", "msg");
    var after = DateTime.UtcNow;

    entry.Timestamp.ShouldBeGreaterThanOrEqualTo(before);
    entry.Timestamp.ShouldBeLessThanOrEqualTo(after);
    entry.Level.ShouldBe(LogLevel.Warn);
    entry.Module.ShouldBe("Test");
    entry.Message.ShouldBe("msg");
  }

  // ==================== InMemoryLogWriter Tests ====================

  [Test]
  public void InMemoryLogWriter_WriteMessage_AddsEntry()
  {
    var writer = new InMemoryLogWriter();
    LogEntry? captured = null;
    writer.OnNewLogEntry += e => captured = e;

    writer.WriteMessage("Info (ModA): hello world");

    captured.ShouldNotBeNull();
    captured!.Level.ShouldBe(LogLevel.Info);
    captured.Module.ShouldBe("ModA");
    captured.Message.ShouldBe("hello world");
  }

  [Test]
  public void InMemoryLogWriter_WriteWarning_AddsWarnEntry()
  {
    var writer = new InMemoryLogWriter();
    LogEntry? captured = null;
    writer.OnNewLogEntry += e => captured = e;

    writer.WriteWarning("Warn (ModA): careful");

    captured.ShouldNotBeNull();
    captured!.Level.ShouldBe(LogLevel.Warn);
  }

  [Test]
  public void InMemoryLogWriter_WriteError_AddsErrorEntry()
  {
    var writer = new InMemoryLogWriter();
    LogEntry? captured = null;
    writer.OnNewLogEntry += e => captured = e;

    writer.WriteError("Error (ModA): boom");

    captured.ShouldNotBeNull();
    captured!.Level.ShouldBe(LogLevel.Error);
  }

  [Test]
  public void InMemoryLogWriter_MinLevel_FiltersOutLowerLevels()
  {
    var writer = new InMemoryLogWriter { MinLevel = LogLevel.Warn };
    writer.WriteMessage("Info (ModA): ignored");
    writer.WriteWarning("Warn (ModA): kept");
    writer.WriteError("Error (ModA): kept");

    var entries = writer.GetEntries();
    entries.Count.ShouldBe(2);
    entries.All(e => e.Level >= LogLevel.Warn).ShouldBeTrue();
  }

  [Test]
  public void InMemoryLogWriter_BufferOverflow_DropsOldest()
  {
    var writer = new InMemoryLogWriter(maxBufferSize: 3);
    writer.WriteMessage("Info (M): 1");
    writer.WriteMessage("Info (M): 2");
    writer.WriteMessage("Info (M): 3");
    writer.WriteMessage("Info (M): 4");

    var entries = writer.GetEntries().ToList();
    entries.Count.ShouldBe(3);
    entries[0].Message.ShouldBe("2");
    entries[2].Message.ShouldBe("4");
  }

  [Test]
  public void InMemoryLogWriter_GetRecentEntries_ReturnsLastN()
  {
    var writer = new InMemoryLogWriter();
    for (int i = 0; i < 10; i++)
      writer.WriteMessage($"Info (M): msg-{i}");

    var recent = writer.GetRecentEntries(3).ToList();
    recent.Count.ShouldBe(3);
    recent[0].Message.ShouldBe("msg-7");
    recent[2].Message.ShouldBe("msg-9");
  }

  [Test]
  public void InMemoryLogWriter_GetEntriesByLevel_FiltersCorrectly()
  {
    var writer = new InMemoryLogWriter();
    writer.WriteMessage("Info (M): 1");
    writer.WriteWarning("Warn (M): 2");
    writer.WriteError("Error (M): 3");
    writer.WriteMessage("Info (M): 4");

    var warns = writer.GetEntriesByLevel(LogLevel.Warn).ToList();
    warns.Count.ShouldBe(2);
    warns.All(e => e.Level >= LogLevel.Warn).ShouldBeTrue();
  }

  [Test]
  public void InMemoryLogWriter_Clear_RemovesAll()
  {
    var writer = new InMemoryLogWriter();
    writer.WriteMessage("Info (M): a");
    writer.WriteMessage("Info (M): b");
    writer.Clear();
    writer.GetEntries().Count.ShouldBe(0);
  }

  // ==================== LogConfig Tests ====================

  [Test]
  public void LogConfig_DefaultLogDir_IsInTemp()
  {
    var cfg = new LogConfig();
    cfg.LogDirectory.ShouldStartWith(Path.GetTempPath());
    cfg.LogDirectory.ShouldContain("AutoCMEX");
  }

  [Test]
  public void LogConfig_DefaultValues_AreSet()
  {
    var cfg = new LogConfig();
    cfg.MinLevel.ShouldBe(LogLevel.Info);
    cfg.MaxFileCount.ShouldBe(LogConfig.DefaultMaxFileCount);
    cfg.InMemoryBufferSize.ShouldBe(LogConfig.DefaultInMemoryBufferSize);
    cfg.FileName.ShouldBe(LogConfig.DefaultLogFileName);
  }

  [Test]
  public void LogConfig_CurrentFilePath_JoinsCorrectly()
  {
    var cfg = new LogConfig { LogDirectory = "/tmp/abc", FileName = "x.log" };
    cfg.CurrentFilePath.ShouldBe(Path.Combine("/tmp/abc", "x.log"));
  }

  // ==================== LogService Tests ====================

  [Test]
  public void LogService_GetLogger_SameModuleReturnsSameInstance()
  {
    var cfg = new LogConfig { LogDirectory = _testLogDir };
    using var svc = new LogService(cfg, includeGodotConsole: false);
    var a = svc.GetLogger("ModA");
    var b = svc.GetLogger("ModA");
    a.ShouldBeSameAs(b);
  }

  [Test]
  public void LogService_GetLogger_DifferentModulesDifferentInstances()
  {
    var cfg = new LogConfig { LogDirectory = _testLogDir };
    using var svc = new LogService(cfg, includeGodotConsole: false);
    var a = svc.GetLogger("ModA");
    var b = svc.GetLogger("ModB");
    a.ShouldNotBeSameAs(b);
  }

  [Test]
  public void LogService_LogIsWrittenToFile()
  {
    var cfg = new LogConfig { LogDirectory = _testLogDir };
    using var svc = new LogService(cfg, includeGodotConsole: false);
    var log = svc.GetLogger("TestModule");
    log.Print("hello from test");
    // Force flush - file writer already flushes per write
    File.Exists(cfg.CurrentFilePath).ShouldBeTrue();
    var content = File.ReadAllText(cfg.CurrentFilePath);
    content.ShouldContain("hello from test");
    content.ShouldContain("TestModule");
  }

  [Test]
  public void LogService_LogIsWrittenToInMemoryWriter()
  {
    var cfg = new LogConfig { LogDirectory = _testLogDir };
    using var svc = new LogService(cfg, includeGodotConsole: false);
    var log = svc.GetLogger("TestModule");
    log.Print("memory line");
    var entries = svc.InMemoryWriter.GetEntries();
    entries.ShouldContain(e => e.Message == "memory line");
  }

  [Test]
  public void LogService_RotateIfNeeded_DeletesExcessFiles()
  {
    var cfg = new LogConfig { LogDirectory = _testLogDir, MaxFileCount = 3 };
    Directory.CreateDirectory(_testLogDir);

    // 制造 5 个文件 (app.log + app.1.log..app.4.log)
    File.WriteAllText(Path.Combine(_testLogDir, "app.log"), "0");
    File.WriteAllText(Path.Combine(_testLogDir, "app.1.log"), "1");
    File.WriteAllText(Path.Combine(_testLogDir, "app.2.log"), "2");
    File.WriteAllText(Path.Combine(_testLogDir, "app.3.log"), "3");
    File.WriteAllText(Path.Combine(_testLogDir, "app.4.log"), "4");

    using var svc = new LogService(cfg, includeGodotConsole: false);
    svc.RotateIfNeeded();

    var remaining = Directory.GetFiles(_testLogDir, "app*.log");
    remaining.Length.ShouldBeLessThanOrEqualTo(3);
  }

  [Test]
  public void LogService_Shutdown_IsIdempotent()
  {
    var cfg = new LogConfig { LogDirectory = _testLogDir };
    var svc = new LogService(cfg, includeGodotConsole: false);
    svc.Shutdown();
    Should.NotThrow(() => svc.Shutdown());
  }
}
