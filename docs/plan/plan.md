# AutoCMEX — 托管猜测闭环实现方案

## 1. 整体目标

实现 Koishi 消息 → AutoCMEX 处理 → Koishi 回复的完整托管猜测闭环。仅在 Koishi 与 AutoCMEX 有效连接时启用托管流程；未连接时手动猜测流程不受影响。

## 2. 验收标准

| 验收项       | 标准                                                                   |
| ------------ | ---------------------------------------------------------------------- |
| 托管猜测闭环 | Koishi 群聊消息 → AutoCMEX 判定/处理 → guess_result 回传 → Koishi 回复 |
| 非猜测处理   | AI 返回 `NOT_A_GUESS` 时仅向 Koishi 说明非猜测，不回复群聊             |
| 连接解耦     | Koishi 未连接时，GuessingPanel 手动猜测与手动模糊化仍可正常使用        |
| 测试通过     | `godot --run-tests --quit-on-finish` 全部通过                          |

## 3. 设计原则

- 托管链路是增强，不得成为手动流程前置依赖
- "是否为猜测"与"处理猜测文本"合并为一条链路，由统一编排服务调用
- AiFuzzifier 提示词要求对不像猜测的输入返回 `NOT_A_GUESS`，程序识别后不回复

## 4. 核心链路

```
Koishi 群聊消息
    ↓
Koishi 插件 → command: guess → WebSocket → AutoCMEX
    ↓
CommandHandler → GuessProcessingService.ProcessManagedAsync()
    ├── 严格格式匹配成功 → 直接处理
    ├── 严格格式失败 → AiFuzzifier 模糊化
    │   ├── 返回 NOT_A_GUESS → 非猜测，不回复
    │   └── 返回严格格式 → 继续处理
    └── 处理失败 → 记录日志，不回复
    ↓
AutoCMEX → ack + event: guess_result → WebSocket → Koishi 插件
    ↓
Koishi 插件 → 引用原消息回复 / 普通回复
```

## 5. 已确认决策

| 决策项           | 结论                                                     |
| ---------------- | -------------------------------------------------------- |
| 非猜测固定返回值 | `NOT_A_GUESS`                                            |
| Koishi 回复策略  | 优先引用原消息回复，失败时降级普通回复                   |
| 处理成功但无回复 | 群内静默，必须记日志                                     |
| 未选中 Boss      | 群内静默，仅在 AutoCMEX 侧记录错误日志                   |
| Koishi 会话关联  | 保存原始 session 对象，配套过期清理                      |
| 手动流程兼容     | GuessingPanel 复用 GuessProcessingService，不依赖 Koishi |

## 6. 文件变更

### 新增

| 文件                                           | 说明                 |
| ---------------------------------------------- | -------------------- |
| `src/core/guessing/IGuessProcessingService.cs` | 统一猜测处理服务接口 |
| `src/core/guessing/GuessProcessingService.cs`  | 统一猜测处理服务实现 |
| `src/core/guessing/GuessProcessingResult.cs`   | 处理结果模型         |
| `test/src/GuessProcessingServiceTest.cs`       | 猜测处理服务测试     |

### 修改

| 文件                                    | 变更内容                                       |
| --------------------------------------- | ---------------------------------------------- |
| `src/core/ai/AiFuzzifier.cs`            | 新增 `NOT_A_GUESS` 常量与固定返回判定          |
| `src/core/ai/AiServiceFactory.cs`       | 适配新接口                                     |
| `src/core/storage/DataManager.cs`       | 暴露 `SelectedBossIndex` 共享状态              |
| `src/models/AppSettings.cs`             | 新增 `SelectedBossIndex` 字段                  |
| `src/core/websocket/CommandHandler.cs`  | 接入 GuessProcessingService，支持多消息返回    |
| `src/core/websocket/IMessageHandler.cs` | 返回类型改为 `IReadOnlyList<WebSocketMessage>` |
| `src/core/websocket/MessageRouter.cs`   | 适配多消息返回                                 |
| `src/core/websocket/WebSocketServer.cs` | 适配多消息发送                                 |
| `src/core/websocket/WebSocketClient.cs` | 适配多消息发送                                 |
| `src/core/websocket/EventHandler.cs`    | 适配新接口                                     |
| `src/ui/guessing/GuessingPanel.cs`      | 复用 GuessProcessingService，不依赖 Koishi     |
| `src/ui/main/MainWindow.cs`             | 装配 GuessProcessingService                    |
| `src/plugin/koishi/src/index.ts`        | requestId→session 映射，guess_result 回复      |
| `test/src/WebSocketTest.cs`             | 新增多消息返回测试                             |

## 7. UI 复用方案

- GuessingPanel 通过 `IGuessProcessingService` 调用 `ProcessManualAsync()`
- 手动流程不依赖 WebSocket 连接状态
- WebSocket 未启动、未连接或异常时，手动处理仍可正常运作

## 8. 兼容性

- 现有手动猜测流程不受影响
- AppSettings 新增字段有默认值
- Koishi 未连接时托管链路静默不生效
