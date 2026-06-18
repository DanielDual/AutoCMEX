# AutoCMEX — WebSocket 服务器重构方案

## 1. 需求核心目标与可量化验收标准

### 1.1 整体目标

重构现有 WebSocket 服务器，从单客户端升级为多客户端架构，实现标准化消息协议（command/event/error/ack），使 Koishi 插件能够通过 WebSocket 与 AutoCMEX 进行双向实时通信。

### 1.2 验收标准

| 验收项         | 标准                                                                      |
| -------------- | ------------------------------------------------------------------------- |
| 服务器监听     | WebSocket 服务器基于 HttpListener 成功监听（默认端口 5140，沿用现有配置） |
| 多客户端连接   | 支持多客户端并发连接，ConnectionManager 正确管理连接生命周期              |
| 双向通信       | 支持 command/event/error/ack 四种消息类型的双向 JSON 传输                 |
| Token 鉴权     | 可选 Token 鉴权，握手阶段验证，验证失败拒绝连接                           |
| 心跳保活       | HeartbeatService 独立管理每连接 Ping/Pong，超时自动断开                   |
| Koishi 插件    | 插件完整适配新协议，与 AutoCMEX 正常通信                                  |
| WebSocket 面板 | 独立 UI 面板显示服务器状态和已连接客户端列表                              |
| 测试覆盖       | GoDotTest 测试覆盖率 ≥ 80%                                                |

---

## 2. 技术选型与依赖说明

### 2.1 核心框架

基于现有技术栈扩展，不引入新的 NuGet 包。

| 类型 | 名称                    | 用途                           |
| ---- | ----------------------- | ------------------------------ |
| 内置 | System.Net.WebSockets   | WebSocket 核心实现             |
| 内置 | System.Net.HttpListener | HTTP Upgrade 握手              |
| 内置 | System.Text.Json        | JSON 序列化/反序列化           |
| 已有 | Chickensoft.Log         | 日志记录（ILog 接口）          |
| 已有 | Chickensoft.AutoInject  | 依赖注入（IProvide\<T\> 模式） |

### 2.2 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                      WebSocket 模块架构                      │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────┐                                            │
│  │ Koishi 插件  │──── WebSocket ────┐                       │
│  └─────────────┘                    │                       │
│                                     ▼                       │
│  ┌──────────────────────────────────────────────┐           │
│  │              WebSocketServer                  │           │
│  │  (HttpListener + 握手 + 消息收发循环)         │           │
│  │  统一异常捕获 → 转换为 error 响应             │           │
│  └──────────────────┬───────────────────────────┘           │
│                     │                                       │
│         ┌───────────┼───────────┐                           │
│         ▼           ▼           ▼                           │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐                    │
│  │Connection│ │ Protocol │ │ Message  │                    │
│  │ Manager  │ │ Handler  │ │ Router   │                    │
│  │(每连接队列)│ │(JSON解析)│ │(类型分发) │                    │
│  └──────────┘ └──────────┘ └────┬─────┘                    │
│                                 │                           │
│         ┌───────────────────────┼───────────────┐          │
│         ▼                       ▼               ▼          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────┐      │
│  │ Heartbeat    │    │ Command      │    │ Event    │      │
│  │ Service      │    │ Handler      │    │ Handler  │      │
│  │(独立心跳管理) │    │(业务命令处理) │    │(状态通知) │      │
│  └──────────────┘    └──────────────┘    └──────────┘      │
│                                                             │
│  ┌──────────────────────────────────────────────┐           │
│  │              WebSocketPanel (UI)              │           │
│  │  服务器状态 + 连接列表 + 启停按钮             │           │
│  └──────────────────────────────────────────────┘           │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. 代码结构与模块划分

### 3.1 文件变更清单

**新增文件**（`src/core/websocket/` 扁平结构）：

```
src/core/websocket/
├── WebSocketMessage.cs          # 消息基类（新增）
├── ConnectionInfo.cs            # 连接信息模型（新增）
├── IWebSocketServer.cs          # 服务器接口（新增）
├── IConnectionManager.cs        # 连接管理器接口（新增）
├── IProtocolHandler.cs          # 协议处理器接口（新增）
├── IMessageHandler.cs           # 消息处理器接口（新增）
├── ConnectionManager.cs         # 连接管理器实现（新增）
├── ProtocolHandler.cs           # 协议处理器实现（新增）
├── MessageRouter.cs             # 消息路由器（新增）
├── HeartbeatService.cs          # 心跳服务（新增）
├── CommandHandler.cs            # 命令处理器（新增，替代 MessageHandler）
├── EventHandler.cs              # 事件处理器（新增）
├── WebSocketServer.cs           # 重构：多客户端 + HttpListener
└── MessageHandler.cs            # 废弃（逻辑迁移至 CommandHandler）
```

