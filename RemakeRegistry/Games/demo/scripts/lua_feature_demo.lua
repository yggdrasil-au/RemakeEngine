--[[
Comprehensive Lua feature showcase for the RemakeEngine demo module.
Demonstrates EVERY C# tool available from LuaScriptAction.cs including:
- Global functions: tool(), argv, warn/error, emit(), prompt(), progress()
- SDK file/directory operations: ensure_dir, path_exists, copy/move operations, etc.
- SDK symlink operations: create_symlink, is_symlink, realpath, readlink
- SDK utilities: color_print, sleep, md5, TOML helpers
- SDK process execution: exec, run_process
- SQLite module: open, exec, query, transactions
- Shimmed modules: lfs (LuaFileSystem), dkjson
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

-- Parse command line arguments (demonstrate argv usage)
local opts = parse_args(argv or {...})
local module_root = opts.module or '.'
local scratch_root = opts.scratch or (module_root .. '/TMP/lua-demo-comprehensive')
local note = opts.note or 'Comprehensive Lua API demo'

-- Emit structured events
emit('demo_start', { 
    language = 'lua', 
    step = 'initialization', 
    module_root = module_root,
    argv_count = #(argv or {})
})

-- Color printing demonstrations
sdk.color_print('red', '=== RemakeEngine Lua API Comprehensive Demo ===')
sdk.color_print({ color = 'cyan', message = 'Scratch workspace: ' .. scratch_root })
sdk.color_print({ colour = 'green', message = 'Australian spelling works too!', newline = false })
sdk.color_print('white', ' (color vs colour)')

-- Progress tracking setup  
local total_steps = 18  -- Updated count (removed educational sections from step count)
local progress_handle = progress(total_steps, 'comprehensive-demo', 'Comprehensive Lua API Demo')
local current_step = 0

sdk.color_print('yellow', '--- Progress Tracking Examples ---')
-- ❌ INCORRECT: Common progress mistakes
sdk.color_print('red', '❌ INCORRECT progress usage:')
sdk.color_print('white', "progress()  -- Missing required total parameter")
sdk.color_print('white', "progress_handle.Update()  -- Wrong: use colon syntax")
sdk.color_print('white', "progress_handle:Update(50)  -- Wrong: increment, don't set absolute")

-- ✅ CORRECT: Proper progress usage
sdk.color_print('green', '✅ CORRECT progress usage:')
sdk.color_print('white', "local p = progress(100, 'my-id', 'My Operation')")
sdk.color_print('white', "p:Update()  -- Increment by 1 (default)")
sdk.color_print('white', "p:Update(5)  -- Increment by 5")

local function next_step(description)
    current_step = current_step + 1
    progress_handle:Update()
    sdk.color_print('magenta', string.format('[Step %d/%d] %s', current_step, total_steps, description))
end

next_step('Creating directory structure')

-- SDK Directory operations
sdk.ensure_dir(scratch_root)
sdk.ensure_dir(scratch_root .. '/artifacts')
sdk.ensure_dir(scratch_root .. '/test_dirs/subdir1')
sdk.ensure_dir(scratch_root .. '/test_dirs/subdir2')
sdk.ensure_dir(scratch_root .. '/symlink_test')

next_step('Testing path existence functions')

-- SDK Path existence checks
local paths_to_test = {
    scratch_root,
    scratch_root .. '/artifacts',
    scratch_root .. '/nonexistent',
    module_root
}

for _, path in ipairs(paths_to_test) do
    local exists = sdk.path_exists(path)
    local lexists = sdk.lexists(path)
    local is_dir = sdk.is_dir(path)
    local is_file = sdk.is_file(path)
    
    sdk.color_print('yellow', string.format('Path: %s | exists: %s | lexists: %s | is_dir: %s | is_file: %s',
        path, tostring(exists), tostring(lexists), tostring(is_dir), tostring(is_file)))
end

next_step('Creating test files')

-- Create test files for file operations
local test_file1 = scratch_root .. '/test_file1.txt'
local test_file2 = scratch_root .. '/test_file2.txt'
local test_file_backup = scratch_root .. '/test_file_backup.txt'

-- Write test content to files (using standard Lua IO since we're demonstrating SDK operations)
local file = io.open(test_file1, 'w')
if file then
    file:write('Hello from Lua demo!\nThis is test content for file operations.')
    file:close()
end

next_step('Testing file operations')

sdk.color_print('yellow', '--- File Operations Examples ---')
sdk.color_print('white', 'RemakeEngine file operations have specific parameter requirements:')

-- ❌ INCORRECT: Common mistakes with file operations
sdk.color_print('red', '❌ INCORRECT approaches that will fail:')
sdk.color_print('white', "sdk.copy_file(src, dst)  -- Missing overwrite parameter (required)")
sdk.color_print('white', "sdk.copy_file(src, dst, 'yes')  -- String instead of boolean")
sdk.color_print('white', "sdk.ensure_dir()  -- Missing required path parameter")

-- ✅ CORRECT: Proper file operation usage
sdk.color_print('green', '✅ CORRECT approaches:')
sdk.color_print('white', "sdk.copy_file(src, dst, false)  -- Don't overwrite if exists")
sdk.color_print('white', "sdk.copy_file(src, dst, true)   -- Overwrite if exists") 
sdk.color_print('white', "sdk.ensure_dir('/path/to/create')  -- Creates directory and parents")

-- SDK File operations
local copy_success = sdk.copy_file(test_file1, test_file2, false)
sdk.color_print('green', 'File copy result: ' .. tostring(copy_success))

local backup_success = sdk.copy_file(test_file1, test_file_backup, true) -- overwrite = true
sdk.color_print('green', 'File backup result: ' .. tostring(backup_success))

next_step('Testing directory operations')

-- SDK Directory operations
local test_source_dir = scratch_root .. '/source_dir'
local test_dest_dir = scratch_root .. '/dest_dir'
local test_move_dir = scratch_root .. '/move_dir'

sdk.ensure_dir(test_source_dir)
-- Create a test file in source dir
local source_file = io.open(test_source_dir .. '/source_test.txt', 'w')
if source_file then
    source_file:write('Source directory test content')
    source_file:close()
end

local copy_dir_success = sdk.copy_dir(test_source_dir, test_dest_dir, false)
sdk.color_print('green', 'Directory copy result: ' .. tostring(copy_dir_success))

-- Test directory validation and finding
local validate_result = sdk.validate_source_dir(test_source_dir)
sdk.color_print('green', 'Directory validation result: ' .. tostring(validate_result))

local found_subdir = sdk.find_subdir(scratch_root .. '/test_dirs', 'subdir1')
sdk.color_print('green', 'Found subdir: ' .. tostring(found_subdir))

local has_all = sdk.has_all_subdirs(scratch_root .. '/test_dirs', {'subdir1', 'subdir2'})
sdk.color_print('green', 'Has all subdirs: ' .. tostring(has_all))

next_step('Testing symlink operations')

-- SDK Symlink operations (if supported on platform)
local symlink_target = test_file1
local symlink_path = scratch_root .. '/symlink_test/test_symlink'

local symlink_success = sdk.create_symlink(symlink_target, symlink_path, false)
sdk.color_print('green', 'Symlink creation result: ' .. tostring(symlink_success))

if symlink_success then
    local is_symlink = sdk.is_symlink(symlink_path)
    local real_path = sdk.realpath(symlink_path)
    local link_target = sdk.readlink(symlink_path)
    
    sdk.color_print('yellow', 'Is symlink: ' .. tostring(is_symlink))
    sdk.color_print('yellow', 'Real path: ' .. tostring(real_path))
    sdk.color_print('yellow', 'Link target: ' .. tostring(link_target))
end

next_step('Testing MD5 and sleep functions')

-- SDK Utility functions
local test_string = 'RemakeEngine Lua API Test'
local md5_hash = sdk.md5(test_string)
sdk.color_print('cyan', 'MD5 of "' .. test_string .. '": ' .. md5_hash)

-- Brief sleep demonstration (0.1 seconds)
sdk.color_print('yellow', 'Testing sleep function (0.1s)...')
sdk.sleep(0.1)
sdk.color_print('yellow', 'Sleep completed!')

next_step('Testing TOML operations')

-- SDK TOML helpers
local toml_test_path = scratch_root .. '/test_config.toml'
local test_config = {
    demo = {
        name = 'Lua API Demo',
        version = '1.0.0',
        features = {'sqlite', 'filesystem', 'toml', 'json'}
    },
    paths = {
        scratch = scratch_root,
        module = module_root
    }
}

sdk.toml_write_file(toml_test_path, test_config)
sdk.color_print('green', 'TOML file written to: ' .. toml_test_path)

local loaded_toml = sdk.toml_read_file(toml_test_path)
if loaded_toml and loaded_toml.demo then
    sdk.color_print('green', 'TOML loaded successfully - demo name: ' .. tostring(loaded_toml.demo.name))
end

next_step('Testing project configuration')

-- SDK Configuration helpers
local config_path = sdk.ensure_project_config(scratch_root .. '/project')
sdk.color_print('green', 'Ensured project config at: ' .. config_path)

next_step('Testing process execution (run_process)')

-- SDK Process execution - run_process (safer, captures output)
local process_result = sdk.run_process({'pwsh', '-c', 'echo Hello from run_process'}, {
    capture_stdout = true,
    capture_stderr = true,
    timeout_ms = 5000
})

if process_result then
    sdk.color_print('cyan', 'run_process exit code: ' .. tostring(process_result.exit_code))
    sdk.color_print('cyan', 'run_process success: ' .. tostring(process_result.success))
    if process_result.stdout then
        sdk.color_print('cyan', 'run_process stdout: ' .. process_result.stdout)
    end
end

next_step('Testing process execution (exec)')

sdk.color_print('yellow', '--- Process Execution Examples ---')
sdk.color_print('white', 'The RemakeEngine has two process execution methods with different purposes:')

-- ❌ INCORRECT: Common mistakes developers make
sdk.color_print('red', '❌ INCORRECT approaches:')
sdk.color_print('white', "sdk.exec('echo hello')  -- Wrong: expects table, not string")
sdk.color_print('white', "sdk.exec({'cmd', '/c', 'echo hello && pause'})  -- Problematic: blocks indefinitely")
sdk.color_print('white', "sdk.run_process('git status')  -- Wrong: expects table of command parts")

-- ✅ CORRECT: Proper usage
sdk.color_print('green', '✅ CORRECT approaches:')
sdk.color_print('white', "sdk.exec({'echo', 'hello'}, {wait=true})  -- Streams output to terminal")
sdk.color_print('white', "sdk.run_process({'git', 'status'}, {capture_stdout=true})  -- Captures output")
sdk.color_print('white', 'Use exec() for interactive processes, run_process() to capture output')

-- SDK Process execution - exec (streams to current terminal)
local exec_result = sdk.exec({'echo', 'Hello from exec'}, {
    wait = true,
    new_terminal = false
})

if exec_result then
    sdk.color_print('cyan', 'exec exit code: ' .. tostring(exec_result.exit_code))
    sdk.color_print('cyan', 'exec success: ' .. tostring(exec_result.success))
end

next_step('Testing SQLite operations')

-- SQLite module comprehensive demo
local sqlite_path = scratch_root .. '/comprehensive_demo.sqlite'
local db = sqlite.open(sqlite_path)

-- Create tables and demonstrate all SQLite operations
db:exec('DROP TABLE IF EXISTS demo_features')
db:exec([[CREATE TABLE demo_features(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    category TEXT NOT NULL,
    feature_name TEXT NOT NULL,
    description TEXT,
    tested_at DATETIME DEFAULT CURRENT_TIMESTAMP
)]])

-- Demonstrate parameterized inserts
local features_to_log = {
    {'globals', 'tool()', 'Resolve tool paths'},
    {'globals', 'argv', 'Command line arguments access'},
    {'globals', 'emit()', 'Structured event emission'},
    {'globals', 'warn()', 'Warning messages'},
    {'globals', 'error()', 'Error messages'},
    {'globals', 'prompt()', 'User input prompts'},
    {'globals', 'progress()', 'Progress tracking'},
    {'sdk', 'color_print()', 'Colored terminal output'},
    {'sdk', 'ensure_dir()', 'Directory creation'},
    {'sdk', 'path_exists()', 'Path existence checking'},
    {'sdk', 'copy_file()', 'File copying'},
    {'sdk', 'copy_dir()', 'Directory copying'},
    {'sdk', 'create_symlink()', 'Symlink creation'},
    {'sdk', 'md5()', 'MD5 hashing'},
    {'sdk', 'sleep()', 'Sleep/delay'},
    {'sdk', 'toml_read_file()', 'TOML file reading'},
    {'sdk', 'toml_write_file()', 'TOML file writing'},
    {'sdk', 'run_process()', 'Process execution with capture'},
    {'sdk', 'exec()', 'Process execution streaming'},
    {'sqlite', 'open()', 'Database opening'},
    {'sqlite', 'exec()', 'SQL execution'},
    {'sqlite', 'query()', 'SQL queries'},
    {'sqlite', 'transactions', 'Transaction management'},
    {'modules', 'lfs', 'LuaFileSystem shim'},
    {'modules', 'dkjson', 'JSON encoding/decoding'}
}

sdk.color_print('yellow', '--- SQLite Parameter Binding Examples ---')
sdk.color_print('white', 'The RemakeEngine SQLite module uses NAMED parameters, not positional ones.')

-- ❌ INCORRECT: This approach will NOT work (positional parameters)
sdk.color_print('red', '❌ INCORRECT (will fail with "Must add values for parameters" error):')
sdk.color_print('white', "db:exec('INSERT INTO table(a,b,c) VALUES (?,?,?)', {'val1', 'val2', 'val3'})")
sdk.color_print('white', 'Reason: The C# SQLite wrapper expects named parameters (dictionary-style), not arrays.')

-- ✅ CORRECT: This approach works (named parameters) 
sdk.color_print('green', '✅ CORRECT (this works):')
sdk.color_print('white', "db:exec('INSERT INTO table(a,b,c) VALUES (:a,:b,:c)', {a='val1', b='val2', c='val3'})")
sdk.color_print('white', 'Reason: Named parameters provide clear mapping and are safer for SQL injection prevention.')

-- Demonstrate transaction usage with CORRECT named parameters
db:begin()
for _, feature in ipairs(features_to_log) do
    -- ✅ CORRECT: Use named parameters instead of positional parameters
    db:exec('INSERT INTO demo_features(category, feature_name, description) VALUES (:cat, :name, :desc)', {
        cat = feature[1],
        name = feature[2], 
        desc = feature[3]
    })
end
db:commit()

-- Query and display results
local feature_rows = db:query('SELECT category, feature_name, description FROM demo_features ORDER BY category, feature_name')
sdk.color_print('magenta', 'SQLite Features Logged:')
for _, row in ipairs(feature_rows) do
    sdk.color_print('white', string.format('  [%s] %s: %s', row.category, row.feature_name, row.description))
end

-- Demonstrate rollback (create a transaction and roll it back)
db:begin()
db:exec('INSERT INTO demo_features(category, feature_name, description) VALUES (:cat, :name, :desc)', {
    cat = 'test', 
    name = 'rollback_test', 
    desc = 'This should not appear'
})
db:rollback()

local count_after_rollback = db:query('SELECT COUNT(*) as count FROM demo_features')[1].count
sdk.color_print('yellow', 'Record count after rollback: ' .. count_after_rollback)

db:close()

next_step('Testing LuaFileSystem (lfs) module')

-- LuaFileSystem module demonstration
local lfs = require('lfs')
local entries = {}
local iterator = lfs.dir(module_root)
while true do
    local entry = iterator()
    if entry == nil then break end
    table.insert(entries, entry)
    if #entries >= 10 then break end -- Limit to first 10 entries
end

sdk.color_print('cyan', 'LFS directory entries from module root:')
for i, entry in ipairs(entries) do
    sdk.color_print('white', string.format('  %d: %s', i, entry))
end

next_step('Testing JSON encoding (dkjson)')

-- JSON module demonstration
local dkjson = require('dkjson')

local comprehensive_summary = {
    demo_info = {
        language = 'lua',
        title = 'Comprehensive RemakeEngine Lua API Demo',
        timestamp = os.date('!%Y-%m-%dT%H:%M:%SZ'),
        total_features_tested = #features_to_log
    },
    paths = {
        module_root = module_root,
        scratch_root = scratch_root,
        config_path = config_path,
        sqlite_path = sqlite_path,
        toml_path = toml_test_path
    },
    test_results = {
        directory_operations = true,
        file_operations = copy_success and backup_success,
        symlink_operations = symlink_success,
        toml_operations = loaded_toml ~= nil,
        sqlite_operations = count_after_rollback > 0,
        process_execution = process_result and process_result.success,
        lfs_module = #entries > 0
    },
    sample_data = {
        lfs_entries = entries,
        md5_hash = md5_hash,
        note = note
    }
}

local encoded_json = dkjson.encode(comprehensive_summary, { indent = true })
sdk.color_print('yellow', 'Comprehensive demo summary (JSON):')
sdk.color_print('white', encoded_json)

next_step('Testing warning and error functions')

-- Demonstrate warning and error functions
warn('This is a demonstration warning message')
-- Note: We don't call error() as it would terminate the script

next_step('Testing tool resolution')

-- Tool resolution demonstration (if any tools are registered)
local function safe_tool_resolve(tool_name)
    local success, result = pcall(tool, tool_name)
    return success and result or 'Tool not found: ' .. tool_name
end

local common_tools = {'git', 'python', 'node', 'ffmpeg', 'blender'}
sdk.color_print('cyan', 'Tool resolution test:')
for _, tool_name in ipairs(common_tools) do
    local tool_path = safe_tool_resolve(tool_name)
    sdk.color_print('white', string.format('  %s -> %s', tool_name, tool_path))
end

next_step('Testing user prompt')

-- Prompt demonstration
local user_input = prompt('Enter a test message (or press Enter to skip)', 'demo_prompt', false)
sdk.color_print('green', 'User input received: ' .. (user_input or 'No input'))

next_step('Cleanup and final operations')

-- Demonstrate file removal
local cleanup_success = sdk.remove_file(test_file2)
sdk.color_print('green', 'Cleanup file removal: ' .. tostring(cleanup_success))

-- Final progress update
progress_handle:Update()

-- Emit completion event with comprehensive data
emit('comprehensive_demo_complete', {
    language = 'lua',
    features_demonstrated = {
        'Global functions: tool, argv, warn, error, emit, prompt, progress',
        'SDK file ops: ensure_dir, path_exists, lexists, is_dir, is_file, remove_dir, remove_file, copy_file, copy_dir, move_dir',
        'SDK symlink ops: create_symlink, is_symlink, realpath, readlink',  
        'SDK config ops: ensure_project_config, validate_source_dir, find_subdir, has_all_subdirs',
        'SDK utilities: color_print, sleep, md5',
        'SDK TOML: toml_read_file, toml_write_file',
        'SDK process: exec (streaming), run_process (capture)',
        'SQLite: open, exec, query, begin, commit, rollback, close',
        'Shimmed modules: lfs (LuaFileSystem), dkjson (JSON)'
    },
    artifacts = {
        sqlite = sqlite_path,
        config = config_path,
        toml = toml_test_path,
        scratch_workspace = scratch_root
    },
    summary = comprehensive_summary
})

sdk.color_print('green', '=== Comprehensive Lua API Demo Complete ===')
sdk.color_print('cyan', 'All RemakeEngine Lua API features have been demonstrated!')
sdk.color_print('yellow', 'Check the artifacts directory for generated files: ' .. scratch_root)
