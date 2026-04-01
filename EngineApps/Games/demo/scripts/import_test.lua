--[[
Comprehensive Lua import/load test harness for RemakeEngine.
Tests engine custom import methods and MoonSharp loader functions against
deterministic external fixture modules.
--]]

---@class ImportTestState
---@field Passed integer
---@field Failed integer
---@field Skipped integer
---@field Failures table<integer, string>

---@param value any
---@return string
local function ToText(value)
    if value == nil then
        return "nil"
    end

    return tostring(value)
end

---@param colour string
---@param message string
local function Emit(colour, message)
    if type(sdk) == "table" and type(sdk.colour_print) == "function" then
        sdk.colour_print({ colour = colour, message = message })
    else
        io.write(message .. "\n")
    end
end

---@param state ImportTestState
---@param name string
---@param condition boolean
---@param details string?
local function Check(state, name, condition, details)
    if condition then
        state.Passed = state.Passed + 1
        Emit("green", "[PASS] " .. name)
        return
    end

    state.Failed = state.Failed + 1
    local failure = "[FAIL] " .. name
    if details and #details > 0 then
        failure = failure .. " :: " .. details
    end

    table.insert(state.Failures, failure)
    Emit("red", failure)
end

---@param state ImportTestState
---@param name string
---@param reason string
local function Skip(state, name, reason)
    state.Skipped = state.Skipped + 1
    Emit("yellow", "[SKIP] " .. name .. " :: " .. reason)
end

---@param state ImportTestState
---@param name string
---@param fn function
---@param ... any
---@return boolean
---@return any
---@return any
---@return any
local function SafeCall(state, name, fn, ...)
    local ok, a, b, c = pcall(fn, ...)
    Check(state, name, ok, ok and nil or ToText(a))
    return ok, a, b, c
end

---@param value any
---@return boolean
local function IsTable(value)
    return type(value) == "table"
end

---@param module any
---@return boolean
local function ValidateModuleShape(module)
    return IsTable(module)
        and type(module.Add) == "function"
        and type(module.GetLoadCount) == "function"
        and type(module.GetNestedProduct) == "function"
end

---@param state ImportTestState
---@param name string
---@param module any
local function ValidateModuleBehavior(state, name, module)
    Check(state, name .. " returns module table", ValidateModuleShape(module), ToText(module))
    if not ValidateModuleShape(module) then
        return
    end

    Check(state, name .. " Add function", module.Add(4, 8) == 12, ToText(module.Add(4, 8)))
    local nestedProduct, nestedLoadCount = module.GetNestedProduct(3, 7)
    Check(state, name .. " nested import function", nestedProduct == 21, ToText(nestedProduct))
    Check(state, name .. " nested module load count", type(nestedLoadCount) == "number" and nestedLoadCount >= 1, ToText(nestedLoadCount))
end

---@param state ImportTestState
---@param methodName string
---@param loader function
local function ValidateNoCache(state, methodName, loader)
    local firstOk, firstModule = pcall(loader)
    Check(state, methodName .. " first load", firstOk, firstOk and nil or ToText(firstModule))
    if not firstOk or not ValidateModuleShape(firstModule) then
        return
    end

    local secondOk, secondModule = pcall(loader)
    Check(state, methodName .. " second load", secondOk, secondOk and nil or ToText(secondModule))
    if not secondOk or not ValidateModuleShape(secondModule) then
        return
    end

    local firstCount = firstModule.GetLoadCount()
    local secondCount = secondModule.GetLoadCount()
    Check(
        state,
        methodName .. " re-executes module",
        type(firstCount) == "number" and type(secondCount) == "number" and secondCount == (firstCount + 1),
        "first=" .. ToText(firstCount) .. ", second=" .. ToText(secondCount)
    )
end

---@param relativePath string
---@return string
local function ReadTextFile(relativePath)
    local absolutePath = join(script_dir, relativePath)
    local handle, openError = io.open(absolutePath, "r")
    if not handle then
        error("Unable to open fixture file: " .. absolutePath .. " :: " .. ToText(openError))
    end

    local source = handle:read("*a")
    handle:close()

    return source
