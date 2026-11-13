# AutoCMEX — 第三方组件与许可

AutoCMEX 的「混淆 Lua 脚本（实验性）」功能随包分发以下第三方组件。本文件记录版本、来源、SHA256、许可义务与必须的署名，供分发与维护核对；README 的「第三方组件」节、本文件与 `src/tools/lua-obfuscator/` 内的许可原文互为补充。

## Prometheus（Lua 混淆器）

| 项          | 值                                                                                                                               |
| ----------- | -------------------------------------------------------------------------------------------------------------------------------- |
| 组件        | Prometheus（纯 Lua 5.1 源码混淆器）                                                                                              |
| 版本        | v0.3.1（GitHub tag）                                                                                                             |
| 来源        | https://github.com/prometheus-lua/Prometheus ，归档 `https://codeload.github.com/prometheus-lua/Prometheus/zip/refs/tags/v0.3.1` |
| 归档 SHA256 | `88c344a63a9c16485ca9c96429452c6cb080a3e07022778fc5c3575678b8eaee`                                                               |
| 用途        | 对 mod 包内的 Lua 脚本做源码级混淆                                                                                               |
| 仓库位置    | `src/tools/lua-obfuscator/prometheus/`（`cli.lua` + `src/` + `LICENSE`）                                                         |

### 许可要点

Copyright (c) 2025 Elias Oelschner。修改版 MIT，与标准 MIT 的差异集中在署名与再分发义务：

- §1 Retained Notice：源码或目标码分发须附版权声明与本许可。
- §2 Attribution Requirement：任何使用（含修改、混淆、内嵌、打包）本软件的产品或服务，必须显著署名，精确文本为 `Based on Prometheus by Elias Oelschner, https://github.com/prometheus-lua/Prometheus`；位置至少一处（产品界面关于/致谢、产品网站、CLI 的 `--version`/`--help`、文档或 README）。
- §3 Derivative Statement：公开分发的衍生、修改或复刻版本须在 README 声明基于 Prometheus 并含上述精确文本。
- §4 Generated Output Exception：混淆产物本身无需携带版权或许可文本；使用它的产品仍须履行 §2 署名。
- §5 不授予商标许可；§7 允许再许可但须传递本许可条款；§8、§9 免责；§10 法域由许可人选择。

AutoCMEX 的合规落点：README「第三方组件」节的精确署名（满足 §2 d），本文件与工具目录内保留的许可原文（满足 §1），以及在 README 与文档中声明混淆功能基于 Prometheus；本项目未修改 Prometheus 源码（随包内容与 v0.3.1 原始源码逐文件一致）。

### 许可全文（逐字转载）

以下为 `src/tools/lua-obfuscator/prometheus/LICENSE` 原文。

```text
PROMETHEUS LICENSE
Copyright (c) 2025 Elias Oelschner
https://github.com/prometheus-lua/Prometheus

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

  1. Retained Notice.
     The above copyright notice and this permission notice shall be included
     in all copies or substantial portions of the Software that are distributed
     in source or object form, unless explicitly stated otherwise in this
     license.

  2. Attribution Requirement.
     Any Product or Service that (a) includes, links to, wraps, depends on,
     is derived from, uses, or otherwise incorporates the Software (including
     via modification, obfuscation, binary linking, packaging, or embedding),
     whether distributed, provided for use, or made available over a network
     (collectively, "Using Products/Services"), must provide clear,
     reasonably prominent attribution to Prometheus as follows:

       "Based on Prometheus by Elias Oelschner, https://github.com/prometheus-lua/Prometheus"

     This attribution must appear in at least one of the following locations,
     as applicable and reasonably prominent for the type of Using Product/Service:

       a) In-user interface: a visible "About", "Credits", or equivalent screen
          within the product's primary user interface (graphical or web UI).
       b) On the product web site: a visible place such as a footer, About page,
          or documentation landing page that is accessible to the product's users.
       c) In the tool itself: the program's --version, --help output, or similar
          command-line/displayed metadata for CLI tools.
       d) Documentation: in user or developer documentation, README, or product
          manual that accompanies the product.

     For Software provided as a Network Service or SaaS (i.e., made available
     for use over a network rather than distributed as a copy), the attribution
     must be visible on the public-facing web UI (for example on the login
     screen, footer, About page, or a help/credits page accessible without an
     account), and must include the exact phrase above and the URL.

  3. Derivative Statement.
     Any derivative work, modification, fork, or reimplementation of the
     Software that is publicly distributed, publicly hosted, or provided as a
     service must include a prominent statement in its README (or equivalent
     public-facing documentation) that it is based on Prometheus and include the
     exact attribution text in section 2.

  4. Generated Output Exception.
     Files or artifacts produced by running or using the Software (for example,
     obfuscated output files, compiled artifacts, transformed source files,
     or other generated outputs) are NOT required by this license to carry
     copyright notices or license text. The attribution obligations in section 2
     continue to apply to the Using Product/Service that produces, distributes,
     or serves those outputs.

  5. No Trademark License.
     This license does not grant any rights to use the trade names, trademarks,
     service marks, or product names of the Licensor except as required for the
     textual attribution specified in section 2.

  6. Compliance and Termination.
     Any failure to comply with the obligations in sections 2 or 3 will
     terminate the rights granted under this license for the non-compliant
     party with respect to the Software. Rights may be reinstated if the
     non-compliant party cures the breach within thirty (30) days after
     receiving written notice from the Licensor and provides reasonable proof
     of cure.

  7. Sublicensing.
     You may sublicense the Software, provided that any sublicensee is bound
     by the terms of this license and the attribution obligations described
     herein.

  8. Warranty Disclaimer.
     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
     OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
     MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
     IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
     CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
     TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
     SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

  9. Limitation of Liability.
     IN NO EVENT WILL THE COPYRIGHT HOLDER BE LIABLE FOR ANY INDIRECT,
     INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES ARISING IN ANY
     WAY OUT OF THE USE OF THE SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY
     OF SUCH DAMAGE.

  10. Governing Law.
     This license shall be governed by and construed in accordance with the
     laws of the jurisdiction chosen by the Licensor. This clause does not
     limit any mandatory consumer protections that may apply in certain
     jurisdictions.

By exercising any rights granted to you under this License, you accept and
agree to be bound by the terms and conditions of this License.
```

