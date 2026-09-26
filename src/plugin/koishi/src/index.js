// AutoCMEX Koishi v4 Plugin
// 安装：由 AutoCMEX 一键安装到 Koishi 工作区的 external/adapter-autocmex/，
//       再在控制台「插件配置 → 右键 → 添加插件」里搜 adapter-autocmex（或看「适配器」分类）启用
// 功能：将群聊消息通过 WebSocket 转发到 AutoCMEX，并返回处理结果
// 协议：command / event / error / ack
// 模式：client（连接 AutoCMEX）/ server（等待 AutoCMEX 连接）

const { Schema, h } = require("koishi");
const WebSocket = require("ws");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const DEFAULT_HOST = "127.0.0.1";
const DEFAULT_PORT = 5140;
const RECONNECT_INTERVAL = 5000;
const HEARTBEAT_INTERVAL = 30000;
const REQUEST_TTL = 5 * 60 * 1000;

// 合并转发节点使用的署名（OneBot 的 node 必须带昵称与 QQ 号）
const FORWARD_NICKNAME = "AutoCMEX";
const FORWARD_UIN = "10000";

// Client mode state
let ws = null;
let messageQueue = [];
let reconnectTimer = null;
let heartbeatTimer = null;
let pendingRequests = new Map();

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
  const requestId = generateId();
  return {
    id: requestId,
    type: "command",
    timestamp: Date.now(),
    payload: {
      action: "guess",
      params: {
        message: session.content || "",
        sender: session.username || session.userId || "",
        timestamp: new Date().toISOString(),
        sessionId: session.sid || "",
        channelId: session.channelId || "",
        guildId: session.guildId || "",
        messageId: session.messageId || "",
      },
    },
  };
}

function rememberRequest(ctx, requestId, session) {
  pendingRequests.set(requestId, {
    session,
    createdAt: Date.now(),
  });
  ctx.logger.info(
    `[AutoCMEX] Stored pending request ${requestId} (total=${pendingRequests.size})`,
  );
  cleanupExpiredRequests(ctx);
}

function cleanupExpiredRequests(ctx) {
  const now = Date.now();
  for (const [requestId, entry] of pendingRequests.entries()) {
    if (!entry || now - entry.createdAt > REQUEST_TTL) {
      ctx.logger.info(
        `[AutoCMEX] Expired pending request ${requestId} (age=${now - entry.createdAt}ms)`,
      );
      pendingRequests.delete(requestId);
    }
  }
}

async function replyToSession(ctx, session, replyText) {
  if (!session || !replyText) return;

  const messageId = session.messageId || "";
  if (messageId) {
    try {
      await session.send(`${h.quote(messageId)}${replyText}`);
      return;
    } catch (err) {
      ctx.logger.warn(
        `[AutoCMEX] Quote reply failed, fallback to normal reply: ${err.message}`,
      );
    }
  }

  await session.send(replyText);
}

/**
 * 通过收到请求的同一条连接回传事件。
 */
function sendEvent(send, eventName, data) {
  if (typeof send !== "function") return;
  send({
    id: generateId(),
    type: "event",
    timestamp: Date.now(),
    payload: { event: eventName, data },
  });
}

/**
 * 判断本地文件存在且非空（图片经适配器上传时会静默丢图，必须在发送前自检）。
 */
function isUsableFile(filePath) {
  try {
    return fs.statSync(filePath).isFile() && fs.statSync(filePath).size > 0;
  } catch (err) {
    return false;
  }
}

/**
 * 本地绝对路径转 file:// URL（OneBot 的 image.file 支持 file:// 形式）。
 */
function toFileUrl(filePath) {
  const normalized = path.resolve(filePath).replace(/\\/g, "/");
  return `file:///${encodeURI(normalized).replace(/^%2F/, "")}`;
}

/**
 * 选择用于主动发送的机器人：优先在线实例，其次任意实例。
 */
function pickBot(ctx) {
  const bots = ctx.bots || [];
  return bots.find((bot) => bot.status === 1) || bots[0] || null;
}

/**
 * 统一次版本的适配器返回形状：Satori 风格（`koishi-plugin-adapter-onebot` 6.x 等）返回
 * `{ data: [...] }`，更旧的直接返回数组；其它形状（null、字符串、对象）一律当空列表。
 */
