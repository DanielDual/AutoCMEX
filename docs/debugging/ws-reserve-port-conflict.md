# ws-reserve 模式端口冲突调试记录

## 现象

Koishi Server 模式 + AutoCMEX Client 模式时，AutoCMEX 日志循环输出：

```
WebSocketClient: connecting to ws://<koishi-host>:5140...
WebSocketClient: connected to ws://<koishi-host>:5140.
WebSocketClient: server closed connection. Status=NormalClosure, Desc=
WebSocketClient: reconnecting in 5000ms...
```

Koishi 端无任何连接日志，仿佛请求从未到达。

## 环境

- Koishi：宿主机 `<koishi-host>`，Node.js `ws` 库
- AutoCMEX：虚拟机 `<autocmex-vm>`（NAT 模式），.NET `ClientWebSocket`

## 排查过程

### 1. 怀疑 perMessageDeflate 压缩不兼容

Node.js `ws` 库默认启用 `perMessageDeflate`，与 .NET `ClientWebSocket` 的压缩协商可能不兼容。

→ 设置 `perMessageDeflate: false`，无效。

### 2. 怀疑 http.sys URL 预留残留

AutoCMEX Server 模式使用 `HttpListener`，会在 Windows http.sys 中注册 URL 前缀。残留注册可能拦截请求。

→ `netsh http delete urlacl url=http://+:5140/` 报错 `Error: 2`，无残留。

### 3. 怀疑防火墙

→ 关闭防火墙，无效。

### 4. 添加诊断日志

在 Koishi 插件中添加 `verifyClient`、`headers` 事件、`listening` 事件、close code/reason 日志。

发现：**在 AutoCMEX 启动前**，已有来自 `<koishi-host>`（本机）的连接到达 Koishi，路径为 `GET /status`。

### 5. 关键发现

某次测试中 Koishi 日志同时出现：

```
server listening at http://<koishi-host>:5140       ← Koishi 内置 HTTP 服务器
[AutoCMEX] Server mode: listening on port 5140    ← 插件 ws.Server
```

**两个服务器绑定同一端口 5140**。Windows `SO_REUSEADDR` 允许双绑，但连接随机分配给其中一个：

- 分给 Koishi 内置服务器 → 不处理 WebSocket → 返回 `NormalClosure`
- 分给插件 `ws.Server` → 正常连接

这解释了为什么 AutoCMEX 有时能连上、有时不能。

## 修复

插件 Server 模式改用独立端口 **5141**，避开 Koishi 内置服务器的 5140。

```javascript
// 尝试挂载到 Koishi HTTP 服务器，失败则用独立端口
const server = ctx.http?.server;
const wsPort = server ? null : 5141;

wss = new WebSocket.Server({
  ...(server ? { server } : { port: wsPort }),
  perMessageDeflate: false,
});
```

## 教训

1. **端口冲突优先排查**：`SO_REUSEADDR` 让多进程绑定同一端口成为可能，连接随机分配导致间歇性故障
2. **先加日志再猜原因**：`verifyClient`、`headers`、close code 是诊断 WebSocket 问题的关键日志点
3. **`NormalClosure` + 空描述** = 对方主动关闭，不是协议错误
4. **`perMessageDeflate: false`** 是 Node.js `ws` 与 .NET `ClientWebSocket` 通信的安全选项
5. **VM NAT 模式**下 TCP 可达，但端口冲突问题与网络模式无关
