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
   - 别名表段含一条「别名 → 主名」的示范，用于演示别名转换规则；没有别名的创作者行标注为「无别名」而不是空括号。别名表为空时，该示范与表头一并省略。
2. 用户消息：猜测文本（放在末尾，利用缓存命中）

### 模型配置项

每个 AI 模型配置包含以下字段：

| 字段     | 说明                |
| -------- | ------------------- |
| API 格式 | OpenAI 或 Anthropic |
| 请求地址 | API 端点 URL        |
| 模型 ID  | 模型标识符          |
| API 密钥 | 用于认证的密钥      |

除上表的单模型字段外，另有一个**全局**的「请求超时（秒）」设置（1~600，默认 100）：

- 由 `AiServiceFactory.GetActiveService()` 读取并传给 `CreateService(config, timeoutSeconds)`，最终写入服务的 `HttpClient.Timeout`；因此模糊化处理与托管猜测都吃这一项。
- 「测试连接」也跟随该设置，但经 `AiModelConfigPanel.ResolveTestConnectionTimeout()` 取 `min(设置值, 30)`（下限 1 秒），避免一次连通性验证长时间阻塞。

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

通过 Koishi 插件实现框架与 AutoCMEX 之间的 WebSocket 通信，两种模式（设置 → 群聊 → 模式）二选一：

```
┌──────────────┐   WebSocket (JSON)   ┌──────────────┐
│              │ ←──────────────────→ │              │
│   Koishi     │                      │  AutoCMEX    │
│   框架       │   插件仅做消息转发    │  本项目      │
│              │                      │              │
└──────────────┘                      └──────────────┘
```

- **Server 模式**（默认）：AutoCMEX 监听设置里的端口，Koishi 插件作为客户端连入。
- **Client 模式**：AutoCMEX 主动连接设置里的 Koishi 地址（自动补全 `ws://` 前缀并附 Token）。
- 插件仅做消息转发，所有处理逻辑在 AutoCMEX 完成。
- 回应通过同一 WebSocket 连接返回。
- **Client 模式未填 Koishi 地址时不会退化成 Server**：仍按 Client 建立实例，启动失败并把「未配置 Koishi 地址」记进 `IWebSocketServer.LastError`，由状态面板的红色错误行显示——避免「设置写 Client、实际跑 Server」的静默偏差。
- 状态面板显示的**模式、端口/地址、连接数、错误**一律取自实际运行的 `IWebSocketServer` 实例（`Mode` / `Port` / `Url` / `LastError`），不取自设置：设置改动后实例重建前两者可能短暂不一致。

