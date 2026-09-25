--- 录制流程：跳卡 → 起录 → 收尾 → 写结果 → 退出游戏。
---
--- **帧钩子**：本机引擎（0.8.22）没有 `lstg.globalEventDispatcher`，录制器是靠 `THlib/ext/ext.lua` 手工接线抓帧的，
--- 所以插件也用同一处——在 `afterTHlib`（`ext.lua` 已加载）之后包装全局 `DoFrame`：引擎每帧按名字取 `DoFrame`，
--- 包装即生效（`ext.lua:209` 覆盖 `core.lua:36` 就是同一机制）。
---
--- **判定时机**：boss 卡推进发生在 `DoFrame` 内部（`ObjFrame` → `system:frame` → `doCard` 设 `b.current_card`），
--- 所以必须在原 `DoFrame` 跑完之后判定；而 `GameScene:onRender` 开头就是 `start_capture`，
--- 同一帧的渲染即可抓到卡首帧（`include_previous=false` 时前一阶段是 60 帧入场移动，不是台词）。

local PLUGIN_DIR = "plugins/autocmex/"

--- 同目录模块优先按固定目录 `dofile`（不受其它插件同名模块干扰），失败再退回 `require`。
---@param name string
---@return table
local function load_sibling(name)
    local ok, mod = pcall(dofile, PLUGIN_DIR .. name .. ".lua")
    if ok and type(mod) == "table" then
        return mod
    end
    local ok2, mod2 = pcall(require, name)
    if ok2 and type(mod2) == "table" then
        return mod2
    end
    error(string.format("autocmex: load module '%s' failed: dofile(%s) / require(%s)",
        name, tostring(mod), tostring(mod2)), 0)
end

local log = load_sibling("log")
local jobspec = load_sibling("job")
local cards = load_sibling("cards")

local record = {}

local DEFAULT_INTERVAL = 3
local DEFAULT_MAX_FRAME = 350
local READY_TIMEOUT_FRAMES = 60 * 60      -- 等 mod 产出 `_editor_class`
local START_TIMEOUT_FRAMES = 60 * 60      -- 跳卡后等目标卡开始（含 30 帧舞台初始化与 60 帧入场移动）
local RECORD_TAIL_FRAMES = 60 * 20        -- 录制收尾余量
local PRACTICE_STAGE = "Spell Practice@Spell Practice"
local OUTPUT_DIR = "danmaku_recorder/output/"

local ctx = nil
local self_wired = false

--------------------------------------------------------------------------
--- 收尾

local function quit_game()
    pcall(function()
        stage.QuitGame()
    end)
end

--- 写结果文件并退出游戏；进程内只生效一次。
---@param result table
local function finish(result)
    if not ctx or ctx.done then
        return
    end
    ctx.done = true
    local ok, err = jobspec.write_result(ctx.spec, result)
    if ok then
        log.write("result written -> %s", tostring(ctx.spec.result_path))
    else
        log.write("ERROR: write result failed: %s", tostring(err))
    end
    quit_game()
end

---@param message string
local function finish_error(message)
    log.write("ERROR: %s", tostring(message))
    finish({ status = "error", error = tostring(message) })
end

--------------------------------------------------------------------------
--- 录制器

---@return table|nil, string
local function get_recorder()
    local ok, mod = pcall(require, "danmaku_recorder.recorder")
    if ok and type(mod) == "table" then
        return mod
    end
    return nil, tostring(mod)
end

--- 抓帧钩子是否已由 THlib 侧接线（0.9+ 走事件派发；0.8.x 看 ext.lua 补丁）。
---@return boolean
local function capture_hooks_present()
    if lstg.globalEventDispatcher then
        return true
    end
    local fh = io.open("packages/thlib-scripts/THlib/ext/ext.lua", "rb")
    if not fh then
        return false
    end
    local src = fh:read("*a")
    fh:close()
    return src ~= nil and src:find("recorder:start_capture", 1, true) ~= nil
end

