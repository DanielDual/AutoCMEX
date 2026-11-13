# Lua 混淆工具（随包随附，固定版本）

本目录是 AutoCMEX「混淆 Lua 脚本（实验性）」选项使用的**固定版本**离线工具集：
Prometheus Lua 混淆器（纯 Lua 5.1 源码） + Lua 5.1.5 解释器。运行期由
`PrometheusLuaObfuscator` 从 `res://src/tools/lua-obfuscator/` 读取 `manifest.json`，
校验逐文件 SHA256 后释放到 `%TEMP%/AutoCMEX/tools/<bundle hash>/`，再用绝对路径子进程调用；
不联网下载任何内容。清单只覆盖运行期必需文件，本 README 与 `manifest.json` 自身不参与哈希。

## 布局

```
lua-obfuscator/
  manifest.json                 # 仅运行期文件：逐文件 SHA256 + bundleHash（校验/缓存键）
  configs/nocff_seed42.lua      # 本项目固定配置：NoCff + Seed=42（确定性输出）
  prometheus/                   # Prometheus v0.3.1
    cli.lua                     # 入口（package.path 相对自身定位）
    src/                        # 混淆器实现（106 个 .lua 文件）
    LICENSE                     # Prometheus 许可原文
  runtime/                      # Lua 5.1.5（LuaBinaries Win64 最小运行集）
    lua5.1.exe                  # 17,920 B
    lua5.1.dll                  # 314,880 B（与 exe 同目录，Windows 进程启动时自动加载）
    COPYRIGHT                   # Lua 许可原文（MIT）
```

## 版本与来源

| 组件 | 版本 | 来源 | 归档 SHA256 |
| --- | --- | --- | --- |
| Prometheus | v0.3.1 | https://codeload.github.com/prometheus-lua/Prometheus/zip/refs/tags/v0.3.1 | `88c344a63a9c16485ca9c96429452c6cb080a3e07022778fc5c3575678b8eaee` |
| LuaBinaries (Windows 64 位) | 5.1.5 | https://sourceforge.net/projects/luabinaries/files/5.1.5/Tools%20Executables/lua-5.1.5_Win64_bin.zip/download | `5f34cf7d40a20a587ea351482a4207d93b92ef6f1983e910a13338253819fe93` |
| Lua 官方源码（仅用于许可取证） | 5.1.5 | https://www.lua.org/ftp/lua-5.1.5.tar.gz | `2640fc56a795f29d28ef15e13c34a47e223960b0240e8cb0a82d9b0738695333` |

最小运行集 `lua5.1.exe`（SHA256 `c9bf063327f6a719aa2d2c25a13a6fb006eba3f23b53bcd6a629dc04507c18b6`）+
`lua5.1.dll`（SHA256 `0d620e2ac810a76ce6d43cc8efba7b2329611be37a8b16d55bb58e09b059fc6c`）；
实测不需要 VC80 CRT 目录、`lua51.dll`、`luac` 或 `wlua`。

## 许可

- Prometheus：Copyright (c) 2025 Elias Oelschner，修改版 MIT，全文见 `prometheus/LICENSE`。
  本产品按许可第 2 条署名：

  > Based on Prometheus by Elias Oelschner, https://github.com/prometheus-lua/Prometheus

- Lua 5.1.5：Copyright (C) 1994-2012 Lua.org, PUC-Rio.，MIT，全文见 `runtime/COPYRIGHT`。
  LuaBinaries 声明与 Lua 5.1 同许可。
- 混淆产物本身按 Prometheus 许可第 4 条无需附带版权/许可文本。

## 调用方式

```
runtime/lua5.1.exe prometheus/cli.lua --config configs/nocff_seed42.lua --Lua51 --nocolors --out <out.lua> <in.lua>
```

- Prometheus v0.3.1 **不支持批处理**：一次一个输入文件，上层按文件循环、每文件一个子进程。
- 退出码 0 成功 / 非 0 失败；失败原因在 stderr（如 `Lexing Error`、`Parsing Error`）。
- 默认配置为自定义 NoCff 步骤（EncryptStrings + ConstantArray(Shuffle/Rotate) + WrapInFunction）+
  `Seed = 42`，输出确定可复现；不使用 Weak/Medium（Cff 产物体积/控制流上限风险，见
  `docs/technical-specification.md` 的「整合导出混淆（实验性）」）。

## 维护与升级

- `manifest.json` 只列运行期必需文件：`prometheus/cli.lua`、`prometheus/src/**`、`prometheus/LICENSE`、
  `runtime/lua5.1.exe`、`runtime/lua5.1.dll`、`runtime/COPYRIGHT`、`configs/nocff_seed42.lua`。
  `README.md` 与 `manifest.json` 自身不参与哈希，也不会被释放到临时目录。
- 运行期三重锚定：重算 bundleHash == `manifest.json` 的 `bundleHash` ==
  `PrometheusLuaObfuscator.ExpectedBundleHash`；任一不符即 fail-closed（导出直接报错，不产出半混淆包）。
- 升级/替换任何运行期文件后必须同时做两件事，否则运行期会因锚不一致而拒绝导出：
  1. 重出 `manifest.json`：`files` 按路径排序，逐文件记录 `path`/`size`/`sha256`；
     `bundleHash` = 对每个文件按「`path` + 换行 + 小写 `sha256` + 换行」拼接后取 SHA256；
  2. 同步更新 `src/core/merge/PrometheusLuaObfuscator.cs` 的 `ExpectedBundleHash`。
- 当前锚值：`6c5868a0f594881be6757ad8f70c45f455a658b452f791fae5ad1639ad6e5d0b`（112 个运行期文件）。

## 边界

- 仅支持 Lua 5.1 语法；含 LuaJIT 扩展语法（`goto`/label）的文件无法解析，导出时 fail-closed。
- 混淆提高阅读门槛，不是加密；输出仍是纯 Lua 5.1 源码。
- 大文件体积约 2.3x（实测 384KB → 895KB 可被 Lua 5.1 与 LuaJIT 加载）。
