// AutoCMEX Koishi v4 Plugin
// 将此文件夹复制到 Koishi 的 plugins 目录即可安装
// 功能：将群聊消息通过 WebSocket 转发到 AutoCMEX，并返回处理结果

const WebSocket = require('ws');

const DEFAULT_PORT = 5140;
const RECONNECT_INTERVAL = 5000;

let ws = null;
let messageQueue = [];
let reconnectTimer = null;

/**
 * Koishi 插件入口
 */
module.exports.name = 'auto-cmex';

module.exports.apply = (ctx) => {
  const config = ctx.config.plugins?.['auto-cmex'] || {};
  const port = config.port || DEFAULT_PORT;

  ctx.logger.info(`[AutoCMEX] Connecting to ws://127.0.0.1:${port}`);

  connect(ctx, port);

  // 监听所有群聊消息
  ctx.on('message', (session) => {
    const message = {
      type: 'guess_message',
      payload: {
        text: session.content || '',
        sender: session.username || session.userId || '',
        timestamp: new Date().toISOString()
      }
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
  ctx.command('auto-cmex.status', '查看 AutoCMEX 连接状态')
    .action(() => {
      if (ws && ws.readyState === WebSocket.OPEN) {
        return 'AutoCMEX 已连接';
      }
      return 'AutoCMEX 未连接';
    });
};

/**
 * 连接到 AutoCMEX WebSocket 服务
 */
function connect(ctx, port) {
  if (ws) {
    ws.close();
  }

  ws = new WebSocket(`ws://127.0.0.1:${port}`);

  ws.on('open', () => {
    ctx.logger.info('[AutoCMEX] Connected');

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
  });

  ws.on('message', (data) => {
    try {
      const msg = JSON.parse(data.toString());
      if (msg.type === 'response') {
        // 回应消息由 AutoCMEX 处理，此处仅记录
        ctx.logger.debug(`[AutoCMEX] Response: ${msg.payload?.text}`);
      }
    } catch (e) {
      ctx.logger.warn(`[AutoCMEX] Failed to parse message: ${e.message}`);
    }
  });

  ws.on('close', () => {
    ctx.logger.warn('[AutoCMEX] Disconnected, reconnecting...');
    if (!reconnectTimer) {
      reconnectTimer = setInterval(() => connect(ctx, port), RECONNECT_INTERVAL);
    }
  });

  ws.on('error', (err) => {
    ctx.logger.warn(`[AutoCMEX] Error: ${err.message}`);
  });
}