- **单实例单链路**：进程内任一时刻只允许一个 `IWebSocketServer` 实例在工作，一个实例只允许一条链路。实例的创建/启停/重启统一经 `WebSocketLifecycle`：`StopAsync` 一律取消并等待循环退出（不得按「是否已连接」提前返回），`StartAsync` 以「循环是否还活着」判幂等，重启串行化，且**期望配置与当前实例有效配置一致时跳过重建**。原因：配置绑定的 `OnValue` 是**订阅即回调**（启动时模式/端口/地址三个绑定会连打三次重启），若每次重启各建实例而旧的又停不掉，旧实例会成为继续连接的孤儿，对端的「新连接踢旧连接」即演变为连接震颤。
- **停止的可见语义**：`IsRunning` = 已连接（Server 为已监听），`IsActive` = 实例仍在工作（含 Client 的断线重连等待）。状态标签的文案用 `IsRunning` 判定、按钮文案用 `IsActive` 判定——重连等待中若按 `IsRunning` 显示，用户会「点一下反而更连不上、也停不掉」。
- **启停只有一个入口**：界面（状态面板）**不得**自行判方向、也不得对自己的实例引用调 `StartAsync` / `StopAsync`，一律经 `WebSocketLifecycle.ToggleAsync()`——方向在锁内按当前实例判定，与重启共用同一把锁。界面拿的是上次推送的实例引用，配置变更重启期间它可能已指向被替换的旧实例；由界面自行判方向即会出现「旧实例被重新拉起 + 新实例同时在跑」，正是连接震颤的成因。
- **停止后不留任务**：`StopAsync` 返回即代表该实例上再无活动任务。除连接/接受循环外，心跳等派生循环也必须持有自己的句柄并在停止时取消 + 等待退出（心跳还须绑定本次连接的 socket 与令牌，不读共享字段，否则重连后旧心跳会跟着新 socket 继续发 ping）。
- **端口变更**：`StopAsync` 必须真正关掉监听器并等待接受循环退出，否则旧监听器仍占端口，新端口表现为启动失败。

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
- **发布内容的群内呈现方式由应用侧决定**：`info_publish_forward` 的 payload 带 `mode` 字段（`forward` = 合并转发一张可展开卡片，`image` = 逐节点各发一条图片消息），插件只照做、不自行判定内容形态；缺省（旧版应用不带该字段）按 `forward` 处理，保证两端版本错配时仍能正常发送。`image` 模式仍受发送前的附件预检约束，发送失败会回报具体原因而不静默回落成合并转发。
- 插件离开本项目无法使用，不对外发布。
- 一键安装：复制插件文件夹到 Koishi 工作区的 `external/adapter-autocmex/`（目录名 = 包短名，与控制台的插件索引名一致；覆盖安装即命中工作区链接指向的目录）。
- 插件包自检口径：`package.json` 的 `main` 直接指向真实入口 `src/index.js`（内容即 CommonJS，无需构建产物，`koishi dev` 与 `koishi start` 行为一致）；`koishi.category = "adapter"` 决定其在控制台「添加插件」里的分类；插件自报名必须等于包短名。

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

### 丢包与重试

- **落包时机**：严格管道未命中且走 AI 兜底后仍失败（AI 不可用、超时、返回不可解析）时，`GuessProcessingService` 落一条丢包记录，除原文与失败原因外还留档 `requestId`（来源消息标识）、`sender`（发送者）与 `filterMode`（当时的口径）；本地手输路径没有来源，三者存空串而非 null。
- **重放口径**：`RetryDroppedGuessAsync` 按记录里的 `filterMode` 重放，**不读当前设置**——丢包后用户改过筛选模式（如 strict → ai）不该让这条记录永远救不回来。重放**不新落记录、也不删记录**（旧写法是「删旧 + 落新」，用户看到的「丢包被重新解析了一遍」即源于此）。
- **回帖与去留**：重放成功后由 `DroppedGuessRetryService` 用原 `requestId` 经活跃端点推送 `guess_result`（与消息到达链路共用 `GuessReplyFactory`，回帖口径只有一处定义）。去留判据收在协调器一处，按结局分为：
  | 结局 | 含义 | 记录 |
  | ---- | ---- | ---- |
  | `Replied` | 已引用原消息回帖 | 移除 |
  | `NoReplyTarget` | 无来源可回（本地手输产生的丢包） | 移除 |
  | `NoReplyNeeded` | 有来源但按回应策略无需回帖 | 移除 |
  | `NotGuess` | 重放仍未得到有效猜测 | 保留 |
  | `Failed` | 重放报错 | 保留 |
  | `LinkInactive` | 链路未运行 / 没有已连接对端 / 一条都没送出去 / 推送抛异常 | 保留 |
- **送达判据**：`IWebSocketServer.BroadcastAsync` 对单个连接失败只记日志、不抛异常，因此它**返回实际送达的连接数**（客户端链路送出即 1，未连接即 0）；需要「送不出去就保留记录」的调用方必须看这个返回值，不能靠异常判断——否则记录会被删掉，而群里根本没有回复。
- **手动出口**：丢包列表的「重试全部丢包」经协调器逐条重放并把逐条结局显示在回应栏（协调器异常时由面板兜住并提示「重试中断」，按钮状态照常还原）；「删除选中丢包」按选中下标映射到记录 Id 逐条删除，未选中时只提示不动数据。

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

