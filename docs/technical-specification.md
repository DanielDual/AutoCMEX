# AutoCMEX — 技术规格

## 技术栈

| 层级     | 技术                           |
| -------- | ------------------------------ |
| GUI 框架 | Godot 4.x + .NET 8             |
| 脚本语言 | C#                             |
| UI 方案  | Godot 原生 Control 节点        |
| 外部通信 | WebSocket（AutoCMEX 为服务端） |
| 群聊集成 | Koishi v4 插件                 |
| 本地存储 | JSON 文件                      |
| 加密方案 | AES，密钥文件存放于用户目录    |
| 日志     | 文件日志 + 控制台输出          |

---

## AI 模型集成

### 用途

AI 模型用于猜测板块的以下功能：

| 功能       | 说明                                                           |
| ---------- | -------------------------------------------------------------- |
| 模糊化处理 | 将非严格格式的猜测文本转换为严格格式，并将别名转换为创作者主名 |
| 智能匹配   | 判断群聊消息是否为猜测文本                                     |

### API 格式

支持两种 API 格式：

| 格式               | 说明                                       |
| ------------------ | ------------------------------------------ |
| OpenAI 兼容格式    | `/chat/completions` 端点，适用于大多数厂商 |
| Anthropic 原生格式 | Anthropic Messages API                     |

### 提示词结构

模糊化处理的 AI 提示词按以下顺序组织：

1. 系统提示：任务说明 + 创作者别名表 + 符卡列表
2. 用户消息：猜测文本（放在末尾，利用缓存命中）

### 模型配置项

每个 AI 模型配置包含以下字段：

| 字段     | 说明                |
| -------- | ------------------- |
| API 格式 | OpenAI 或 Anthropic |
| 请求地址 | API 端点 URL        |
| 模型 ID  | 模型标识符          |
| API 密钥 | 用于认证的密钥      |

### 安全要求

- **API 密钥必须安全保存，绝对不能泄露。**
- 密钥使用 AES 加密存储，密钥文件存放于用户目录。
- 不得以明文写入日志或显示在 UI 上。
- UI 中密钥字段以掩码形式（`****`）显示。
- 导出配置时排除或加密密钥字段。

### 多模型支持

- 支持配置多个 AI 模型。
- 用户可为不同功能选择不同模型。
- 支持 CRUD 操作。
- 提供"测试连接"按钮验证 API 连通性。

### 异常处理

- AI 调用失败时返回错误并通知用户，不自动重试。
- 未配置 AI 模型时，模糊化处理按钮禁用，hover 提示"请先配置 AI 模型"。
- 别名表为空时，模糊化处理完全依赖 AI 推断。

---

## 群聊机器人集成

### 支持框架

Koishi v4。

### 集成方式

通过 Koishi 插件实现框架与 AutoCMEX 之间的 WebSocket 通信：

```
┌──────────────┐   WebSocket (JSON)   ┌──────────────┐
│              │ ←──────────────────→ │              │
│   Koishi     │                      │  AutoCMEX    │
│   框架       │   插件仅做消息转发    │  本项目      │
│   (客户端)   │                      │  (服务端)    │
└──────────────┘                      └──────────────┘
```

- AutoCMEX 启动 WebSocket 服务端，监听配置的端口。
- Koishi 插件作为客户端连接。
- 插件仅做消息转发，所有处理逻辑在 AutoCMEX 完成。
- 回应通过同一 WebSocket 连接返回。

### WebSocket 协议

- **消息格式**：JSON，`id` + `type` + `timestamp` + `payload` 结构。
- **消息类型**：

| type      | 方向              | 说明                       |
| --------- | ----------------- | -------------------------- |
| `command` | Koishi → AutoCMEX | 命令（如 `guess`、`ping`） |
| `event`   | AutoCMEX → Koishi | 事件（如 `guess_result`）  |
| `ack`     | AutoCMEX → Koishi | 命令确认                   |
| `error`   | AutoCMEX → Koishi | 错误响应                   |

- **托管猜测流程**：Koishi 发送 `command: guess` → AutoCMEX 返回 `ack` + `event: guess_result` → Koishi 回复原消息。
- **断线处理**：Koishi 插件自动重连，断开期间消息缓存到内存队列，重连后批量发送。

