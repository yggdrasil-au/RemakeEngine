-- Lua feature showcase for the RemakeEngine demo module.
-- Demonstrates prompts, progress reporting, and EngineSdk event emission.

local args = {...}

local moduleRoot = "."
local scratchRoot = nil
local note = "Lua demo note"

for index = 1, #args do
    local value = args[index]
    if value == "--module" and args[index + 1] then
        moduleRoot = args[index + 1]
    elseif value == "--scratch" and args[index + 1] then
        scratchRoot = args[index + 1]
    elseif value == "--note" and args[index + 1] then
        note = args[index + 1]
    end
end

if not scratchRoot or scratchRoot == "" then
    scratchRoot = moduleRoot .. "/TMP/lua-demo"
end

emit("start", { language = "lua", step = "initialise" })
sdk.ensure_dir(scratchRoot)
sdk.ensure_dir(scratchRoot .. "/logs")
sdk.colour_print({ colour = "cyan", message = "Lua demo scratch workspace: " .. scratchRoot })

local userNote = prompt("Enter a note for the Lua demo log", "lua_note_prompt", false)
if userNote and #userNote > 0 then
    note = userNote
end

local progressHandle = progress(3, "lua-demo", "Lua feature demo")
progressHandle:Update()

local function safe_write(path, contents)
    local file, err = io.open(path, "w")
    if not file then
        emit("warning", { language = "lua", message = "Failed to write file: " .. (err or path) })
        return nil
    end
    file:write(contents)
    file:close()
    return path
end

local logPath = scratchRoot .. "/lua_demo.txt"
local logContents = table.concat({
    "Module root: " .. moduleRoot,
    "Scratch root: " .. scratchRoot,
    "Note: " .. note
}, "\n") .. "\n"
safe_write(logPath, logContents)
emit("info", { language = "lua", log = logPath })
progressHandle:Update()

local function escapeJson(str)
    local result = str or ""
    result = result:gsub("\\", "\\\\")
    result = result:gsub("\"", "\\\"")
    result = result:gsub("\n", "\\n")
    return result
end

local summaryPath = scratchRoot .. "/lua_demo.json"
local summaryLines = {
    "{",
    string.format('  "note": "%s",', escapeJson(note)),
    string.format('  "module": "%s",', escapeJson(moduleRoot)),
    string.format('  "scratch": "%s",', escapeJson(scratchRoot)),
    string.format('  "timestamp": "%s"', escapeJson(os.date("!%Y-%m-%dT%H:%M:%SZ"))),
    "}",
    ""
}
safe_write(summaryPath, table.concat(summaryLines, "\n"))
progressHandle:Update()

emit("lua-demo-complete", {
    language = "lua",
    artifacts = {
        log = logPath,
        summary = summaryPath
    }
})

emit("end", { success = true, exit_code = 0 })