| 阶段 | `phase`     | 任务关键字段                                                               | 结果关键字段                                                                               |
| ---- | ----------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| 枚举 | `enumerate` | —                                                                          | `boss_name` / `boss_class` / `cards[]`（`absolute_index`/`name`/`is_sc`/`is_combat`/`t3`） |
| 录制 | `record`    | `absolute_index` / `interval` / `max_frame` / `scale` / `include_previous` | `task_name` / `gif_path` / `frames` / `complete` / `success` / `size`                      |

- 结果必须回传同一 `job_id`，CMEX 据此拒绝上一轮遗留的结果文件。
- 插件先写 `.tmp` 再改名，避免 CMEX 读到写了一半的 JSON。

### 关键设计决策

- **跳卡置 `lstg.var.sc_index = nil`**（而非沿用 `StageDebugView` 的 `-1`）：`UI.lua` 只判真值就直接索引 `_sc_table[sc_index][1]`，`-1` 会索引 nil 崩渲染。
- **逐帧推进靠包装全局 `DoFrame`**：引擎每帧按名字取该函数；插件在 `afterTHlib` 事件后包装，并在原函数执行**完毕之后**判定当前卡（`b.current_card` 由 `DoFrame` 内部赋值）。
- **收尾以录制器自报为准**：`get_last_record_info()` 的 `success`/`frame`/`task_name`/`size` 可直接读，无需轮询文件大小；录满 `max_frame` 时由录制器自行收尾，插件不得重复 `end_record`。
- **产物分辨率由放缩比决定**：插件在 `start_record` 之前调录制器的 `set_scale(job.scale)`，产物像素 = 捕获区域 × `screen.scale` × 放缩比（与录制器 `CreateRenderTarget` 的算法一致，故日志里会打出预期尺寸）；与抓帧区域同理，`set_scale` 也只在 `status == "initialized"` 时生效。CMEX 侧以整数百分比暴露（`ScalePercent`，10..100，默认 50 = 录制器默认的 0.5），写任务文件时除以 100 还原成倍率；插件只做「正数 + 0.1..1.0」的兜底收敛（越界会写 `WARN`）。放缩比同时影响清晰度与产物体积，故它是 30MB 换挡判据的上游变量。
- **同一引擎目录不可并发**：录制器产物名是秒级时间戳、临时目录在启动时被清空，并发会互相破坏，串行化由编排层保证。
- **进程超时必须把编码耗时算进去**：`end_record` 同步编码，实测约 0.08–0.17 s/帧（350 帧约 36 s、702 帧约 58 s）。
- **卡表口径**：`_editor_class[boss].cards` 含对话阶段，`_sc_table` 只含符卡，二者不同构；序号一律取 `cards` 中的**绝对下标**（1 基）。字段与引擎自身判定同源：`is_combat` 由 `spboss.lua:1433`（`c.is_combat = not (fake)`，对话卡按 `fake` 处理后为 `false`）置位，`is_sc` 由 `boss_card.lua:44`（`c.is_sc = (name ~= '')`）置位；`t3` 在引擎内是**帧**（`boss_card.lua:42` 的 `int(t3) * 60`），插件按 ÷60 换算成秒后写出。

### 单卡录制闭环（P2）

`RecordingOrchestrator.RecordCardAsync(engineDir, modPackName, card, outputDir, config, token, timeout)` 把一张卡录成集内文件 `{序号}.gif`，产物命名与序号在归集时**重写**，与录制器自报的秒级时间戳产物名解耦。