### 插件说明

- 插件代码放在 `src/plugin/koishi/`。
- 插件维护 `requestId → session` 映射，收到 `guess_result` 事件后优先引用回复，失败降级普通回复。
- 插件离开本项目无法使用，不对外发布。
- 一键安装：复制插件文件夹到 Koishi 的 plugins 目录。

### 消息筛选

用户可在设置中配置筛选方式：

| 方式                    | 说明                                        | 依赖           |
| ----------------------- | ------------------------------------------- | -------------- |
| 仅严格格式匹配          | 正则匹配 `<spellcard-id><creator> ...` 格式 | 无             |
| 仅 AI 模型智能匹配      | AI 判断消息是否为猜测文本                   | 需配置 AI 模型 |
| 先严格匹配，未命中再 AI | 先用正则筛选，未命中时调用 AI               | 需配置 AI 模型 |

---

## 猜测处理引擎

### 处理管道

手动输入和群聊抓取的猜测文本走统一处理管道：

```
猜测文本 → [模糊化处理(可选)] → 别名转换 → 格式校验 → 匹配对错 → 生成回应
```

### 回应策略

采用策略模式，`IGuessResponseHandler` 接口抽象，便于替换和扩展：

| 猜测符卡数 | 回应                   |
| ---------- | ---------------------- |
| ≥ 3 张     | 返回猜对的个数         |
| = 2 张     | 全对回"对"，有错回"错" |
| ≤ 1 张     | 不回应                 |

- 已揭晓的符卡跳过不参与判断，回应中注明已揭晓。
- 猜测符卡数仅计未揭晓的符卡。

---

## 数据存储

### 存储内容

| 数据              | 存储方式     | 格式           |
| ----------------- | ------------ | -------------- |
| 符卡—创作者对应表 | 本地文件     | CSV / Excel    |
| 创作者别名表      | 本地文件     | CSV / Excel    |
| AI 模型配置       | 本地加密存储 | JSON，AES 加密 |
| 群聊机器人配置    | 本地配置文件 | JSON           |
| 应用设置          | 本地配置文件 | JSON           |
| 录制配置          | 本地配置文件 | JSON           |

### 保存策略

- 自动保存，1-2 秒防抖延迟。
- 用户停止操作后自动写入，无需手动保存。

### 安全原则

1. 敏感信息（API 密钥等）使用 AES 加密存储，密钥文件存放于用户目录。
2. 不得将敏感信息写入日志。
3. 配置文件导出时自动排除敏感字段。

---

## 符卡 GIF 录制模块

由 CMEX 驱动 LuaSTG 引擎进程，自动逐张录制 Boss 的符卡与非符，产出与手工整理**同构**的 GIF 集（`manifest.json` + `序号.gif`），以复用信息板块既有的导入与展示链路。

### 引擎侧启动契约

启动参数是一个**单参数**，内容是 Lua 赋值代码（整串用双引号包裹，值内不得出现引号或换行）：

```
LuaSTGSub.exe "setting.mod='<工程包名>'; setting.autocmex_job='autocmex/jobs/<任务号>.json'; setting.showcfg=false; start_game=true; cheat=true"
```

- `start_game=true` 必带，否则引擎会把 `setting.mod` 覆盖成启动器。
- `cheat=true` 必带：`cheat` 是引擎全局无敌开关，不开则自机被弹幕撞死、卡提前结束、GIF 不完整。
- **不覆盖** `setting.resx/resy`：分辨率归玩家 `userdata/setting.json` 所有，覆盖会改坏画面比例；GIF 尺寸因此随玩家设置浮动（实测玩家 1280×960 且录制器 `scale=0.5` 时产物为 640×480）。
- `setting.autocmex_job` 这类裸赋值自定义键不会被引擎写回 `userdata/setting.json`，故插件可常驻 `game/plugins/autocmex/` 且不影响玩家正常启动。

### 任务与结果契约

任务由 CMEX 写、结果与诊断日志由插件写，目录固定为 `<引擎>/game/autocmex/{jobs,results,logs}/`（由 CMEX 预建）；任务内的路径一律用正斜杠、相对 `game/`（即引擎进程的工作目录）。