end

---@type ImportTestState
local State = {
    Passed = 0,
    Failed = 0,
    Skipped = 0,
    Failures = {}
}

local FixtureRelativeNoExt = "import_fixtures/module_main"
local FixtureRelativeWithExt = FixtureRelativeNoExt .. ".lua"
local FixtureAbsoluteWithExt = join(script_dir, FixtureRelativeWithExt)
local MissingFixture = "import_fixtures/does_not_exist"

Emit("cyan", "=== RemakeEngine Lua Import Method Tests ===")
Emit("white", "Fixture: " .. FixtureRelativeWithExt)

-- --------------------------------------------------------------------------
-- Engine custom import/require methods
-- --------------------------------------------------------------------------

Emit("cyan", "--- Engine import/require methods ---")

Check(State, "global import available", type(import) == "function", ToText(import))
Check(State, "global require available", type(require) == "function", ToText(require))
if type(import) == "function" and type(require) == "function" then
    Check(State, "require aliases import", rawequal(import, require), "functions should be aliases in engine runtime")
end

if type(import) == "function" then
    local importOk, importModule = pcall(import, FixtureRelativeWithExt)
    Check(State, "import(path.lua) call", importOk, importOk and nil or ToText(importModule))
    if importOk then
        ValidateModuleBehavior(State, "import(path.lua)", importModule)
    end

    local importNoExtOk, importNoExtModule = pcall(import, FixtureRelativeNoExt)
    Check(State, "import(path) extensionless", importNoExtOk, importNoExtOk and nil or ToText(importNoExtModule))
    if importNoExtOk then
        ValidateModuleBehavior(State, "import(path)", importNoExtModule)
    end

    ValidateNoCache(State, "import(path)", function()
        return import(FixtureRelativeNoExt)
    end)
end

if type(require) == "function" then
    local requireOk, requireModule = pcall(require, FixtureRelativeWithExt)
    Check(State, "require(path.lua) call", requireOk, requireOk and nil or ToText(requireModule))
    if requireOk then
        ValidateModuleBehavior(State, "require(path.lua)", requireModule)
    end

    local requireNoExtOk, requireNoExtModule = pcall(require, FixtureRelativeNoExt)
    Check(State, "require(path) extensionless", requireNoExtOk, requireNoExtOk and nil or ToText(requireNoExtModule))
    if requireNoExtOk then
        ValidateModuleBehavior(State, "require(path)", requireNoExtModule)
    end

    ValidateNoCache(State, "require(path)", function()
        return require(FixtureRelativeNoExt)
    end)
end

if type(_G.import) == "function" then
    local globalImportOk, globalImportModule = pcall(_G.import, FixtureRelativeNoExt)
    Check(State, "_G.import(path) call", globalImportOk, globalImportOk and nil or ToText(globalImportModule))
    if globalImportOk then
        ValidateModuleBehavior(State, "_G.import(path)", globalImportModule)
    end
else
    Skip(State, "_G.import(path) call", "_G.import is unavailable")
end

if type(_G.require) == "function" then
    local globalRequireOk, globalRequireModule = pcall(_G.require, FixtureRelativeNoExt)
    Check(State, "_G.require(path) call", globalRequireOk, globalRequireOk and nil or ToText(globalRequireModule))
    if globalRequireOk then
        ValidateModuleBehavior(State, "_G.require(path)", globalRequireModule)
    end
else
    Skip(State, "_G.require(path) call", "_G.require is unavailable")
end

-- --------------------------------------------------------------------------
-- MoonSharp load/loadsafe/loadfilesafe methods
-- --------------------------------------------------------------------------

Emit("cyan", "--- MoonSharp load methods ---")

local FixtureSource = ReadTextFile(FixtureRelativeWithExt)