function normalizeGroupList(value) {
  if (Array.isArray(value)) return value;
  if (value && Array.isArray(value.data)) return value.data;
  return [];
}

/**
 * 取单个机器人可见的群：先试公开 API（新版返回 `{ data }`、旧版返回数组），为空再退回内部 API
 * （OneBot 群聊只有这条路径能拿到群）。
 *
 * 两次调用各自兜住异常：QQ 频道机器人的 `getGuildList` 必抛（它走的是频道接口），
 * 不能因此放弃它下面同样可用的 `internal.getGroupList`，更不能让一个坏机器人拖垮整次请求。
 */
async function fetchBotGroups(ctx, bot) {
  try {
    if (typeof bot.getGuildList === "function") {
      const list = normalizeGroupList(await bot.getGuildList());
      if (list.length > 0) return list;
    }
  } catch (err) {
    ctx.logger.warn(
      `[AutoCMEX] Failed to fetch group list (getGuildList): ${err.message}`,
    );
  }

  try {
    if (bot.internal && typeof bot.internal.getGroupList === "function") {
      return normalizeGroupList(await bot.internal.getGroupList());
    }
  } catch (err) {
    ctx.logger.warn(
      `[AutoCMEX] Failed to fetch group list (getGroupList): ${err.message}`,
    );
  }

  return [];
}

/**
 * 拉取所有机器人可见的群，按 channelId 去重。
 */
async function collectGroups(ctx) {
  const groups = [];
  const seen = new Set();

  for (const bot of ctx.bots || []) {
    const list = await fetchBotGroups(ctx, bot);

    for (const item of list) {
      const channelId = String(
        item.id || item.group_id || item.channelId || "",
      );
      if (!channelId || seen.has(channelId)) continue;

      seen.add(channelId);
      groups.push({
        channelId,
        guildId: String(item.guildId || item.guild_id || ""),
        name: String(item.name || item.group_name || ""),
      });
    }
  }

  return groups;
}

/**
 * 处理群列表查询（info_group_list_request → info_group_list_result）。
 */
async function handleGroupListRequest(ctx, send) {
  const groups = await collectGroups(ctx);
  ctx.logger.info(`[AutoCMEX] Group list request: ${groups.length} group(s)`);

  sendEvent(send, "info_group_list_result", {
    groups,
    count: groups.length,
  });
}

/**
 * 处理主动发布请求（info_publish_forward → info_publish_result）。
 *
 * 关键约束：合并转发整条消息只能由 node 元素组成；图片经适配器上传失败只会记 warn，
 * 因此这里对每个附件做本地可读性自检，任何一张不可读都整条拒发并回报具体序号。
 */