| 阶段 | `phase`     | 任务关键字段                                                     | 结果关键字段                                                                               |
| ---- | ----------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| 枚举 | `enumerate` | —                                                                | `boss_name` / `boss_class` / `cards[]`（`absolute_index`/`name`/`is_sc`/`is_combat`/`t3`） |
| 录制 | `record`    | `absolute_index` / `interval` / `max_frame` / `include_previous` | `task_name` / `gif_path` / `frames` / `complete` / `success` / `size`                      |

- 结果必须回传同一 `job_id`，CMEX 据此拒绝上一轮遗留的结果文件。
- 插件先写 `.tmp` 再改名，避免 CMEX 读到写了一半的 JSON。

### 关键设计决策

- **跳卡置 `lstg.var.sc_index = nil`**（而非沿用 `StageDebugView` 的 `-1`）：`UI.lua` 只判真值就直接索引 `_sc_table[sc_index][1]`，`-1` 会索引 nil 崩渲染。
- **逐帧推进靠包装全局 `DoFrame`**：引擎每帧按名字取该函数；插件在 `afterTHlib` 事件后包装，并在原函数执行**完毕之后**判定当前卡（`b.current_card` 由 `DoFrame` 内部赋值）。
- **收尾以录制器自报为准**：`get_last_record_info()` 的 `success`/`frame`/`task_name`/`size` 可直接读，无需轮询文件大小；录满 `max_frame` 时由录制器自行收尾，插件不得重复 `end_record`。
- **同一引擎目录不可并发**：录制器产物名是秒级时间戳、临时目录在启动时被清空，并发会互相破坏，串行化由编排层保证。
- **进程超时必须把编码耗时算进去**：`end_record` 同步编码，实测约 0.08–0.17 s/帧（350 帧约 36 s、702 帧约 58 s）。
- **卡表口径**：`_editor_class[boss].cards` 含对话阶段，`_sc_table` 只含符卡，二者不同构；序号一律取 `cards` 中的**绝对下标**（1 基）。字段与引擎自身判定同源：`is_combat` 由 `spboss.lua:1433`（`c.is_combat = not (fake)`，对话卡按 `fake` 处理后为 `false`）置位，`is_sc` 由 `boss_card.lua:44`（`c.is_sc = (name ~= '')`）置位；`t3` 在引擎内是**帧**（`boss_card.lua:42` 的 `int(t3) * 60`），插件按 ÷60 换算成秒后写出。

### 单卡录制闭环（P2）

`RecordingOrchestrator.RecordCardAsync(engineDir, modPackName, card, outputDir, config, token, timeout)` 把一张卡录成集内文件 `{序号}.gif`，产物命名与序号在归集时**重写**，与录制器自报的秒级时间戳产物名解耦。

- **「录两遍」是常态而非补救**：`config.MaxFrame` 默认 350 帧，`interval=3`（20 fps）只覆盖 17.5 秒，长卡必然录满被截断；故首次 `result.complete == true` 即换 `config.SecondInterval`（默认 5，12 fps）重录一次，并**采用第二次的产物**，`RecordingCardOutcome.Attempts` 记为 2。
- **截断即视为不完整**：凡触发重录的卡一律回 `Complete = false`（保守口径），实际帧数与间隔照实回传，由调用方决定是否接受。
- **失败重试只发生在单次尝试内部**：超时 / 非零退出 / 结果缺失 / 产物缺失 / 卡名核对不符即自动重试 1 次；两次都失败则本卡判失败并返回原因。第二次尝试彻底失败时**不**回退首次的截断产物——半截 GIF 混进集里比明确失败更难排查。
- **单次尝试的超时预算**：`min(card.t3, maxFrame × interval ÷ 60) + 45 秒启动余量 + maxFrame × 0.3 秒编码余量`（`CardTimeout`）；预算按「一次尝试」计，不随重试与第二次尝试叠加。
- **归集**：`GifSetBuilder` 负责把录制器产物按 `{CombatOrdinal}.gif` 拷进输出目录（同名覆盖），并读 GIF 逻辑屏尺寸回填 `Width`/`Height`；产物无 GIF 魔数或头截断时抛 `InvalidDataException`，由编排层转成可展示的失败原因。

