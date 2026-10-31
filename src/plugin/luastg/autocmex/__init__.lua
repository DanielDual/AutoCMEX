--- AutoCMEX 符卡录制插件（游戏侧）入口。
---
--- 只有 CMEX 通过启动参数带上 <c>setting.autocmex_job</c> 时才工作；正常游玩本插件不产生任何副作用，
--- 因此可以常驻 <c>game/plugins/autocmex/</c> 且默认启用。
---
--- 真正的工作在 <c>afterTHlib</c> 事件里开始（`THlib.lua:6`）：此前 `Include 'THlib/THlib.lua'` 已完成，
--- 于是 <c>THlib/ext/ext.lua</c> 里的全局 <c>DoFrame</c> 与录制器的 `recorder:init()` 都已就绪，
--- 插件据此包装 <c>DoFrame</c> 取得逐帧推进；mod 的 <c>_editor_class</c> 稍后由 `_editor_output.lua` 产出，
--- 由帧内逻辑等待就绪（不依赖 mod 加载时机）。

local PLUGIN_DIR = "plugins/autocmex/"

--- 依次尝试按固定目录 dofile 与 require（插件目录由 Lplugin 加入搜索路径）。
---@param name string 模块名（不含扩展名）
---@return table
local function load_module(name)
    local ok, mod = pcall(dofile, PLUGIN_DIR .. name .. ".lua")
    if ok and type(mod) == "table" then
        return mod
    end
    local ok_require, required = pcall(require, name)
    if ok_require and type(required) == "table" then
        return required
    end
    error(string.format("autocmex: load module '%s' failed: dofile(%s) / require(%s)",
        name, tostring(mod), tostring(required)), 0)
end

--- 模块都加载不出来时的兜底：错误只写文件与标准输出，绝不打断游戏启动。
---@param message string
local function write_fallback_error(message)
    print("[autocmex] " .. tostring(message))
    local fh = io.open("autocmex/last_error.txt", "w")
    if fh then
        fh:write(tostring(message), "\n")
        fh:close()
    end
end

local job_path = setting and setting.autocmex_job
if type(job_path) ~= "string" or job_path == "" then
    return
end

lstg.plugin.RegisterEvent("afterTHlib", "autocmex", 100, function()
    local ok, message = pcall(function()
        load_module("record").run(job_path)
    end)
    if not ok then
        write_fallback_error(message)
    end
end)
