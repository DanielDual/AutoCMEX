--- 枚举 `_editor_class[<Boss类名>].cards`。
---
--- 卡对象由 `boss.dialog.New` / `boss.card.New` / `boss.move.New` 生成，字段（`THlib/enemy/boss_card.lua:36,44`）：
---   `name`（符卡名，非符为空串）/ `is_sc`（名字非空）/ `is_combat`（是否战斗阶段，对话为 false）/ `t3`（最长时长，帧）
--- 绝对下标 = 该卡在 `cards` 中的位置，与 `_sc_table` 的 `scene_index` 同源（`_sc_table` 只含符卡，两者不同构）。

local cards = {}

--- 与 `lib/debug/StageDebugView.lua` 的 `isBossClass` 同源：沿 `base` 链找到 `boss`。
---@param class_type table
---@return boolean
local function is_boss_class(class_type)
    local t = class_type
    while type(t) == "table" do
        if t == boss then
            return true
        end
        t = t.base
    end
    return false
end

--- 列出所有「boss 类且卡表非空」的类名（升序，保证多次枚举结果稳定）。
---@return string[]
function cards.list_boss_classes()
    local names = {}
    if type(_editor_class) ~= "table" then
        return names
    end
    for class_name, class_type in pairs(_editor_class) do
        if type(class_type) == "table"
            and is_boss_class(class_type)
            and type(class_type.cards) == "table"
            and #class_type.cards > 0 then
            table.insert(names, tostring(class_name))
        end
    end
    table.sort(names)
    return names
end

--- 类名是否可用（存在且有卡表）。
---@param class_name string
---@return boolean
function cards.has_cards(class_name)
    local class_type = type(_editor_class) == "table" and _editor_class[class_name] or nil
    return type(class_type) == "table"
        and type(class_type.cards) == "table"
        and #class_type.cards > 0
end

--- Boss 显示名（`_editor_class[x].name`，样例为 "Ibuki Suika"）。
---@param class_name string
---@return string
function cards.display_name(class_name)
    local class_type = type(_editor_class) == "table" and _editor_class[class_name] or nil
    if type(class_type) == "table" and type(class_type.name) == "string" and class_type.name ~= "" then
        return class_type.name
    end
    return tostring(class_name)
end

--- 取某张卡对象（用于跳卡后的对象同一性判定）。
---@param class_name string
---@param absolute_index integer
---@return table|nil
function cards.get(class_name, absolute_index)
    local class_type = type(_editor_class) == "table" and _editor_class[class_name] or nil
    if type(class_type) ~= "table" or type(class_type.cards) ~= "table" then
        return nil
    end
    return class_type.cards[absolute_index]
end

--- 枚举整张卡表。
---@param class_name string
---@return table[] 每项 `{absolute_index, name, is_sc, is_combat, t3}`
function cards.enumerate(class_name)
    local class_type = type(_editor_class) == "table" and _editor_class[class_name] or nil
    if type(class_type) ~= "table" or type(class_type.cards) ~= "table" then
        return {}
    end

    local list = {}
    for i, card in ipairs(class_type.cards) do
        local is_sc = card.is_sc == true
        local is_combat = card.is_combat == true
        table.insert(list, {
            absolute_index = i,
            name = type(card.name) == "string" and card.name or "",
            is_sc = is_sc,
            is_combat = is_combat,
            -- 供 CMEX 估算单卡时长；非战斗阶段（对话）没有 t3
            t3 = is_combat and (tonumber(card.t3) or 0) / 60 or 0,
        })
    end
    return list
end

return cards
