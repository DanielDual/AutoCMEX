# AutoCMEX — 变更日志

## v0.0.1 (开发中)

### 新增

- **整合板块：按创作者分组文件夹（`GroupByCreatorFolders`）与 Object 节点命中模板判断**——① 新增持久化布尔开关（`MergeConfig.GroupByCreatorFolders`，`merge_config.json`），开启后注入资源/Object 节点时按包（创作者）分组：为每个有贡献的包建立专属 `General.Folder`（Name=创作者名），把该包的资源节点与 Object 定义分别放入各自文件夹（不再全部堆叠到单一注入点旁）；`MergeOptions` 新增 `GroupByCreatorFolders`，`Merge` 新增可选参数 `creatorNames`（按包序给文件夹命名，缺失回退包名），`MergeEngine.BuildAndMerge` 读取配置并从 `CreatorPackage.CreatorName` 组装 `creatorNames` 数组传入；`BuildInjectedSegments` 支持分组注入（文件夹头=注入点同级、其内节点位于下一层，资源「归档空间与资源互为同级兄弟、非归档子树」的流式语义保持）；符卡注入点不受此开关影响。② Object/定义节点补齐「命中模板」判断（判据=名字，经用户实测反馈定案）：资源此前已按「最内层归属 `ArchiveSpace` 命中「模板已有归档∪排除清单」」跳过不搬；Object 定义（除共享 `BossDefine`、`.General.Code`）此前无条件注入。初版曾按归档命中判断，但真机实测**所有 Obj 定义都被命中跳过**（创作者定义也常落归档/模板下）——用户指示改判据为**名字**（Obj/定义实际不允许重名，同名会告警），故 `Merger` 新增 `CollectTemplateDefinitionNames`（枚举模板可移植定义 Name）与 `DefinitionName`，普通定义命中「模板已有同名可移植定义」则跳过（不搬）；`.General.Code` 仍按归档归属排除（模板已有该归档则不搬）。UI：`MergePanel.tscn/.cs` 新增 `GroupByCreatorFoldersToggle` CheckBox 完整绑定链（`PersistConfig`/`SyncOutputToModel`/`LoadConfigToControls`）。测试：`MergerTest` 新增 `Merge_Definition_SameNameAsTemplate_Skipped`、`Merge_Definition_DifferentNameFromTemplate_Injected`（名字判据命中/未命中）、`Merge_GroupByCreatorFolders_True_CreatesFolderPerCreator`（多人各自文件夹、资源/对象归属不串包）、`Merge_GroupByCreatorFolders_False_NoCreatorFolders`（开关关闭零回归、平铺）；`MergeEngineTest` 新增真实数据 `sample_rich_package` 分组往返（父链合法+可解析）；`MergeModelsTest` round-trip 补 `GroupByCreatorFolders` 赋值+断言。验证：dotnet build 0 错误、GoDotTest 314 通过 / 0 失败。
- **整合板块：强制 Perform Action 配置项（`ForcePerformAction`）**——新增持久化布尔开关（`MergeConfig.ForcePerformAction`，`merge_config.json`），导出全量符卡时，对**每张符卡**（不论其自身 `Performing action` 属性）：若其**紧邻前一兄弟节点**为 `Boss.Dialog`/`Boss.MoveTo`/`Boss.BossMoveTo`（**不含前一张 BossSpellCard**），即强制按 Perform Action 方式整合（携带该紧邻前序节点及整棵子树一起注入，行为与 Perform Action 一致）；否则按普通符卡整合。实现：`MergeOptions` 新增 `ForcePerformAction`，`MergeEngine.BuildAndMerge` 从配置读取并组装，`SpellCardExtractor.Extract` 增加第二参数 `forcePerformAction=false` 与 `IsImmediatelyPrecededByNonCard` 紧邻判定（复用既有 `CollectLeadingStages` 紧邻收集，判定排除符卡）；`MergePanel.tscn/.cs` 新增 `ForcePerformActionToggle` CheckBox 完整绑定链（`PersistConfig`/`SyncOutputToModel`/`LoadConfigToControls`）。测试：`MergerTest` 新增 `Merge_ForcePerformAction_True_ImmediatelyPrecededByDialog_CarriesLeading`、`..._ByBossMoveTo_CarriesLeading`、`..._ByLegacyMoveTo_CarriesLeading`（旧版 `.Boss.MoveTo` 双候选）、`..._BySpellCard_NotForced`（紧邻前序为符卡不强制）、`..._NoImmediatelyPreceding_NotForced`（无前序）、`..._False_NoChange`（开关关闭零回归）；`MergeModelsTest` round-trip 补 `ForcePerformAction` 赋值+断言。验证：dotnet build 0 错误、GoDotTest 309 通过 / 0 失败。
- **整合板块（阶段1）：LuaSTG 工程整合引擎**——解析 `.lstgproj/.lstges`（每行 `{level},{JSON}`）为节点树；按约定 `General.Comment` 注释（`Insert spellcards/resource/objects here`）检测符卡/资源/Object 注入点；抽取 `BossSpellCard` 整棵子树并按 SCName 区分符卡/非符；检测资源节点（`LoadImage/LoadBGM/AddFile` 等）与对象定义（`ObjectDefine/TaskDefine/BulletDefine` 等）；合并器把多创作者包按「符卡→Creator」映射顺序重编号注入模板 Boss，对象/顶层资源折叠到各自注入点并重写路径至纯文件名，收集命名冲突（保留原名 + 可选自动改名建议 `SuggestedName`），深度克隆隔离源包、多注入点每步前重扫避免索引错位；`SharpCliInvoker` 外调 `LuaSTGEditorSharp.Core.Cli.exe`（`-d -n -p`）编译打包 mod zip；对应表导出生成三列（Boss/符卡名/创作者）UTF-8 CSV 供猜测模块 `CsvImporter` 读回
- **整合板块（阶段2）：模型层与持久化**——新增 `CreatorPackage`（包名/创作者名/源路径/软删除标记）、`MergeConfig`（模板路径、Sharp 路径、插件 dll、输出目录、`IncludeLstges` 与 `ObfuscateLua` 两个导出开关、编辑中的对应表 `Mapping`）、`SpellCardMappingEntry`（名称/非符标记/Creator/来源包与源卡下标，顺序由列表位置决定，区分符卡与非符）；扩展 `DataManager` 读写 `creator_packages.json` / `merge_config.json`（AutoValue/AutoList + 现有转换器），不回归既有三文件
- **整合板块（阶段3）：四栏联动 UI 与导出**——新增 `IMergePanel`（父级引用接口，规避自定义脚本面板接口适配缺失）与 `MergePanel`（实现四栏联动、AutoValue/AutoList 绑定驱动，事件处理器只写数据模型）；`MergePanel.tscn` 重写为四栏结构（`RootSplit` 左右 `HSplit`、左/右各 `VSplit` 分上下，左上创作者包信息、左下工程模板配置、右上对应表、右下导出功能）；`MainWindow` 将 `MergePanelNode` 类型改为 `IMergePanel` 并注册 `_panels["merge"]` 供板块切换。导出链路：`MergeEngine` 桥接 `DataManager` 与引擎——`BuildAndMerge` 按映射顺序合并并依导出选项（是否包含工程文件/混淆开关）落地合并 `.lstgproj`，`ExportMapping` 生成三列（Boss/符卡名/创作者）对应表 CSV 供猜测模块导入；资源冲突可选自动改名，冲突汇总到列表展示
- **整合板块（阶段3 增强）：四栏联动补齐与操作引导**——`CreatorPackage` 新增 `SpellCards/Resources/Objects` 三个清单缓存（导入 zip 时用 `SpellCardExtractor`/`ResourceDetector`/`ObjectDetector` 同步抽取填充，UI 直接展示免二次解析）；`MergeConfig` 新增 `SelectedPackageIndex`（`AutoValue<int>`，默认 -1）作为单一数据源；`MergePanel` 选中包后三栏（符卡/资源/对象清单）由 `SelectedPackageIndex` 绑定驱动联动刷新，事件处理器只写模型（遵循指示 24）；删除包时同步清理对应表中该包的映射条目（避免删包后再导出失败），并清空三栏；`MergePanel.tscn` 为四栏各列表新增说明 `Label`（左下补「工程模板配置」标题、右下补「导出功能」标题），引导用户理解导入后流程
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