async function handlePublishForward(ctx, payload, send) {
  const requestId = payload?.requestId || "";
  const channelId = String(payload?.channelId || "");
  const kind = payload?.kind || "";
  const nodes = Array.isArray(payload?.nodes) ? payload.nodes : [];

  const report = (success, message, failedNodes) => {
    sendEvent(send, "info_publish_result", {
      requestId,
      channelId,
      kind,
      success,
      message: message || "",
      failedNodes: failedNodes || [],
    });
  };

  if (!requestId || !channelId) {
    report(false, "发布请求缺少 requestId 或 channelId。");
    return;
  }

  if (nodes.length === 0) {
    report(false, "发布请求没有可发送的内容。");
    return;
  }

  const failedNodes = [];
  for (const node of nodes) {
    if (!node || !node.imagePath) continue;
    if (!isUsableFile(node.imagePath)) {
      failedNodes.push({
        index: node.index || 0,
        title: node.title || node.text || "",
        reason: `图片不存在或为空：${node.imagePath}`,
      });
    }
  }

  if (failedNodes.length > 0) {
    report(
      false,
      `预检失败：${failedNodes.length} 个附件的图片不可读，未发送。`,
      failedNodes,
    );
    return;
  }

  const bot = pickBot(ctx);
  if (!bot) {
    report(false, "Koishi 当前没有可用的机器人实例。");
    return;
  }

  const forwardNodes = nodes.map((node) => {
    const title = node?.text || node?.title || "";
    const content = [];
    if (title) content.push({ type: "text", data: { text: `${title}\n` } });
    if (node?.imagePath)
      content.push({
        type: "image",
        data: { file: toFileUrl(node.imagePath) },
      });

    return {
      type: "node",
      data: {
        user_id: FORWARD_UIN,
        nickname: FORWARD_NICKNAME,
        content,
      },
    };
  });

  const numericChannelId = Number(channelId);
  if (!Number.isFinite(numericChannelId)) {
    report(false, `无法解析群 ID：${channelId}。`);
    return;
  }

  const internal = bot.internal || {};
  if (typeof internal.sendGroupForwardMsg !== "function") {
    report(false, "当前适配器不支持 sendGroupForwardMsg，无法发送合并转发。");
    return;
  }

  try {
    await internal.sendGroupForwardMsg(numericChannelId, forwardNodes);
    ctx.logger.info(
      `[AutoCMEX] Published ${forwardNodes.length} node(s) to ${channelId} (kind=${kind}, requestId=${requestId})`,
    );
    report(true, "");
  } catch (err) {
    // 兼容不同 OneBot 实现的字段命名差异后重试一次
    try {
      const legacyNodes = forwardNodes.map((node) => ({
        type: "node",
        data: {
          name: node.data.nickname,
          uin: node.data.user_id,
          content: node.data.content,
        },
      }));
      await internal.sendGroupForwardMsg(numericChannelId, legacyNodes);
      ctx.logger.info(
        `[AutoCMEX] Published to ${channelId} with legacy node fields.`,
      );
      report(true, "");
    } catch (retryErr) {
      ctx.logger.warn(`[AutoCMEX] Publish failed: ${retryErr.message}`);
      report(false, `发送失败：${retryErr.message}`);
    }
  }
}

/**
 * 插件配置项
 */
module.exports.Config = Schema.object({
  mode: Schema.string()
    .default("client")
    .description(
      "运行模式：client（连接 AutoCMEX）/ server（等待 AutoCMEX 连接）",
    ),
  host: Schema.string()
    .default(DEFAULT_HOST)
    .description("Client 模式：AutoCMEX 地址"),
  port: Schema.number()
    .default(DEFAULT_PORT)
    .description(
      "Client 模式：AutoCMEX 端口（Server 模式使用 Koishi 自身端口）",
    ),
  token: Schema.string().default("").description("鉴权 Token（留空不启用）"),
}).description("AutoCMEX 配置");

/**
 * Koishi 插件入口
 * 名称必须等于包短名（koishi-plugin-adapter-autocmex → adapter-autocmex）：
 * 控制台按短名索引插件，这里写别的名字会出现「列表一套名、配置页另一套名」
 */
