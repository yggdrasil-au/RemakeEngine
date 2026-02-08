---@meta

-- =============================================================================
-- 1. Custom UserData / Object Types
-- =============================================================================

--- Represents an open file handle returned by io.open().
---@class FileHandle
local FileHandle = {}

--- Reads from the file.
--- Note: This implementation requires a format parameter; supported values are
--- a byte count (number), "*a"/"*all" (read all), and "*l"/"*line" (read line).
---@param format number|"*a"|"*all"|"*l"|"*line" The read mode.
---@return string|nil content The read content, or nil on EOF.
function FileHandle:read(format) end

--- Writes content to the file.
---@param content string The content to write.
function FileHandle:write(content) end

--- Seeks to a position in the file.
---@param whence? "set"|"cur"|"end" The reference point (default "cur").
---@param offset? integer The offset from the reference point (default 0).
---@return integer|nil position The new position, or nil on error.
function FileHandle:seek(whence, offset) end

--- Flushes any buffered data to the file.
function FileHandle:flush() end

--- Closes the file handle.
function FileHandle:close() end


--- Represents a progress bar displayed in the side panel.
---@class PanelProgress
---@field Current integer The current step count.
---@field Total integer The total number of steps.
---@field Id string The progress id.
---@field Label string|nil The progress label.
local PanelProgress = {}

--- Updates the progress bar.
---@param increment? integer The number of steps to advance (default 1).
function PanelProgress:Update(increment) end

--- Marks the progress as complete and closes the panel.
function PanelProgress:Complete() end

--- Disposes the progress handle (same as Complete()).
function PanelProgress:Dispose() end

--- Represents a progress bar for the currently running script.
---@class ScriptProgress
---@field Current integer The current step count.
---@field Total integer The total number of steps.
---@field Id string The progress id.
---@field Label string|nil The progress label.
local ScriptProgress = {}

--- Updates the progress bar.
---@param increment? integer The number of steps to advance (default 1).
---@param label? string Optional text to display on the progress bar.
function ScriptProgress:Update(increment, label) end

--- Sets the total number of steps for the progress bar.
---@param newTotal integer
function ScriptProgress:SetTotal(newTotal) end

--- Marks the progress as complete.
function ScriptProgress:Complete() end


--- SQLite Database Module.
---@class SqliteModule
sqlite = {}

--- Opens a SQLite database.
--- Path must be within allowed workspace areas.
---@param path string The path to the database file.
---@return SqliteHandle handle The database connection handle.
function sqlite.open(path) end


--- Represents an active SQLite database connection.
---@class SqliteHandle
local SqliteHandle = {}

--- Executes a non-query SQL statement (INSERT, UPDATE, DELETE).
---@overload fun(self: SqliteHandle, sql: string, params?: table<string, any>): integer
---@overload fun(sql: string, params?: table<string, any>): integer
---@return integer affected_rows The number of rows affected.
function SqliteHandle.exec(...) end

--- Executes a query and returns the results.
---@overload fun(self: SqliteHandle, sql: string, params?: table<string, any>): table<integer, table<string, any>>
---@overload fun(sql: string, params?: table<string, any>): table<integer, table<string, any>>
---@return table<integer, table<string, any>> results A list of rows (tables).
function SqliteHandle.query(...) end

--- Begins a transaction.
---@overload fun(self: SqliteHandle)
---@overload fun()
function SqliteHandle.begin(...) end

--- Commits the current transaction.
---@overload fun(self: SqliteHandle)
---@overload fun()
function SqliteHandle.commit(...) end

--- Rolls back the current transaction.
---@overload fun(self: SqliteHandle)
---@overload fun()
function SqliteHandle.rollback(...) end

--- Closes the database connection.
---@overload fun(self: SqliteHandle)
---@overload fun()
function SqliteHandle.close(...) end

--- Alias for close().
---@overload fun(self: SqliteHandle)
---@overload fun()
function SqliteHandle.dispose(...) end


-- =============================================================================
-- 2. Standard Libraries (Modified)
-- =============================================================================

--- Modified IO library.
---@class IO_Lib
local io = {}

--- Opens a file in the specified mode.
--- Note: Paths are validated against workspace security rules.
---@param path string The file path.
---@param mode? "r"|"w"|"a"|"rb"|"wb"|"ab" The file mode (default "r").
---@return FileHandle|nil handle The file handle, or nil on error.
---@return string? error_message The error message if opening failed.
function io.open(path, mode) end

--- Writes text to the engine UI/Console (Standard Output).
---@param content string
function io.write(content) end

--- [Removed] Reading from stdin is not supported.
---@type nil
io.read = nil

--- [Removed] Process execution via io.popen is not supported. Use sdk.run_process.
---@type nil
io.popen = nil

--- [Removed/Disabled] Flush global output.
---@type nil
io.flush = nil


