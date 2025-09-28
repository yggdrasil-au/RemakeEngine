--[[
Lua feature showcase for the RemakeEngine demo module.
Exercises the helper APIs exposed to embedded scripts: progress, SDK helpers,
JSON encoding, sqlite access, and event emission.
--]]

local function parse_args(list)
    local opts = { extras = {} }
    local i = 1
    while i <= #list do
        local key = list[i]
        if key == '--module' and list[i + 1] then
            opts.module = list[i + 1]
            i = i + 2
        elseif key == '--scratch' and list[i + 1] then
            opts.scratch = list[i + 1]
            i = i + 2
        elseif key == '--note' and list[i + 1] then
            opts.note = list[i + 1]
            i = i + 2
        else
            table.insert(opts.extras, key)
            i = i + 1
        end
    end
    return opts
end

local opts = parse_args({...})
local module_root = opts.module or '.'
local scratch_root = opts.scratch or (module_root .. '/TMP/lua-demo')
local note = opts.note or 'Lua demo note (default)'

emit('info', { language = 'lua', step = 'start', module_root = module_root })

sdk.ensure_dir(scratch_root)
sdk.ensure_dir(scratch_root .. '/artifacts')
sdk.color_print({ color = 'cyan', message = 'Lua demo scratch workspace: ' .. scratch_root })

local progress_handle = progress(4, 'lua-demo', 'Lua feature showcase')
progress_handle:Update()

local config_path = sdk.ensure_project_config(scratch_root .. '/project')
sdk.color_print({ color = 'green', message = 'Ensured project config at ' .. config_path })
progress_handle:Update()

local lfs = require('lfs')
local entries = {}
local iterator = lfs.dir(module_root)
while true do
    local entry = iterator()
    if entry == nil then break end
    table.insert(entries, entry)
    if #entries >= 5 then break end
end

local sqlite_path = scratch_root .. '/lua_demo.sqlite'
local db = sqlite.open(sqlite_path)
db:exec('CREATE TABLE IF NOT EXISTS feature_log(id INTEGER PRIMARY KEY AUTOINCREMENT, category TEXT, message TEXT)')
db:exec('INSERT INTO feature_log(category, message) VALUES (:category, :message)', { category = 'sdk', message = 'Created demo workspace at ' .. scratch_root })
db:exec('INSERT INTO feature_log(category, message) VALUES (:category, :message)', { category = 'note', message = note })
db:exec('INSERT INTO feature_log(category, message) VALUES (:category, :message)', { category = 'entries', message = 'Collected ' .. tostring(#entries) .. ' directory entries' })
progress_handle:Update()

local rows = db:query('SELECT id, category, message FROM feature_log ORDER BY id ASC')
for _, row in ipairs(rows) do
    emit('info', {
        language = 'lua',
        id = row.id,
        category = row.category,
        message = row.message
    })
end
db:close()

local summary = {
    language = 'lua',
    module_root = module_root,
    scratch = scratch_root,
    config_path = config_path,
    sqlite_path = sqlite_path,
    sample_entries = entries,
    note = note,
    timestamp = os.date('!%Y-%m-%dT%H:%M:%SZ')
}

local dkjson = require('dkjson')
local encoded = dkjson.encode(summary, { indent = true })
sdk.color_print({ color = 'yellow', message = encoded })

progress_handle:Update()
emit('lua-demo-complete', {
    language = 'lua',
    artifacts = {
        sqlite = sqlite_path,
        config = config_path
    }
})