- **「录两遍」是常态而非补救**：`config.MaxFrame` 默认 350 帧，`interval=3`（20 fps）只覆盖 17.5 秒，长卡必然录满被截断；故首次 `result.complete == true`**或首次产物已达 QQ 的动图体积上限**（`GifSizeLimitBytes` = **30,000,000 字节**，即 30MB 按十进制取——这是两种可能口径里**保守**的那个：若 QQ 实际按 1024 进制限流，十进制阈值只会更早触发换挡、不会漏放；反之取 1024 进制则会放过 30,000,000..31,457,280 字节这一段，而真机上确有一张 30,291,854 字节的产物落在该带内）即换 `config.SecondInterval`（默认 5，12 fps）重录一次，并**采用第二次的产物**，`RecordingCardOutcome.Attempts` 记为 2。两条触发条件共用同一段重录逻辑（判据集中在 `DescribeSecondAttempt`）：截断的日志沿用「录满 N 帧被截断」，体积的日志给「产物 X MiB 已达 30MB 上限」；重录失败的原因按触发条件区分为「首次截断后重录失败」与「首次产物超限后重录失败」。换挡后仍 `>= 30MB` 时**不做第三次尝试**：产物本身可用，故照常采用，只补一条带卡号与体积的 `WARN` 日志供真机定位；每张卡收尾时也会把产物实际体积（MiB，与资源管理器显示同口径）打进日志。
- **截断即视为不完整**：凡触发重录的卡（含体积超限触发的换挡）一律回 `Complete = false`（保守口径），实际帧数与间隔照实回传，由调用方决定是否接受。
- **失败重试只发生在单次尝试内部**：超时 / 非零退出 / 结果缺失 / 产物缺失 / 卡名核对不符即自动重试 1 次；两次都失败则本卡判失败并返回原因。第二次尝试彻底失败时**不**回退首次的截断产物——半截 GIF 混进集里比明确失败更难排查。
- **单次尝试的超时预算**：`min(card.t3, maxFrame × interval ÷ 60) + 45 秒启动余量 + maxFrame × 0.3 秒编码余量`（`CardTimeout`）；预算按「一次尝试」计，不随重试与第二次尝试叠加。
- **归集**：`GifSetBuilder` 负责把录制器产物按 `{CombatOrdinal}.gif` 拷进输出目录（同名覆盖），并读 GIF 逻辑屏尺寸回填 `Width`/`Height`；产物无 GIF 魔数或头截断时抛 `InvalidDataException`，由编排层转成可展示的失败原因。

真机验收（2026-09-25，`sample_pre_project`）：枚举 8 张卡（4 对话 + 2 非符 + 2 符卡），取 `t3` 最短的「耐久符」（绝对下标 8 → 序号 4）→ 首次 `interval=3` 录满被截断 → 自动 `interval=5` 重录 → 采用第二次产物 `274 帧 / 12 fps / complete=false`，输出目录仅 `4.gif`（640×480），2 次尝试 = 2 次进程启动。

### 批量并行录制（P3）

一轮的入口是 `RecordingOrchestrator.RunAsync(request, progress?, token)`：清扫残留沙箱 → 前置检查 → 枚举 → 并行逐卡录制 → 串行归集 → 写报告 → 删沙箱。

**沙箱隔离**（`RecordingSandbox`）：真引擎目录全程只读——每轮把工程包与运行必需的引擎文件复制进 `<沙箱根>/sb_<轮次>_<worker>/` 的独立副本，任务、结果、`engine.log` 与录制器产物全落在沙箱里，归集时才往输出目录写。原因是录制器把 `danmaku_recorder/tmp` 当进程级工作区（`recorder:init()` 会清空它）、产物名又是秒级时间戳，多实例共用同一引擎目录必然互相破坏。沙箱清单只含运行必需项：`packages/`、`plugins/`（含 `autocmex` 与弹幕录制器插件）、`userdata/`、启动文件与 `*.dll`；`mod/` 只放本轮手选的工程包。

- 沙箱根默认取系统临时目录下的固定子目录，可由 `config.SandboxRoot` 覆盖。
- 每轮开始清扫**超 1 小时**的 `sb_*` 残留（上次崩溃留下的）；建沙箱前按「引擎 + 工程包 + exe」估算单份占用，剩余空间不足直接中止（不半途失败）。

**并行调度**（`RecordingScheduler`）：worker 数 = `min(config.Parallelism（默认 16，上限 32）, 战斗卡数)`，再按临时卷剩余空间压一次（只降不报错，被压过则回一条 `Warning`）。枚举用的那个沙箱直接复用给 worker 1，省一份复制；其余 worker 各自建沙箱。取卡是「抢」的——谁先空出谁取下张，逐卡结果按 `combatOrdinal` 回填，故报告顺序与「谁先录完」无关。

