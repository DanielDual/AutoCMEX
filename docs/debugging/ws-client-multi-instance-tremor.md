# 多客户端实例互踢（连接震颤）调试记录

- 日期：2026-09-26
- 分支：`fix/websocket-connection-tremor`
- 相关实现：`src/core/websocket/WebSocketLifecycle.cs`（新增）、`WebSocketClient.cs`、`WebSocketServer.cs`、`src/ui/main/MainWindow.cs`、`src/ui/websocket/WebSocketPanel.cs`

## 现象

Koishi 端每隔约 2~3 秒出现一次「AutoCMEX 连上又被断开」，AutoCMEX 侧客户端日志循环输出：

```
WebSocketClient: connecting to ws://<koishi-host>:5141...
WebSocketClient: connected to ws://<koishi-host>:5141.
WebSocketClient: server closed connection. Status=NormalClosure, Desc=
WebSocketClient: reconnecting in 5000ms...
```

两个关键观测：

1. **不需要任何用户操作**——应用启动后一段时间自己就开始震颤（用户既没改设置也没点按钮）。
2. **连接频次高于单客户端上限**——重连间隔是 5s，单客户端每 5s 至多产生 1 次连接；实测 10 秒内约 10 次连接，是理论值的 4 倍 ⇒ 进程内存在 ≥2 个并发客户端。

## 环境

- AutoCMEX：Godot 4.x + .NET，**Client 模式**，目标地址即 Koishi 主机 `5141`。
- Koishi：插件以 `ws.Server` 监听 `5141`，策略是**新连接踢掉旧连接**。
- 日志：`%APPDATA%\Godot\app_userdata\AutoCMEX\logs\godot.log`（应用侧）与 Koishi 控制台（服务端侧）。

## 排查过程

### 1. 先排除 2026-06-20 那份旧记录的病因

`ws-reserve-port-conflict.md` 记录过**客户端日志逐字相同**的循环，但其根因是 5140 被 Koishi 内置 HTTP 服务器与插件 `ws.Server` 双绑，连接被随机丢给不处理 WS 的那个；那份记录的判据是「**Koishi 端完全没有连接日志，仿佛请求从未到达**」。

本轮该判据不成立：插件已固定 5141，Koishi 端每次都记到 `Handshake → HTTP request → AutoCMEX client connected`，说明连接确实到达了插件服务器、并且是**由插件主动断开**的。⇒ 本次是新根因，端口冲突一项已排除。

### 2. 应用启动段日志：触发点是「启动」本身

```
Info (DataManager): DataManager.LoadAll: dir=...             ← LoadAll 起始
Info (DataManager): DataManager: ActiveAiModelId changed     ┐
Info (DataManager): DataManager: WebSocketPort changed       │ 绑定刚注册
Info (DataManager): DataManager: WebSocketMode changed       │ 就立刻回调了 4 次
Info (DataManager): DataManager: KoishiWebSocketUrl changed  ┘
Info (DataManager): DataManager.LoadAll: bosses=1, ...        ← LoadAll 汇总
Info (WebSocket): MainWindow: restarting WebSocket...         ┐
Info (WebSocket): MainWindow: WebSocket restarted.            │ 连续 3 次
Info (WebSocket): MainWindow: restarting WebSocket...         │
Info (WebSocket): MainWindow: WebSocket restarted.            │
Info (WebSocket): MainWindow: restarting WebSocket...         ┘
Info (WebSocket): WebSocketClient: connecting to ws://<koishi-host>:5141...   ← ×3
Info (WebSocket): WebSocketClient: connected to ws://<koishi-host>:5141.     ← ×3
Info (WebSocket): WebSocketClient: server closed connection. Status=NormalClosure / reconnecting in 5000ms...
```

三条关键事实：