--- 兜底自接线：ext.lua 未打补丁时，在 `GameScene:onRender` 前后各挂一次抓帧。
---@param recorder table
---@return boolean
local function ensure_capture_wired(recorder)
    if self_wired or capture_hooks_present() then
        return true
    end
    local ok, SceneManager = pcall(require, "foundation.SceneManager")
    if not ok or type(SceneManager) ~= "table" or type(SceneManager.getCurrent) ~= "function" then
        log.write("WARN: cannot self-wire capture hooks: SceneManager unavailable")
        return false
    end
    local scene = SceneManager.getCurrent()
    if type(scene) ~= "table" or type(scene.onRender) ~= "function" then
        log.write("WARN: cannot self-wire capture hooks: current scene has no onRender")
        return false
    end
    if type(scene.getName) == "function" and scene:getName() ~= "GameScene" then
        log.write("WARN: cannot self-wire capture hooks: current scene is %s", tostring(scene:getName()))
        return false
    end
    local origin = scene.onRender
    scene.onRender = function(self, ...)
        recorder:start_capture()
        origin(self, ...)
        recorder:end_capture()
    end
    self_wired = true
    log.write("capture hooks missing, self-wired GameScene:onRender")
    return true
end

--- 录制器就绪（未初始化且 ext.lua 未接线时自行 init；ffmpeg 路径沿用插件默认值）。
---@param recorder table
---@return boolean, string|nil
local function ensure_recorder_ready(recorder)
    local status = recorder:get_status()
    if status == "initialized" or status == "recording" then
        return true
    end
    log.write("recorder status=%s, calling init()", tostring(status))
    pcall(function()
        recorder:init()
    end)
    status = recorder:get_status()
    if status ~= "initialized" and status ~= "recording" then
        return false, "recorder_not_initialized: " .. tostring(status)
    end
    return true
end

--------------------------------------------------------------------------
--- 结果汇集与起录

--- 汇集录制产物为结果（`end_record` 同步执行：返回时 GIF 已落盘、`last_record_info` 已就绪）。
---@param warning string|nil
local function collect_result(warning)
    local ok, info = pcall(ctx.recorder.get_last_record_info)
    if not ok or type(info) ~= "table" then
        return finish_error("record_info_unavailable: " .. tostring(info))
    end

    local task_name = tostring(info.task_name or "")
    local frames = tonumber(info.frame) or 0
    local success = info.success == true

    local result = {
        status = "ok",
        boss_name = ctx.boss_label,
        boss_class = ctx.boss_class,
        absolute_index = ctx.spec.absolute_index,
        card_name = type(ctx.target_card) == "table" and tostring(ctx.target_card.name) or "",
        task_name = task_name,
        gif_path = OUTPUT_DIR .. task_name .. ".gif",
        frames = frames,
        interval = ctx.interval,
        max_frame = ctx.max_frame,
        complete = frames >= ctx.max_frame,
        success = success,
        size = tonumber(info.size) or 0,
    }
    if warning then
        result.warning = warning
    end
    if not success or frames <= 0 or task_name == "" then
        result.status = "error"
        result.error = "gif_not_produced"
    end

    log.write("record done: task=%s frames=%d success=%s complete=%s size=%s",
        task_name, frames, tostring(success), tostring(result.complete), tostring(result.size))
    finish(result)
end

local function start_record()
    local recorder = ctx.recorder
    ensure_capture_wired(recorder)

    local ok, err = pcall(function()
        recorder:start_record()
    end)
    if not ok then
        return finish_error("start_record_failed: " .. tostring(err))
    end
    local status = recorder:get_status()
    if status ~= "recording" then
        return finish_error("start_record_rejected: status=" .. tostring(status))
    end

    ctx.stage = "recording"
    ctx.record_frames = 0
    log.write("recording started (max_frame=%d interval=%d budget=%d frames)",
        ctx.max_frame, ctx.interval, ctx.record_budget)
end

--------------------------------------------------------------------------
--- 帧内推进

--- 找练习关卡里的 boss 实例（`CardsSystem:init` 把 `cards` 挂在 boss 对象上）。
---@return table|nil
local function find_boss_object()
    for _, obj in ObjList(GROUP_ENEMY) do
        local ok, matched = pcall(function()
            return type(obj.cards) == "table"
        end)
        if ok and matched then
            return obj
        end
    end
    return nil
end

--- 目标卡是否正在演：靠对象同一性判定（练习模式 `b.cards` 只是传入的子集，`card_num` 不是绝对下标）。
---@return boolean
local function target_card_running()
    if not ctx.boss_ref or not IsValid(ctx.boss_ref) then
        return false
    end
    return ctx.boss_ref.current_card == ctx.target_card
end

