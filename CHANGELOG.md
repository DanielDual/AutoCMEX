# AutoCMEX — 变更日志

## v0.0.1 (开发中)

### 新增

- **整合板块（阶段1）：LuaSTG 工程整合引擎**——解析 `.lstgproj/.lstges`（每行 `{level},{JSON}`）为节点树；按约定 `General.Comment` 注释（`Insert spellcards/resource/objects here`）检测符卡/资源/Object 注入点；抽取 `BossSpellCard` 整棵子树并按 SCName 区分符卡/非符；检测资源节点（`LoadImage/LoadBGM/AddFile` 等）与对象定义（`ObjectDefine/TaskDefine/BulletDefine` 等）；合并器把多创作者包按「符卡→Creator」映射顺序重编号注入模板 Boss，对象/顶层资源折叠到各自注入点并重写路径至纯文件名，收集命名冲突（保留原名 + 可选自动改名建议 `SuggestedName`），深度克隆隔离源包、多注入点每步前重扫避免索引错位；`SharpCliInvoker` 外调 `LuaSTGEditorSharp.Core.Cli.exe`（`-d -n -p`）编译打包 mod zip；对应表导出生成三列（Boss/符卡名/创作者）UTF-8 CSV 供猜测模块 `CsvImporter` 读回
- **整合板块（阶段2）：模型层与持久化**——新增 `CreatorPackage`（包名/创作者名/源路径/软删除标记）、`MergeConfig`（模板路径、Sharp 路径、插件 dll、输出目录、`IncludeLstges` 与 `ObfuscateLua` 两个导出开关、编辑中的对应表 `Mapping`）、`SpellCardMappingEntry`（名称/非符标记/Creator/来源包与源卡下标，顺序由列表位置决定，区分符卡与非符）；扩展 `DataManager` 读写 `creator_packages.json` / `merge_config.json`（AutoValue/AutoList + 现有转换器），不回归既有三文件
- **整合板块（阶段3）：四栏联动 UI 与导出**——新增 `IMergePanel`（父级引用接口，规避自定义脚本面板接口适配缺失）与 `MergePanel`（实现四栏联动、AutoValue/AutoList 绑定驱动，事件处理器只写数据模型）；`MergePanel.tscn` 重写为四栏结构（`RootSplit` 左右 `HSplit`、左/右各 `VSplit` 分上下，左上创作者包信息、左下工程模板配置、右上对应表、右下导出功能）；`MainWindow` 将 `MergePanelNode` 类型改为 `IMergePanel` 并注册 `_panels["merge"]` 供板块切换。导出链路：`MergeEngine` 桥接 `DataManager` 与引擎——`BuildAndMerge` 按映射顺序合并并依导出选项（是否包含工程文件/混淆开关）落地合并 `.lstgproj`，`ExportMapping` 生成三列（Boss/符卡名/创作者）对应表 CSV 供猜测模块导入；资源冲突可选自动改名，冲突汇总到列表展示
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
- **丢包重试不再手动刷新 UI**：`GuessingPanel.OnRetryAllDropped()` 事件处理器移除 `finally` 中的手动 `RefreshDroppedUI()`，改为仅恢复按钮忙碌禁用状态，丢包列表 UI 由 `DroppedGuesses`（`AutoList`）绑定 `OnModify` 自动驱动——遵循「事件只写数据模型、UI 由同步绑定传播」的重构核心原则

### 修复

- **LogService 资源泄漏**：`Shutdown()` 正确释放 `RotatingFileWriter` 并清空内存缓冲区
- **HeartbeatService CTS 浪费**：使用 `using` 声明替代显式 `finally Dispose`
- **DataManager 竞态条件**：`TriggerAutoSave` 使用 `volatile` 标志 + `async/await` 防止保存重叠
- **DataManager.LoadAll 集合替换**：`LoadAll()` 清空并重新填充现有 `ObservableCollection` 而非创建新实例，保持 `CollectionChanged` 事件订阅有效
- **WebSocket 分片消息截断**：`ReceiveLoop` 使用 `MemoryStream` 累积分片，检查 `EndOfMessage`
- **SettingsPanel 场景树耦合**：改为配置驱动，`AppSettings.PropertyChanged` → `MainWindow` 自动重启 WebSocket
- **AesEncryptor 路径硬编码**：提取 `DefaultKeyFileName` 常量和 `GetDefaultKeyPath()` 方法，统一所有调用点
- **运行时 KeyNotFoundException（面板接口适配缺失）**：`GuessingPanel`/`AiModelConfigPanel` 等自定义脚本面板被父级 `[Node]` 引用时，AutoInject 的非泛型 `AdaptNode` 无法按自定义运行时类型获取节点接口适配器而抛 `KeyNotFoundException`（连带 `SettingsPanel.OnReady`、`ModelEntryPanel.Setup` 的 `NullReferenceException`）。修复：新增 `IGuessingPanel`/`ISettingsPanel`/`ILogPanel`/`IAiModelConfigPanel`/`IChatConfigPanel` 接口并由对应面板实现，`MainWindow`/`SettingsPanel` 的 `[Node]` 属性类型改为对应接口（无脚本面板改用 Godot `Control`）；`AiModelConfigPanel` 修正 `ModelEntryPanel` 先加入节点树再 `Setup` 的时序 bug
- **符卡面板导入后显示空白**：重构将 `SpellCardPanel` 提为独立子场景时丢失了 Boss 选择器——`RefreshBossSelect()` 空实现、`_currentBoss` 从未被 UI 赋值，原 `BossSelect` 下拉残留在父场景 `GuessingPanel.tscn` 却无脚本引用。导致即便成功导入"符卡—创作者表"，`_currentBoss` 仍为 `null` 使树恒为空。修复：将 `BossSelect` 下拉移入 `SpellCardPanel` 自身场景；面板改为**纯 Sync 绑定驱动**——当前 Boss 以 `AppSettings.SelectedBossIndex`（`AutoValue<int>`）为单一数据源（与猜测流程共享），`Bosses`/`SelectedBossIndex`/当前 Boss `SpellCards` 三条 `Bind()` 自动推送 UI，事件处理器只写数据模型；导入后自动把选中下标规范到首个 Boss，树不再空白。补充回归测试覆盖"导入后自动选中首个 Boss、树非空白"、“越界下标回落”、“空表清空”