1. **配置绑定是「订阅即回调」**。`DataManager.LoadAll` 先完成 `_settings` 加载与 `EnsureIntegrity()`，随后才调 `BindSettingsChanges()`；那 4 条 `changed` 出现在「LoadAll 起始日志」与「LoadAll 汇总日志」之间，而之后没有任何再赋值动作 ⇒ 只能来自**注册瞬间以当前值回调一次**（`Chickensoft.Sync` 的 `Bind().OnValue(...)` 不是只在变更时触发）。
2. **所以启动那一刻就连打 3 次 `RestartWebSocket`**。`MainWindow.OnReady` 为「模式 / 端口 / 地址」各注册一个 `OnValue(_ => RestartWebSocket())`，与日志里 3 组 `restarting / restarted` 严格对应。
3. **3 次重启留下了 3 个仍在工作的实例**。旧 `WebSocketClient.StopAsync` 首行是 `if (!IsRunning) return;`，而此刻实例尚未连接、`IsRunning` 为 false ⇒ **同步返回、取消令牌没取消**；于是「停旧」实际没停，每次重启都新建实例并启动连接循环，前两个实例的循环无人回收、继续连接 ⇒ 3 条 `connecting / connected` 一一对应。

### 3. 两端日志对同一次断开的口径不同

Koishi 记的是 `Client disconnected: code=1006`，AutoCMEX 记的是 `server closed connection. Status=NormalClosure`。这不是矛盾：`NormalClosure` 是**收到的**关闭状态（服务端主动踢时发的就是它），而 1006 是因为被踢方收到 Close 帧后**没有回 close 帧**——旧 `ReceiveLoop` 的分支只打印日志然后 `break`，下一轮循环开头就 `_ws?.Dispose()` 了。判读日志口径时要先确认「谁主动关闭」。

## 根因

震颤 = **同一进程里存活了多个客户端实例**，被 Koishi 的「新连接踢旧连接」放大成互踢。四个具体缺陷：

| 编号 | 位置                                                      | 缺陷                                                                                                                                                                                                                                         |
| ---- | --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| R1   | `WebSocketClient.StopAsync` / `WebSocketServer.StopAsync` | 用 `IsRunning` 做「是否需要停」的判据并提前返回。Client 在断线重连等待中 `IsRunning` 为 false，取消令牌未取消 ⇒ 循环 5 秒后自己连回来（「停不掉的客户端」）；Server 同样因此不关监听器，端口变更后旧监听器仍占端口，新端口表现为「监听失败」 |
| R2   | `StartAsync`                                              | 同样用 `IsRunning` 判幂等，重连等待中再次启动会给同一实例起**第二条**连接循环                                                                                                                                                                |
| R3   | `MainWindow.RestartWebSocket`                             | 未串行化且是 `async void`；三个绑定各调一次，重启过程相互重叠、相互覆盖 `_webSocketServer`（放大器）                                                                                                                                         |
| R4   | 停止时序                                                  | 停止返回时不等待连接/接受循环真正退出，链路仍在跑；socket 未优雅关闭，对端只能记成异常断开                                                                                                                                                   |

## 修复

- **新增 `WebSocketLifecycle`**：作为实例创建/启动/停止/重启的唯一入口，持有当前实例并把启停与重启全部串行化（界面启停经 `ToggleAsync()`，见下节）。
- **幂等与必停判据改为「循环任务句柄是否还活着」**（`Task? _loopTask` / `_acceptLoopTask` + `SemaphoreSlim(1,1)`），不再依赖对外状态 `IsRunning`；`StopAsync` 一律「取消 → 关监听器/中止 socket → `await` 循环退出」。
- **校验式重启**：期望配置（模式 + 该模式对应的端口/地址）与当前实例的有效配置一致时，原样返回现有实例，**不重建不启停**——启动时那三次订阅回调因此退化为空操作；配置真变化时才「停旧 → 建新 → 启新」。
- **`IWebSocketServer.IsActive`（新增）**：表示「实例是否仍在工作」（含 Client 重连等待）。状态面板的按钮与状态标签文案按它显示（重连等待时为「断开」/「未连接（重连中）」）——否则重连中的客户端「点一下反而更连不上、也停不掉」。
- **`WebSocketServer` 的接受循环只服务本次启动的 listener 实例**（不读字段，避免重启换实例时操作错对象），启动失败一律收尾（丢掉半成品监听器）并把原因写进 `LastError`。

