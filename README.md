<div align="center">
  <img src="icon.png" width="120" alt="AutoCMEX">
  <h1>AutoCMEX</h1>
  <p>CMEX 活动组织者桌面工具</p>
</div>

AutoCMEX 把一场 CMEX 活动从收稿到公布串起来：合并参与者提交的 LuaSTG 工程，在群聊里托管猜创作者，再把符卡 GIF 与统计表发到群里。

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[功能](#功能) · [运行](#运行) · [构建](#构建) · [配合-koishi](#配合-koishi) · [目录结构](#目录结构) · [文档](#文档) · [许可](#许可)

## 功能

- **整合**：读取创作者的 `.lstgproj` / `.lstges`，按对应表把符卡注入统一模板，导出合并工程与三列对应表 CSV。
- **猜测**：接收群聊里的猜测消息，按严格、别名或 AI 模糊化三种口径匹配创作者，回帖并记录丢包重试。
- **信息**：四栏展示符卡 GIF 集、符卡猜测情况表、创作者剩余表与活动规则；两张表按内容自适应出图直接发群，猜测表变化可自动推送。
- **录制**：驱动 LuaSTG 引擎批量并行录制符卡 GIF，产物带 `manifest.json`，可直接作为 GIF 集导入。
- **设置与日志**：AI 模型、群聊连接、录制参数；运行日志可实时查看并滚动落盘。

## 运行

要求 Windows、[Godot 4.7.1（.NET）](https://godotengine.org/) 与 .NET 8 SDK。

用 Godot 打开 `project.godot` 后运行，或在项目目录执行 `godot --path .`。

程序数据保存在用户数据目录的 JSON 文件中，AI Key 用 AES 加密后落盘。

仓库不包含 `addons/` 下的编辑器内 MCP 工具（本地开发用，未跟踪）。首次打开时若提示插件或 autoload 缺失，可忽略，不影响主程序运行。

## 构建

```sh
dotnet build
```

测试在 Godot 进程内跑，需要窗口（不能 `--headless`）：

```sh
godot --run-tests --quit-on-finish
```

覆盖率（需要 `$GODOT`、`coverlet` 与 `reportgenerator`）：

```sh
bash coverage.sh
```

## 配合 Koishi

群聊侧通过 Koishi v4 插件收发消息，插件源码在 `src/plugin/koishi`。

1. 把插件安装到 Koishi 应用的 `external/adapter-autocmex`；
2. 在 Koishi 控制台启用 `adapter-autocmex`；
3. 配一个 OneBot 适配器（如 NapCat）把消息送到 QQ。

AutoCMEX 作为 WebSocket 服务端，默认监听 `5140` 端口；Koishi 侧填同一个地址。

## 目录结构

```
src/
  core/        业务逻辑：merge / guessing / info / recording / ai / storage / websocket
  ui/          各板块界面（一个板块一个 .tscn）
  plugin/      Koishi 插件与 LuaSTG 录制插件
docs/          功能、技术、UI 文档
test/          GoDotTest 测试
```

## 文档

- [功能需求](docs/functional-requirements.md)
- [技术规格](docs/technical-specification.md)
- [UI 设计](docs/ui-design.md)
- [变更日志](CHANGELOG.md)

## 状态

整合、猜测、信息板块已可用；设置板块覆盖 AI、群聊与录制配置；帮助板块还没做。

## 许可

[MIT](LICENSE) © 2026 AutoCMEX
