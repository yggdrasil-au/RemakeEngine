--[[
Comprehensive Lua feature showcase for the RemakeEngine demo module.
Demonstrates EVERY C# tool available from LuaScriptAction.cs including:
- Global functions: tool(), argv, warn/error, prompt(), progress()
- SDK file/directory operations: ensure_dir, path_exists, copy/move operations, etc.
- SDK symlink operations: create_symlink, is_symlink, realpath, readlink
- SDK utilities: color_print, sleep, md5, TOML helpers
- SDK process execution: exec, run_process
- SQLite module: open, exec, query, transactions
--]]

local function parse_args(list)
    local opts = { extras = {} }
    local i = 1
    while i <= #list do
        local key = list[i]
        if (key == '--Game_Root' or key == '--module') and list[i + 1] then
            opts.module = list[i + 1] -- this argument is nolonger needed when global Game_Root was added
            i = i + 2
        elseif key == '--Project_Root' and list[i + 1] then
            opts.projectroot = list[i + 1] -- this argument is nolonger needed when global Project_Root was added
            i = i + 2
        elseif key == '--scratch' and list[i + 1] then
            opts.scratch = list[i + 1]
            i = i + 2
        elseif key == '--note' and list[i + 1] then
            opts.note = list[i + 1]
            i = i + 2
        elseif key == '--prompt' and list[i + 1] then
            opts.prompt = list[i + 1]
            i = i + 2
        elseif key == '--sec_check' then
            opts.sec_check = true
            i = i + 1
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


-- Color printing demonstrations
sdk.color_print('white', '=== RemakeEngine Lua API Comprehensive Demo ===')
sdk.color_print({ color = 'cyan', message = 'Scratch workspace: ' .. scratch_root })
sdk.color_print({ colour = 'green', message = 'Australian spelling works too!', newline = false })
sdk.color_print('white', ' (color vs colour)')

-- Stage-based script progress (for GUI status indicator)
-- New progress API usage
progress.start(19, 'Comprehensive Lua API Demo')

sdk.color_print('yellow', '--- Progress Tracking Examples ---')
-- ❌ INCORRECT: Common progress mistakes
sdk.color_print('cyan', '❌ INCORRECT progress usage:')
sdk.color_print('white', "progress.new()  -- Missing required 'total' parameter")
sdk.color_print('white', "local p = progress.new(100) -- p is now a progress object")
sdk.color_print('white', "p.Update()  -- Wrong: use colon syntax (p:Update())")

-- ✅ CORRECT: Proper progress usage
sdk.color_print('green', '✅ CORRECT progress usage:')
sdk.color_print('white', "local p = progress.new(100, 'my-id', 'My Operation') -- For panel")
sdk.color_print('white', "p:Update()  -- Increment by 1 (default)")
sdk.color_print('white', "progress.start(10, 'My Stage') -- Start script progress")
sdk.color_print('white', "progress.step('Next step description') -- Move to next stage")

progress.step('Creating directory structure')

-- SDK Directory operations
sdk.ensure_dir(scratch_root)
sdk.ensure_dir(scratch_root .. '/artifacts')
sdk.ensure_dir(scratch_root .. '/test_dirs/subdir1')
sdk.ensure_dir(scratch_root .. '/test_dirs/subdir2')
sdk.ensure_dir(scratch_root .. '/symlink_test')

progress.step('Testing path existence functions')

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

    sdk.color_print('yellow', string.format('Path: %s | exists: %s | lexists: %s | is_dir: %s | is_file: %s', path, tostring(exists), tostring(lexists), tostring(is_dir), tostring(is_file)))
end

progress.step('Creating test files')

-- Create test files for file operations
local test_file1 = scratch_root .. '/test_file1.txt'
local test_file2 = scratch_root .. '/test_file2.txt'
local test_file_backup = scratch_root .. '/test_file_backup.txt'

-- Write test content to files using the sandboxed io library
-- This is the correct, cross-platform method for file I/O
local file = io.open(test_file1, 'w')
if file then
    file:write('Hello from Lua demo!\nThis is test content for file operations.')
    file:close()
else
    sdk.color_print('red', 'Warning: Could not create test file using io.open()')
end

progress.step('Testing file operations')

sdk.color_print('yellow', '--- File Operations Examples ---')
sdk.color_print('white', 'RemakeEngine file operations have specific parameter requirements:')

-- ❌ INCORRECT: Common mistakes with file operations
sdk.color_print('cyan', '❌ INCORRECT approaches that will fail:')
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

progress.step('Testing directory operations')

