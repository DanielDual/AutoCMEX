--- 任务（`autocmex/jobs/{jobId}.json`）读取校验与结果（`autocmex/results/{jobId}.json`）写出。
---
--- 字段契约见 `.codebuddy/plans/recording-module-solution.md` §7.2：
---   job:    `job_id` / `phase`(`enumerate`|`record`) / `result_path` / `log_path` / `boss_class`(可空=自动定位) /
---           `absolute_index` / `interval` / `max_frame` / `include_previous`
---   result: `job_id` / `status`(`ok`|`error`) / `error` / `boss_name` / `boss_class` / `cards[]` /
---           `task_name` / `gif_path` / `frames` / `interval` / `complete` / `success`

local job = {}

--- 读整个文件；不存在或读取失败返回 nil。
---@param path string
---@return string|nil
local function read_all(path)
    local fh = io.open(path, "rb")
    if not fh then
        return nil
    end
    local content = fh:read("*a")
    fh:close()
    return content
end

---@param v any
---@return boolean
local function is_non_empty_string(v)
    return type(v) == "string" and v ~= ""
end

--- 读取并校验 job.json。失败返回 `nil, 原因`（不抛错，调用方决定如何兜底）。
---@param path string
---@return table|nil, string|nil
function job.load(path)
    local text = read_all(path)
    if not text then
        return nil, "job file not readable: " .. tostring(path)
    end

    local ok, data = pcall(cjson.decode, text)
    if not ok or type(data) ~= "table" then
        return nil, "job json decode failed: " .. tostring(data)
    end
    if not is_non_empty_string(data.job_id) then
        return nil, "job_id missing"
    end
    if data.phase ~= "enumerate" and data.phase ~= "record" then
        return nil, "unknown phase: " .. tostring(data.phase)
    end
    if not is_non_empty_string(data.result_path) then
        return nil, "result_path missing"
    end
    if data.phase == "record" then
        if type(data.absolute_index) ~= "number" or data.absolute_index < 1 then
            return nil, "absolute_index invalid: " .. tostring(data.absolute_index)
        end
    end
    return data
end

--- 写出结果文件：先写 `<path>.tmp` 再改名，避免 CMEX 读到写了一半的 JSON。
--- 若引擎禁用 `os.rename`，退化为直接覆盖写（结果仍可用，只是少了原子性）。
---@param spec table job 表
---@param result table
---@return boolean, string|nil
function job.write_result(spec, result)
    result.job_id = spec.job_id

    local ok, text = pcall(cjson.encode, result)
    if not ok or type(text) ~= "string" then
        return false, "encode result failed: " .. tostring(text)
    end

    local path = spec.result_path
    local tmp = path .. ".tmp"
    local fh = io.open(tmp, "wb")
    if not fh then
        return false, "cannot write " .. tmp
    end
    fh:write(text)
    fh:close()

    if type(os.rename) == "function" then
        os.remove(path)
        local renamed = os.rename(tmp, path)
        if renamed then
            return true
        end
    end

    -- 退化路径：直接写最终文件
    local direct = io.open(path, "wb")
    if not direct then
        return false, "cannot write " .. path
    end
    direct:write(text)
    direct:close()
    os.remove(tmp)
    return true
end

return job
