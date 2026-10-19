-- The objects and queries of a running config, with the field names and types of the pinned release. A window, a
-- workspace, a monitor and a group are userdata that hold only what names them: a window's address, a workspace's
-- address, a monitor's name, and for a group the address of the window it was read from. Each field is read when it is
-- read, from Winland's query replies, which the runtime keeps until a dispatch runs or the call ends. An object whose
-- window, workspace or monitor is gone reads nil for every field, as an expired object does in the pinned release.
local hl, native, version = ...
local fetch, userdata, note, matches, history, unsubscribe, optionName, running, run =
    native.fetch, native.userdata, native.note, native.matches, native.history, native.unsubscribe, native.option, native.running, native.run
local bindInfo, bindEnable, bindRemove, timerInfo, timerSet =
    native.bindinfo, native.bindenable, native.bindremove, native.timerinfo, native.timerset

local keys = setmetatable({}, { __mode = "k" })

local Window = { __name = "HL.Window" }
local Workspace = { __name = "HL.Workspace" }
local Monitor = { __name = "HL.Monitor" }
local Group = { __name = "HL.Group" }
local Subscription = { __name = "HL.EventSubscription" }
local Keybind = { __name = "HL.Keybind" }
local Timer = { __name = "HL.Timer" }
local Device = { __name = "HL.Device" }

local function readOnly()
    note("attempt to modify read-only hl object", 2)
end

local function same(a, b)
    return getmetatable(a) == getmetatable(b) and keys[a] == keys[b]
end

for _, kind in ipairs({ Window, Workspace, Monitor, Group, Subscription, Keybind, Timer, Device }) do
    kind.__newindex = readOnly
    kind.__eq = same
end

-- A new object of a kind. A key of nil makes an expired one.
local function object(kind, key)
    local made = userdata(kind)
    keys[made] = key or false
    return made
end

local function window(address) return object(Window, address) end
local function workspace(key) return object(Workspace, key) end
local function monitor(name) return object(Monitor, name) end

local function list(command)
    local reply = fetch(command)
    if type(reply) == "table" then
        return reply
    end
    return {}
end

local function clients() return list("ja/clients") end
local function workspaces() return list("j/workspaces") end
local function monitors() return list("j/monitors") end

local function find(entries, field, value)
    if value == nil or value == false then
        return nil
    end
    for _, entry in ipairs(entries) do
        if entry[field] == value then
            return entry
        end
    end
    return nil
end

local function client(address) return find(clients(), "address", address) end
local function workspaceEntry(key) return find(workspaces(), "address", key) end
-- A monitor, enabled or not.
local function monitorEntry(name) return find(monitors(), "name", name) or find(list("j/monitors all"), "name", name) end

local function activeAddress()
    local active = fetch("j/activewindow")
    return type(active) == "table" and active.address or nil
end

local function focusedMonitor()
    return find(monitors(), "focused", true)
end

local function nonEmpty(text)
    if text == nil or text == "" then
        return nil
    end
    return text
end

local function float(number)
    if type(number) ~= "number" then
        return number
    end
    return number + 0.0
end

local function copy(items)
    local copied = {}
    for i, item in ipairs(items or {}) do
        copied[i] = item
    end
    return copied
end

