# AutoCMEX — 工程纪律与开发规则

本文件列出自项目重构以来沉淀的 54 条工程纪律，覆盖依赖注入、节点树与场景、数据同步、测试体系、技术栈与流程规范。每条都是可执行要求，代码评审与任务验收以本文件为判定依据。

**迁移说明**：本文件原为项目内部的本地笔记（源自项目重构计划），现作为正式文档入库，原笔记保留为指向本文件的短指针。迁移时只做了两处必要修正，未改动任何规则的约束含义：规则 44 去掉本机绝对路径（公开仓库不落本地路径），规则 49 的「按 plan.md」改为「按已批准方案」（原计划文件名已不存在）。

## 适用范围

- 适用于本仓库全部业务代码（`src/`）与测试（`test/`）的新增、修改与评审；纯文档与脚本改动不强制适用技术栈条款。
- 「必须 / 禁止」类规则默认不新增例外；确需例外时，必须写进 `docs/technical-specification.md` 并注明理由与代价。既有例外：对话框自带按钮（`AcceptDialog.AddButton` 创建的「取消录制」「只重试失败项」）不写进场景，属对话框 chrome（见技术规格「工程纪律与已知旧账」）。
- 规则编号会被代码注释、评审清单与其它文档直接引用（如「规则 19」），因此编号是稳定标识，见「维护方式」。

## 与 AGENTS.md 的关系

- `AGENTS.md` 是项目最高行为准则（优先级：项目根目录强制规则 > `AGENTS.md` > 项目 Skill > 全局 Skill > AI 通用能力）；本文件是其下层的工程纪律细则，把「怎么做」落到代码与测试层面。
- 两者冲突时以 `AGENTS.md` 为准；本文件不重复 `AGENTS.md` 已规定的禁止事项、权限边界、提交与变更日志规范。
- 代码格式细则以仓库根的 `.editorconfig`（Chickensoft C# Style）为准，本文件不重复缩进、换行等格式条款。

## 维护方式

- 规则的新增、修改与废止随需求评审一起过，不单独提交；改完同步检查 `AGENTS.md` 与 `docs/technical-specification.md` 里对规则编号的引用。
- 新增规则继续占用递增编号（当前到 54），不复用已废止的编号；废止的规则保留编号并标注「已废止（原因）」，避免历史引用指向别的规则。
- 规则目前没有 analyzer / CI 守护（技术规格「已知旧账」有记录），执行靠评审与自查；后续若加守护，优先覆盖规则 4、7、18、19、20 这几条可机械检查的。

## 一、依赖注入（DI）

1. 所有 Godot 节点脚本必须使用 `Chickensoft.AutoInject` 接入依赖注入。
2. 禁止手动注入：不得使用 `SetServer`、`BindToService`、fallback factory 等绕过 DI 容器的方案。
3. 依赖声明统一使用 `[Dependency]` 属性 + `this.DependOn<T>()` 表达式。
4. 节点引用统一使用 `[Node("%UniqueName")]` 属性注入，禁止使用 `GetNode<>` 手动查找节点。
5. 每个面板脚本必须声明 `[Meta(typeof(IAutoNode))]` 并实现 `public override void _Notification(int what) => this.Notify(what);`。
6. 生命周期方法约定：`OnReady()` 中连接信号与初始化 UI；`OnResolved()` 中读取依赖并建立数据绑定；`_ExitTree()` 中释放所有 Binding。

## 二、节点树与场景

7. 禁止动态构建节点树：不得使用 `new VBoxContainer()`、`new Button()` 等代码创建 UI 节点。
8. 所有 UI 结构必须定义在 `.tscn` 场景文件中，代码只操作场景中已存在的节点。
9. 每个场景根节点上只能有一个脚本；子节点脚本必须提取为独立场景（如 `SpellCardPanel.tscn`、`AliasPanel.tscn`）。
10. 脚本只操作直接子节点，不跨层操作孙节点。
11. 子场景通过 `instance=ExtResource` 场景实例引用嵌入父场景，父场景通过 `[Node]` 属性引用子场景根节点。
12. 面板切换/显隐通过 `Visible` 属性控制，不动态创建/销毁节点。
13. `.tscn` 文件修改必须使用 gopeak-godot-mcp 工具，禁止手动编辑（唯一例外：`instance=ExtResource` 场景实例引用可手动编辑，且需参考已有用法）。

## 三、数据同步（Chickensoft.Sync）