**修改文件**：

| 文件                               | 变更内容                                                               |
| ---------------------------------- | ---------------------------------------------------------------------- |
| `src/models/AppSettings.cs`        | 新增 WebSocket 配置字段（EnableAuth、AuthToken、MaxConnections 等）    |
| `src/ui/main/MainWindow.cs`        | 新增 `IProvide<IWebSocketServer>`，OnReady 创建并启动，OnExitTree 停止 |
| `src/ui/main/MainWindow.tscn`      | 左栏导航新增「WebSocket」按钮                                          |
| `src/ui/settings/SettingsPanel.cs` | 适配新的 AppSettings 字段                                              |
| `src/plugin/koishi/src/index.ts`   | 完整适配新协议（command/event/error/ack）                              |

**新增 UI 文件**：

```
src/ui/websocket/
├── WebSocketPanel.tscn          # WebSocket 面板场景
└── WebSocketPanel.cs            # WebSocket 面板逻辑
```

**新增测试文件**（`test/src/websocket/`）：

```
test/src/websocket/
├── WebSocketServerTest.cs       # 服务器生命周期测试
├── ConnectionManagerTest.cs     # 连接管理测试
├── ProtocolHandlerTest.cs       # 协议处理测试
├── MessageRouterTest.cs         # 消息路由测试
├── CommandHandlerTest.cs        # 命令处理测试
├── HeartbeatServiceTest.cs      # 心跳服务测试
└── WebSocketIntegrationTest.cs  # 集成测试
```

### 3.2 分层架构遵循

- WebSocket 模块独立于业务层，通过事件/消息与核心业务解耦
- 使用 Chickensoft.AutoInject 的 `IProvide<T>` 模式管理依赖
- 接口与实现分离，便于单元测试 Mock
- 日志通过构造函数注入 `ILog`（Chickensoft.Log），与现有模块一致

---

## 4. 核心业务逻辑

### 4.1 服务器启动流程

```
MainWindow.OnReady()
    ↓
创建 WebSocketServer（注入 ILog、AppSettings 中的 WebSocket 配置）
    ↓
this.Provide() → 发布 IWebSocketServer 到 AutoInject 树
    ↓
WebSocketServer.StartAsync()
    ↓
创建 HttpListener，添加 http://127.0.0.1:{port}/ 前缀
    ↓
启动后台 Task：AcceptConnectionsLoop()
```

### 4.2 客户端连接建立流程

```
Koishi 插件发起 WebSocket 连接 → ws://127.0.0.1:{port}/
    ↓
HttpListener 接收 HTTP 请求 → 验证是否为 WebSocket 升级请求
    ↓
[可选] Token 鉴权：从 QueryString 或 Authorization Header 提取 Token 验证
    ├── 验证失败 → 返回 401，关闭连接
    └── 验证成功 → 继续
    ↓
HttpListenerContext.AcceptWebSocketAsync() → 完成握手
    ↓
ConnectionManager.RegisterConnection(webSocket) → 生成 connectionId
    ↓
HeartbeatService.StartHeartbeat(connectionId, webSocket)
    ↓
EventHandler.OnConnected(connectionId) → 广播连接事件
    ↓
启动消息接收循环：ReceiveLoop(connectionId, webSocket)
```

### 4.3 消息接收与处理流程

```
ReceiveLoop 接收到 WebSocket 消息
    ↓
ProtocolHandler.ParseMessage(rawMessage) → 解析 JSON
    ├── 解析失败 → 抛出 ProtocolException
    ├── type 字段非法 → 抛出 ProtocolException
    └── 解析成功 → WebSocketMessage
    ↓
MessageRouter.Route(message, connectionId)
    ├── type=command → CommandHandler.HandleAsync()
    │   └── 根据 payload.action 分发业务逻辑
    │       ├── guess → 触发猜测管道
    │       └── ... → 其他命令
    ├── type=event → EventHandler.HandleAsync()
    └── type=ack → 处理确认
    ↓
生成响应消息 → ProtocolHandler.SerializeMessage(response)
    ↓
ConnectionManager.SendAsync(connectionId, response) → 定向发送
    ↓
[异常捕获] WebSocketServer 统一 catch
    ├── ProtocolException → error 响应（INVALID_FORMAT / UNKNOWN_TYPE）
    ├── AuthException → error 响应（UNAUTHORIZED）
    └── Exception → error 响应（INTERNAL_ERROR）+ 记录日志
```

