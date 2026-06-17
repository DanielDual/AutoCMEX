# AutoCMEX — 日志系统实现方案

## 1. 需求核心目标与可量化验收标准

### 1.1 整体目标

基于 **Chickensoft.Log** 和 **Chickensoft.Log.Godot** 库，构建统一的日志系统，实现各模块运行时行为的可观测性，提供日志显示界面供用户实时查看。

### 1.2 验收标准

| 模块           | 验收标准                                                                             |
| -------------- | ------------------------------------------------------------------------------------ |
| **日志核心**   | 基于 `Chickensoft.Log` 实现 `ILog` 接口的日志服务，支持多 `ILogWriter` 输出          |
| **Godot 集成** | 使用 `GDWriter` 输出到 Godot 控制台，`GDFileWriter` 输出到 `user://logs/app.log`     |
| **日志存储**   | 日志文件按数量轮转，保留文件数量可用户配置（默认 30 个），单文件无大小限制           |
| **日志面板**   | UI 面板支持实时滚动显示日志，提供日志级别过滤（Info/Warn/Error）、模块筛选、清空按钮 |
| **模块覆盖**   | 高优先级模块（AI 服务、WebSocket、猜测管道）100% 埋点；中优先级模块 80% 埋点         |

---

## 2. 技术选型与依赖说明

### 2.1 核心日志库

| 包名                    | 版本   | 用途                                                                |
| ----------------------- | ------ | ------------------------------------------------------------------- |
| `Chickensoft.Log`       | Latest | 核心日志接口与实现（`ILog`, `Log`, `ConsoleWriter`, `TraceWriter`） |
| `Chickensoft.Log.Godot` | Latest | Godot 专用输出器（`GDWriter`, `GDFileWriter`）                      |