module.exports.name = "adapter-autocmex";

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
    rememberRequest(ctx, message.id, session);

    if (mode === "server") {
      if (autoCmexClient && autoCmexClient.readyState === WebSocket.OPEN) {
        autoCmexClient.send(JSON.stringify(message));
      } else {
        messageQueue.push(message);
        ctx.logger.info(
          `[AutoCMEX] Queued message ${message.id} (queue=${messageQueue.length})`,
        );
        if (messageQueue.length > 1000) {
          const dropped = messageQueue.shift();
          ctx.logger.warn(
            `[AutoCMEX] Queue full, dropped oldest message ${dropped?.id}`,
          );
        }
      }
    } else {
      if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
      } else {
        messageQueue.push(message);
        ctx.logger.info(
          `[AutoCMEX] Queued message ${message.id} (queue=${messageQueue.length})`,
        );
        if (messageQueue.length > 1000) {
          const dropped = messageQueue.shift();
          ctx.logger.warn(
            `[AutoCMEX] Queue full, dropped oldest message ${dropped?.id}`,
          );
        }
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
      const queuedCount = messageQueue.length;
      if (queuedCount > 0) {
        ctx.logger.info(`[AutoCMEX] Flushing ${queuedCount} queued messages`);
      }
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
          ws.send(
            JSON.stringify({
              id: generateId(),
              type: "command",
              timestamp: Date.now(),
              payload: { action: "ping" },
            }),
          );
        }
      }, HEARTBEAT_INTERVAL);
    });

    ws.on("message", (data) =>
      handleMessage(ctx, data, (message) => {
        if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(message));
      }),
    );

    ws.on("close", () => {
      ctx.logger.warn("[AutoCMEX] Disconnected, reconnecting...");
      if (heartbeatTimer) {
        clearInterval(heartbeatTimer);
        heartbeatTimer = null;
      }
      if (!reconnectTimer) {
        reconnectTimer = setInterval(connect, RECONNECT_INTERVAL);
      }
    });

    ws.on("error", (err) =>
      ctx.logger.warn(`[AutoCMEX] Error: ${err.message}`),
    );
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
          `origin=${info.origin}, secure=${info.secure}`,
      );
      cb(true);
    },
  });

  ctx.logger.info(
    server
      ? `[AutoCMEX] Server mode: attached to Koishi HTTP server`
      : `[AutoCMEX] Server mode: listening on port ${wsPort}`,
  );

  wss.on("error", (err) => {
    ctx.logger.error(`[AutoCMEX] Server error: ${err.message}`);
  });

  // 诊断：记录所有到达 HTTP 服务器的请求
  wss.on("headers", (headers, req) => {
    ctx.logger.info(
      `[AutoCMEX] HTTP request: ${req.method} ${req.url} from ${req.socket.remoteAddress}`,
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
      `[AutoCMEX] AutoCMEX client connected from ${req.socket.remoteAddress}`,
    );

    // 发送缓存消息
    const queuedCount = messageQueue.length;
    if (queuedCount > 0) {
      ctx.logger.info(`[AutoCMEX] Flushing ${queuedCount} queued messages`);
    }
    while (messageQueue.length > 0) {
      if (client.readyState === WebSocket.OPEN) {
        client.send(JSON.stringify(messageQueue.shift()));
      }
    }

    client.on("message", (data) =>
      handleMessage(ctx, data, (message) => {
        if (client.readyState === WebSocket.OPEN)
          client.send(JSON.stringify(message));
      }),
    );

    client.on("close", (code, reason) => {
      ctx.logger.warn(
        `[AutoCMEX] Client disconnected: code=${code}, reason=${reason?.toString() || "none"}`,
      );
      if (autoCmexClient === client) autoCmexClient = null;
    });

    client.on("error", (err) =>
      ctx.logger.warn(`[AutoCMEX] Client error: ${err.message}`),
    );
  });
}

/**
 * 处理收到的消息
 */
async function handleMessage(ctx, data, send) {
  try {
    const msg = JSON.parse(data.toString());
    switch (msg.type) {
      case "ack":
        ctx.logger.debug(
          `[AutoCMEX] ACK: id=${msg.payload?.originalId}, status=${msg.payload?.status}`,
        );
        break;
      case "event":
        if (msg.payload?.event === "guess_result") {
          const requestId = msg.payload?.data?.requestId;
          const replyText = msg.payload?.data?.replyText || "";
          const pending = requestId ? pendingRequests.get(requestId) : null;

          if (!requestId) {
            ctx.logger.warn("[AutoCMEX] guess_result missing requestId");
            break;
          }

          if (!pending || !pending.session) {
            ctx.logger.warn(
              `[AutoCMEX] No pending session for request ${requestId}`,
            );
            pendingRequests.delete(requestId);
            break;
          }

          if (!replyText) {
            ctx.logger.info(
              `[AutoCMEX] guess_result for ${requestId} contains empty replyText, skipped`,
            );
            pendingRequests.delete(requestId);
            break;
          }

          await replyToSession(ctx, pending.session, replyText);
          ctx.logger.info(`[AutoCMEX] Replied to request ${requestId}`);
          pendingRequests.delete(requestId);
          break;
        }

        if (msg.payload?.event === "info_group_list_request") {
          await handleGroupListRequest(ctx, send);
          break;
        }

        if (msg.payload?.event === "info_publish_forward") {
          await handlePublishForward(ctx, msg.payload?.data || {}, send);
          break;
        }

        ctx.logger.info(`[AutoCMEX] Event: ${msg.payload?.event}`);
        break;
      case "error":
        ctx.logger.warn(
          `[AutoCMEX] Error: [${msg.payload?.code}] ${msg.payload?.message}`,
        );
        break;
      default:
        ctx.logger.debug(`[AutoCMEX] Unknown type: ${msg.type}`);
    }
  } catch (e) {
    ctx.logger.warn(`[AutoCMEX] Parse error: ${e.message}`);
  }
}
