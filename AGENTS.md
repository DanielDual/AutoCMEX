# AGENTS.md

## 生效规则

1. 本文是项目最高行为准则，所有AI模型必须遵守
2. 规则优先级：项目根目录强制规则 > 本文件 > 项目Skill > 全局Skill > AI通用能力

## 项目结构

- `/src`：业务代码主目录
- `/doc`：项目文档主目录

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
3. **测试阶段**：按`csharp-test-granularity-standard`规范编写测试，必须通过运行时测试验证功能正常；测试不通过优先修正业务逻辑
4. **文档阶段**：按`csharp-document-writing-standard`规范更新文档
5. **审查阶段**：输出代码审查清单，等待用户审查通过
   _注：纯问答、只读任务不走此流程_

### 核心Skill说明

| Skill | 用途 |
|--|--|
| `csharp-solution-output-standard` | 规范实现方案的输出格式与内容要求 |
| `csharp-test-granularity-standard` | 规范测试代码的颗粒度与覆盖要求 |
| `csharp-document-writing-standard` | 规范项目文档的编写标准 |

## 权限边界

### 目录权限
- 可写：`/src`、`/doc`、`/CHANGELOG.md`
- 禁止：`.git`、`/bin`、`/obj`、核心配置文件（`.csproj`等）、`.env`等环境文件

### 命令权限
- 允许：`dotnet build/test/clean`、`git checkout/branch/add/commit/push`
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
- `feat: 实现【xxx】核心业务逻辑`
- `test: 补充【xxx】功能单元测试`
- `docs: 更新【xxx】功能相关文档与变更日志`
- `fix: 修复【xxx】业务逻辑，解决测试不通过问题`

### 编码规范
- 遵循C#编码规范、分层架构，禁止跨层调用
- 类、方法必须添加XML注释
- 测试覆盖率≥80%

## 知识库

- 项目变更日志：`/CHANGELOG.md`
- 团队编码规范：`/.editorconfig`