local function startsWith(text, prefix)
    return text:sub(1, #prefix) == prefix
end

local function workspaceOf(entry)
    return entry.workspace and entry.workspace.address or nil
end

-- The workspace a dispatcher's selector names: its number, special:name, or name:name.
local function workspaceSelector(key)
    if key:match("^%d+$") or startsWith(key, "special:") then
        return key
    end
    return "name:" .. key
end

-- Shown on a monitor, as its workspace or its open special workspace.
local function shown(entry)
    for _, m in ipairs(monitors()) do
        if (m.activeWorkspace and m.activeWorkspace.address == entry.address)
            or (m.specialWorkspace and m.specialWorkspace.address == entry.address) then
            return true
        end
    end
    return false
end

-- Window.

local function groupMembers(address)
    local entry = client(address)
    if entry and entry.grouped and #entry.grouped > 0 then
        return entry.grouped
    end
    return nil
end

local windowFields = {
    address = function(c) return c.address end,
    mapped = function(c) return c.mapped end,
    hidden = function(c) return c.hidden end,
    visible = function(c) return c.visible end,
    accepts_input = function(c) return c.acceptsInput end,
    at = function(c) return { x = c.at[1], y = c.at[2] } end,
    size = function(c) return { x = c.size[1], y = c.size[2] } end,
    workspace = function(c)
        local key = workspaceOf(c)
        return key and workspace(key) or nil
    end,
    floating = function(c) return c.floating end,
    monitor = function(c)
        local entry = find(monitors(), "id", c.monitor)
        return entry and monitor(entry.name) or nil
    end,
    class = function(c) return c.class end,
    title = function(c) return c.title end,
    initial_class = function(c) return c.initialClass end,
    initial_title = function(c) return c.initialTitle end,
    pid = function(c) return c.pid end,
    xwayland = function(c) return c.xwayland end,
    pinned = function(c) return c.pinned end,
    pin_fullscreened = function(c) return c.pinFullscreened end,
    fullscreen = function(c) return c.fullscreen end,
    fullscreen_client = function(c) return c.fullscreenClient end,
    allowed_over_fullscreen = function(c) return c.allowedOverFullscreen end,
    fullscreen_handler = function(c) return c.fullscreenHandler end,
    group = function(c)
        if c.grouped and #c.grouped > 0 then
            return object(Group, c.address)
        end
        return nil
    end,
    tags = function(c) return copy(c.tags) end,
    swallowing = function(c)
        if c.swallowing and c.swallowing ~= "0x0" then
            return window(c.swallowing)
        end
        return nil
    end,
    focus_history_id = function(c)
        local focused = history("focus")
        for i = #focused, 1, -1 do
            if focused[i] == c.address then
                return #focused - i
            end
        end
        return -1
    end,
    inhibiting_idle = function(c) return c.inhibitingIdle end,
    xdg_tag = function(c) return nonEmpty(c.xdgTag) end,
    xdg_description = function(c) return nonEmpty(c.xdgDescription) end,
    content_type = function(c) return c.contentType end,
    stable_id = function(c) return c.stableId and string.lower(c.stableId) or nil end,
    layout = function(c)
        if c.floating then
            return nil
        end
        local entry = workspaceEntry(workspaceOf(c))
        if entry and entry.tiledLayout then
            return { name = entry.tiledLayout }
        end
        return nil
    end,
    active = function(c) return c.address == activeAddress() end,
    tearing_hint = function(c) return c.tearingHint end,
}

Window.__index = function(self, key)
    local entry = client(keys[self])
    local field = windowFields[key]
    if entry == nil or field == nil then
        return nil
    end
    return field(entry)
end

Window.__tostring = function(self)
    if client(keys[self]) == nil then
        return "HL.Window(expired)"
    end
    return "HL.Window(" .. keys[self] .. ")"
end

-- Group.

-- The member a group shows, which is the one not hidden.
local function currentIndex(members)
    for i, address in ipairs(members) do
        local entry = client(address)
        if entry and not entry.hidden then
            return i
        end
    end
    return 1
end

-- add and remove, set further down.
local groupMethods = {}

local groupFields = {
    size = function(members) return #members end,
    current_index = currentIndex,
    current = function(members) return window(members[currentIndex(members)]) end,
    members = function(members)
        local windows = {}
        for i, address in ipairs(members) do
            windows[i] = window(address)
        end
        return windows
    end,
}

Group.__index = function(self, key)
    local members = groupMembers(keys[self])
    if members == nil then
        return nil
    end
    if groupMethods[key] then
        return groupMethods[key]
    end
    local field = groupFields[key]
    if field == nil then
        return nil
    end
    return field(members)
end

-- Two group objects are the same group when the windows they were read from are members of one group.
Group.__eq = function(a, b)
    local members = groupMembers(keys[a])
    if members == nil then
        return keys[a] == keys[b]
    end
    for _, address in ipairs(members) do
        if address == keys[b] then
            return true
        end
    end
    return false
end

Group.__tostring = function(self)
    if groupMembers(keys[self]) == nil then
        return "HL.Group(expired)"
    end
    return "HL.Group(" .. keys[self] .. ")"
end

-- Workspace.

local function windowsOn(entry, mappedOnly)
    local windows = {}
    for _, c in ipairs(clients()) do
        if workspaceOf(c) == entry.address and (not mappedOnly or c.mapped) then
            windows[#windows + 1] = window(c.address)
        end
    end
    return windows
end

local function groupsOn(entry)
    local groups, seen = {}, {}
    for _, c in ipairs(clients()) do
        if workspaceOf(c) == entry.address and c.grouped and #c.grouped > 0 and not seen[c.grouped[1]] then
            for _, member in ipairs(c.grouped) do
                seen[member] = true
            end
            groups[#groups + 1] = object(Group, c.address)
        end
    end
    return groups
end

local workspaceFields = {
    -- Only a numbered workspace has an id.
    id = function(w) return w.id end,
    name = function(w) return w.name end,
    addressable_name = function(w) return w.address end,
    monitor = function(w)
        if w.monitor and w.monitor ~= "" then
            return monitor(w.monitor)
        end
        return nil
    end,
    windows = function(w) return w.windows end,
    visible = shown,
    special = function(w) return w.type == "special" end,
    active = shown,
    has_urgent = function(w)
        for _, address in ipairs(history("urgent")) do
            local entry = client(address)
            if entry and workspaceOf(entry) == w.address then
                return true
            end
        end
        return false
    end,
    fullscreen_mode = function(w)
        local mode = 0
        for _, c in ipairs(clients()) do
            if workspaceOf(c) == w.address and (c.fullscreen or 0) > mode then
                mode = c.fullscreen
            end
        end
        return mode
    end,
    has_fullscreen = function(w) return w.hasfullscreen end,
    is_persistent = function(w) return w.ispersistent end,
    is_empty = function(w) return w.windows == 0 end,
    tiled_layout = function(w) return w.tiledLayout or "unknown" end,
    last_window = function(w)
        if w.lastwindow and w.lastwindow ~= "0x0" then
            return window(w.lastwindow)
        end
        return nil
    end,
    fullscreen_window = function(w)
        for _, c in ipairs(clients()) do
            if workspaceOf(c) == w.address and (c.fullscreen or 0) > 0 then
                return window(c.address)
            end
        end
        return nil
    end,
    get_windows = function()
        return function(self)
            local entry = workspaceEntry(keys[self])
            return entry and windowsOn(entry, false) or {}
        end
    end,
    get_groups = function()
        return function(self)
            local entry = workspaceEntry(keys[self])
            return entry and groupsOn(entry) or {}
        end
    end,
    groups = function(w) return #groupsOn(w) end,
}

Workspace.__index = function(self, key)
    local entry = workspaceEntry(keys[self])
    local field = workspaceFields[key]
    if entry == nil or field == nil then
        return nil
    end
    return field(entry)
end

Workspace.__tostring = function(self)
    local entry = workspaceEntry(keys[self])
    if entry == nil then
        return "HL.Workspace(expired)"
    end
    return "HL.Workspace(" .. entry.address .. ":" .. entry.name .. ")"
end

-- Monitor.

local function workspaceIn(slot)
    if slot and slot.address and slot.address ~= "" then
        return workspace(slot.address)
    end
    return nil
end

local monitorFields = {
    id = function(m) return m.id end,
    name = function(m) return m.name end,
    description = function(m) return m.description end,
    serial = function(m) return m.serial end,
    width = function(m) return m.width end,
    height = function(m) return m.height end,
    physical_width = function(m) return m.physicalWidth end,
    physical_height = function(m) return m.physicalHeight end,
    refresh_rate = function(m) return float(m.refreshRate) end,
    x = function(m) return m.x end,
    y = function(m) return m.y end,
    active_workspace = function(m) return workspaceIn(m.activeWorkspace) end,
    active_special_workspace = function(m) return workspaceIn(m.specialWorkspace) end,
    position = function(m) return { x = m.x, y = m.y } end,
    size = function(m) return { width = m.width, height = m.height } end,
    scale = function(m) return float(m.scale) end,
    transform = function(m) return m.transform end,
    dpms_status = function(m) return m.dpmsStatus end,
    vrr_active = function(m) return m.vrr end,
    is_mirror = function(m) return m.mirrorOf ~= nil and m.mirrorOf ~= "" and m.mirrorOf ~= "none" end,
    mirrors = function(m)
        local found = {}
        for _, entry in ipairs(list("j/monitors all")) do
            if tostring(entry.mirrorOf) == tostring(m.id) then
                found[#found + 1] = monitor(entry.name)
            end
        end
        return found
    end,
    available_modes = function(m)
        local modes = {}
        for _, mode in ipairs(m.availableModes or {}) do
            local width, height, rate = tostring(mode):match("^(%d+)x(%d+)@([%d%.]+)Hz$")
            if width then
                modes[#modes + 1] = { width = tonumber(width), height = tonumber(height), refresh_rate = float(tonumber(rate)), preferred = false }
            end
        end
        return modes
    end,
    focused = function(m) return m.focused end,
    cm = function(m) return m.colorManagementPreset end,
    reserved = function(m)
        local r = m.reserved or { 0, 0, 0, 0 }
        return { top = float(r[2]), right = float(r[3]), bottom = float(r[4]), left = float(r[1]) }
    end,
    mode = function(m) return { width = m.width, height = m.height, refresh_rate = float(m.refreshRate) } end,
    enabled = function(m) return not m.disabled end,
    -- Windows reports none of these, and Winland runs on no other backend.
    hardware_details = function() return { backend = "windows", hdr = false, chroma = false, bt2020 = false, vrr_capable = false } end,
}

-- set_workspace and set_special_workspace, set further down.
local monitorMethods = {}

Monitor.__index = function(self, key)
    local entry = monitorEntry(keys[self])
    if entry == nil then
        return nil
    end
    if monitorMethods[key] then
        return monitorMethods[key]
    end
    local field = monitorFields[key]
    if field == nil then
        return nil
    end
    return field(entry)
end

Monitor.__tostring = function(self)
    local entry = monitorEntry(keys[self])
    if entry == nil then
        return "HL.Monitor(expired)"
    end
    return "HL.Monitor(" .. entry.id .. ":" .. entry.name .. ")"
end

-- Event subscriptions.

local subscriptions = setmetatable({}, { __mode = "k" })

local subscriptionMethods = {
    remove = function(self)
        local held = subscriptions[self]
        if held == nil or not held.active then
            return
        end
        unsubscribe(held.handle)
        held.active = false
    end,
    is_active = function(self)
        local held = subscriptions[self]
        return held ~= nil and held.active
    end,
}

Subscription.__index = function(_, key)
    return subscriptionMethods[key]
end

Subscription.__tostring = function(self)
    local held = subscriptions[self]
    return "HL.EventSubscription(" .. held.handle .. "," .. (held.active and "active" or "inactive") .. ")"
end

local function subscription(handle)
    local made = object(Subscription, handle)
    subscriptions[made] = { handle = handle, active = true }
    return made
end

-- Keybinds: what hl.bind returns. Its fields are the bind's until it is removed, and nil after.

local keybindMethods = {
    set_enabled = function(self, enabled)
        if type(enabled) ~= "boolean" then
            note("HL.Keybind:set_enabled: expected a boolean", 2)
            return
        end
        bindEnable(keys[self], enabled)
    end,
    is_enabled = function(self)
        local info = bindInfo(keys[self])
        if info == nil then
            return nil
        end
        return info.enabled
    end,
    remove = function(self) bindRemove(keys[self]) end,
}
keybindMethods.unbind = keybindMethods.remove

Keybind.__index = function(self, key)
    if keybindMethods[key] then
        return keybindMethods[key]
    end
    local info = bindInfo(keys[self])
    if info == nil then
        return nil
    end
    return info[key]
end

Keybind.__tostring = function(self)
    if bindInfo(keys[self]) == nil then
        return "HL.Keybind(expired)"
    end
    return string.format("HL.Keybind(0x%x)", keys[self])
end

local function keybind(id) return object(Keybind, id) end

-- Timers: what hl.timer returns. A one-shot timer that fired is gone.

local timerMethods = {
    set_enabled = function(self, enabled)
        if type(enabled) ~= "boolean" then
            note("HL.Timer:set_enabled: expected a boolean", 2)
            return
        end
        timerSet(keys[self], enabled, nil)
    end,
    set_timeout = function(self, timeout)
        if type(timeout) ~= "number" then
            note("HL.Timer:set_timeout: expected a number", 2)
            return
        end
        timerSet(keys[self], true, math.floor(timeout))
    end,
    is_enabled = function(self) return timerInfo(keys[self]) end,
}

Timer.__index = function(_, key)
    return timerMethods[key]
end

Timer.__tostring = function(self)
    if timerInfo(keys[self]) == nil then
        return "HL.Timer(expired)"
    end
    return string.format("HL.Timer(0x%x)", keys[self])
end

local function timer(id) return object(Timer, id) end

-- Devices: what hl.get_devices returns, read from Winland's devices reply.

local function devices()
    local reply = fetch("j/devices")
    local found = {}
    if type(reply) ~= "table" then
        return found
    end
    for _, group in ipairs({ { "mice", "pointer" }, { "keyboards", "keyboard" }, { "tablets", "tablet" }, { "touch", "touch" }, { "switches", "switch" } }) do
        for _, entry in ipairs(reply[group[1]] or {}) do
            found[#found + 1] = { kind = group[2], entry = entry }
        end
    end
    return found
end

local function deviceEntry(address)
    for _, device in ipairs(devices()) do
        if device.entry.address == address then
            return device
        end
    end
    return nil
end

local deviceFields = {
    pointer = {
        default_speed = function(e) return float(e.defaultSpeed) end,
        scroll_factor = function(e) return float(e.scrollFactor) end,
    },
    keyboard = {
        rules = function(e) return e.rules end,
        model = function(e) return e.model end,
        layout = function(e) return e.layout end,
        variant = function(e) return e.variant end,
        options = function(e) return e.options end,
        main = function(e) return e.main end,
        active_layout_index = function(e) return e.active_layout_index end,
        active_keymap = function(e) return e.active_keymap end,
        caps_lock = function(e) return e.capsLock end,
        num_lock = function(e) return e.numLock end,
    },
}

Device.__index = function(self, key)
    local device = deviceEntry(keys[self])
    if device == nil then
        return nil
    end
    if key == "address" then
        return device.entry.address
    elseif key == "type" then
        return device.kind
    elseif key == "name" then
        return device.entry.name
    end
    local field = (deviceFields[device.kind] or {})[key]
    if field == nil then
        return nil
    end
    return field(device.entry)
end

Device.__tostring = function(self)
    if deviceEntry(keys[self]) == nil then
        return "HL.Device(expired)"
    end
    return "HL.Device(" .. keys[self] .. ")"
end

function hl.get_devices()
    local found = {}
    for i, device in ipairs(devices()) do
        found[i] = object(Device, device.entry.address)
    end
    return found
end

-- Selectors: an object, or the text a dispatcher takes.

local function kindOf(value)
    if type(value) == "userdata" then
        return getmetatable(value)
    end
    return nil
end

-- A window selector, resolved as Winland resolves one: a match on the focused window's workspace wins.
local function selectWindow(text)
    text = text:match("^%s*(.-)%s*$")
    local active = activeAddress()
    if text == "" or startsWith(text, "active") then
        return active
    end

    local all = clients()
    local activeEntry = find(all, "address", active)
    local home = activeEntry and workspaceOf(activeEntry) or nil

    local function first(match)
        local elsewhere
        for _, c in ipairs(all) do
            if match(c) then
                if home ~= nil and workspaceOf(c) == home then
                    return c.address
                end
                elsewhere = elsewhere or c.address
            end
        end
        return elsewhere
    end

    for _, layer in ipairs({ "floating", "tiled" }) do
        if startsWith(text, layer) then
            if home == nil then
                return nil
            end
            for _, c in ipairs(all) do
                if c.mapped and c.floating == (layer == "floating") and workspaceOf(c) == home then
                    return c.address
                end
            end
            return nil
        end
    end

    local function after(prefix)
        return startsWith(text, prefix) and text:sub(#prefix + 1) or nil
    end

    local value = after("tag:")
    if value then
        return first(function(c)
            for _, tag in ipairs(c.tags or {}) do
                if matches(tag, value) then
                    return true
                end
            end
            return false
        end)
    end

    value = after("stableid:")
    if value then
        return first(function(c) return c.stableId == value end)
    end

    value = after("address:")
    if value then
        return first(function(c) return c.address == value end)
    end

    value = after("pid:")
    if value then
        return first(function(c) return tostring(c.pid) == value end)
    end

    local field, pattern = "class", text
    for _, prefix in ipairs({ { "initialtitle:", "initialTitle" }, { "title:", "title" }, { "initialclass:", "initialClass" }, { "class:", "class" } }) do
        value = after(prefix[1])
        if value then
            field, pattern = prefix[2], value
            break
        end
    end
    return first(function(c) return matches(c[field] or "", pattern) end)
end

local function selectorText(value)
    if type(value) == "number" then
        return tostring(math.tointeger(value) or math.floor(value))
    end
    return value
end

-- The window a selector or a window object names, or nil.
local function resolveWindow(value, label)
    if value == nil then
        return nil
    end
    if kindOf(value) == Window then
        return client(keys[value]) and keys[value] or nil
    end
    if type(value) == "string" or type(value) == "number" then
        return selectWindow(selectorText(value))
    end
    note(label .. ": expected a window object or selector", 3)
    return nil
end

-- The workspace entry a selector or a workspace object names, or nil: its number or id, name:, special: or its name.
local function resolveWorkspace(value, label)
    if value == nil then
        return nil
    end
    if kindOf(value) == Workspace then
        return workspaceEntry(keys[value])
    end
    if type(value) ~= "string" and type(value) ~= "number" then
        note(label .. ": expected a workspace object or selector", 3)
        return nil
    end

    -- Winland reads the selector as a dispatcher on the focused monitor reads it, relative forms too, and finds the
    -- workspace it names without making one.
    local focused = focusedMonitor()
    if focused == nil then
        return nil
    end
    local address = fetch("j/resolveworkspace " .. focused.name .. " " .. selectorText(value))
    if type(address) ~= "string" then
        return nil
    end
    return workspaceEntry(address)
end

-- The monitor entry a selector or a monitor object names, or nil: its id, its name, or desc: and its description.
local function resolveMonitor(value, label)
    if value == nil then
        return nil
    end
    if kindOf(value) == Monitor then
        return monitorEntry(keys[value])
    end
    if type(value) ~= "string" and type(value) ~= "number" then
        note(label .. ": expected a monitor object or selector", 3)
        return nil
    end

    local text = selectorText(value)
    local number = math.tointeger(tonumber(text))
    for _, entry in ipairs(monitors()) do
        if number ~= nil then
            if entry.id == number then
                return entry
            end
        elseif startsWith(text, "desc:") then
            if startsWith(entry.description or "", text:sub(6)) then
                return entry
            end
        elseif entry.name == text then
            return entry
        end
    end
    return nil
end

local function monitorOrFocused(value, label)
    if value == nil then
        return focusedMonitor()
    end
    return resolveMonitor(value, label)
end

-- The selector a dispatcher's table takes for an object, or nil for any other value.
local function selector(value)
    local kind = kindOf(value)
    if kind == Window then
        return "address:" .. tostring(keys[value])
    elseif kind == Workspace then
        return keys[value] and workspaceSelector(keys[value]) or nil
    elseif kind == Monitor then
        return keys[value] or nil
    end
    return nil
end

-- The monitor methods and the group methods, made of Winland's pipe commands, which change nothing but what they
-- name: the focus stays on the monitor it is on.

-- An error from a pipe command as a warning at the caller, without its "error: " prefix.
local function failed(label, reply, level)
    if type(reply) == "string" and reply ~= "ok" then
        note(label .. ": " .. reply:gsub("^error: ", ""), level + 1)
        return true
    end
    return false
end

-- m:set_workspace(selector or workspace): shows the workspace on the monitor, read from that monitor, made when missing
-- and moved there first when another monitor has it. A special workspace is left alone.
function monitorMethods.set_workspace(self, value)
    local text
    if kindOf(value) == Workspace then
        if keys[value] == nil or workspaceEntry(keys[value]) == nil then
            note("HLMonitor.set_workspace: workspace object is expired", 2)
            return
        end
        text = workspaceSelector(keys[value])
    elseif type(value) == "string" or type(value) == "number" then
        text = selectorText(value)
    else
        note("HLMonitor.set_workspace: expected a workspace object or selector", 2)
        return
    end

    failed("HLMonitor.set_workspace", run("setworkspace " .. keys[self] .. " " .. text), 2)
end

-- m:set_special_workspace(name or workspace): opens that special workspace on the monitor. nil closes the one open.
function monitorMethods.set_special_workspace(self, value)
    local target
    if type(value) == "string" or type(value) == "number" then
        local text = selectorText(value)
        if startsWith(text, "special:") then
            text = text:sub(#"special:" + 1)
        end
        target = (text == "" or text == "special") and "special:special" or ("special:" .. text)
    elseif kindOf(value) == Workspace then
        target = keys[value]
        if target == nil or not startsWith(target, "special:") then
            return
        end
    elseif value ~= nil then
        note("HLMonitor.set_special_workspace: expected a workspace object or selector", 2)
        return
    end

    local command = "setspecialworkspace " .. keys[self]
    if target ~= nil then
        command = command .. " " .. target
    end
    failed("HLMonitor.set_special_workspace", run(command), 2)
end

-- The 1-based index a group method takes, checked against the most it may be, or false after a warning. nil when none is
-- given.
local function groupIndex(label, index, most)
    if index == nil then
        return nil
    end
    if math.type(index) ~= "integer" then
        note(label .. ": index must be an integer", 3)
        return false
    end
    if index < 1 or index > most then
        note(label .. ": index out of range", 3)
        return false
    end
    return index
end

-- g:add(window, index): puts the window into the group at that index, or where group:insert_after_current puts it, and
-- shows it there. A window in another group brings that group's members with it.
function groupMethods.add(self, value, index)
    local label = "HL.Group:add"
    local members = groupMembers(keys[self])
    if members == nil then
        note(label .. ": group object is expired", 2)
        return
    end
    if kindOf(value) ~= Window then
        note(label .. ": expected a window object", 2)
        return
    end
    if client(keys[value]) == nil then
        note(label .. ": window object is expired", 2)
        return
    end
    local at = groupIndex(label, index, #members + 1)
    if at == false then
        return
    end

    local command = "groupadd address:" .. keys[self] .. " address:" .. keys[value]
    if at ~= nil then
        command = command .. " " .. at
    end
    failed(label, run(command), 2)
end

-- g:remove(window or index): takes that member out of the group.
function groupMethods.remove(self, value)
    local label = "HL.Group:remove"
    local members = groupMembers(keys[self])
    if members == nil then
        note(label .. ": group object is expired", 2)
        return
    end

    local address
    if math.type(value) == "integer" then
        local at = groupIndex(label, value, #members)
        if not at then
            return
        end
        address = members[at]
    elseif kindOf(value) == Window then
        if client(keys[value]) == nil then
            note(label .. ": window object is expired", 2)
            return
        end
        address = keys[value]
    else
        note(label .. ": expected a window object", 2)
        return
    end

    local member = false
    for _, other in ipairs(members) do
        if other == address then
            member = true
        end
    end
    if not member then
        note(label .. ": window is not a group member", 2)
        return
    end

    -- The object keeps naming the group when the window it was read from is the one that leaves.
    if address == keys[self] then
        for _, other in ipairs(members) do
            if other ~= address then
                keys[self] = other
                break
            end
        end
    end
    failed(label, run("groupremove address:" .. address), 2)
end

-- The queries.

local function contains(point, m)
    return point.x >= m.x and point.x < m.x + m.width and point.y >= m.y and point.y < m.y + m.height
end

local function monitorAt(point)
    for _, m in ipairs(monitors()) do
        if contains(point, m) then
            return monitor(m.name)
        end
    end
    return nil
end

local function windowsMatching(query)
    local windows = {}
    for _, c in ipairs(clients()) do
        local fits = true
        if query.monitor ~= nil and c.monitor ~= query.monitor then fits = false end
        if query.workspace ~= nil and workspaceOf(c) ~= query.workspace then fits = false end
        if query.floating ~= nil and c.floating ~= query.floating then fits = false end
        if query.mapped ~= nil and c.mapped ~= query.mapped then fits = false end
        if query.class ~= nil and c.class ~= query.class then fits = false end
        if query.title ~= nil and c.title ~= query.title then fits = false end
        if query.tag ~= nil then
            local tagged = false
            for _, tag in ipairs(c.tags or {}) do
                if tag == query.tag then
                    tagged = true
                end
            end
            fits = fits and tagged
        end
        if fits then
            windows[#windows + 1] = window(c.address)
        end
    end
    return windows
end

local function optionalText(value)
    if value == nil then
        return nil
    end
    return tostring(value)
end

function hl.get_windows(filters)
    local query = { mapped = true }
    if filters ~= nil then
        if type(filters) ~= "table" then
            note("hl.get_windows: expected no args or a table of filters", 2)
            return nil
        end
        if filters.monitor ~= nil then
            local entry = resolveMonitor(filters.monitor, "hl.get_windows")
            query.monitor = entry and entry.id or false
        end
        if filters.workspace ~= nil then
            local entry = resolveWorkspace(filters.workspace, "hl.get_windows")
            query.workspace = entry and entry.address or false
        end
        if filters.floating ~= nil then query.floating = filters.floating and true or false end
        if filters.mapped ~= nil then query.mapped = filters.mapped and true or false end
        query.class = optionalText(filters.class)
        query.title = optionalText(filters.title)
        query.tag = optionalText(filters.tag)
    end
    return windowsMatching(query)
end

function hl.get_window(value)
    local address = resolveWindow(value, "hl.get_window")
    return address and window(address) or nil
end

function hl.get_active_window()
    local address = activeAddress()
    return address and window(address) or nil
end

function hl.get_urgent_window()
    for _, address in ipairs(history("urgent")) do
        if client(address) then
            return window(address)
        end
    end
    return nil
end

function hl.get_workspaces()
    local found = {}
    for i, entry in ipairs(workspaces()) do
        found[i] = workspace(entry.address)
    end
    return found
end

function hl.get_workspace(value)
    local entry = resolveWorkspace(value, "hl.get_workspace")
    return entry and workspace(entry.address) or nil
end

function hl.get_active_workspace(value)
    local entry = monitorOrFocused(value, "hl.get_active_workspace")
    return entry and workspaceIn(entry.activeWorkspace) or nil
end

function hl.get_active_special_workspace(value)
    local entry = monitorOrFocused(value, "hl.get_active_special_workspace")
    return entry and workspaceIn(entry.specialWorkspace) or nil
end

-- hl.get_monitors({ all = true }) lists the disabled monitors too.
function hl.get_monitors(options)
    local all = false
    if options ~= nil then
        if type(options) ~= "table" then
            note("hl.get_monitors: expected no args or a table of options", 2)
            return nil
        end
        if options.all ~= nil then
            if type(options.all) ~= "boolean" then
                note("hl.get_monitors: option 'all': expected a boolean", 2)
                return nil
            end
            all = options.all
        end
    end

    local found = {}
    for i, entry in ipairs(all and list("j/monitors all") or monitors()) do
        found[i] = monitor(entry.name)
    end
    return found
end

function hl.get_monitor(value)
    local entry = resolveMonitor(value, "hl.get_monitor")
    return entry and monitor(entry.name) or nil
end

function hl.get_active_monitor()
    local entry = focusedMonitor()
    return entry and monitor(entry.name) or nil
end

function hl.get_monitor_at(x, y)
    if type(x) == "table" then
        if type(x.x) ~= "number" or type(x.y) ~= "number" then
            note("hl.get_monitor_at: expected a table { x, y }", 2)
            return nil
        end
        return monitorAt(x)
    end
    if type(x) ~= "number" then
        note("get_monitor_at: bad argument 1: expected a number", 2)
        return nil
    end
    if type(y) ~= "number" then
        note("get_monitor_at: bad argument 2: expected a number", 2)
        return nil
    end
    return monitorAt({ x = x, y = y })
end

function hl.get_cursor_pos()
    local cursor = fetch("j/cursorpos")
    if type(cursor) ~= "table" or cursor.x == nil then
        return nil
    end
    return { x = float(cursor.x), y = float(cursor.y) }
end

function hl.get_monitor_at_cursor()
    local cursor = hl.get_cursor_pos()
    return cursor and monitorAt(cursor) or nil
end

function hl.get_layers(filters)
    if filters ~= nil and type(filters) ~= "table" then
        note("hl.get_layers: expected no args or a table of filters", 2)
        return nil
    end
    return {}
end

function hl.get_workspace_windows(value)
    local entry = resolveWorkspace(value, "hl.get_workspace_windows")
    if entry == nil then
        return {}
    end
    return windowsOn(entry, true)
end

function hl.get_last_window()
    local current = activeAddress()
    local focused = history("focus")
    for i = #focused, 1, -1 do
        local entry = client(focused[i])
        if entry and entry.mapped and focused[i] ~= current then
            return window(focused[i])
        end
    end
    return nil
end

function hl.get_last_workspace(value)
    local entry = monitorOrFocused(value, "hl.get_last_workspace")
    if entry == nil or entry.activeWorkspace == nil then
        return nil
    end
    local current = entry.activeWorkspace.address
    local visited = history("workspace")
    for i = #visited, 1, -1 do
        local previous = workspaceEntry(visited[i])
        if previous and visited[i] ~= current and (value == nil or previous.monitor == entry.name) then
            return workspace(visited[i])
        end
    end
    return nil
end

function hl.get_current_submap()
    local submap = fetch("j/submap")
    if type(submap) ~= "string" or submap == "default" then
        return ""
    end
    return submap
end

-- hl.get_config("general.gaps_in"): the value, as its type is in Lua, or nil and why.
local kinds = {
    int = function(value) return math.tointeger(value) or value end,
    float = float,
    bool = function(value) return value end,
    str = function(value) return value end,
    css = function(value)
        local top, right, bottom, left = tostring(value):match("^(%-?%d+) (%-?%d+) (%-?%d+) (%-?%d+)$")
        if top == nil then
            return value
        end
        return { top = tonumber(top), right = tonumber(right), bottom = tonumber(bottom), left = tonumber(left) }
    end,
}

function hl.get_config(name)
    if type(name) ~= "string" then
        note("hl.get_config: bad type for arg 1, expected a string", 2)
        return nil
    end
    if not running() then
        return nil
    end
    local option = optionName(name)
    local reply = fetch("getoption " .. option)
    if type(reply) == "table" then
        for kind, read in pairs(kinds) do
            if reply[kind] ~= nil then
                return read(reply[kind])
            end
        end
    end
    return nil, "unknown config key '" .. name .. "'"
end

function hl.version()
    return version
end

function hl.get_loaded_plugins()
    local plugins = {}
    for i, plugin in ipairs(list("plugin list")) do
        plugins[i] = { name = plugin.name, author = "", version = tostring(plugin.version), description = "" }
    end
    return plugins
end

-- Custom layouts: the ctx and the targets a registered layout's recalculate and layout_msg get. A target places itself
-- with :place(box) or :set_box(box), which note the box for Winland to apply once the callback returns.

local LayoutTarget = { __name = "HL.LayoutTarget", __newindex = readOnly }

-- The type an argument check names, as the pinned release names it: a number reads as a string there too.
local function kindName(value)
    local kind = type(value)
    if kind == "string" or kind == "number" then
        return "string"
    elseif kind == "boolean" then
        return "bool"
    elseif kind == "nil" or kind == "function" or kind == "table" then
        return kind
    end
    return "?"
end

local function numberIn(value)
    if type(value) == "number" then
        return value
    end
    if type(value) == "string" then
        return tonumber(value)
    end
    return nil
end

-- A box table { x, y, w, h }, copied with no negative size, or nil when it is not one.
local function layoutBox(value)
    if type(value) ~= "table" then
        return nil
    end
    local x, y, w, h = numberIn(value.x), numberIn(value.y), numberIn(value.w), numberIn(value.h)
    if not (x and y and w and h) then
        return nil
    end
    return { x = x, y = y, w = math.max(0, w), h = math.max(0, h) }
end

local function boxCopy(box)
    return { x = box.x, y = box.y, w = box.w, h = box.h }
end

local function placeTarget(self, box)
    local held = getmetatable(self) == LayoutTarget and keys[self]
    if not held then
        error("bad argument #1 to 'place' (HL.LayoutTarget expected)", 2)
    end
    local placed = layoutBox(box)
    if not placed then
        note("HL.LayoutTarget: place expects a box table { x, y, w, h }", 2)
        return
    end
    held.box = placed
    held.placed[held.index] = boxCopy(placed)
end

LayoutTarget.__index = function(self, key)
    if type(key) ~= "string" then
        note("HL.LayoutTarget.__index: bad argument 2: expected string, got " .. kindName(key), 2)
        return nil
    end
    local held = keys[self]
    if key == "index" then
        return held.index
    elseif key == "window" then
        return held.address and window(held.address) or nil
    elseif key == "box" then
        return boxCopy(held.box)
    elseif key == "place" or key == "set_box" then
        return placeTarget
    end
    return nil
end

LayoutTarget.__tostring = function(self)
    return "HL.LayoutTarget(" .. keys[self].index .. ")"
end

local function integerArgument(value, label, position)
    if math.type(value) ~= "integer" then
        note(label .. ": bad argument " .. position .. ": expected integer, got " .. kindName(value), 3)
        return nil
    end
    return value
end

local function areaOf(ctx)
    return layoutBox(type(ctx) == "table" and ctx.area or nil) or { x = 0, y = 0, w = 0, h = 0 }
end

local layoutMethods = {}

function layoutMethods.grid_cell(ctx, i, cols, rows)
    local area = areaOf(ctx)
    i = integerArgument(i, "grid_cell", 2)
    if not i then return end
    cols = integerArgument(cols, "grid_cell", 3)
    if not cols then return end
    local index, width = math.max(1, i), math.max(1, cols)
    local height
    if type(rows) == "number" then
        height = math.max(1, math.tointeger(rows) or math.floor(rows))
    else
        local count = math.max(1, type(ctx.targets) == "table" and #ctx.targets or 0)
        height = math.max(1, math.ceil(count / width))
    end
    local row, col = (index - 1) // width, (index - 1) % width
    return layoutBox({ x = area.x + area.w * col / width, y = area.y + area.h * row / height, w = area.w / width, h = area.h / height })
end

function layoutMethods.column(ctx, i, n)
    local area = areaOf(ctx)
    i = integerArgument(i, "column", 2)
    if not i then return end
    n = integerArgument(n, "column", 3)
    if not n then return end
    local index, count = math.max(1, i), math.max(1, n)
    return layoutBox({ x = area.x + area.w * (index - 1) / count, y = area.y, w = area.w / count, h = area.h })
end

function layoutMethods.row(ctx, i, n)
    local area = areaOf(ctx)
    i = integerArgument(i, "row", 2)
    if not i then return end
    n = integerArgument(n, "row", 3)
    if not n then return end
    local index, count = math.max(1, i), math.max(1, n)
    return layoutBox({ x = area.x, y = area.y + area.h * (index - 1) / count, w = area.w, h = area.h / count })
end

function layoutMethods.split(_, box, side, ratio)
    local area = layoutBox(box)
    if not area then
        note("ctx:split expects a box table as first argument", 2)
        return
    end
    if type(side) ~= "string" and type(side) ~= "number" then
        note("split: bad argument 1: expected string, got " .. kindName(side), 2)
        return
    end
    side = tostring(side)
    local share = numberIn(ratio)
    if not share then
        note("split: bad argument 4: expected number, got " .. kindName(ratio), 2)
        return
    end
    share = math.max(0.0, math.min(1.0, share))
    if side == "left" then
        return layoutBox({ x = area.x, y = area.y, w = area.w * share, h = area.h })
    elseif side == "right" then
        return layoutBox({ x = area.x + area.w * (1.0 - share), y = area.y, w = area.w * share, h = area.h })
    elseif side == "top" or side == "up" then
        return layoutBox({ x = area.x, y = area.y, w = area.w, h = area.h * share })
    elseif side == "bottom" or side == "down" then
        return layoutBox({ x = area.x, y = area.y + area.h * (1.0 - share), w = area.w, h = area.h * share })
    end
    note("ctx:split side must be left, right, top, or bottom", 2)
end

-- The ctx of one callback: the tiling area, the targets in the layout's order, and the helpers. The second result
-- holds the box each target placed, by its index.
local function layoutContext(area, entries)
    local placed = {}
    local targets = {}
    for i, entry in ipairs(entries) do
        local made = userdata(LayoutTarget)
        keys[made] = { index = i, address = entry.address, box = entry.box, placed = placed }
        targets[i] = made
    end
    local ctx = { area = area, targets = targets }
    for name, method in pairs(layoutMethods) do
        ctx[name] = method
    end
    return ctx, placed
end

return {
    window = window,
    workspace = workspace,
    monitor = monitor,
    subscription = subscription,
    selector = selector,
    keybind = keybind,
    timer = timer,
    layoutContext = layoutContext,
}
