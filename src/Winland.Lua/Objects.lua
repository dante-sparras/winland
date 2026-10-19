-- The objects and queries of a running config, with the field names and types of the pinned release. A window, a
-- workspace, a monitor and a group are userdata that hold only what names them: a window's address, a workspace's
-- address, a monitor's name, and for a group the address of the window it was read from. Each field is read when it is
-- read, from Winland's query replies, which the runtime keeps until a dispatch runs or the call ends. An object whose
-- window, workspace or monitor is gone reads nil for every field, as an expired object does in the pinned release.
local hl, native, version = ...
local fetch, userdata, note, matches, history, unsubscribe, optionName, running =
    native.fetch, native.userdata, native.note, native.matches, native.history, native.unsubscribe, native.option, native.running

local keys = setmetatable({}, { __mode = "k" })

local Window = { __name = "HL.Window" }
local Workspace = { __name = "HL.Workspace" }
local Monitor = { __name = "HL.Monitor" }
local Group = { __name = "HL.Group" }
local Subscription = { __name = "HL.EventSubscription" }

local function readOnly()
    note("attempt to modify read-only hl object", 2)
end

local function same(a, b)
    return getmetatable(a) == getmetatable(b) and keys[a] == keys[b]
end

for _, kind in ipairs({ Window, Workspace, Monitor, Group, Subscription }) do
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
local function monitorEntry(name) return find(monitors(), "name", name) end

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

-- A workspace's id. The replies give it only for a numbered workspace; a named one is -1, -2, ... in name order, and a
-- special one -99 for the unnamed one and -98, -97, ... for the others in name order, as Winland numbers them.
local function workspaceId(entry)
    if entry.id ~= nil then
        return entry.id
    end

    local named, specials = {}, {}
    for _, other in ipairs(workspaces()) do
        if other.id == nil then
            if other.type == "special" then
                specials[#specials + 1] = other.address
            else
                named[#named + 1] = other.address
            end
        end
    end

    table.sort(named)
    table.sort(specials)
    if entry.type == "special" then
        if entry.address == "special:special" then
            return -99
        end
        local next = -98
        for _, address in ipairs(specials) do
            if address == entry.address then
                return next
            end
            if address ~= "special:special" then
                next = next + 1
            end
        end
        return 0
    end

    for i, address in ipairs(named) do
        if address == entry.address then
            return -i
        end
    end
    return 0
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

local function notHonored(what)
    return function()
        note(what .. " is not honored yet", 2)
    end
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
    stable_id = function(c) return tonumber(c.stableId or "", 16) end,
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

local groupFields = {
    add = function() return notHonored("HL.Group:add") end,
    remove = function() return notHonored("HL.Group:remove") end,
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
    local field = groupFields[key]
    if members == nil or field == nil then
        return nil
    end
    return field(members)
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
    id = workspaceId,
    name = function(w) return w.name end,
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
    config_name = function(w)
        if w.type == "special" then
            return w.name
        end
        if w.id ~= nil and w.id > 0 then
            return tostring(w.id)
        end
        return "name:" .. w.name
    end,
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
    return "HL.Workspace(" .. workspaceId(entry) .. ":" .. entry.name .. ")"
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
    mirrors = function() return {} end,
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
    set_workspace = function() return notHonored("HL.Monitor:set_workspace") end,
    set_special_workspace = function() return notHonored("HL.Monitor:set_special_workspace") end,
}

Monitor.__index = function(self, key)
    local entry = monitorEntry(keys[self])
    local field = monitorFields[key]
    if entry == nil or field == nil then
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

    local text = selectorText(value)
    local number = math.tointeger(tonumber(text))
    for _, entry in ipairs(workspaces()) do
        if number ~= nil then
            if workspaceId(entry) == number then
                return entry
            end
        elseif text == "special" then
            if entry.address == "special:special" then
                return entry
            end
        elseif startsWith(text, "name:") then
            if entry.type ~= "special" and (entry.name == text:sub(6) or entry.address == text:sub(6)) then
                return entry
            end
        elseif entry.address == text or entry.name == text then
            return entry
        end
    end
    return nil
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

function hl.get_monitors()
    local found = {}
    for i, entry in ipairs(monitors()) do
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

return {
    window = window,
    workspace = workspace,
    monitor = monitor,
    subscription = subscription,
    selector = selector,
}