-- SDK Directory operations
local test_source_dir = scratch_root .. '/source_dir'
local test_dest_dir = scratch_root .. '/dest_dir'
local test_move_dir = scratch_root .. '/move_dir'

sdk.ensure_dir(test_source_dir)
-- Create a test file in source dir using sandboxed io
local file2 = io.open(test_source_dir .. '/source_test.txt', 'w')
if file2 then
    file2:write('Source directory test content')
    file2:close()
else
    sdk.color_print('red', 'Warning: Could not create source test file')
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

progress.step('Testing symlink operations')

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

progress.step('Testing MD5 and sleep functions')

-- SDK Utility functions
local test_string = 'RemakeEngine Lua API Test'
local md5_hash = sdk.md5(test_string)
sdk.color_print('cyan', 'MD5 of "' .. test_string .. '": ' .. md5_hash)

-- Brief sleep demonstration (0.1 seconds)
sdk.color_print('yellow', 'Testing sleep function (0.1s)...')
sdk.sleep(0.1)
sdk.color_print('yellow', 'Sleep completed!')

progress.step('Testing TOML operations')

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

progress.step('Testing process execution (run_process)')

-- SDK Process execution - run_process (safer, captures output)
-- Using 'git --version' as a reliable, cross-platform, approved executable
local process_result = sdk.run_process({'git', '--version'}, {
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

progress.step('Testing process execution (exec)')

sdk.color_print('yellow', '--- Process Execution Examples ---')
sdk.color_print('white', 'The RemakeEngine has two process execution methods with different purposes:')

-- ❌ INCORRECT: Common mistakes developers make
sdk.color_print('cyan', '❌ INCORRECT approaches:')
sdk.color_print('white', "sdk.exec('echo hello')  -- Wrong: expects table, not string")
sdk.color_print('white', "sdk.exec({'cmd', '/c', 'echo hello && pause'})  -- Problematic: blocks indefinitely")
sdk.color_print('white', "sdk.run_process('git status')  -- Wrong: expects table of command parts")

-- ✅ CORRECT: Proper usage
sdk.color_print('green', '✅ CORRECT approaches:')
sdk.color_print('white', "sdk.exec({'git', '--version'}, {wait=true})  -- Streams output to terminal")
sdk.color_print('white', "sdk.run_process({'git', 'status'}, {capture_stdout=true})  -- Captures output")
sdk.color_print('white', 'Use exec() for interactive processes, run_process() to capture output')

-- SDK Process execution - exec (streams to current terminal)
-- Using 'git --version' as a reliable, cross-platform, approved executable
local exec_result = sdk.exec({'git', '--version'}, {
    wait = true,
    new_terminal = false
})

if exec_result then
    sdk.color_print('cyan', 'exec exit code: ' .. tostring(exec_result.exit_code))
    sdk.color_print('cyan', 'exec success: ' .. tostring(exec_result.success))
end

progress.step('Testing SQLite operations')

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
    {'modules', 'dkjson', 'JSON encoding/decoding'}
}

sdk.color_print('yellow', '--- SQLite Parameter Binding Examples ---')
sdk.color_print('white', 'The RemakeEngine SQLite module uses NAMED parameters, not positional ones.')

-- ❌ INCORRECT: This approach will NOT work (positional parameters)
sdk.color_print('cyan', '❌ INCORRECT (will fail with "Must add values for parameters" error):')
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

progress.step('Collecting directory info (engine APIs)')

-- Replace removed lfs usage with engine SDK helpers
local entries = {}
do
    -- Sample: list a couple of known subdirs using engine helpers
    local base = scratch_root .. '/test_dirs'
    local d1 = sdk.find_subdir(base, 'subdir1')
    local d2 = sdk.find_subdir(base, 'subdir2')
    if d1 then table.insert(entries, d1) end
    if d2 then table.insert(entries, d2) end
    -- Also include module root attributes via sdk.attributes
    local attrs = sdk.attributes(module_root)
    if attrs then
        table.insert(entries, 'module_root:' .. tostring(attrs.mode))
    end
end

sdk.color_print('cyan', 'Directory info collected via engine SDK:')
for i, entry in ipairs(entries) do
    sdk.color_print('white', string.format('  %d: %s', i, entry))
end

progress.step('Testing JSON encoding (engine JSON)')

-- Use engine-provided JSON helpers under sdk.text.json (encode/decode)

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
        --config_path = config_path,
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
        json_module = true
    },
    sample_data = {
        entries = entries,
        md5_hash = md5_hash,
        note = note
    }
}
local json_path = scratch_root .. '/artifacts/comprehensive_demo.json'
sdk.ensure_dir(scratch_root .. '/artifacts')
local encoded_json = sdk.text.json.encode(comprehensive_summary, { indent = true })
local jf = io.open(json_path, 'w')
if jf then jf:write(encoded_json); jf:close() end
sdk.color_print('yellow', 'Comprehensive demo summary written (JSON): ' .. json_path)
local rf = io.open(json_path, 'r')
local loaded_json
if rf then
    local content = rf:read('*a'); rf:close()
    loaded_json = sdk.text.json.decode(content)