local function tick_wait_card()
    ctx.stage_frames = ctx.stage_frames + 1
    if ctx.stage_frames > START_TIMEOUT_FRAMES then
        return finish_error("card_start_timeout")
    end

    if not ctx.boss_ref or not IsValid(ctx.boss_ref) then
        ctx.boss_ref = find_boss_object()
        if ctx.boss_ref then
            log.write("practice boss found at frame %d", ctx.stage_frames)
        end
    end
    if not ctx.boss_ref then
        return
    end

    if target_card_running() then
        log.write("target card started at frame %d (card_num=%s)",
            ctx.stage_frames, tostring(ctx.boss_ref.card_num))
        return start_record()
    end

    if ctx.stage_frames % 300 == 0 then
        local current = ctx.boss_ref.current_card
        log.write("waiting target card: frame=%d current=%s card_num=%s",
            ctx.stage_frames, tostring(current and current.name), tostring(ctx.boss_ref.card_num))
    end
end

local function tick_recording()
    local recorder = ctx.recorder
    ctx.record_frames = ctx.record_frames + 1
    local status = recorder:get_status()
    local captured = recorder:get_recorded_frame_count()

    if ctx.record_frames % 120 == 0 then
        log.write("recording: frame=%d status=%s captured=%d", ctx.record_frames, tostring(status), captured)
    end

    if status == "recording" then
        -- 卡演完（boss 对象失效）即显式收尾；练习关卡随后会弹暂停菜单，必须抢在它之前退出
        if not (ctx.boss_ref and IsValid(ctx.boss_ref)) then
            log.write("card finished at record frame %d (captured=%d), end_record", ctx.record_frames, captured)
            pcall(function()
                recorder:end_record()
            end)
            return collect_result(nil)
        end
        if ctx.record_frames > ctx.record_budget then
            log.write("WARN: record budget exceeded (%d frames), force end_record", ctx.record_frames)
            pcall(function()
                recorder:end_record()
            end)
            return collect_result("record_timeout")
        end
        return
    end

    -- 录制器自己收尾（`end_capture` 内 index >= max_frame 时调 end_record），不要再调一次
    log.write("recorder auto-finished: status=%s captured=%d", tostring(status), captured)
    collect_result(nil)
end

--------------------------------------------------------------------------
--- 阶段一：枚举

