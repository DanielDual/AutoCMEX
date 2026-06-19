// AutoCMEX Koishi v4 Plugin
// 将此文件夹复制到 Koishi 的 plugins 目录即可安装
// 功能：将群聊消息通过 WebSocket 转发到 AutoCMEX，并返回处理结果
// 协议：command / event / error / ack
// 模式：client（连接 AutoCMEX）/ server（等待 AutoCMEX 连接）

const { Schema } = require("koishi");
const WebSocket = require("ws");
const crypto = require("crypto");

const DEFAULT_HOST = "127.0.0.1";
const DEFAULT_PORT = 5140;
const RECONNECT_INTERVAL = 5000;
const HEARTBEAT_INTERVAL = 30000;

// Client mode state
let ws = null;
let messageQueue = [];
let reconnectTimer = null;
let heartbeatTimer = null;

// Server mode state
let wss = null;
let autoCmexClient = null;

/**
 * 生成 UUID v4
 */
function generateId() {
  return crypto.randomUUID();
}

/**
 * 构建 guess 命令消息
 */
function buildGuessMessage(session) {
  return {
    id: generateId(),
    type: "command",
    timestamp: Date.now(),
    payload: {
      action: "guess",
      params: {
        message: session.content || "",
        sender: session.username || session.userId || "",
        timestamp: new Date().toISOString(),
      },
    },
  };
}

/**
 * 插件配置项
 */
module.exports.Config = Schema.object({
  mode: Schema.string()
    .default("client")
    .description("运行模式：client（连接 AutoCMEX）/ server（等待 AutoCMEX 连接）"),
  host: Schema.string()
    .default(DEFAULT_HOST)
    .description("Client 模式：AutoCMEX 地址"),
  port: Schema.number()
    .default(DEFAULT_PORT)
    .description("Client 模式：AutoCMEX 端口（Server 模式使用 Koishi 自身端口）"),
  token: Schema.string()
    .default("")
    .description("鉴权 Token（留空不启用）"),
}).description("AutoCMEX 配置");

/**
 * Koishi 插件入口
 */
module.exports.name = "auto-cmex";

module.exports.apply = (ctx, config) => {
  const mode = config.mode || "client";
  const host = config.host || DEFAULT_HOST;
  const port = config.port || DEFAULT_PORT;
  const token = config.token || "";

  if (mode === "server") {
    startServer(ctx, token);
  } else {
    startClient(ctx, host, port, token);
  }

  // 监听所有群聊消息
  ctx.on("message", (session) => {
    const message = buildGuessMessage(session);

    if (mode === "server") {
      if (autoCmexClient && autoCmexClient.readyState === WebSocket.OPEN) {
        autoCmexClient.send(JSON.stringify(message));
      } else {
        messageQueue.push(message);
        if (messageQueue.length > 100) messageQueue.shift();
      }
    } else {
      if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
      } else {
        messageQueue.push(message);
        if (messageQueue.length > 100) messageQueue.shift();
      }
    }
  });

  // 注册命令
  ctx.command("auto-cmex.status", "查看 AutoCMEX 连接状态").action(() => {
    if (mode === "server") {
      if (autoCmexClient && autoCmexClient.readyState === WebSocket.OPEN) {
        return "AutoCMEX 已连接（Server 模式）";
      }
      return "等待 AutoCMEX 连接（Server 模式）";
    }
    if (ws && ws.readyState === WebSocket.OPEN) {
      return "AutoCMEX 已连接（Client 模式）";
    }
    return "AutoCMEX 未连接（Client 模式）";
  });
};

/**
 * Client 模式：连接到 AutoCMEX WebSocket 服务
 */
