-- .vscode/engine_plugin.lua

-- Debug logger: Writes to a file so we can prove the plugin is running
local function log_debug(msg)
    -- Change this path to somewhere easy to find
    local f = io.open("A:/RemakeEngine/plugin_debug.log", "a")
    if f then
        f:write(msg .. "\n")
        f:close()
    end
end

local function get_dir(uri)
    return uri:match("^(.*[/\\])") or ""
end

function ResolveRequire(uri, name)
    log_debug("--- NEW IMPORT ---")
    log_debug("File calling import: " .. tostring(uri))
    log_debug("Requested module: " .. tostring(name))

    local file_name = name:gsub("\\", "/")
    if not file_name:match("%.lua$") then
        file_name = file_name .. ".lua"
    end

    local resolved_uri
    if file_name:match("^[A-Za-z]:") or file_name:match("^/") then
        local safe_path = file_name
        if not safe_path:match("^/") then safe_path = "/" .. safe_path end
        resolved_uri = "file://" .. safe_path
    else
        local current_dir = get_dir(uri)
        resolved_uri = current_dir .. file_name
    end

    log_debug("Resolved to: " .. resolved_uri)
    return { resolved_uri }
end