真机验收（2026-09-25，`CMEX21_PreProject`）：枚举 8 张卡（4 对话 + 2 非符 + 2 符卡），取 `t3` 最短的「耐久符」（绝对下标 8 → 序号 4）→ 首次 `interval=3` 录满被截断 → 自动 `interval=5` 重录 → 采用第二次产物 `274 帧 / 12 fps / complete=false`，输出目录仅 `4.gif`（640×480），2 次尝试 = 2 次进程启动。

### 实施阶段

P1 插件 + 枚举最小闭环（已完成）→ P2 单卡录制闭环（已完成）→ P3 编排/重试/取消/报告 → P4 配置与设置面板 → P5 归集、`manifest.json` 与自动导入 → P6 单测补齐与真机验收。

- C# 侧（`src/core/recording/`）：`EngineLocator` 校验引擎目录并定位 exe；`RecordingJobWriter` 写任务文件并维护运行期目录；`GameProcessRunner` 构造参数串、启动进程、超时杀进程、校验并读回结果、回收 `engine.log` 尾部；`GifNaming` 承载序号与命名规则；`GifSetBuilder` 归集产物（拷贝改名 + 读 GIF 头尺寸）；`RecordingOrchestrator` 串起「校验引擎 → 写任务 → 启动进程 → 校验结果 → 派生序号」的枚举闭环，并在此之上续接「截断换挡重录 → 归集产物」的单卡录制闭环。
- 游戏侧插件（`src/plugin/luastg/autocmex/`）：`__init__` 入口（无任务时零副作用）、`job` 任务校验与结果写出、`cards` 卡表枚举、`record` 跳卡/起录/收尾/退出、`log` 诊断日志。

---

## 项目结构

```
AutoCMEX/
├── src/                    # 业务代码
│   ├── ui/                 # UI 场景与脚本（各板块独立 .tscn）
│   │   ├── main/           # 主窗口布局
│   │   ├── merge/          # 整合板块
│   │   ├── guessing/       # 猜测板块
│   │   ├── info/           # 信息板块（四栏 + 下栏目标群与发布）
│   │   ├── settings/       # 设置板块
│   │   └── help/           # 帮助板块（Markdown 渲染）
│   ├── core/               # 核心业务逻辑
│   │   ├── guessing/       # 猜测处理引擎（策略模式）
│   │   ├── info/           # 信息板块：两表推导、GIF 集导入、离屏出图、发布编排
│   │   ├── recording/      # 符卡 GIF 录制：引擎定位、任务写出、进程运行、命名规则
│   │   ├── ai/             # AI 模型调用（OpenAI / Anthropic）
│   │   └── storage/        # 数据存储与 AES 加密
│   └── plugin/             # 外部插件
│       ├── koishi/         # Koishi v4 插件代码
│       └── luastg/         # LuaSTG 引擎侧插件
│           └── autocmex/   # 符卡录制插件（随应用分发到 game/plugins/autocmex/）
├── docs/                   # 项目文档
└── assets/                 # 静态资源（Logo 等）
```

场景管理：所有板块场景在启动时预加载，切换时显隐控制。

---

## 当前任务状态

| 板块 | 状态     | 说明                                    |
| ---- | -------- | --------------------------------------- |
| 整合 | 暂不开发 | 细节待补充                              |
| 猜测 | 已实现   | 核心板块，需求已明确                    |
| 信息 | 已实现   | 四栏展示 + 一键转发；符卡录制模块实施中 |
| 设置 | 部分实现 | AI 模型与群聊配置已实现                 |
| 帮助 | 待开发   | 内置 Markdown 渲染                      |

**当前阶段**：核心功能（猜测引擎、AI 模糊化、数据存储、WebSocket 服务）已实现，设置板块 AI 模型与群聊配置、信息板块四栏展示与发布已完成；符卡 GIF 录制模块进行中，已完成引擎侧插件、枚举最小闭环（P1）与单卡录制闭环（P2）。