- 单卡失败只记该卡，不中断整轮。
- **取消**：停止领卡并杀掉在跑的进程树；随后照常归集已产出、删沙箱、写报告，正在录与没轮到的卡一律记「未录制」，报告 `cancelled = true`。预取消（进轮前就已取消）连沙箱都不建。

**归集与报告**（固定串行）：输出目录与报告是全局共享资源，且报告必须按战斗序号升序，故归集不并行——逐卡 `GifSetBuilder.PlaceCardGif` 改名落地（`{序号}.gif`，同名覆盖）、读 GIF 头回填宽高，再按卡表顺序组装 `RecordingCardReport`（帧数 / 帧率 / 时长 / 完整标记 / 尝试次数 / 进程启动次数 / worker / 失败原因 + `engine.log` 尾部）。「拿到结果的才判成败，没轮到的一律记未录制」，据此可区分「试过没成」与「压根没轮到」。

- **报告**：`RecordingReport` 落 `<输出目录>/recording_report.json`；写盘失败只作为 `Warning` 回传，不把整轮判失败。
- **进度**：`IProgress<RecordingProgress>` 按「阶段（枚举 / 录制 / 归集）+ 总数 + 已完成 + 已失败 + 各 worker 正在录的卡」发快照，内部加锁聚合。
- **返回值**：`RecordingRunResult` 含 `Error`（前置失败原因）、`Report`、`OutputDir`、`Warning`（并行度被压缩等非致命提示）与 `EngineLogTail`（前置失败时附）。

真机验收（2026-09-25，`sample_pre_project`，4 张战斗卡）：2 worker 并行跑通全流程——沙箱各自独立、产物按序号归集 4 个、报告落盘、临时目录无残留沙箱；`t3` 封顶的「耐久符」与串行轮的产物帧数/帧率一致（274 帧 / 12 fps，抽样比对内容近似度 84~85%）。按既定裁定「截断属正常产物」：玩家不干预时 45~60 秒的长卡在 350 帧预算下两档都会截断并标 `complete = false`。

### 配置与插件部署（P4）

设置板块「信息」类别页底部挂一组「符卡 GIF 录制设置」（`RecordingConfigPanel`，默认折叠）：引擎目录、并行度、沙箱根、插件状态、高级参数五段。该类别页没有独立场景（节点直接写在 `SettingsPanel.tscn` 里），故分组按本页既有的「C# 构建行」范式在依赖解析后创建（`InfoConfigPanel.EnsureRecordingSection`），并在该页每次被切显时刷新一次状态（`VisibilityChanged`）。

配置项一律复用 `DataManager.RecordingConfig` 的既有字段（引擎目录 / 并行度 / 沙箱根 / 帧数上限 / 两个抽帧间隔 / 上次输出目录），**不新增持久化字段**，写入走既有防抖自动保存。写回纪律：

- **非法值不落盘**：引擎目录经 `EngineLocator.TryValidate`、沙箱根经 `RecordingSandbox.TryValidateRoot` 校验，不通过则保留原值、只把原因写在状态行上——提示与配置分离，避免「面板提示已改、实际没改」的错觉。
- **越界收敛**：并行度 1~32、帧数上限 1~1000、抽帧间隔 1~60，由 `RecordingConfig.ClampParallelism`/`ClampMaxFrame`/`ClampInterval` 收敛后写回。
- **推断只作建议**：「从 Sharp 目录推断」复用 `EngineLocator.TryInferFromSharpDir`（沿「整合」板块的 Editor Sharp 目录上溯找 `game/launch`），结果仍走同一套校验，不隐式改写用户配置。

**插件部署**（`PluginDeployer`）：判定与动作收在一处，UI 与沙箱共用同一判据（`TryValidateForRecording`），避免出现「面板显示就绪、起录却被拒」的两套口径；`RecordingSandbox.ValidateSource` 不再自己判插件，只转述该判据的原因串（原异常文案逐字不变）。