14. 集合数据使用 `AutoList<T>`，标量数据使用 `AutoValue<T>`，禁止使用 `ObservableCollection` 和 `INotifyPropertyChanged`。
15. 数据模型迁移规则：`ObservableCollection<T>` → `AutoList<T>`；`INotifyPropertyChanged` 属性 → `AutoValue<T>`。
16. 所有 `AutoValue<T>` 属性访问必须加 `.Value`（读取与赋值均如此）。
17. 数据变更通知由 Sync 内部管理，禁止手动事件订阅/取消订阅。
18. UI 刷新必须由 Sync 绑定驱动：`AutoList<T>.Bind().OnModify(...)`、`AutoValue<T>.Bind().OnValue(...)`，禁止手动调用 Refresh 方法。
19. 绑定回调中操作 UI 必须使用 `CallDeferred(nameof(RefreshXxx))` 调度到主线程，禁止在回调中直接操作 UI 节点。
20. 所有 Binding 必须在 `_ExitTree()` 中 `Dispose()`，防止泄漏。
21. 跨线程访问 AutoList（如 WebSocket ThreadPool、`Task.WhenAll` 并行任务）必须用 `lock` 保护集合操作。
22. 遍历集合时若会修改集合（Remove/Clear），必须先用快照（`ToList()`）再遍历，禁止在枚举期间修改集合。
23. 数据流约定：用户操作 UI → `[Node]` 属性绑定 → AutoList/AutoValue 自动变更通知 → DataManager 自动保存（防抖）→ JSON 文件持久化 → UI 自动刷新（Sync 驱动，无需手动 Refresh）。

## 四、测试体系

24. 测试框架：GoDotTest + GodotTestDriver + Shouldly + Moq。
25. 所有 UI 测试必须使用单元测试模式：`FakeNodeTree` + `FakeDependency` + `_Notification((int)Node.NotificationEnterTree)` + `_Notification((int)Node.NotificationReady)`，禁止实例化完整场景。
26. 测试 Setup 中必须设置 `(_panel as IAutoInit).IsTesting = true`。
27. `[Node]` 属性类型必须是接口类型（如 `IButton`、`ITree`），不能是具体类（如 `Button`）。
28. Mock 属性验证需 `SetupProperty(m => m.Prop)` 才能验证赋值；接口方法带可选参数时，Moq 表达式需显式提供所有参数（`It.IsAny<int>()` 等）。
29. 测试中 Mock 的 `ITree` 需转发到真实 `Tree` 实例（`CreateItem`/`GetRoot`），且真实 tree 的 `Columns` 必须与 `OnReady` 中设置一致，否则 `SetEditable` 越界产生 ERROR 日志。
30. 绑定回调使用 `CallDeferred` 后，测试需 `await _panel.ToSignal(TestScene.GetTree(), "process_frame")` 等待一帧再断言。
31. 测试清理统一使用 `_toCleanup.Add(_panel)` + `QueueFree()`。
32. 测试覆盖率要求 >= 80%，避免过度测试或测试不足。
33. 测试 Driver 使用 `[Node]` 属性路径，禁止硬编码路径字符串。

## 五、技术栈（固定版本）

34. GUI 框架：Godot 4.7 + .NET 8，脚本语言 C#。
35. 依赖注入：Chickensoft.AutoInject 2.13.10。
36. 节点接口抽象：Chickensoft.GodotNodeInterfaces 3.0.16。
37. 数据同步：Chickensoft.Sync。
38. 测试框架：Chickensoft.GoDotTest 2.0.35、Chickensoft.GodotTestDriver 3.1.72。
39. 断言：Shouldly 4.3.0。
40. Mock：Moq（或 LightMock.Generator 1.2.3 / LightMoq 0.1.0）。
41. 元数据：Chickensoft.Introspection 3.0.3 + Generator。
42. 日志：Chickensoft.Log 2.0.0 + Chickensoft.Log.Godot 1.0.80。
43. 状态机：Chickensoft.LogicBlocks 6.0.0。
44. 参考实现：Chickensoft 官方示例仓库 GameDemo 与 Chickensoft 架构文章。

## 六、流程规范

45. 新代码必须遵循"一个场景一个脚本"规则，禁止引入技术债务。
46. 禁止使用 `GetNode<>`、fallback factory、动态 UI 构建、`ObservableCollection`、`INotifyPropertyChanged`。
47. 开发顺序：先 `dotnet build` 验证编译，再 `$GODOT --run-tests --quit-on-finish --coverage` 运行测试。
48. 验证时不要只看测试输出最后几行（Godot 关闭时的资源泄露可能隐藏结果）。
49. 任务完成后必须唤出 code-reviewer Subagent 按已批准方案评定代码，任务是否完成由 code-reviewer 决定。
50. 使用 docs-scraper 查阅 Chickensoft.Sync/AutoInject/GodotNodeInterfaces 文档，确保 API 使用正确。
51. 使用 gopeak-godot-mcp 创建和修改 `.tscn` 场景文件。
52. 使用 csharp-solution-output-standard 规范方案输出格式；csharp-test-granularity-standard 规范测试颗粒度；csharp-document-writing-standard 规范文档编写。
53. 使用 godot-master 获取 Godot 4.x 最佳实践；使用 task-commander 分解并行任务；使用 code-explorer 搜索定位代码。
54. 重构完成后更新文档和 CHANGELOG。