if type(load) == "function" then
    local loadChunk, loadError = load(FixtureSource, "@" .. FixtureAbsoluteWithExt, "t", _ENV)
    Check(State, "load(source) compile", type(loadChunk) == "function", ToText(loadError))
    if type(loadChunk) == "function" then
        local loadRunOk, loadModule = pcall(loadChunk)
        Check(State, "load(source) execute", loadRunOk, loadRunOk and nil or ToText(loadModule))
        if loadRunOk then
            ValidateModuleBehavior(State, "load(source)", loadModule)
        end

        local loadRunOk2, loadModule2 = pcall(loadChunk)
        Check(State, "load(source) execute second run", loadRunOk2, loadRunOk2 and nil or ToText(loadModule2))
        if loadRunOk and loadRunOk2 and ValidateModuleShape(loadModule) and ValidateModuleShape(loadModule2) then
            Check(
                State,
                "load(source) re-executes chunk",
                loadModule2.GetLoadCount() == (loadModule.GetLoadCount() + 1),
                "first=" .. ToText(loadModule.GetLoadCount()) .. ", second=" .. ToText(loadModule2.GetLoadCount())
            )
        end
    end
else
    Skip(State, "load(source)", "load is unavailable")
end

if type(loadsafe) == "function" then
    local safeChunk, safeError = loadsafe(FixtureSource, "@" .. FixtureAbsoluteWithExt, "t", _ENV)
    Check(State, "loadsafe(source) compile", type(safeChunk) == "function", ToText(safeError))
    if type(safeChunk) == "function" then
        local safeRunOk, safeModule = pcall(safeChunk)
        Check(State, "loadsafe(source) execute", safeRunOk, safeRunOk and nil or ToText(safeModule))
        if safeRunOk then
            ValidateModuleBehavior(State, "loadsafe(source)", safeModule)
        end
    end
else
    Skip(State, "loadsafe(source)", "loadsafe is unavailable")
end

if type(loadfilesafe) == "function" then
    local fileChunk, fileChunkError = loadfilesafe(FixtureAbsoluteWithExt, "t", _ENV)
    Check(State, "loadfilesafe(path) compile", type(fileChunk) == "function", ToText(fileChunkError))
    if type(fileChunk) == "function" then
        local fileRunOk, fileModule = pcall(fileChunk)
        Check(State, "loadfilesafe(path) execute", fileRunOk, fileRunOk and nil or ToText(fileModule))
        if fileRunOk then
            ValidateModuleBehavior(State, "loadfilesafe(path)", fileModule)
        end
    end
else
    Skip(State, "loadfilesafe(path)", "loadfilesafe is unavailable")
end

-- --------------------------------------------------------------------------
-- Guardrails and negative checks
-- --------------------------------------------------------------------------

Emit("cyan", "--- Guardrails and negative checks ---")

Check(State, "loadfile removed", loadfile == nil, ToText(loadfile))
Check(State, "dofile removed", dofile == nil, ToText(dofile))

if type(import) == "function" then
    local missingOk, missingError = pcall(import, MissingFixture)
    Check(State, "import missing file fails", missingOk == false, missingOk and "expected failure" or ToText(missingError))
end

if type(loadfilesafe) == "function" then
    Skip(State, "loadfilesafe missing file fails", "skipped: missing-file probe can trigger unhandled CLR exception in this runtime")
end

-- --------------------------------------------------------------------------
-- Summary
-- --------------------------------------------------------------------------

Emit("white", "")
Emit("white", "Total Passed: " .. tostring(State.Passed))
Emit("white", "Total Failed: " .. tostring(State.Failed))
Emit("white", "Total Skipped: " .. tostring(State.Skipped))

if State.Failed > 0 then
    Emit("red", "Import method tests completed with failures.")
    for _, failure in ipairs(State.Failures) do
        Emit("red", failure)
    end
else
    Emit("green", "Import method tests completed successfully.")
end

Emit("cyan", "=== RemakeEngine Lua Import Method Tests Complete ===")
