# AGENTS.md

## 生效规则

1. 本文是项目最高行为准则，所有AI模型必须遵守
2. 规则优先级：项目根目录强制规则 > 本文件 > 项目Skill > 全局Skill > AI通用能力
3. 工程纪律细则（依赖注入 / 节点树与场景 / 数据同步 / 测试体系 / 技术栈 / 流程规范）见 `docs/engineering-guidelines.md`；与本文件冲突时以本文件为准

## 项目结构

- `/src`：业务代码主目录
- `/docs`：项目文档主目录
- `/test`：GoDotTest 测试
- `/CHANGELOG.md`：变更日志
- `/.editorconfig`：团队编码规范

## 核心职责

1. 接收需求 → 输出方案 → 等待评审
2. 评审通过后拉取最新代码，创建功能分支开发
3. 编写测试，测试通过后更新文档
4. 输出代码审查清单，等待用户审查
5. 全程遵守规则，不越权、不违规

## 禁止事项

1. 禁止代替用户做最终决策（需求方案、代码合并、版本发布）
2. 禁止私自修改项目架构、核心配置、环境变量
3. 禁止私自合并/推送代码到主分支
4. 禁止未经评审提前编写业务代码
5. 禁止为了通过测试修改测试用例/断言，必须优先修正业务逻辑
6. 禁止输出有安全漏洞的代码或超出需求实现额外功能
7. 禁止在多次尝试无果后继续无效操作，应及时向用户反馈

## 强制工作流

1. **方案阶段**：按`csharp-solution-output-standard`规范输出实现方案，等待评审
2. **开发阶段**：评审通过后拉取最新代码，创建功能分支，实现业务逻辑
3. **测试阶段**：按`csharp-test-granularity-standard`规范编写测试，必须通过运行时测试验证功能正常（`godot --run-tests --quit-on-finish`，需窗口，不能 `--headless`；本仓库无 .NET 测试项目，`dotnet test` 跑不了）；测试不通过优先修正业务逻辑
4. **文档阶段**：按`csharp-document-writing-standard`规范更新文档
5. **审查阶段**：输出代码审查清单，等待用户审查通过
   _注：纯问答、只读任务不走此流程_

### 核心Skill说明

| Skill                              | 用途                             |
| ---------------------------------- | -------------------------------- |
| `csharp-solution-output-standard`  | 规范实现方案的输出格式与内容要求 |
| `csharp-test-granularity-standard` | 规范测试代码的颗粒度与覆盖要求   |
| `csharp-document-writing-standard` | 规范项目文档的编写标准           |

## 权限边界

### 目录权限

- 可写：`/src`、`/docs`、`/CHANGELOG.md`
- 禁止：`.git`、`/bin`、`/obj`、核心配置文件（`.csproj`等）、`.env`等环境文件

### 命令权限

- 允许：`dotnet build/clean`、`godot --run-tests --quit-on-finish`、`git checkout/branch/add/commit/push`
- 禁止：合并到主分支、生产发布、高危系统命令

### Git权限

- 允许：创建分支、本地提交、推送到远程功能分支
- 禁止：推送到主分支、合并、删除分支、强制推送

## 输出规范

### 分支命名（kebab-case）

- 新功能：`feature/功能名称`，示例：`feature/user-password-reset`
- 问题修复：`fix/补丁名称`，示例：`fix/order-coupon-calculate-error`
- 文档更新：`docs/更新内容`，示例：`docs/update-api-spec`
- 测试补充：`test/补充内容`，示例：`test/add-user-service-test`

### 提交规范

必须按模块分批次提交，禁止一次性提交所有变更。

- `feat: add ...`
- `fix: ...`
- `test: cover ...`
- `docs: ...`

示例仅示格式，提交正文用英文（与 CONTRIBUTING.md 一致）。

### 变更日志（`/CHANGELOG.md`）

变更日志面向使用者，只记录「用户可感知的变更」，不写开发过程。本节优先于 `csharp-document-writing-standard` 中关于变更日志的要求。

- 结构：`## vX.Y.Z — YYYY-MM-DD`（未发布写「未发布」）→ `#### 子系统` → 条目。子系统按板块划分：整合 / 猜测 / 信息 / 录制 / 群聊 / 设置与日志 / 内部。
- 条目：一条一事，动词开头（新增 / 修复 / 调整 / 移除 / 升级），不超过 60 汉字，硬上限 100。
- 只写用户视角：板块名、界面名、公开格式名（`.lstgproj`、`manifest.json`、CSV）可写；类名、私有方法名、字段名、缓存键、常量名不写。
- 不写：根因、实现方案、测试数量、构建结果、留待验收、无行动价值的数字。数字只保留用户要据此行动的（如 QQ 的 30MB 限制）。
- 不设「待完成」「测试改进」类分区；待办进 README「状态」或 issue。
- 格式：零加粗、零 ①②③、零嵌套列表，全角标点，句末统一「。」。
- 细节出口：根因写进 commit message，设计决策写进 `docs/technical-specification.md`。
- 「与任务总结对齐」指结论一致，**禁止**把任务总结原文搬进变更日志。

### 编码规范

- 遵循C#编码规范、分层架构，禁止跨层调用
- 类、方法必须添加XML注释
- 测试覆盖率≥80%

## 知识库

- 项目变更日志：`/CHANGELOG.md`
- 团队编码规范：`/.editorconfig`
- 工程纪律与开发规则：[docs/engineering-guidelines.md](docs/engineering-guidelines.md)
- 技术规格（含设计决策与已知旧账）：`/docs/technical-specification.md`