function startClient(ctx, host, port, token) {
  const wsUrl = token
    ? `ws://${host}:${port}/?token=${encodeURIComponent(token)}`
    : `ws://${host}:${port}`;

  ctx.logger.info(`[AutoCMEX] Client mode: connecting to ${wsUrl}`);

  function connect() {
    if (ws) ws.close();
    ws = new WebSocket(wsUrl);

    ws.on("open", () => {
      ctx.logger.info("[AutoCMEX] Connected to server");
      while (messageQueue.length > 0) {
        ws.send(JSON.stringify(messageQueue.shift()));
      }
      if (reconnectTimer) {
        clearInterval(reconnectTimer);
        reconnectTimer = null;
      }
      if (heartbeatTimer) clearInterval(heartbeatTimer);
      heartbeatTimer = setInterval(() => {
        if (ws && ws.readyState === WebSocket.OPEN) {
          ws.send(JSON.stringify({
            id: generateId(),
            type: "command",
            timestamp: Date.now(),
            payload: { action: "ping" },
          }));
        }
      }, HEARTBEAT_INTERVAL);
    });

    ws.on("message", (data) => handleMessage(ctx, data));

    ws.on("close", () => {
      ctx.logger.warn("[AutoCMEX] Disconnected, reconnecting...");
      if (heartbeatTimer) { clearInterval(heartbeatTimer); heartbeatTimer = null; }
      if (!reconnectTimer) {
        reconnectTimer = setInterval(connect, RECONNECT_INTERVAL);
      }
    });

    ws.on("error", (err) => ctx.logger.warn(`[AutoCMEX] Error: ${err.message}`));
  }

  connect();
}

/**
 * Server 模式：启动 WebSocket 服务器等待 AutoCMEX 连接
 */
function startServer(ctx, token) {
  // 尝试挂载到 Koishi 已有的 HTTP 服务器，避免端口冲突
  const server = ctx.http?.server;
  const wsPort = server ? null : 5141; // 无法挂载时使用独立端口 5141

  wss = new WebSocket.Server({
    ...(server ? { server } : { port: wsPort }),
    perMessageDeflate: false,
    verifyClient: (info, cb) => {
      ctx.logger.info(
        `[AutoCMEX] Handshake from ${info.req.socket.remoteAddress}, ` +
        `origin=${info.origin}, secure=${info.secure}`
      );
      cb(true);
    },
  });

  ctx.logger.info(
    server
      ? `[AutoCMEX] Server mode: attached to Koishi HTTP server`
      : `[AutoCMEX] Server mode: listening on port ${wsPort}`
  );

  wss.on("error", (err) => {
    ctx.logger.error(`[AutoCMEX] Server error: ${err.message}`);
  });

  // 诊断：记录所有到达 HTTP 服务器的请求
  wss.on("headers", (headers, req) => {
    ctx.logger.info(
      `[AutoCMEX] HTTP request: ${req.method} ${req.url} from ${req.socket.remoteAddress}`
    );
  });

  wss.on("connection", (client, req) => {
    // Token 鉴权
    if (token) {
      const urlParams = new URLSearchParams(req.url?.split("?")[1] || "");
      const clientToken = urlParams.get("token") || "";
      if (clientToken !== token) {
        ctx.logger.warn("[AutoCMEX] Auth failed, closing connection");
        client.close(4001, "Unauthorized");
        return;
      }
    }

    // 断开旧连接
    if (autoCmexClient) {
      autoCmexClient.close();
    }
    autoCmexClient = client;
    ctx.logger.info(
      `[AutoCMEX] AutoCMEX client connected from ${req.socket.remoteAddress}`
    );

    // 发送缓存消息
    while (messageQueue.length > 0) {
      if (client.readyState === WebSocket.OPEN) {
        client.send(JSON.stringify(messageQueue.shift()));
      }
    }

    client.on("message", (data) => handleMessage(ctx, data));

    client.on("close", (code, reason) => {
      ctx.logger.warn(
        `[AutoCMEX] Client disconnected: code=${code}, reason=${reason?.toString() || "none"}`
      );
      if (autoCmexClient === client) autoCmexClient = null;
    });

    client.on("error", (err) =>
      ctx.logger.warn(`[AutoCMEX] Client error: ${err.message}`)
    );
  });
}

/**
 * 处理收到的消息
 */
function handleMessage(ctx, data) {
  try {
    const msg = JSON.parse(data.toString());
    switch (msg.type) {
      case "ack":
        ctx.logger.debug(
          `[AutoCMEX] ACK: id=${msg.payload?.originalId}, status=${msg.payload?.status}`
        );
        break;
      case "event":
        ctx.logger.info(`[AutoCMEX] Event: ${msg.payload?.event}`);
        break;
      case "error":
        ctx.logger.warn(
          `[AutoCMEX] Error: [${msg.payload?.code}] ${msg.payload?.message}`
        );
        break;
      default:
        ctx.logger.debug(`[AutoCMEX] Unknown type: ${msg.type}`);
    }
  } catch (e) {
    ctx.logger.warn(`[AutoCMEX] Parse error: ${e.message}`);
  }
}