### 4.4 连接关闭与清理流程

```
检测到连接关闭（客户端断开 / 异常 / 心跳超时）
    ↓
HeartbeatService.StopHeartbeat(connectionId)
    ↓
ConnectionManager.UnregisterConnection(connectionId)
    ↓
清理连接资源（WebSocket Dispose、队列清空）
    ↓
EventHandler.OnDisconnected(connectionId) → 广播断开事件
    ↓
记录 Info 日志
```

### 4.5 心跳保活机制（HeartbeatService）

```
HeartbeatService 为每个连接独立维护：
    ↓
定时器每 30 秒触发 → 发送 Ping 帧
    ↓
等待 Pong 响应，超时 10 秒
    ├── 收到 Pong → 重置定时器
    └── 超时 → 标记连接失效 → 触发关闭流程
```

### 4.6 消息发送模型

```
定向发送：ConnectionManager.SendAsync(connectionId, message)
    ↓
查找对应连接的发送队列 → 入队
    ↓
发送循环从队列取出 → WebSocket.SendAsync()
    ↓
广播：ConnectionManager.BroadcastAsync(message)
    ↓
遍历所有连接 → 分别入队发送
```

---

## 5. 边界与风险处理

### 5.1 入参校验规则

| 校验项     | 规则                               | 失败处理                            |
| ---------- | ---------------------------------- | ----------------------------------- |
| Token 鉴权 | 若启用鉴权，握手阶段验证 Token     | 返回 401，关闭连接                  |
| 消息格式   | 必须是合法 JSON                    | 抛出 ProtocolException → error 响应 |
| 消息字段   | type 必须为 command/event/ack 之一 | 抛出 ProtocolException → error 响应 |
| 消息大小   | 单条消息 ≤ 64KB                    | 抛出 ProtocolException → error 响应 |

### 5.2 异常场景处理方案

| 异常场景                 | 处理方案                                                                   |
| ------------------------ | -------------------------------------------------------------------------- |
| 端口被占用               | 启动时检查，记录 Error 日志，WebSocketServer.IsRunning=false，不阻塞主程序 |
| WebSocket 握手失败       | 记录 Warning 日志，关闭 HTTP 连接                                          |
| 消息解析异常             | 统一 catch 转换为 error 响应，不中断连接，记录 Warning 日志                |
| 消息处理异常（业务逻辑） | 统一 catch 转换为 error 响应，记录 Error 日志，保持连接                    |
| 客户端异常断开           | catch WebSocketException，执行关闭流程，记录 Info 日志                     |
| 服务器端主动关闭         | 遍历所有连接发送 Close 帧，等待确认或超时，清理资源                        |
| 心跳超时                 | HeartbeatService 触发 OnHeartbeatTimeout 事件 → 执行关闭流程               |
| 并发连接数超限           | 拒绝新连接，返回 503，记录 Warning 日志                                    |

### 5.3 权限控制（本次迭代）

| 控制项       | 方案                                                                |
| ------------ | ------------------------------------------------------------------- |
| 连接鉴权     | 可选 Token 鉴权，通过 AppSettings.EnableAuth 控制开关，握手阶段验证 |
| 消息大小限制 | 单条消息 ≤ 64KB（硬编码，后续可配置化）                             |

> **后续迭代预留**：Origin 白名单、IP 白名单、速率限制、HMAC-SHA256 签名

---

## 6. 数据模型

### 6.1 消息协议格式

```json
// 基础消息结构
{
  "id": "uuid-string",
  "type": "command|event|error|ack",
  "timestamp": 1699123456789,
  "payload": {}
}

// 命令消息（Koishi → AutoCMEX）
{
  "id": "cmd-001",
  "type": "command",
  "timestamp": 1699123456789,
  "payload": {
    "action": "guess",
    "params": { "message": "..." }
  }
}

// 事件消息（AutoCMEX → Koishi）
{
  "id": "evt-001",
  "type": "event",
  "timestamp": 1699123456789,
  "payload": {
    "event": "guess_result",
    "data": { "result": "..." }
  }
}

// 错误消息
{
  "id": "err-001",
  "type": "error",
  "timestamp": 1699123456789,
  "payload": {
    "code": "INVALID_COMMAND",
    "message": "Unknown command action",
    "details": {}
  }
}

// 确认消息（ACK）
{
  "id": "ack-001",
  "type": "ack",
  "timestamp": 1699123456789,
  "payload": {
    "originalId": "cmd-001",
    "status": "success|failure"
  }
}
```

