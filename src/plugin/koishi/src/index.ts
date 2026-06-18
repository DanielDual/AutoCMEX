// AutoCMEX Koishi v4 Plugin
// 将此文件夹复制到 Koishi 的 plugins 目录即可安装
// 功能：将群聊消息通过 WebSocket 转发到 AutoCMEX，并返回处理结果
// 协议：command / event / error / ack

const { Schema } = require("koishi");
const WebSocket = require("ws");
const crypto = require("crypto");

const DEFAULT_HOST = "127.0.0.1";
const DEFAULT_PORT = 5140;
const RECONNECT_INTERVAL = 5000;
const HEARTBEAT_INTERVAL = 30000;

let ws = null;
let messageQueue = [];
let reconnectTimer = null;
let heartbeatTimer = null;

/**
 * 生成 UUID v4
 */
function generateId() {
  return crypto.randomUUID();
}

/**
 * 插件配置项
 */
module.exports.Config = Schema.object({
  host: Schema.string()
    .default(DEFAULT_HOST)
    .description("AutoCMEX WebSocket 服务地址"),
  port: Schema.number()
    .default(DEFAULT_PORT)
    .description("AutoCMEX WebSocket 服务端口"),
  token: Schema.string()
    .default("")
    .description("WebSocket 鉴权 Token（留空则不启用鉴权）"),
}).description("AutoCMEX 配置");

/**
 * Koishi 插件入口
 */
module.exports.name = "auto-cmex";

module.exports.apply = (ctx, config) => {
  const host = config.host || DEFAULT_HOST;
  const port = config.port || DEFAULT_PORT;
  const token = config.token || "";

  const wsUrl = token
    ? `ws://${host}:${port}/?token=${encodeURIComponent(token)}`
    : `ws://${host}:${port}`;

  ctx.logger.info(`[AutoCMEX] Connecting to ${wsUrl}`);

  connect(ctx, wsUrl);

  // 监听所有群聊消息
  ctx.on("message", (session) => {
    const message = {
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

    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(message));
    } else {
      // 缓存消息
      messageQueue.push(message);
      if (messageQueue.length > 100) {
        messageQueue.shift();
      }
    }
  });

  // 注册命令
  ctx.command("auto-cmex.status", "查看 AutoCMEX 连接状态").action(() => {
    if (ws && ws.readyState === WebSocket.OPEN) {
      return "AutoCMEX 已连接";
    }
    return "AutoCMEX 未连接";
  });
};

/**
 * 连接到 AutoCMEX WebSocket 服务
 */
function connect(ctx, wsUrl) {
  if (ws) {
    ws.close();
  }

  ws = new WebSocket(wsUrl);

  ws.on("open", () => {
    ctx.logger.info("[AutoCMEX] Connected");

    // 发送缓存消息
    while (messageQueue.length > 0) {
      const msg = messageQueue.shift();
      ws.send(JSON.stringify(msg));
    }

    // 清除重连定时器
    if (reconnectTimer) {
      clearInterval(reconnectTimer);
      reconnectTimer = null;
    }

    // 启动心跳
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
    }
    heartbeatTimer = setInterval(() => {
      if (ws && ws.readyState === WebSocket.OPEN) {
        const ping = {
          id: generateId(),
          type: "command",
          timestamp: Date.now(),
          payload: {
            action: "ping",
          },
        };
        ws.send(JSON.stringify(ping));
      }
    }, HEARTBEAT_INTERVAL);
  });

  ws.on("message", (data) => {
    try {
      const msg = JSON.parse(data.toString());
      switch (msg.type) {
        case "ack":
          ctx.logger.debug(
            `[AutoCMEX] ACK: id=${msg.payload?.originalId}, status=${msg.payload?.status}`
          );
          break;
        case "event":
          ctx.logger.info(
            `[AutoCMEX] Event: ${msg.payload?.event}`
          );
          break;
        case "error":
          ctx.logger.warn(
            `[AutoCMEX] Error: [${msg.payload?.code}] ${msg.payload?.message}`
          );
          break;
        default:
          ctx.logger.debug(`[AutoCMEX] Unknown message type: ${msg.type}`);
      }
    } catch (e) {
      ctx.logger.warn(`[AutoCMEX] Failed to parse message: ${e.message}`);
    }
  });

  ws.on("close", () => {
    ctx.logger.warn("[AutoCMEX] Disconnected, reconnecting...");
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
    if (!reconnectTimer) {
      reconnectTimer = setInterval(
        () => connect(ctx, wsUrl),
        RECONNECT_INTERVAL,
      );
    }
  });

  ws.on("error", (err) => {
    ctx.logger.warn(`[AutoCMEX] Error: ${err.message}`);
  });
}
