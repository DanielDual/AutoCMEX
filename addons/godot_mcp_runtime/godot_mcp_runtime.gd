@tool
extends EditorPlugin

## Godot MCP Runtime Plugin
## Enables real-time communication between a running Godot game and the MCP server.
##
## This plugin provides:
## - WebSocket server for bidirectional communication
## - Scene tree inspection
## - Property modification at runtime
## - Method calling at runtime
## - Performance metrics reporting
## - Signal watching

const DEFAULT_PORT = 7777
const PROTOCOL_VERSION = "1.0"

var _server: TCPServer
var _clients: Array[StreamPeerTCP] = []
var _port: int = DEFAULT_PORT
var _enabled: bool = false


# 注意：这里**不再**调用 add_autoload_singleton()。
# 旧行为会在编辑器启动时把 MCPRuntime 注入 ProjectSettings，而导出会连同注入的
# autoload 一起烘进发布构件（开发机配置因此泄露到发行版里）。现在 autoload 由仓库根
# 的 override.cfg 提供——它只在非编辑器运行时生效，且不会被导出。
# 本文件因此不再注册任何东西；保留文件是为了与上游目录结构一致。


func _enter_tree() -> void:
	print("[MCP Runtime] Plugin loaded (autoload comes from override.cfg, not registered here)")


func _exit_tree() -> void:
	print("[MCP Runtime] Plugin unloaded")