### 6.2 错误码定义

| 错误码                | 说明                         |
| --------------------- | ---------------------------- |
| `INVALID_FORMAT`      | 消息格式非法（非 JSON）      |
| `UNKNOWN_TYPE`        | 未知消息类型                 |
| `INVALID_COMMAND`     | 命令格式错误或 action 不存在 |
| `UNAUTHORIZED`        | Token 鉴权失败               |
| `INTERNAL_ERROR`      | 服务器内部错误               |
| `SERVICE_UNAVAILABLE` | 连接数超限，服务暂时不可用   |

### 6.3 AppSettings 扩展字段

```csharp
// 在现有 AppSettings.cs 中新增以下字段：
public bool WebSocketEnableAuth { get; set; } = false;
public string WebSocketAuthToken { get; set; } = string.Empty;
public int WebSocketMaxConnections { get; set; } = 100;
public int WebSocketHeartbeatIntervalMs { get; set; } = 30000;
public int WebSocketHeartbeatTimeoutMs { get; set; } = 10000;
```

> 现有 `WebSocketPort`（默认 5140）和 `MessageFilterMode` 保持不变。

---

## 7. 接口契约定义

### 7.1 IWebSocketServer

```csharp
public interface IWebSocketServer
{
    Task StartAsync();
    Task StopAsync();
    bool IsRunning { get; }
    int ConnectionCount { get; }
    event Action<string>? OnClientConnected;
    event Action<string>? OnClientDisconnected;
}
```

### 7.2 IConnectionManager

```csharp
public interface IConnectionManager
{
    string RegisterConnection(WebSocket webSocket);
    void UnregisterConnection(string connectionId);
    ConnectionInfo? GetConnection(string connectionId);
    IReadOnlyList<ConnectionInfo> GetAllConnections();
    Task SendAsync(string connectionId, string message);
    Task BroadcastAsync(string message);
    int Count { get; }
    int MaxConnections { get; }
}
```

### 7.3 IProtocolHandler

```csharp
public interface IProtocolHandler
{
    WebSocketMessage ParseMessage(string rawMessage);
    string SerializeMessage(WebSocketMessage message);
}
```

### 7.4 IMessageHandler

```csharp
public interface IMessageHandler
{
    bool CanHandle(string messageType);
    Task<WebSocketMessage?> HandleAsync(WebSocketMessage message, string connectionId);
}
```

---

## 8. 权限与安全设计

### 8.1 Token 鉴权流程

```
Koishi 插件发起连接时携带 Token：
  ws://127.0.0.1:5140/?token=<token>
  或 Header: Authorization: Bearer <token>
    ↓
HttpListener 收到请求 → 检查 AppSettings.WebSocketEnableAuth
    ├── false → 跳过鉴权，直接握手
    └── true → 提取 Token，与 AppSettings.WebSocketAuthToken 比对
        ├── 匹配 → 继续握手
        └── 不匹配 → 返回 401，关闭连接
```

### 8.2 消息安全

- 消息大小限制：单条 ≤ 64KB
- 后续迭代预留：TLS/SSL 加密（wss://）、HMAC-SHA256 签名

---

## 9. 测试覆盖方案

### 9.1 测试框架与位置

- 框架：Chickensoft.GoDotTest + GodotTestDriver
- 位置：`test/src/websocket/`
- 覆盖目标：核心业务类代码行覆盖率 ≥ 80%

### 9.2 测试用例

| 测试类                     | 测试场景                                             |
| -------------------------- | ---------------------------------------------------- |
| `WebSocketServerTest`      | 启动/停止服务器、端口绑定、IsRunning 状态            |
| `ConnectionManagerTest`    | 注册/注销连接、并发安全、超限拒绝、定向发送、广播    |
| `ProtocolHandlerTest`      | JSON 解析/序列化、非法格式异常、未知类型异常         |
| `MessageRouterTest`        | 消息类型路由分发、未匹配处理器                       |
| `CommandHandlerTest`       | guess 命令处理、未知 action 处理                     |
| `HeartbeatServiceTest`     | Ping/Pong 正常流程、超时检测、启停管理               |
| `WebSocketIntegrationTest` | 完整连接→消息收发→断开流程、多客户端并发、Token 鉴权 |