## Lua 5.1.5（LuaBinaries Windows 64 位最小运行集）

| 项          | 值                                                                                                                                                                                                        |
| ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 组件        | Lua 5.1.5 解释器（LuaBinaries 构建）                                                                                                                                                                      |
| 版本        | 5.1.5                                                                                                                                                                                                     |
| 来源        | `https://sourceforge.net/projects/luabinaries/files/5.1.5/Tools%20Executables/lua-5.1.5_Win64_bin.zip/download`                                                                                           |
| 归档 SHA256 | `5f34cf7d40a20a587ea351482a4207d93b92ef6f1983e910a13338253819fe93`                                                                                                                                        |
| 运行集文件  | `lua5.1.exe`（17,920 B，SHA256 `c9bf063327f6a719aa2d2c25a13a6fb006eba3f23b53bcd6a629dc04507c18b6`）、`lua5.1.dll`（314,880 B，SHA256 `0d620e2ac810a76ce6d43cc8efba7b2329611be37a8b16d55bb58e09b059fc6c`） |
| 用途        | 运行 Prometheus 混淆器（Lua 解释器）                                                                                                                                                                      |
| 仓库位置    | `src/tools/lua-obfuscator/runtime/`（`lua5.1.exe`、`lua5.1.dll`、`COPYRIGHT`）                                                                                                                            |

- 许可：MIT（与 Lua 5.1 相同）。LuaBinaries 声明「LuaBinaries is free software and uses the same license as Lua 5.1.」。
- 官方 Lua 5.1.5 源码归档（许可取证用）：`https://www.lua.org/ftp/lua-5.1.5.tar.gz`，SHA256 `2640fc56a795f29d28ef15e13c34a47e223960b0240e8cb0a82d9b0738695333`。
- 分发义务：须保留版权声明与许可（MIT 条件），本仓库保留 `runtime/COPYRIGHT`。

### 许可全文（逐字转载）

以下为 `src/tools/lua-obfuscator/runtime/COPYRIGHT` 原文。

```text
Lua License
-----------

Lua is licensed under the terms of the MIT license reproduced below.
This means that Lua is free software and can be used for both academic
and commercial purposes at absolutely no cost.

For details and rationale, see http://www.lua.org/license.html .

===============================================================================

Copyright (C) 1994-2012 Lua.org, PUC-Rio.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

===============================================================================

(end of COPYRIGHT)
```

## 混淆产物

按 Prometheus 许可 §4，AutoCMEX 导出的混淆 Lua 脚本本身无需附带版权或许可文本；但 AutoCMEX 作为生成并分发这些产物的产品，仍须满足 §2 的署名要求（见上）。

## 文件位置与维护

- `src/tools/lua-obfuscator/manifest.json`：只登记运行期必需文件（`cli.lua` 与 `src/`、`configs/`、解释器与 `runtime/COPYRIGHT`、`prometheus/LICENSE`）的逐文件 SHA256 与 `bundleHash`；README 与 manifest 自身不参与哈希，也不随包释放。
- `src/tools/lua-obfuscator/configs/nocff_seed42.lua`：本项目固定的混淆配置（NoCff + `Seed=42`）。
- `src/tools/lua-obfuscator/prometheus/LICENSE`：Prometheus 许可原文。
- `src/tools/lua-obfuscator/runtime/COPYRIGHT`：Lua 版权与 MIT 许可原文。
- `src/tools/lua-obfuscator/README.md`：工具集的来源、版本、许可与调用说明（非运行期文档，不参与哈希与释放）。

升级任一组件时，需同步更新工具目录、`manifest.json`（逐文件 SHA256 与 `bundleHash`）与代码内 `ExpectedBundleHash` 锚值，以及本文件与 README 中的版本与来源，并重新核对许可原文；重算值、清单声明值、代码锚三者不一致时运行期 fail-closed。当前 `bundleHash` 为 `6c5868a0f594881be6757ad8f70c45f455a658b452f791fae5ad1639ad6e5d0b`。
