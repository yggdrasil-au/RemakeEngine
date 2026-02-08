---@meta

-- =============================================================================
-- 1. Custom UserData / Object Types
-- =============================================================================

--- Represents an open file handle returned by io.open().
---@class FileHandle
local FileHandle = {}

--- Reads from the file.
---@param format? "n"|"*n"|"a"|"*a"|"l"|"*l"|number The format to read (number of bytes, "*a" for all, "*l" for line).
---@return string|number|nil content The read content, or nil on EOF.
function FileHandle:read(format) end

--- Writes content to the file.
---@param content string|number The content to write.
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
local PanelProgress = {}

--- Represents a progress bar for the currently running script.
---@class ScriptProgress
---@field Current integer The current step count.
---@field Total integer The total number of steps.
local ScriptProgress = {}

--- Updates the progress bar.
---@param increment integer The number of steps to advance.
---@param label? string Optional text to display on the progress bar.
function ScriptProgress:Update(increment, label) end

--- Sets the total number of steps for the progress bar.
---@param newTotal integer
function ScriptProgress:SetTotal(newTotal) end

--- Marks the progress as complete.
function ScriptProgress:Complete() end


--- Represents an active SQLite database connection.
---@class SqliteHandle
local SqliteHandle = {}

--- Executes a non-query SQL statement (INSERT, UPDATE, DELETE).
---@param sql string The SQL statement.
---@param params? table<string, any> Optional parameters for the query.
---@return integer affected_rows The number of rows affected.
function SqliteHandle:exec(sql, params) end

--- Executes a query and returns the results.
---@param sql string The SQL query.
---@param params? table<string, any> Optional parameters.
---@return table<integer, table<string, any>> results A list of rows (tables).
function SqliteHandle:query(sql, params) end

--- Begins a transaction.
function SqliteHandle:begin() end

--- Commits the current transaction.
function SqliteHandle:commit() end

--- Rolls back the current transaction.
function SqliteHandle:rollback() end

--- Closes the database connection.
function SqliteHandle:close() end

--- Alias for close().
function SqliteHandle:dispose() end


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

--- Returns a string or a table containing date and time.
---@param format? string Format string (e.g., "%Y-%m-%d"). If "*t" or "!*t", returns a table.
---@return string|osdate The formatted date string or table.
function os.date(format) end

--- Returns the current time (Unix timestamp).
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

-- =============================================================================
-- 3. SDK Modules
-- =============================================================================

---@class SDK
---@field IO IO_Lib Access to IO functions (same as global 'io').
---@field cpu_count integer The number of logical processors available.
---@field ResolveToolPath fun(id: string, ver?: string): string Resolves external tool paths.
sdk = {}

--- Executes a process and streams output to the engine console in real-time.
--- Security: The executable must be in the approved tools list (use 'tool()' to resolve).
---@param args string[] A table of strings containing the command and arguments (e.g., {"git", "status"}).
---@param options? ExecOptions Configuration options.
---@return ExecResult result The execution result.
function sdk.exec(args, options) end

--- Executes a process like sdk.exec, but suppresses the output to the engine console.
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


--- SQLite Database Module.
---@class SqliteModule
sqlite = {}

--- Opens a SQLite database.
--- Path must be within allowed workspace areas.
---@param path string The path to the database file.
---@return SqliteHandle handle The database connection handle.
function sqlite.open(path) end


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