- 三态：`Ready`（目录在且启用）、`InstalledDisabled`（目录在、清单 `enable = false` 或清单缺失导致引擎不会加载）、`Missing`；清单文件存在但不可解析时另给 `ManifestBroken`，此时**一切部署动作关闭**——宁可不让用户动手，也不拿坏清单去写。
- **清单缺失不等于损坏**：文件不存在时按空清单继续判插件（`ManifestExists = false`），安装动作会把清单新建出来，故在全新引擎上不会出现「两个按钮全灰、用户无从下手」。判据只有一处差别：录制前的 `TryValidateForRecording` 把「清单缺失」直接判为不可录（引擎不读清单就等于不加载插件），能装 ≠ 能录。
- 条目命中比的是**整段目录名**（先 `name` 精确匹配，再比 `path` 的最后一段；第三方插件允许版本后缀，并剥掉 `[pluginpackage]` 前缀），不按子串匹配——否则 `autocmex_old` 这类同名前缀目录会被误当成目标条目，安装会去改写别人的 `enable` 而不是新增一条。
- 清单兼容两种登记方式（新式 `name` + `path` 与 LuaSTG-CN 的 `[pluginpackage]danmaku_recorder_x.y.z` 路径）。引擎目录未设置、清单损坏、缺第三方录制器三种情况各自给可执行的原因（缺录制器明说「第三方插件，需自行获取」）。
- **一键安装**（自家插件）：先备份既有插件目录（`<目录名>.autocmex-bak`，已存在则不叠加）→ 复制自带插件 → 登记为启用；**幂等**，重复点击不产生第二份备份、不重复登记。清单损坏时抛 `PluginDeployException` 且不改盘。
- **一键启用**（第三方弹幕录制器）：只把条目 `enable` 置 `true`，不动文件、不覆盖条目里的其它字段。
- **写前必备份**：改 `plugins.json` 前先落 `plugins.json.autocmex-bak`，再以临时文件 + 原子替换写入，避免半截 JSON 让引擎读不到任何插件。
- **零写入约束**：用户没点任何按钮时，面板只做只读检查（`Inspect`），不碰引擎目录。
- 沙箱侧新增 `TryValidateRoot`（沙箱根存在性 + 一次性写入探针；配置为空时允许就地创建默认目录，用户手选的目录必须已存在）；`EstimateFootprint` 允许工程包路径为空（设置页刚配引擎时还没有包），缺失项按 0 计。

验证：编译 0 错误，GoDotTest **588 通过 / 0 失败**（带窗口运行；新增 `PluginDeployerTest`（三态判定、清单损坏、清单缺失、同名前缀目录不误命中、禁用条目、只读检查不写盘、安装幂等与备份、启用最小改动、缺件原因串逐字比对）、`RecordingConfigPanelTest`（配置回填、非法值不落盘、越界收敛、推断失败不写配置、状态渲染与按钮启停、一键部署联动与失败原因、无引擎目录时误点不改盘）与 `RecordingSandboxTest` 的 `TryValidateRoot` 与四种插件缺件拒绝建副本）。

### 录制入口、`manifest.json` 与自动导入（P5，进行中）

入口复用信息板块 GIF 集栏里既有的「录制」按钮（`GifSetPanel.%RecordButton`，此前为占位禁用），不新增类别页、不改 `GifSetPanel.tscn`。点击后按序执行：**前置检查**（引擎目录、插件状态、沙箱根，复用 `PluginDeployer` 与 `RecordingSandbox.TryValidateRoot`；不通过则不起任何进程并在原处说明原因）→ **手选工程包 zip** → **手选输出目录** → `RecordingOrchestrator.RunAsync`；进度以 C# 构建的模态对话框呈现（阶段 + 已完成/总数 + 各 worker 正在录的卡 + 取消），取消沿用既有语义。

`manifest.json` 由编排层在**归集之后**写出（只有归集后才拿得到尺寸），字段来源：

