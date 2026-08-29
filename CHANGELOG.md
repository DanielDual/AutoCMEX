# AutoCMEX — 变更日志

## v0.0.1 (开发中)

### 新增

- **猜测板块**：符卡—创作者对应表导入/编辑、别名表导入/编辑、猜测文本处理管道
- **AI 服务**：OpenAI 兼容格式与 Anthropic 原生格式 API 集成，支持多模型配置
- **AI 模糊化**：将非严格格式猜测文本转为严格格式，别名自动转换为主名
- **设置板块**：AI 模型配置（CRUD + 测试连接）、群聊配置（WebSocket 端口、消息筛选模式、Koishi 一键安装）
- **数据存储**：JSON 文件持久化，AES 加密敏感信息，CSV/Excel 导入
- **WebSocket 服务**：服务端/客户端双模式，支持 Koishi 群聊机器人消息转发
- **Koishi 插件**：v4 插件代码，消息转发到 AutoCMEX，支持托管猜测闭环（消息上报 → 处理 → 结果回传 → 回复）
- **托管猜测闭环**：Koishi 消息 → AutoCMEX 统一判定/处理 → guess_result 回传 → Koishi 回复原消息
- **主窗口**：左右两栏布局，板块切换导航
- **日志系统**：基于 Chickensoft.Log 的统一日志服务，支持文件轮转、内存缓冲、敏感信息脱敏、UI 面板实时查看
- **单元测试**：核心模块测试覆盖（AI、猜测引擎、存储、WebSocket、UI、模型、日志）

### 重构

- **核心层清理**：移除 AiFuzzifier 未使用字段、提取 LogEntry 工厂方法、移除空 Flush() 方法、CloneSettingsForSave 改用 JSON 深拷贝、合并重复正则、统一 WebSocket 发送路径
- **服务抽象层**：提取 IImporter 接口 + ImporterFactory 工厂模式、创建 StringEscapeHelper 工具类、创建 PluginInstaller 服务
- **UI 层职责分离**：提取 LogConfigPanel、WebSocketPanel 改用 Godot Timer、MainWindow 剥离 WebSocket 初始化到 WebSocketInitializer
- **架构统一**：GuessProcessingService 合并 ProcessManualAsync/ProcessManagedAsync 为 ProcessAsync、DataManager 使用 ObservableCollection 实现自动 UI 更新、GuessPipeline/AiFuzzifier 改用 IReadOnlyList 接口

### 修复

- **LogService 资源泄漏**：`Shutdown()` 正确释放 `RotatingFileWriter` 并清空内存缓冲区
- **HeartbeatService CTS 浪费**：使用 `using` 声明替代显式 `finally Dispose`
- **DataManager 竞态条件**：`TriggerAutoSave` 使用 `volatile` 标志 + `async/await` 防止保存重叠
- **DataManager.LoadAll 集合替换**：`LoadAll()` 清空并重新填充现有 `ObservableCollection` 而非创建新实例，保持 `CollectionChanged` 事件订阅有效
- **WebSocket 分片消息截断**：`ReceiveLoop` 使用 `MemoryStream` 累积分片，检查 `EndOfMessage`
- **SettingsPanel 场景树耦合**：改为配置驱动，`AppSettings.PropertyChanged` → `MainWindow` 自动重启 WebSocket
- **AesEncryptor 路径硬编码**：提取 `DefaultKeyFileName` 常量和 `GetDefaultKeyPath()` 方法，统一所有调用点

### 架构优化

- **丢包仓储提取**：`GuessProcessingService` 的丢包管理抽取为 `IDroppedGuessRepository` / `DroppedGuessRepository`
- **GuessingPanel 子节点脚本化**：符卡表/别名表/导入导出逻辑下放到 `SpellCardTreeHandler` / `AliasTreeHandler` 子节点脚本

### 测试改进

- **测试覆盖率提升**：新增 43 个测试（从 138 → 181），覆盖 StringEscapeHelper、DroppedGuess/DroppedGuessRepository、ImporterFactory 等 0% 覆盖模块
- **自定义 TestDriver**：创建 GuessingPanelDriver、SpellCardTreeHandlerDriver、AliasTreeHandlerDriver，封装复杂 UI 节点操作为高阶 API，解耦测试与节点路径
- **测试质量提升**：将存在性行为测试改为行为测试，补充 INotifyPropertyChanged 测试和负面用例
- **修复失败测试**：更新 GuessEngineTest 和 GuessProcessingServiceTest 断言以匹配当前 GuessResponseHandler 行为（emoji 格式）
- **修复 CloneSettingsForSave 加密逻辑**：保存前加密 API 密钥，避免敏感数据明文写入 JSON

### 待完成

- 整合板块（暂不开发）
- 信息板块（暂不开发）
- 帮助板块（待开发）
- 设置面板剩余 5 个分类配置（猜测/整合/信息/帮助/通用）
- 「从工程文件导入」按钮（计划任务 B7）
- 消息筛选模式接入 WebSocket 处理器（计划任务 C13）
- Koishi 一键安装 `res://` 路径修正（计划任务 E5）

## v0.0.2 (开发中) — Chickensoft 生态统一重构

### 数据同步统一

- **数据模型迁移**：`Boss`/`CreatorAlias`/`SpellCard`/`AppSettings` 从 `ObservableCollection`/`INotifyPropertyChanged` 迁移到 `AutoList`/`AutoValue`，消除手动事件订阅
- **DataManager 重构**：数据集合改用 `AutoList`，配置变更通过 `AutoValue.Bind()` 自动通知，移除手动 `PropertyChanged` 订阅

### 场景重构

- **子节点脚本提取**：`SpellCardTreeHandler`/`AliasTreeHandler` 提取为独立场景 `SpellCardPanel.tscn`/`AliasPanel.tscn`，遵循"一个场景一个脚本"规则
- **SettingsPanel 静态化**：动态 UI 构建迁移为静态 `.tscn`，创建 `AiModelConfigPanel`/`ChatConfigPanel` 独立子场景
- **MainWindow 面板引用**：`LogPanel`/`WebSocketPanel` 节点属性改为 `INode` 接口类型，通过 `GodotNodeInterfaces` 适配器访问

### 依赖注入统一

- **WebSocketPanel 接入 AutoInject**：使用 `[Node]` 属性替代 `GetNode<>` 手动查找，`[Dependency]` 获取 `IWebSocketServer`
- **LogConfigPanel 接入 AutoInject**：使用 `[Node]` 属性替代 `GetNode<>` 手动查找，`[Dependency]` 获取 `ILogService`
- **GuessingPanel 节点属性接口化**：`[Node]` 属性类型从具体类（`Button`/`TextEdit` 等）改为 GodotNodeInterfaces 接口类型

### 测试体系重构

- **单元测试模式统一**：所有 UI 测试改用 `FakeNodeTree` + `FakeDependency` + `_Notification(NotificationEnterTree/Ready)` 模式，避免实例化完整场景
- **Moq 替代 LightMoq**：测试统一使用 Moq 伪造节点树，与 GameDemo 参考实现一致
- **补充缺失测试**：新增 `TestSpellCardPanel`/`TestAliasPanel`/`TestWebSocketPanel`/`TestLogPanel`/`TestLogConfigPanel`/`TestAiModelConfigPanel`/`TestChatConfigPanel`
- **测试 Driver 更新**：所有 Driver 使用 `[Node]` 属性路径（`%` 前缀），消除硬编码路径字符串