--- Restricted OS library.
---@class OS_Lib
local os = {}

--- Returns date/time data.
--- If format is nil/empty, returns the current Unix timestamp (seconds).
--- If format is "*t" or "!*t", returns a table (local time unless "!" is used).
---@param format? string Format string (e.g., "%Y-%m-%d").
---@return string|number|osdate value Formatted date string, unix time, or table.
function os.date(format) end

--- Returns the current time (Unix timestamp).
--- Note: The optional table argument is currently ignored.
---@param table? osdate Optional table to convert to time.
---@return number seconds
function os.time(table) end

--- Returns the approximate CPU time used by the program.
---@return number seconds
function os.clock() end

--- Terminates the script execution.
---@param code? integer Exit code (default 0).
function os.exit(code) end

--- Gets the value of an environment variable.
--- Note: Sensitive variables (e.g., USERNAME, TEMP) are blocked.
---@param varname string
---@return string|nil value
function os.getenv(varname) end

--- [Removed] Use sdk.exec or sdk.run_process instead.
---@type nil
os.execute = nil


-- =============================================================================
-- 2. Process Execution Types
-- =============================================================================

--- Options for sdk.exec and sdk.execSilent.
---@class ExecOptions
---@field cwd? string The working directory. Must be an allowed path.
---@field env? table<string, string> Environment variable overrides.
---@field new_terminal? boolean If true, attempts to launch in a new terminal window.
---@field keep_open? boolean (Used with new_terminal) Keeps the terminal open after exit.
---@field title? string Optional title for the process/window.
---@field wait? boolean (Default: true) If false, returns immediately without waiting for exit.
local ExecOptions = {}

--- Result of sdk.exec.
---@class ExecResult
---@field success boolean True if the process exited with code 0.
---@field exit_code integer The process exit code.
local ExecResult = {}

--- Options for sdk.run_process.
---@class RunOptions
---@field cwd? string The working directory.
---@field capture_stdout? boolean (Default: true) Capture standard output.
---@field capture_stderr? boolean (Default: true) Capture standard error.
---@field timeout_ms? integer Timeout in milliseconds.
---@field env? table<string, string> Environment variable overrides.
local RunOptions = {}

--- Result of sdk.run_process.
---@class RunResult
---@field success boolean True if exit_code is 0.
---@field exit_code integer The process exit code.
---@field stdout? string The captured standard output.
---@field stderr? string The captured standard error.
local RunResult = {}

--- Options for sdk.spawn_process.
---@class SpawnOptions
---@field cwd? string The working directory.
---@field capture_stdout? boolean (Default: true) Capture standard output for polling.
---@field capture_stderr? boolean (Default: true) Capture standard error for polling.
---@field env? table<string, string> Environment variable overrides.
local SpawnOptions = {}

--- Result of sdk.spawn_process.
---@class SpawnResult
---@field pid integer The internal process ID (managed ID, not OS PID).
local SpawnResult = {}

--- Status returned by sdk.poll_process and sdk.wait_process.
---@class ProcessStatus
---@field running boolean True if the process is still active.
---@field exit_code? integer The exit code (nil if still running).
---@field stdout string The complete standard output accumulated so far.
---@field stderr string The complete standard error accumulated so far.
---@field stdout_delta string The new standard output since the last check.
---@field stderr_delta string The new standard error since the last check.
local ProcessStatus = {}

--- Options for sdk.text.json.encode.
---@class JsonEncodeOptions
---@field indent? boolean If true, pretty-prints JSON.
local JsonEncodeOptions = {}

-- =============================================================================
-- 3. SDK Modules
-- =============================================================================

---@class SDK
---@field IO IO_Lib Access to IO functions (same as global 'io').
---@field cpu_count integer The number of logical processors available.
---@field text SdkText JSON/text helpers.
sdk = {}

--- Prints colored text to the engine console/UI.
--- Accepts either (color, message[, newline]) or a table { color/colour, message, newline }.
---@param color string|table
---@param message? string
---@param newline? boolean
function sdk.color_print(color, message, newline) end

--- Alias for sdk.color_print (AU/UK spelling).
---@param color string|table
---@param message? string
---@param newline? boolean
function sdk.colour_print(color, message, newline) end

--- Validates a module directory structure.
---@param dir string
---@return boolean ok
function sdk.validate_source_dir(dir) end

--- Extracts an archive to the destination directory.
---@param archive_path string
---@param dest_dir string
---@return boolean ok
function sdk.extract_archive(archive_path, dest_dir) end

--- Creates an archive from the source path.
---@param src_path string
---@param archive_path string
---@param type string Archive type (e.g., "zip").
---@return boolean ok
function sdk.create_archive(src_path, archive_path, type) end

--- Ensures a directory exists (creates parents as needed).
---@param path string
---@return boolean ok
function sdk.ensure_dir(path) end

