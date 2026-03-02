-- Test script for exploring different Lua import methods in RemakeEngine

sdk.colour_print({ colour = "cyan", message = "=== Lua Import Method Tests ===" })


-- 1. Standard 'require' (Module-based, cached)
-- Usually looks in package.path. For operations, lua_feature_demo is typically available.
sdk.colour_print({ colour = "white", message = "Testing: require('lua_feature_demo.lua')" })
local status, Utils = pcall(require, "./lua_feature_demo.lua")
if status then
    sdk.colour_print({ colour = "green", message = "✓ require('lua_feature_demo.lua') successful" })
    sdk.colour_print({ colour = "gray", message = "  Path Sep: " .. tostring(Utils.path_sep) })
else
    sdk.colour_print({ colour = "red", message = "✗ require('lua_feature_demo.lua') failed: " .. tostring(Utils) })
end

-- 2. global 'import' (if defined by engine, often for direct file execution/injection)
if _G.import then
    sdk.colour_print({ colour = "white", message = "Testing: _G.import" })
    -- Note: Paths might need to be absolute or relative to workspace root depending on engine implementation
    local import_status, result = pcall(_G.import, "lua_feature_demo.lua")
    if import_status then
        sdk.colour_print({ colour = "green", message = "✓ _G.import successful" })
    else
        sdk.colour_print({ colour = "yellow", message = "⚠ _G.import failed or not applicable for this path: " .. tostring(result) })
    end
else
    sdk.colour_print({ colour = "gray", message = "Note: _G.import is not defined in this environment" })
end


-- 4. loadfile (Compiles but doesn't run immediately)
sdk.colour_print({ colour = "white", message = "Testing: bootstrap" })

local function bootstrap()
    local utils_path = join(script_dir, "lua_feature_demo.lua")
    local fh, err = io.open(utils_path, "r")
    if not fh then error("Failed to open lua_feature_demo.lua: " .. tostring(err)) end
    local src = fh:read("*a")
    fh:close()
    local chunk = assert(load(src, "@" .. utils_path, "t", _ENV))
    return chunk()
end

local lua_feature_demo = bootstrap()


sdk.colour_print({ colour = "cyan", message = "=== Tests Completed ===" })
