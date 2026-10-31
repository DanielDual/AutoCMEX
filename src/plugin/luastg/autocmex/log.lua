--- 插件侧日志：同时输出到引擎控制台与 <c>autocmex/logs/{job_id}.log</c>。

local log = {}

local log_path = nil

---@param path string|nil 相对 game/ 的日志文件路径
function log.set_path(path)
    log_path = path
end

---@param fmt string
function log.write(fmt, ...)
    local ok, text = pcall(string.format, fmt, ...)
    if not ok then
        text = tostring(fmt)
    end
    local line = string.format("[%s] %s", os.date("%H:%M:%S"), text)
    print("[autocmex] " .. line)
    if log_path then
        local fh = io.open(log_path, "a")
        if fh then
            fh:write(line, "\n")
            fh:close()
        end
    end
end

return log