### 2.2 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                        日志系统架构                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐       │
│  │  AI 服务    │    │ WebSocket   │    │ 猜测管道    │       │
│  │ (ILog _log) │    │ (ILog _log) │    │ (ILog _log) │       │
│  └──────┬──────┘    └──────┬──────┘    └──────┬──────┘       │
│         │                  │                  │              │
│         └──────────────────┼──────────────────┘              │
│                            ▼                                │
│              ┌─────────────────────────┐                    │
│              │     ILogFormatter       │                    │
│              │  (默认格式: [时间][级别][模块] 消息)         │
│              └─────────────────────────┘                    │
│                            │                                │
│         ┌──────────────────┼──────────────────┐              │
│         ▼                  ▼                  ▼              │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐       │
│  │  GDWriter   │    │ GDFileWriter│    │ 内存缓冲区   │       │
│  │(Godot控制台) │    │(user://logs)│    │(UI实时显示) │       │
│  └─────────────┘    └─────────────┘    └─────────────┘       │
│                                         │                    │
│                                         ▼                    │
│                              ┌──────────────────┐              │
│                              │   日志面板 UI    │              │
    │                              │ (LogPanel.tscn)  │              │
    │                              │ 独立板块         │              │
    │                              └──────────────────┘              │
    │                                                             │
    └─────────────────────────────────────────────────────────────┘
```

---

## 3. 代码结构与模块划分

### 3.1 新增文件

```
src/
├── core/
│   └── logging/                    # 日志核心模块
│       ├── ILogService.cs          # 日志服务接口
│       ├── LogService.cs           # 日志服务实现
│       ├── InMemoryLogWriter.cs    # 内存日志写入器（供UI读取）
│       ├── LogEntry.cs             # 日志条目数据模型
│       └── LogLevel.cs             # 日志级别枚举
├── ui/
│   └── logging/                    # 日志UI面板
│       ├── LogPanel.tscn           # 日志面板场景
│       └── LogPanel.cs             # 日志面板逻辑
└── Main.cs                         # 修改：初始化日志服务
```

### 3.2 修改文件（日志埋点）

| 文件路径                                | 埋点内容                        |
| --------------------------------------- | ------------------------------- |
| `src/core/ai/OpenAiService.cs`          | API 请求/响应、超时、错误       |
| `src/core/ai/AnthropicService.cs`       | API 请求/响应、超时、错误       |
| `src/core/websocket/WebSocketServer.cs` | 客户端连接/断开、消息收发、错误 |
| `src/core/websocket/MessageHandler.cs`  | 消息处理、分发结果              |
| `src/core/guessing/GuessPipeline.cs`    | 管道执行、各环节耗时            |
| `src/core/guessing/GuessParser.cs`      | 解析成功/失败、错误类型         |
| `src/core/storage/DataManager.cs`       | 自动保存触发、成功/失败         |

---

## 4. 核心业务逻辑

### 4.1 日志服务初始化流程

```
应用启动
  │
  ▼
Main._Ready()
  │
  ├── 检查 NuGet 包：Chickensoft.Log、Chickensoft.Log.Godot
  │
  ├── 创建 LogService 单例
  │   ├── ILogFormatter：默认格式
  │   ├── Writers：
  │   │   ├── GDWriter → Godot 控制台
  │   │   ├── GDFileWriter → user://logs/app.log
  │   │   └── InMemoryLogWriter → 内存缓冲区（供UI读取）
  │   └── 配置：日志文件轮转（10MB/7天）
  │
  └── 注册到依赖注入容器
```

### 4.2 日志记录流程

```
业务代码调用：_log.Print("AI API request sent")
  │
  ▼
Log.Print(string message)
  │
  ├── 构建 LogEntry
  │   ├── Timestamp: DateTime.UtcNow
  │   ├── Level: Info
  │   ├── Module: "OpenAiService"
  │   ├── Message: "AI API request sent"
  │   └── Context: 可选上下文数据
  │
  ├── ILogFormatter.Format(LogEntry) → string
  │   └── 输出: "[2025-01-20 14:32:10][Info][OpenAiService] AI API request sent"
  │
  └── 遍历所有 ILogWriter，调用 Write(formattedMessage)
      ├── GDWriter.Write() → Godot 控制台
      ├── GDFileWriter.Write() → 文件（自动轮转）
      └── InMemoryLogWriter.Write() → 内存队列（供UI读取）
```

### 4.3 日志面板实时刷新流程

```
LogPanel._Ready()
  │
  ├── 获取 InMemoryLogWriter 实例
  ├── 订阅 OnNewLogEntry 事件
  └── 启动 Timer（0.5秒间隔批量刷新）
  │
  OnNewLogEntry(LogEntry entry)
    │
    ├── 添加到待显示队列
    └── 触发 Timer 立即刷新（防抖）
  │
  Timer.Timeout()
    │
    ├── 获取待显示队列
    ├── 根据当前过滤条件筛选
    │   ├── 日志级别过滤（Info/Warn/Error）
    │   └── 模块筛选（AI/WebSocket/猜测等）
    ├── 添加到 RichTextLabel
    └── 自动滚动到底部
  │
  用户点击"清空"按钮
    │
    ├── 清空 RichTextLabel
    └── InMemoryLogWriter.Clear()
```

---

## 5. 数据模型

### 5.1 核心日志模型

```csharp
// 日志级别 - 与 Chickensoft.Log 原生对齐
public enum LogLevel
{
    Info,    // 一般信息 (对应 ILog.Print)
    Warn,    // 警告 (对应 ILog.Warn)
    Error    // 错误 (对应 ILog.Err)
}

// 日志条目
public class LogEntry
{
    public DateTime Timestamp { get; set; }     // UTC 时间戳
    public LogLevel Level { get; set; }          // 日志级别
    public string Module { get; set; }           // 模块名称
    public string Message { get; set; }          // 日志消息
    public Dictionary<string, object> Context { get; set; }  // 可选上下文
}

// 日志配置
public class LogConfig
{
    public LogLevel MinLevel { get; set; } = LogLevel.Info;  // 最低记录级别（用户可配置）
    public string FilePath { get; set; } = "user://logs/app.log";  // 当前日志文件路径
    public int MaxFileCount { get; set; } = 30;  // 最大保留日志文件数量（用户可配置，默认30个）
    public int InMemoryBufferSize { get; set; } = 1000;  // 内存缓冲区条目数

    // 运行时行为：当检测到日志文件数量超过 MaxFileCount 时，自动删除最旧的文件
}
```

---

## 6. 接口契约定义

### 6.1 ILogService

```csharp
public interface ILogService
{
    // 获取模块特定的 ILog 实例
    ILog GetLogger(string moduleName);

    // 获取内存日志写入器（供UI读取）
    InMemoryLogWriter GetInMemoryWriter();

    // 刷新所有写入器
    void Flush();

    // 关闭日志服务
    void Shutdown();
}
```

### 6.2 InMemoryLogWriter

```csharp
public class InMemoryLogWriter : ILogWriter
{
    // 新日志条目事件（供UI订阅）
    public event Action<LogEntry> OnNewLogEntry;

    // 获取最近 N 条日志
    public IEnumerable<LogEntry> GetRecentEntries(int count);

    // 获取指定级别及以上的日志
    public IEnumerable<LogEntry> GetEntriesByLevel(LogLevel minLevel);

    // 清空缓冲区
    public void Clear();
}
```

---

## 7. 权限与安全设计

| 安全项         | 方案                                                         |
| -------------- | ------------------------------------------------------------ |
| 敏感信息过滤   | 自动检测并脱敏 API 密钥、密码等敏感字段，替换为 `[REDACTED]` |
| 日志文件权限   | 存储于 `user://logs/` 目录，遵循 Godot 沙箱权限              |
| 内存缓冲区限制 | 最大保留 1000 条，防止内存无限增长                           |
| 日志级别控制   | 用户可配置输出等级，默认为 Info，低于设置级别的日志不输出    |
| 应用关闭处理   | 程序关闭时强制将缓存中的日志写入硬盘，防止丢失               |

---

## 8. 测试覆盖方案

### 8.1 单元测试

| 测试类                  | 覆盖场景                                   |
| ----------------------- | ------------------------------------------ |
| `LogServiceTest`        | 服务初始化、获取 Logger、Flush、Shutdown   |
| `InMemoryLogWriterTest` | 写入、事件触发、获取条目、清空、缓冲区溢出 |
| `LogEntryTest`          | 条目创建、属性设置、序列化                 |

### 8.2 集成测试

| 测试场景         | 验证点                                     |
| ---------------- | ------------------------------------------ |
| 日志面板实时刷新 | UI 正确显示新日志、自动滚动、过滤生效      |
| 文件轮转         | 每天创建新文件、按用户配置保留天数自动清理 |
| 多模块并发写入   | 线程安全、无重复/丢失日志                  |
| 程序关闭时刷新   | 确保缓存中的日志全部写入硬盘               |
| 日志级别过滤     | 设置输出等级后，低级别日志不输出           |

---

## 附录：任务清单

### A. 日志核心模块（7 项）

| #   | 任务                                                                 | 优先级 |
| --- | -------------------------------------------------------------------- | ------ |
| A1  | 添加 NuGet 包 `Chickensoft.Log` 和 `Chickensoft.Log.Godot`           | 高     |
| A2  | 创建 `LogLevel.cs` 日志级别枚举                                      | 高     |
| A3  | 创建 `LogEntry.cs` 日志条目数据模型                                  | 高     |
| A4  | 创建 `InMemoryLogWriter.cs` 内存日志写入器（供UI读取）               | 高     |
| A5  | 创建 `ILogService.cs` 接口和 `LogService.cs` 实现                    | 高     |
| A6  | 修改 `Main.cs` 初始化日志服务，配置多 Writer                         | 高     |
| A7  | 创建 `LogConfig.cs` 日志配置文件模型，支持用户配置保留天数和输出等级 | 中     |
| A8  | 实现应用关闭时的日志强制刷新机制                                     | 高     |

### B. 日志面板 UI（8 项）

| #   | 任务                                                                            | 优先级 |
| --- | ------------------------------------------------------------------------------- | ------ |
| B1  | 创建 `LogPanel.tscn` 场景，作为独立板块面板，包含 RichTextLabel、过滤控件、按钮 | 高     |
| B2  | 实现 `LogPanel.cs`，订阅 `InMemoryLogWriter.OnNewLogEntry` 事件                 | 高     |
| B3  | 在 `MainWindow` 左栏导航添加「日志」按钮，与整合/猜测/信息/设置/帮助并列        | 高     |
| B4  | 实现日志级别过滤按钮（Info/Warn/Error）                                         | 中     |
| B5  | 实现模块筛选下拉框（All/AI/WebSocket/Guessing/Storage）                         | 中     |
| B6  | 实现自动滚动到底部和手动暂停功能                                                | 中     |
| B7  | 实现清空日志按钮（仅清空UI显示，不影响文件）                                    | 低     |
| B8  | 在日志面板内提供「日志配置」子区域或跳转按钮，配置 MinLevel/MaxFileCount        | 中     |

### C. 模块日志埋点（11 项）

| #   | 任务                                                                | 优先级 |
| --- | ------------------------------------------------------------------- | ------ |
| C1  | `OpenAiService.cs` 埋点：请求发送、响应接收、超时、错误             | 高     |
| C2  | `AnthropicService.cs` 埋点：请求发送、响应接收、超时、错误          | 高     |
| C3  | `WebSocketServer.cs` 埋点：服务启动/停止、客户端连接/断开、消息收发 | 高     |
| C4  | `MessageHandler.cs` 埋点：消息处理开始/结束、异常                   | 高     |
| C5  | `GuessPipeline.cs` 埋点：管道执行开始/结束、各环节耗时              | 高     |
| C6  | `GuessParser.cs` 埋点：解析成功/失败、错误类型                      | 中     |
| C7  | `DataManager.cs` 埋点：自动保存触发、成功/失败                      | 中     |
| C8  | `AiFuzzifier.cs` 埋点：模糊化处理开始/结束、耗时                    | 中     |
| C9  | `GuessingPanel.cs` 埋点：用户操作（导入/编辑/猜测）                 | 低     |
| C10 | `SettingsPanel.cs` 埋点：配置变更                                   | 低     |
| C11 | `Main.cs` 埋点：应用启动/关闭、场景加载                             | 中     |

---

**总计 25 项任务**，按优先级分布：高 19 项 / 中 5 项 / 低 1 项

---

## 参考文档

- [Chickensoft.Log](https://github.com/chickensoft-games/Log) - 核心日志库
- [Chickensoft.Log.Godot](https://github.com/chickensoft-games/Log.Godot) - Godot 专用输出器