- **整合板块：Performing action 符卡携带**紧邻上一阶段**节点一起注入**：真机符卡具备 `Performing action` 属性（Sharp 中为 `opening_performance`，`DefineSpellCard.cgen/.vm` 实证其语义为「符卡练习时将入口重定向至**上一阶段**」）。此前整合器只抽取符卡子树、丢弃其前序兄弟（`Boss.Dialog`/`Boss.MoveTo`/前一张 `BossSpellCard` 等 `cards` 序列阶段成员），导致该类符卡练习时引用缺失的前序阶段而报错。修复：`SpellCardExtractor` 抽取符卡时读取 `Performing action` 属性，为 `true` 时向前扫描同父 `BossDefine` 下的 `cards` 序列紧邻前序兄弟（含整棵子树）；scprac 通过 `_sc_table` 中以 `#_temporary_class.cards` 记录的该卡下标定位上一阶段 = `cards[seq-1]`，即只带**紧邻的前一个阶段**（非直到 `BossInit` 的所有前序；`BossInit` 是 `:init` 非 cards 成员、为停止边界）。`SpellCardInfo` 新增 `HasPerformingAction`/`LeadingStartIndex`/`LeadingNodes`；`Merger.Merge` 构造符卡 `SubtreeRef` 时把紧邻前序段并入 `Nodes`、`StartIndex` 前移至该前序节点，深度克隆隔离源包、父链对齐兜底。测试：新增 `Merge_SpellCard_PerformingActionTrue_CarriesOnlyImmediatelyPrecedingStage`（卡前多个阶段时只带最近的 BossMoveTo、更早 Dialog 不注入，锁定核心边界）、`..._NoPrecedingStage_CarriesNone`（卡为首个阶段无前序，不携带）、`..._CommentsPreceding_AreSkipped`（跨越 Comment 后命中紧邻 Dialog）、`..._CarriesLeadingDialog`（紧邻前序 Dialog 子树带注入且位于卡前）、`..._False_NoLeadingCarried`（false 不携带）、`..._Missing_NoLeadingCarried`（属性缺失容错）；`sample_rich_package.lstges` L313-315（`Performing action=true` 符卡）真实数据往返父链合法。验证：dotnet build 0 错误、GoDotTest 303 通过 / 0 失败。
- **GuessingPanel 启动 NRE（AppSettings 反序列化 null 覆盖）**：真机运行时 `GuessingPanel.OnResolved()` 抛 `NullReferenceException`（`GuessingPanel.cs:114`），依赖注入链中断、`MainWindow.OnProvided` 未执行，左栏 Tab 按钮 `Pressed` 信号未挂接、点击无反应。根因：`app_settings.json` 含显式 `"activeAiModelId": null`，System.Text.Json 反序列化用 JSON 值覆盖构造器默认值，使 `AppSettings.ActiveAiModelId`（`AutoValue<string?>`）变为 `null`；`DataManager.BindSettingsChanges` 有 `!= null` 防护故未崩，而 `GuessingPanel.OnResolved` 直接 `.Bind()` 无防护即 NRE。修复（根上、单一数据源）：`AppSettings` 新增 `EnsureIntegrity()`，把被 JSON null 覆盖的 AutoValue/AutoList 属性回填为构造器默认值；`DataManager.LoadAll` 反序列化 `Settings` 后调用。测试：`StorageTest` 新增 `DataManager_LoadAll_NullFieldsBackfilled`（写含 null 字段的 app_settings.json，断言各同步属性非空）。验证：dotnet build 0 错误、GoDotTest 296 通过/0 失败、真机复跑 NRE 消除。
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
- **整合板块配置不持久化**（`MergePanel`）：工程模板配置三项（模板路径/Sharp 路径/插件 dll）与输出目录**关掉重开 AutoCMEX 后丢失**。根因在 UI 层而非序列化层（`merge_config.json` 链路健康）：`SharpPathEdit`/`PluginDllEdit` 仅有 `[Node]` 声明无任何 `TextChanged` 写回；`TemplatePathEdit` 只在点导入按钮时写模型；`OutputDirEdit` 只在开关或导出时写；且 `LoadConfigToControls()` 只回填 3 个开关与 `OutputDirEdit`、未回填 3 个路径编辑框。修复（遵循指示 24「事件只写模型」）：新增 `SyncConfigToModel()`，为 4 个 `LineEdit` 统一接 `TextChanged` 写回模型并 `TriggerAutoSave`；`LoadConfigToControls()` 补齐回填全部 4 个编辑框
- **整合板块 Sharp 打包失败（资源归档与物理随迁缺失）**：合并工程 `.lstgproj` 落在不含配套资源的全新目录，`Merger.RewriteResourceNode` 把资源路径**折成纯文件名**（丢掉「源目录中的相对位置」），且**未搬迁创作者包的 `ArchiveSpaceIndicator`**（决定压缩包内相对基准），导致 LuaSTGEditorSharp Cli 打包时基准错乱，退出码 `-2147450730`、stdout/stderr 为空。修复：① `Merger` 保留资源**源目录中的完整相对路径**（`ReplaceResourceFileName` 冲突改名时仅换文件名、保留目录层级）；② 顶层资源未命中排除集时连同其最内层 `ArchiveSpaceIndicator` **原样搬迁注入**（`RootLevel` 以归档空间为子树根，令其落于注入点同级、资源在下，保持相对基准）；③ `MergeEngine.CopyResources` 在构建后把合并工程**引用的全部物理资源**（含符卡/对象子树内嵌）按相对路径从源（包解压目录优先 + 模板目录）复制到工作目录与输出目录。同时新增排除机制：`MergeConfig` 新增 `AutoList<string> ExcludedArchiveSpaces`（默认空）；`Merger` 把「模板已有 ArchiveSpace 集合 ∪ 配置清单」作为排除基准，命中者不检测、不导入、不复制（模板已含、创作者不改动的资源不重复整合）
- **整合板块导出包缺脚本（ArchiveSpace 流式配对缺陷）**：真机导出包缺 `sample_exp/` 脚本。实证定案：**不是复制链路漏脚本**（复制正常、无 missing），而是上游「资源/归档采集」缺陷——Sharp 源码 `ArchiveSpaceIndicator.AddCompileSettings()` 把 `CompileProcess.archiveSpace` 设为该值，证明 **ArchiveSpace 是流式作用域（其后所有节点无论层级归属它，直到下一个 ArchiveSpace）**；而旧 `Merger.InnermostArchiveSpace` 按「父子层级」向上扫 `level<node.Level` 的祖先归档，漏掉了「与归档同级且在归档之后」的脚本 Patch（`sample_exp\` 归档注入了、脚本未注入 → 合并文档无脚本引用 → 无从复制，输出无 `sample_exp/` 也不报 missing）。修复：`InnermostArchiveSpace` 改为**流式前驱配对**——向前扫「位置最近的一个 ArchiveSpace（不限层级）」，使同级脚本正确携带归档注入、脚本进合并、复制随迁到工作/输出目录。测试：新增「脚本与归档同级、在归档后」的注入回归测试；修正排除集成测试数据（未排除资源须在归档**之前**，归档之后的节点流式上归该归档而应被排除）。验证：dotnet build 0 错误、GoDotTest 287 通过/0 失败
- **整合板块 Sharp 打开产物抛 NRE（注入段父链对齐）**：真机用 LuaSTGEditorSharp **编辑器打开**合并 `.lstgproj` 时，`DocumentData.CreateNodeFromFileAsync`（`:328 prev.AddChild`）抛 `NullReferenceException`。根因：Sharp 按行用 `levelgrad=当前层-前层` 重建父链，负跳时反复 `prev=prev.Parent` 回溯；**当回落步数超过现有父链深度时 prev 越过 root 变 null**，下一行 `AddChild` 即 NRE。「大负跳本身合法（`.lstgproj` 允许跳层）」，非法条件是「回溯步数超出父链深度」。修复（新增 `src/core/merge/LstgesHierarchy.cs`——plan 文件清单之外的追加、阶段1 引擎层 bugfix）：`FindFirstInvalidLevel` 逐行模拟 Sharp 重建、返回首个「父链回溯越界」行（含对「跳层后再浅回落」漏报的钉死）；`NormalizeParentChain` **就地父链对齐**，把越界节点层级抬到「现有父链可容纳的最浅合法层」使回溯停在 root 之下、不越界。`Merger` 注入完成后调用父链对齐，若发生修正则记录 warning「合并产物父链已自动对齐」，从根上保证交付产物可被 Sharp 与编辑器安全打开（不再产出 NRE 文件）。测试：新增合法序列→无越界、`0,6,3` 与 `0,1,4,3,2` 越界检测、`NormalizeParentChain` 修正+幂等、富包合并产物父链合法+往返可解析

- **整合板块：独立定义/代码节点移植补全（原静默丢弃）**：`Merger` 原先只注入「符卡 + 对象定义 + 顶层资源」三类，创作者包中不落在任何被注入子树下的独立承载运行时代码节点——`EnemyDefine`（敌人类型）、`BentLaserDefine`（弯折激光）、`BossBGDefine`（Boss 背景定义，如 `test_scbg1`）、`RenderTarget`/`CreateRenderTarget`/`OnRender`/`Render4V`（渲染/渲染目标）、`Data.Function`（Lua 函数定义）、`Advanced.UnidentifiedNode`（自定义节点）——会被静默丢弃，导致产物运行缺类/缺函数/缺背景报错。修复：`ObjectDetector.ObjectTypes` 扩展为「可移植定义/代码节点」集合（纳入上述类型；明确**不含 `.Stage.*`**——关卡由模板统一提供；`BossDefine` 仍由模板共享排除）；`Merger` 把 `CollectObjectSubtrees` + `CollectTopLevelResources` **统一为单一 `CollectTransplantables`**，返回 `(Definitions, Resources)` 两类、共用同一覆盖规则（已被已注入符卡/定义子树覆盖的节点不重复采集，其嵌套定义/资源随所属被注入子树一起移植避免双份），按 `IsResource` 标签分到对象/资源两个注入点；资源类仍走物理随迁链路不回归。测试：新增独立定义注入、嵌套定义不重复注入、BossDefine 不重复注入断言。验证：dotnet build 0 错误、GoDotTest 290 通过 / 0 失败
- **丢包仓储提取**：`GuessProcessingService` 的丢包管理抽取为 `IDroppedGuessRepository` / `DroppedGuessRepository`
- **GuessingPanel 子节点脚本化**：符卡表/别名表/导入导出逻辑下放到 `SpellCardTreeHandler` / `AliasTreeHandler` 子节点脚本

### 测试改进

- **测试覆盖率提升**：新增 43 个测试（从 138 → 181），覆盖 StringEscapeHelper、DroppedGuess/DroppedGuessRepository、ImporterFactory 等 0% 覆盖模块
- **自定义 TestDriver**：创建 GuessingPanelDriver、SpellCardTreeHandlerDriver、AliasTreeHandlerDriver，封装复杂 UI 节点操作为高阶 API，解耦测试与节点路径
- **测试质量提升**：将存在性行为测试改为行为测试，补充 INotifyPropertyChanged 测试和负面用例
- **修复失败测试**：更新 GuessEngineTest 和 GuessProcessingServiceTest 断言以匹配当前 GuessResponseHandler 行为（emoji 格式）
- **整合板块定义节点移植测试**：新增 `Merge_StandaloneDefinitions_InjectedIntoObjectMarker`（独立 `EnemyDefine`/`BentLaserDefine`/`BossBGDefine`/`RenderTarget`/`CreateRenderTarget`/`Data.Function`/`UnidentifiedNode` 注入对象注入点、层级重编号正确）、`Merge_BossDefineChildren_NotStandaloneInjectedWhenCovered`（BossDefine 由模板共享不重复注入）、`Merge_NestedDefinitionInsideInjectedSubtree_NotReInjected`（嵌套在被注入子树内的定义不重复注入，避免双份）
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
