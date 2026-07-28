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

### 待完成

- 整合板块（暂不开发）
- 信息板块（暂不开发）
- 帮助板块（待开发）
- 设置面板剩余 5 个分类配置（猜测/整合/信息/帮助/通用）
- 「从工程文件导入」按钮（计划任务 B7）
- 消息筛选模式接入 WebSocket 处理器（计划任务 C13）
- Koishi 一键安装 `res://` 路径修正（计划任务 E5）