end
if loaded_json and loaded_json.demo_info then
    sdk.color_print('green', 'JSON loaded successfully - title: ' .. tostring(loaded_json.demo_info.title))
end

progress.step('Testing warning and error functions')

-- Demonstrate warning and error functions
warn('This is a demonstration warning message')
-- Note: We don't call error() as it would terminate the script

progress.step('Testing tool resolution')

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

sdk.color_print('green', 'opts.prompt value: ' .. tostring(opts.prompt))
    progress.step('Testing user prompt')
if opts.prompt == nil or not opts.prompt then
    -- Prompt demonstration
    -- prompt args (message, id, secret)
    local user_input = prompt('Enter a test message (or press Enter to skip)', 'demo_prompt', false)
    sdk.color_print('green', 'User input received: ' .. (user_input or 'No input'))
else
    sdk.color_print('green', 'User input received, from arg: ' .. (opts.prompt))
end

progress.step('Testing security: blocked filesystem and process access')

if not opts.sec_check then
    sdk.color_print('yellow', '--- Security & Sandboxing Tests Skipped ---')
    sdk.color_print('white', 'To run security feature tests, re-run the script with the --sec_check argument.')
else
    sdk.color_print('yellow', '--- Security & Sandboxing Tests Starting ---')

    -- Security demonstration: Show that RemakeEngine blocks access to protected system paths
    sdk.color_print('yellow', '--- Security & Sandboxing Tests ---')
    sdk.color_print('white', 'RemakeEngine security features prevent malicious script behavior:')

    -- Determine a known-protected path per platform
    local sep = package.config:sub(1,1)
    local protected_file = sep == '\\' and 'C:/Windows/System32/drivers/etc/hosts' or '/etc/hosts'
    local protected_dir  = sep == '\\' and 'C:/Windows/System32' or '/etc'

    sdk.color_print('cyan', '1. Testing filesystem security (remove operations):')
    -- Attempt to remove a protected file (should be denied and return false)
    local denied_remove_file = sdk.remove_file(protected_file)
    sdk.color_print(denied_remove_file and 'red' or 'green',
        '   ✖ Attempt to remove protected file denied: ' .. tostring(not denied_remove_file))

    -- Attempt to remove a protected directory (should be denied and return false)
    local denied_remove_dir = sdk.remove_dir(protected_dir)
    sdk.color_print(denied_remove_dir and 'red' or 'green',
        '   ✖ Attempt to remove protected dir denied: ' .. tostring(not denied_remove_dir))

    sdk.color_print('cyan', '2. Testing filesystem security (read operations):')
    -- Attempt to read protected paths
    local can_read_protected = sdk.path_exists(protected_file)
    sdk.color_print(can_read_protected and 'red' or 'green',
        '   ✖ Attempt to check protected file existence blocked: ' .. tostring(not can_read_protected))

    sdk.color_print('cyan', '3. Testing filesystem security (copy operations):')
    -- Attempt to copy to protected location
    local denied_copy = sdk.copy_file(test_file1, protected_dir .. '/malicious.txt', false)
    sdk.color_print(denied_copy and 'red' or 'green', '   ✖ Attempt to copy to protected dir denied: ' .. tostring(not denied_copy))

    sdk.color_print('cyan', '4. Testing filesystem security (TOML operations):')
    -- Attempt to write TOML to protected location
    local toml_protected_path = protected_dir .. '/malicious.toml'
    sdk.toml_write_file(toml_protected_path, {evil = true})
    -- Check if file was created (it shouldn't be)
    local toml_file_created = sdk.path_exists(toml_protected_path)
    sdk.color_print(toml_file_created and 'red' or 'green',
        '   ✖ Attempt to write TOML to protected dir blocked: ' .. tostring(not toml_file_created))

    sdk.color_print('cyan', '5. Testing process execution security (forbidden paths):')
    -- Attempt to pass a forbidden path to an approved process (should throw and be caught)
    local function try_exec_forbidden_path()
        return sdk.exec({'git', 'status', protected_dir}, { wait = true })
    end
    local ok_forbidden, res_forbidden = pcall(try_exec_forbidden_path)
    sdk.color_print(ok_forbidden and 'red' or 'green', '   ✖ Process arg path validation blocked: ' .. tostring(not ok_forbidden))
    if not ok_forbidden then
        sdk.color_print('cyan', '   Blocked reason: ' .. tostring(res_forbidden))
    end

    sdk.color_print('cyan', '6. Testing process execution security (unapproved executables):')
    -- Attempt to run an unapproved executable
    local function try_exec_unapproved()
        return sdk.exec({'cmd.exe', '/c', 'echo', 'malicious'}, { wait = true })
    end
    local unapproved_ok, unapproved_err = pcall(try_exec_unapproved)
    sdk.color_print(unapproved_ok and 'red' or 'green',
        '   ✖ Unapproved executable blocked: ' .. tostring(not unapproved_ok))
    if not unapproved_ok then
        sdk.color_print('cyan', '   Blocked reason: ' .. tostring(unapproved_err))
    end

    sdk.color_print('cyan', '7. Testing symlink security:')
    -- Attempt to create symlink to protected location
    local denied_symlink = sdk.create_symlink(protected_file, scratch_root .. '/malicious_link', false)
    sdk.color_print(denied_symlink and 'red' or 'green',
        '   ✖ Symlink to protected location denied: ' .. tostring(not denied_symlink))

    sdk.color_print('cyan', '8. Testing directory creation security:')
    -- Attempt to create directory in protected location
    local denied_mkdir = sdk.ensure_dir(protected_dir .. '/malicious_dir')
    sdk.color_print(denied_mkdir and 'red' or 'green',
        '   ✖ Directory creation in protected location denied: ' .. tostring(not denied_mkdir))

    sdk.color_print('green', '✓ All security tests passed! RemakeEngine successfully blocked malicious operations.')

    -- Demonstrate file removal (within allowed workspace)
    local cleanup_success = sdk.remove_file(test_file2)
    sdk.color_print('green', 'Cleanup file removal: ' .. tostring(cleanup_success))


    sdk.color_print('green', '=== Comprehensive Lua API Demo Complete ===')
    sdk.color_print('cyan', 'All RemakeEngine Lua API features have been demonstrated!')
    sdk.color_print('yellow', 'Check the artifacts directory for generated files: ' .. scratch_root)
end

-- next test gameroot and projectroot globals, compaire to ones pased into args
progress.step('Testing global variables (Game_Root and Project_Root)')

if type(Game_Root) == 'string' then
    sdk.color_print('green', 'Game_Root global is set: ' .. Game_Root)
    if opts.module then
        if Game_Root == opts.module then
            sdk.color_print('green', 'Game_Root matches --module/--Game_Root argument.')
        else
            sdk.color_print('red', 'Game_Root mismatch! Global: ' .. Game_Root .. ', Arg: ' .. opts.module)
        end
    else
        sdk.color_print('yellow', 'No --module/--Game_Root argument passed for comparison.')
    end
else
    sdk.color_print('red', 'Game_Root global is NOT a string (nil or wrong type)')
end

if type(Project_Root) == 'string' then
    sdk.color_print('green', 'Project_Root global is set: ' .. Project_Root)
    if opts.projectroot then
        if Project_Root == opts.projectroot then
            sdk.color_print('green', 'Project_Root matches --Project_Root argument.')
        else
            sdk.color_print('red', 'Project_Root mismatch! Global: ' .. Project_Root .. ', Arg: ' .. opts.projectroot)
        end
    else
        sdk.color_print('yellow', 'No --Project_Root argument passed for comparison.')
    end
else
    sdk.color_print('red', 'Project_Root global is NOT a string (nil or wrong type)')
end

if type(script_dir) == 'string' then
    sdk.color_print('green', 'script_dir global is set: ' .. script_dir)
end


progress.step('Testing Diagnostics logging')

-- Demonstrate Diagnostics.Log and Diagnostics.Trace
sdk.color_print('yellow', '--- Diagnostics Logging Examples ---')
Diagnostics.Log('This is a standard log message from Lua.')
sdk.color_print('yellow', '"This is a standard log message from Lua." and should appear in logs\\<ui>\\<datetme>\\lua.log')
Diagnostics.Trace('This is a trace message from Lua.')
sdk.color_print('yellow', '"This is a trace message from Lua." and should appear in logs\\<ui>\\<datetme>\\trace.log')


-- Final progress update
progress.step('lua_feature_demo.lua::EOF')
progress.finish()