## 收尾：评审列出的两条低危项

评审认为两条都属「轻」，但与震颤同源/同域，用户要求一并收掉：

1. **界面启停没走生命周期控制器的门**。修复后启停时序已在控制器内串行化，但状态面板仍是对**自己手上的实例引用**调 `StartAsync` / `StopAsync`，方向也由面板按显示状态判定。面板拿的是上次推送的引用，配置变更重启期间它可能已是被替换掉的旧实例 ⇒ 在重启那几毫秒内点按钮，会把已停的旧实例重新拉起（与新实例同时在跑，与震颤同一类多实例场景）。收法：控制器新增 `ToggleAsync()`，方向在锁内按**当前实例**判定、与重启共用同一把锁；面板只调它，`MainWindow` 把控制器一并 `IProvide` 给面板。至此「启停只有一个入口」成为事实，不只是文档口径。
2. **心跳循环是 fire-and-forget**。旧写法 `_ = Task.Run(() => HeartbeatLoop(connectionId, token))` 且循环内读共享的 `_ws` 字段：停止返回时它可能正睡在 `Task.Delay` 里、也可能正在写已被释放的 socket；重连后旧心跳会跟着新 socket 继续发 ping（每重连一次多一条）。收法：每条连接一个独立心跳作用域（`CreateLinkedTokenSource`）与该连接的 socket 快照，连接结束即「取消 → `await` 心跳退出」，`StopAsync` 返回即代表实例上再无活动任务。

## 验证

- `dotnet build`：0 错误。
- `$GODOT --run-tests --quit-on-finish --coverage`（**带窗口，非 headless**）：**685 通过 / 0 失败 / 0 跳过**（修复前 656 条）。
- 新增单测：`WebSocketClientLifecycleTest`、`WebSocketServerLifecycleTest`、`WebSocketLifecycleTest`，并在 `TestWebSocketPanel` 补三条（重连中按钮文案与状态标签、重连中点击走停止分支、重启换实例后点击作用在控制器当前实例而非面板旧引用）；收尾两条另加 `WebSocketLifecycleTest` 启停方向三条（工作中则停、已停则启、无实例不凭空造）与 `WebSocketClientLifecycleTest` 一条（停止后不再有心跳到达对端）。
- 覆盖率：整体行 68.3%、分支 57.3%（`WebSocketLifecycle` 36/37 行、`WebSocketClient` 48/66 行、`WebSocketServer` 51/74 行）。
- **真机验收留待用户执行**：Client 模式连 Koishi 5141，启动后 30s 内 Koishi 端只应看到 1 次连接、0 次踢出（手动停止除外）。

## 遗留观察（本次未处理）

收到对端的 Close 帧时回一个 close 帧（`CloseOutputAsync`）会更符合协议礼仪，能让对端记「正常关闭」而不是 `1006`。本次不动：修复后正常路径（用户点断开 / 配置变更）已经走 `CloseAsync(NormalClosure)`，「被对端关闭」这一支只在异常场景出现，属独立问题，避免超出本次需求边界。

## 教训

1. **对外状态不能当内部生命周期判据**。「未连接」不等于「没在工作」（Client 重连等待期正是如此），用 `IsRunning` 判启停直接产生「停不掉」与「重复启动」两个缺陷。
2. **订阅式配置绑定要确认「订阅即回调」语义**。`Bind().OnValue(...)` 会在注册瞬间以当前值回调一次，启动路径因此被放大成 N 次动作；写绑定前先用日志确认回调时机，别按「只在变更时触发」的直觉推演。
3. **停止必须「取消 + 等待循环退出」**，只置标志位或只关 socket 都会留下仍在跑的循环。
4. **两端日志对同一次断开的口径不同**（收到的 Close 状态 vs 对端记的 1006），比对前先确认谁主动关闭，否则容易把「被动断开」误读成「主动异常」。