---

## 10. 兼容性与回滚方案

### 10.1 兼容性保证

- WebSocket 模块在现有 `src/core/websocket/` 目录内重构，不改变目录位置
- 通过 MainWindow `IProvide<T>` 集成，不修改其他业务模块
- AppSettings 新增字段均有默认值，不影响现有配置文件

### 10.2 回滚方案

| 回滚方式   | 操作                                                 |
| ---------- | ---------------------------------------------------- |
| 代码回滚   | Git revert 到重构前的提交                            |
| 运行时禁用 | 不创建 WebSocketServer 实例即可（MainWindow 中跳过） |

### 10.3 降级策略

- WebSocket 服务器启动失败不影响主程序（IsRunning=false，记录日志）
- Koishi 插件连接失败时自动重试

---

## 附录：任务清单

### A. 数据与配置（3 项）

| #   | 任务                                                                                                        | 优先级 |
| --- | ----------------------------------------------------------------------------------------------------------- | ------ |
| A1  | 扩展 `AppSettings.cs`，新增 WebSocket 配置字段                                                              | 高     |
| A2  | 创建消息模型（`WebSocketMessage.cs`、`ConnectionInfo.cs`）                                                  | 高     |
| A3  | 创建接口定义（`IWebSocketServer.cs`、`IConnectionManager.cs`、`IProtocolHandler.cs`、`IMessageHandler.cs`） | 高     |

### B. 核心服务实现（6 项）

| #   | 任务                                                                           | 优先级 |
| --- | ------------------------------------------------------------------------------ | ------ |
| B1  | 实现 `ProtocolHandler.cs`（JSON 解析/序列化、消息验证）                        | 高     |
| B2  | 实现 `ConnectionManager.cs`（注册/注销/查询、每连接发送队列、广播、并发安全）  | 高     |
| B3  | 实现 `HeartbeatService.cs`（独立 Ping/Pong 管理、超时检测）                    | 高     |
| B4  | 重构 `WebSocketServer.cs`（HttpListener 多客户端、消息收发循环、统一异常捕获） | 高     |
| B5  | 实现 `MessageRouter.cs`（类型分发、处理器注册）                                | 高     |
| B6  | 实现 `CommandHandler.cs`（替代 MessageHandler，处理 guess 等命令）             | 高     |

### C. 事件与安全（2 项）

| #   | 任务                                                       | 优先级 |
| --- | ---------------------------------------------------------- | ------ |
| C1  | 实现 `EventHandler.cs`（连接/断开事件、状态变更通知）      | 中     |
| C2  | 实现 Token 鉴权（握手阶段验证，通过 AppSettings 开关控制） | 中     |

### D. UI 集成（3 项）

| #   | 任务                                                                                    | 优先级 |
| --- | --------------------------------------------------------------------------------------- | ------ |
| D1  | 创建 `WebSocketPanel.tscn` + `WebSocketPanel.cs`（服务器状态、连接列表、启停按钮）      | 高     |
| D2  | 修改 `MainWindow.cs`（`IProvide<IWebSocketServer>`，OnReady 创建启动，OnExitTree 停止） | 高     |
| D3  | 修改 `MainWindow.tscn`（左栏导航新增「WebSocket」按钮）                                 | 中     |

### E. Koishi 插件（1 项）

| #   | 任务                                                                             | 优先级 |
| --- | -------------------------------------------------------------------------------- | ------ |
| E1  | 更新 `src/plugin/koishi/src/index.ts`，完整适配新协议（command/event/error/ack） | 高     |

### F. 测试（7 项）

| #   | 任务                               | 优先级 |
| --- | ---------------------------------- | ------ |
| F1  | 编写 `WebSocketServerTest.cs`      | 高     |
| F2  | 编写 `ConnectionManagerTest.cs`    | 高     |
| F3  | 编写 `ProtocolHandlerTest.cs`      | 高     |
| F4  | 编写 `MessageRouterTest.cs`        | 中     |
| F5  | 编写 `CommandHandlerTest.cs`       | 高     |
| F6  | 编写 `HeartbeatServiceTest.cs`     | 中     |
| F7  | 编写 `WebSocketIntegrationTest.cs` | 高     |

---

**总计 22 项任务**，按优先级分布：高 16 项 / 中 6 项 / 低 0 项
