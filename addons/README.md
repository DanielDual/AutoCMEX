# Addons

## GoPeak

### Source

- Upstream: <https://github.com/HaD0Yun/Doyunha-Gopeak>
- Version: [4d83833](https://github.com/HaD0Yun/Doyunha-Gopeak/commit/4d83833c0cd663f1b2e350cde3f0f13e0ad586f2)

### Vendored directories

- `addons/auto_reload`
- `addons/godot_mcp_editor`
- `addons/godot_mcp_runtime`

### 本地启用方式

这些是开发期工具，**不随发行版分发**：

- 导出过滤器 `export_presets.cfg` 的 `exclude_filter` 已排除 `addons/godot_mcp_*` 与 `addons/auto_reload/*`。
- `godot_mcp_runtime` 的 autoload 声明放在仓库根的 `override.cfg`（Godot 原生覆盖层），
  而不是 `project.godot`。原因：`project.godot` 的设置会被打进导出构件，`override.cfg` 不会。
  所以在编辑器里点「运行」时 autoload 生效，导出物里则没有它。
- 该 autoload 默认不监听端口；只有 `mcp/runtime_enabled` 为 true 才启动 TCP 服务。
  开发机由 `override.cfg` 打开，发布构建里该设置不存在。
- 该服务没有鉴权，因此只绑定回环地址 `127.0.0.1`，不对局域网开放。
- 编辑器插件（`editor_plugins`）仍在 `project.godot` 中启用：编辑器进程不读取 `override.cfg`。
  其中 `godot_mcp_runtime` **不再启用**：它的编辑器插件原本会在编辑器启动时把
  `MCPRuntime` autoload 注入 `project.godot`，导出时被一起烘进发布构件。现在该 autoload
  只由 `override.cfg` 在运行时提供。

### License

[MIT](https://github.com/HaD0Yun/Doyunha-Gopeak/blob/main/LICENSE)