| 字段                                      | 来源                                                   |
| ----------------------------------------- | ------------------------------------------------------ |
| `setName`                                 | 手选工程包文件名（去扩展名），即报告里的 `ModPackName` |
| `bossLabel`                               | 游戏侧 Boss 名，枚举时一并 dump（报告里的 `BossName`） |
| `generatedAt`                             | 本轮开始时间，`yyyy-MM-dd HH:mm:ss`                    |
| `entries[].index` / `spellCardName`       | 战斗序号（1 基，只数符卡与非符）/ 卡名                 |
| `entries[].fileName` / `width` / `height` | `{序号}.gif` 与归集时读到的 GIF 头尺寸                 |

写盘**显式使用 camelCase**（`GifSetManifest` 的属性是 PascalCase，读侧大小写不敏感，但契约与文档示例是 camelCase）。**只收 `status == ok` 的卡，序号允许有洞**（集内校验只要求序号为正且唯一）；**0 张成功则不写清单、不导入**。

导入与选中由 UI 层调用方完成，`core/recording` **不反向依赖** `core/info`：`GifSetService.ImportFolder(输出目录)` 成功后**显式**调 `SetActiveSet`（`Register` 只在「当前集为空/失效」时才自动选中），并回写 `RecordingConfig.LastOutputDir`。

失败落点：前置检查不通过 → 不起进程；**导入被拒** → 产物、报告、清单全部保留，逐行列出导入结果明细并提示可手动导入；**输出目录含本轮未声明的 `.gif`** → 不删用户文件、跳过导入并列出明细（集内校验要求清单与目录内图片一一对应，上一轮残留的 `.gif` 会让导入被整体拒绝）。截断（`complete = false`）仍按既有裁定视为正常产物。

### 实施阶段

P1 插件 + 枚举最小闭环（已完成）→ P2 单卡录制闭环（已完成）→ P3 批量并行录制 + 沙箱隔离（已完成）→ P4 配置与设置面板（已完成）→ P5 `manifest.json`、录制入口与自动导入（进行中）→ P6 单测补齐与真机验收（待 P5 交付后另行评审）。

- C# 侧（`src/core/recording/`）：`EngineLocator` 校验引擎目录并定位 exe；`RecordingJobWriter` 写任务文件并维护运行期目录；`GameProcessRunner` 构造参数串、启动进程、超时杀进程、校验并读回结果、回收 `engine.log` 尾部；`GifNaming` 承载序号与命名规则；`GifSetBuilder` 归集产物（拷贝改名 + 读 GIF 头尺寸）；`RecordingSandbox` 造/清沙箱并清扫残留；`RecordingScheduler` 分配 worker 与抢卡；`RecordingOrchestrator` 串起「校验引擎 → 写任务 → 启动进程 → 校验结果 → 派生序号」的枚举闭环与「截断换挡重录 → 归集产物」的单卡录制闭环，并在 `RunAsync` 里编排整轮（并行录制 → 串行归集 → 写报告）。
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
│   │   ├── recording/      # 符卡 GIF 录制：引擎定位、插件部署、沙箱隔离、并行调度、进程运行、归集与报告
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

| 板块 | 状态     | 说明                                                                                                        |
| ---- | -------- | ----------------------------------------------------------------------------------------------------------- |
| 整合 | 暂不开发 | 细节待补充                                                                                                  |
| 猜测 | 已实现   | 核心板块，需求已明确                                                                                        |
| 信息 | 已实现   | 四栏展示 + 一键转发；符卡录制模块已到 P4（配置与插件部署），P5（`manifest.json`、录制入口与自动导入）进行中 |
| 设置 | 部分实现 | AI 模型与群聊配置已实现；「信息」类别页含目标群与录制设置                                                   |
| 帮助 | 待开发   | 内置 Markdown 渲染                                                                                          |

**当前阶段**：核心功能（猜测引擎、AI 模糊化、数据存储、WebSocket 服务）已实现，设置板块 AI 模型与群聊配置、信息板块四栏展示与发布已完成；符卡 GIF 录制模块进行中，已完成引擎侧插件、枚举最小闭环（P1）、单卡录制闭环（P2）、批量并行录制与沙箱隔离（P3）以及配置与插件部署（P4）；P5（`manifest.json`、录制入口与自动导入）进行中，P6（单测补齐与真机验收）待 P5 交付后另行评审。