--- Copies a directory recursively.
---@param src string
---@param dst string
---@param overwrite boolean
---@return boolean ok
function sdk.copy_dir(src, dst, overwrite) end

--- Moves a directory.
---@param src string
---@param dst string
---@param overwrite boolean
---@return boolean ok
function sdk.move_dir(src, dst, overwrite) end

--- Copies a file.
---@param src string
---@param dst string
---@param overwrite boolean
---@return boolean ok
function sdk.copy_file(src, dst, overwrite) end

--- Writes text to a file (creates parent dirs as needed).
---@param path string
---@param content string
---@return boolean ok
function sdk.write_file(path, content) end

--- Reads a file to string.
---@param path string
---@return string|nil content
function sdk.read_file(path) end

--- Renames or moves a file or directory.
---@param old_path string
---@param new_path string
---@return boolean ok
function sdk.rename_file(old_path, new_path) end

--- Removes a directory recursively.
---@param path string
---@return boolean ok
function sdk.remove_dir(path) end

--- Removes a file (or symlink).
---@param path string
---@return boolean ok
function sdk.remove_file(path) end

--- Creates a symlink.
---@param source string
---@param destination string
---@param is_directory boolean
---@param overwrite? boolean
---@return boolean ok
function sdk.create_symlink(source, destination, is_directory, overwrite) end

--- Creates a hardlink (files only).
---@param source string
---@param destination string
---@return boolean ok
function sdk.create_hardlink(source, destination) end

--- Checks whether a path exists.
---@param path string
---@return boolean ok
function sdk.path_exists(path) end

--- Checks whether a path exists (including symlinks).
---@param path string
---@return boolean ok
function sdk.lexists(path) end

--- Checks whether a path is a directory.
---@param path string
---@return boolean ok
function sdk.is_dir(path) end

--- Checks whether a path is a file.
---@param path string
---@return boolean ok
function sdk.is_file(path) end

--- Checks whether a directory is writable.
---@param path string
---@return boolean ok
function sdk.is_writable(path) end

--- Returns whether the path is a symlink.
---@param path string
---@return boolean ok
function sdk.is_symlink(path) end

--- Resolves a path to its real path.
---@param path string
---@return string|nil real_path
function sdk.realpath(path) end

--- Reads a symlink target.
---@param path string
---@return string|nil target
function sdk.readlink(path) end

--- Finds a subdirectory by name.
---@param base_dir string
---@param name string
---@return string|nil found_path
function sdk.find_subdir(base_dir, name) end

--- Checks whether all named subdirs exist under base_dir.
---@param base_dir string
---@param names table<integer, string>
---@return boolean ok
function sdk.has_all_subdirs(base_dir, names) end

--- Gets the current process directory.
---@return string path
function sdk.currentdir() end

--- Gets the directory of the current script file.
---@return string|nil path
function sdk.current_dir() end

--- Creates a directory.
---@param path string
---@return boolean ok
function sdk.mkdir(path) end

--- Returns file attributes (mode, size, etc.).
---@param path string
---@return table|nil attributes
function sdk.attributes(path) end

--- Lists entries in a directory (names only).
---@param path string
---@return table<integer, string> entries
function sdk.list_dir(path) end

--- Computes SHA1 hash of a file (lowercase hex).
---@param path string
---@return string|nil hash
function sdk.sha1_file(path) end

--- Computes MD5 hash of a string (lowercase hex).
---@param text string
---@return string hash
function sdk.md5(text) end

--- Sleeps for a number of seconds.
---@param seconds number
function sdk.sleep(seconds) end

--- Reads a TOML file into a Lua table.
---@param path string
---@return table|nil data
function sdk.toml_read_file(path) end

--- Writes a Lua table to a TOML file.
---@param path string
---@param value table
function sdk.toml_write_file(path, value) end

--- TOML helpers in sdk.text.toml.
---@class SdkTextToml
---@field read_file fun(path: string): table|nil
---@field write_file fun(path: string, value: table)
local SdkTextToml = {}

--- JSON helpers in sdk.text.json.
---@class SdkTextJson
---@field encode fun(value: any, opts?: JsonEncodeOptions): string
---@field decode fun(json: string): any
local SdkTextJson = {}

--- Encodes a Lua value to JSON.
---@param value any
---@param opts? JsonEncodeOptions
---@return string json
function SdkTextJson.encode(value, opts) end

--- Decodes JSON into Lua values.
---@param json string
---@return any value
function SdkTextJson.decode(json) end

---@class SdkText
---@field json SdkTextJson
---@field toml SdkTextToml
local SdkText = {}

sdk.text = {}
sdk.text.json = {}
sdk.text.toml = {}

--- Encodes a Lua value to JSON.
---@param value any
---@param opts? JsonEncodeOptions
---@return string json
function sdk.text.json.encode(value, opts) end