- **猜测面板丢包按钮被挤出窗口**（`GuessingPanel` 布局）：`MainContainer` 为 `VSplitContainer` 却只有一个 pane 且配 `split_offset=40`，又带越界 `offset_right/bottom`，将整块内容钳在顶部并把底部内容推出窗口底缘，`DroppedButtons` 被 `DroppedList` 挤出窗口。修复：`MainContainer` 改 `VBoxContainer` 并全展开、归零越界 offset；`ContentArea` 改 `HSplitContainer` 支持拖动调整左右栏宽度；`DroppedButtons` 加 `custom_minimum_size` 保底防挤出

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
- **MainWindow 面板引用**：`LogPanel`/`WebSocketPanel` 节点属性改为 `IControl` 接口类型，通过 `GodotNodeInterfaces` 适配器访问

### 依赖注入统一

- **WebSocketPanel 接入 AutoInject**：使用 `[Node]` 属性替代 `GetNode<>` 手动查找，`[Dependency]` 获取 `IWebSocketServer`
- **LogConfigPanel 接入 AutoInject**：使用 `[Node]` 属性替代 `GetNode<>` 手动查找，`[Dependency]` 获取 `ILogService`
- **GuessingPanel 节点属性接口化**：`[Node]` 属性类型从具体类（`Button`/`TextEdit` 等）改为 GodotNodeInterfaces 接口类型

### 测试体系重构

- **单元测试模式统一**：所有 UI 测试改用 `FakeNodeTree` + `FakeDependency` + `_Notification(NotificationEnterTree/Ready)` 模式，避免实例化完整场景
- **Moq 替代 LightMoq**：测试统一使用 Moq 伪造节点树，与 GameDemo 参考实现一致
- **补充缺失测试**：新增 `TestSpellCardPanel`/`TestAliasPanel`/`TestWebSocketPanel`/`TestLogPanel`/`TestLogConfigPanel`/`TestAiModelConfigPanel`/`TestChatConfigPanel`
- **测试 Driver 更新**：所有 Driver 使用 `[Node]` 属性路径（`%` 前缀），消除硬编码路径字符串

### 修复

- **AI 模型列表空白排查与稳健化（设置页）**：设置页「AI 模型」分类下已配置模型列表区域空白（下拉框可正确列出模型）。通过新增真实场景复现测试（`TestModelEntryPanelRuntime`）实证：动态实例化 `ModelEntryPanel` 并 `AddChild` 后 AutoInject 能正常解析其 `[Node]`，整面板真实刷新后 `ModelList` 能渲染条目——装配/刷新链路本身正常，故空白并非来源于此。实际补强两处：
  1. **激活模型选择缺失**：`AiModelConfigPanel` 此前未连接 `ActiveModelSelect.ItemSelected`，导致用户选择模型时 `ActiveAiModelId` 从不更新（现存数据 `activeAiModelId` 为无效 id）。现已补上选择处理器，写入数据模型。
  2. **列表迁移到 AutoList 绑定驱动**：`OnResolved` 建立 `_settings.AiModels.Bind().OnModify(...)`，模型增删时列表由绑定自动重建，事件处理器只写数据模型、移除手动 `Refresh()`（符合「禁止手动刷新实现 UI 同步」核心原则）。
- **布局根因（列表可见）**：用户真机确认 `ModelList` 确有每个模型对应的配置节点但不可见——根因是嵌入面板（`AiModelConfigPanel`/`ChatConfigPanel`）作为 `ConfigArea`（`Control`，非容器、不布局子节点）的实例子节点，未铺满父容器导致根节点高度按内容最小化 ≈0，`ModelScroll`（`ScrollContainer`，Expand）分不到高度，从而列表节点存在但不可见。修复：在 `SettingsPanel.tscn` 中给两个嵌入实例节点手动设置 FullRect anchors（`anchors_preset=15`、`anchor_right/bottom=1.0`、`grow_horizontal/vertical=2`，参考 `MainContainer` 的 anchor 写法）；因 gopeak 无法写入 `Control` 的 anchors 属性，经用户许可手动编辑 `.tscn`。验证：`dotnet build` 0 错误，GoDotTest 220 通过 / 0 失败（含真实场景渲染与 AutoList 绑定重建回归测试）。