local function tick_enumerate()
    if type(_editor_class) ~= "table" then
        if ctx.frames > READY_TIMEOUT_FRAMES then
            finish_error("editor_class_timeout")
        end
        return
    end

    local names = cards.list_boss_classes()
    if #names == 0 then
        return finish_error("no_boss_with_cards")
    end
    if #names > 1 then
        log.write("multiple boss classes found: %s", table.concat(names, ", "))
        return finish_error("multiple_boss_unsupported")
    end

    local list = cards.enumerate(names[1])
    log.write("enumerated %s (%s): %d cards", names[1], cards.display_name(names[1]), #list)
    finish({
        status = "ok",
        boss_name = cards.display_name(names[1]),
        boss_class = names[1],
        cards = list,
    })
end

--------------------------------------------------------------------------
--- 阶段二：跳卡

--- 跳卡三件套 + `PracticeStart`（与 `StageDebugView:startBossScene` 同源）。
--- `include_previous` 由 job 决定（CMEX 固定传 false：前一阶段为 60 帧入场移动，不会有台词）。
local function do_jump()
    local class_name
    if type(ctx.spec.boss_class) == "string" and ctx.spec.boss_class ~= "" then
        if not cards.has_cards(ctx.spec.boss_class) then
            return finish_error("boss_class_not_found: " .. tostring(ctx.spec.boss_class))
        end
        class_name = ctx.spec.boss_class
    else
        local names = cards.list_boss_classes()
        if #names == 0 then
            return finish_error("no_boss_with_cards")
        end
        if #names > 1 then
            log.write("multiple boss classes found: %s", table.concat(names, ", "))
            return finish_error("multiple_boss_unsupported")
        end
        class_name = names[1]
    end

    ctx.boss_class = class_name
    ctx.boss_label = cards.display_name(class_name)
    ctx.target_card = cards.get(class_name, ctx.spec.absolute_index)
    if type(ctx.target_card) ~= "table" then
        return finish_error("card_index_out_of_range: " .. tostring(ctx.spec.absolute_index))
    end

    -- 录制器参数只在 status == "initialized" 时生效，必须先设再 start_record
    local ok, err = pcall(function()
        ctx.recorder:set_max_frame(ctx.max_frame)
        ctx.recorder:set_interval(ctx.interval)
        ctx.recorder:set_capture_area_ui()
    end)
    if not ok then
        return finish_error("recorder_config_failed: " .. tostring(err))
    end

    -- 置空 sc_index（而不是 StageDebugView 的 -1）：`UI.lua:424` 只判真值就拿 `_sc_table[sc_index]` 取难度，
    -- -1 会让它索引 nil 崩渲染；置空后走 `sc_pr_data` 路径，而 `sc_pr.lua` 各读取点都有 `> 0` 守卫。
    -- 最高分键走 `ext.lua:159-165` 的 `lstg.var.sc_pr.index`（下面已设），不依赖 sc_index。
    lstg.var.sc_index = nil
    lstg.var.sc_pr_data = {
        class_name = class_name,
        scene_index = ctx.spec.absolute_index,
        include_previous = ctx.include_previous,
    }
    lstg.var.sc_pr = { class_name = class_name, index = ctx.spec.absolute_index }

    stage.group.PracticeStart(PRACTICE_STAGE)
    ctx.stage = "wait_card"
    ctx.stage_frames = 0
    log.write("jump: boss=%s(%s) absolute_index=%d include_previous=%s card=%s",
        class_name, ctx.boss_label, ctx.spec.absolute_index,
        tostring(ctx.include_previous), tostring(ctx.target_card.name))
end

local function tick_record()
    if type(_editor_class) ~= "table" then
        if ctx.frames > READY_TIMEOUT_FRAMES then
            finish_error("editor_class_timeout")
        end
        return
    end

    if not ctx.recorder then
        local recorder, err = get_recorder()
        if not recorder then
            return finish_error("recorder_missing: " .. tostring(err))
        end
        local ready, reason = ensure_recorder_ready(recorder)
        if not ready then
            return finish_error(reason)
        end
        ctx.recorder = recorder
    end

    if ctx.stage == "wait_ready" then
        return do_jump()
    end
    if ctx.stage == "wait_card" then
        return tick_wait_card()
    end
    if ctx.stage == "recording" then
        return tick_recording()
    end
end

--------------------------------------------------------------------------
--- 帧钩子与入口

--- 单帧推进（由包装后的 DoFrame 调用）。
function record.tick()
    if not ctx or ctx.done then
        return
    end
    ctx.frames = ctx.frames + 1

    if ctx.phase == "enumerate" then
        tick_enumerate()
    else
        tick_record()
    end
end

--- 包装全局 DoFrame：先跑完本帧逻辑，再做判定（boss 卡状态在逻辑帧内更新）。
---@return boolean
function record.install_hook()
    if type(DoFrame) ~= "function" then
        return false
    end
    local origin = DoFrame
    DoFrame = function(...)
        local a, b, c = origin(...)
        local ok, err = pcall(record.tick)
        if not ok then
            log.write("ERROR: tick failed: %s", tostring(err))
            pcall(finish_error, "plugin_error: " .. tostring(err))
        end
        return a, b, c
    end
    return true
end

--- 入口：读任务 → 建上下文 → 装钩子。任何异常都转成结果文件，避免静默失败。
---@param job_path string 相对 game/ 的 job.json 路径
function record.run(job_path)
    local spec, err = jobspec.load(job_path)
    if not spec then
        print("[autocmex] " .. tostring(err))
        return
    end

    log.set_path(spec.log_path)
    log.write("job loaded: id=%s phase=%s result=%s",
        tostring(spec.job_id), tostring(spec.phase), tostring(spec.result_path))

    -- 无敌兜底：主路径由 CMEX 在启动参数里带 `cheat=true`，这里只做校验与补设，
    -- 避免「玩家被撞死 → 卡提前结束 → 产物不完整」。
    if cheat ~= true then
        log.write("WARN: global 'cheat' is not true, forcing it on for this recording run")
        cheat = true
    end

    local interval = math.floor(tonumber(spec.interval) or DEFAULT_INTERVAL)
    local max_frame = math.floor(tonumber(spec.max_frame) or DEFAULT_MAX_FRAME)
    ctx = {
        spec = spec,
        phase = spec.phase,
        frames = 0,
        stage = "wait_ready",
        stage_frames = 0,
        record_frames = 0,
        interval = math.max(1, math.min(60, interval)),
        max_frame = math.max(1, math.min(1000, max_frame)),
        include_previous = spec.include_previous == true,
        done = false,
    }
    ctx.record_budget = ctx.max_frame * ctx.interval * 3 + RECORD_TAIL_FRAMES

    if not record.install_hook() then
        return finish_error("frame_hook_unavailable")
    end
    log.write("frame hook installed (DoFrame wrapped)")
end

return record