--- Decodes JSON into Lua values.
---@param json string
---@return any value
function sdk.text.json.decode(json) end

--- Reads a TOML file into a Lua table.
---@param path string
---@return table|nil data
function sdk.text.toml.read_file(path) end

--- Writes a Lua table to a TOML file.
---@param path string
---@param value table
function sdk.text.toml.write_file(path, value) end

--- Executes a process and streams output to the engine console in real-time.
--- Security: The executable must be in the approved tools list (use 'tool()' to resolve).
---@param args string[] A table of strings containing the command and arguments (e.g., {"git", "status"}).
---@param options? ExecOptions Configuration options.
---@return ExecResult result The execution result.
function sdk.exec(args, options) end

--- Executes a process like sdk.exec, but suppresses output to the engine console.
---@param args string[] A table of strings containing the command and arguments.
---@param options? ExecOptions Configuration options.
---@return ExecResult result The execution result.
function sdk.execSilent(args, options) end

--- Executes a process and captures the output (stdout/stderr) into the result table.
--- This blocks until the process finishes or times out.
---@param args string[] A table of strings containing the command and arguments.
---@param options? RunOptions Configuration options.
---@return RunResult result The execution result including captured output.
function sdk.run_process(args, options) end

--- Spawns a non-blocking process in the background.
--- Use poll_process or wait_process to interact with it.
---@param args string[] A table of strings containing the command and arguments.
---@param options? SpawnOptions Configuration options.
---@return SpawnResult result Contains the 'pid' for use with poll/wait.
function sdk.spawn_process(args, options) end

--- Checks the status of a background process spawned via spawn_process.
---@param pid integer The process ID returned by spawn_process.
---@return ProcessStatus status Information about the process state and any new output (delta).
function sdk.poll_process(pid) end

--- Waits for a background process to complete, or for a timeout.
---@param pid integer The process ID returned by spawn_process.
---@param timeout_ms? integer Optional timeout in milliseconds.
---@return ProcessStatus status Information about the process state.
function sdk.wait_process(pid, timeout_ms) end

--- Forcefully closes/kills a background process.
---@param pid integer The process ID returned by spawn_process.
---@return boolean success True if closed successfully.
function sdk.close_process(pid) end



-- =============================================================================
-- 4. Global Functions
-- =============================================================================

--- Resolves the absolute path to an external tool.
---@param id string The tool ID (e.g., "ffmpeg").
---@param version? string The optional tool version.
---@return string path The resolved file path.
function ResolveToolPath(id, version) end

--- Alias for ResolveToolPath.
---@param id string
---@param version? string
---@return string path
function tool(id, version) end

--- Prints a warning message to the host console/UI.
---@param message string
function warn(message) end

--- Prints an error message to the host console/UI.
---@param message string
function error(message) end

--- Prompts the user for input.
---@param message string The question to ask the user.
---@param id? string An ID for this prompt (default: "q1").
---@param secret? boolean If true, hides the input (for passwords).
---@return string response
function prompt(message, id, secret) end

--- Prompts the user for input with a colored message.
---@param message string The question to ask.
---@param color string The color of the prompt text.
---@param id? string An ID for this prompt.
---@param secret? boolean If true, hides the input.
---@return string response
function color_prompt(message, color, id, secret) end

--- Alias for color_prompt (AU/UK spelling).
---@param message string
---@param color string
---@param id? string
---@param secret? boolean
---@return string
function colour_prompt(message, color, id, secret) end

--- Diagnostics helpers.
---@class Diagnostics
---@field Log fun(message: string)
---@field Trace fun(message: string)
Diagnostics = {}

--- Progress helpers for the current script.
---@class Progress
---@field new fun(total: integer, id?: string, label?: string): PanelProgress
---@field start fun(total: integer, label?: string): ScriptProgress
---@field step fun(label?: string)
---@field add_steps fun(count: integer)
---@field finish fun()
progress = {}


-- =============================================================================
-- 5. Global Constants / Environment Variables
-- =============================================================================

--- Arguments passed to the script (1-based index).
---@type table<integer, string>
argv = {}

--- The number of arguments passed to the script.
---@type integer
argc = 0

--- The root directory of the current game context.
---@type string
Game_Root = ""

--- The root directory of the project.
---@type string
Project_Root = ""

--- The directory containing the currently executing script.
---@type string
script_dir = ""

--- True if the engine is running in Debug mode.
---@type boolean
DEBUG = false

--- The current UI mode of the engine.
---@type "cli"|"gui"|"tui"|"unknown"
UIMode = "unknown"

---@class osdate
---@field year integer
---@field month integer
---@field day integer
---@field hour integer
---@field min integer
---@field sec integer
---@field wday integer
---@field yday integer
---@field isdst boolean


