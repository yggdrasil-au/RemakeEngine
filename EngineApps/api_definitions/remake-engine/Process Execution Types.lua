--[[
This file contains type annotations and API definitions for the Lua scripting environment used in the remake engines customised MoonSharp runtime.
It is intended for use with Lua language servers and IDEs to provide autocompletion, type checking, and documentation for script authors.

limitations in lua language server customization of lua builtins requires .luarc.json to disable basic, io, os, and builtin libraries to allow for specific redefintions like os.date and io.open with path security checks.
This file redefines the expected APIs for these libraries as well as the sdk and global functions.
::note the limitations are being fixed in this fork of the language server

The file is organized into sections:
1. Custom UserData / Object Types: Definitions for objects returned by engine APIs (e.g., FileHandle, PanelProgress).
1a. Standard Built-in Functions: Redefinitions of core Lua functions usually provided by the 'basic' library.
1b. MoonSharp runtime additions now live in the core runtime templates.
2. Standard Libraries (Modified): The global 'io' and 'os' libraries with modifications and removals for security and sandboxing.
3. SDK Modules: The 'sdk' table and its submodules (e.g., sdk.text, sdk.exec).
4. Global Functions: Utility functions provided in the global scope (e.g., import, prompt).
5. Global Constants / Environment Variables: Predefined global variables (e.g., Game_Root, UIMode).
6. Global Helper Functions: Additional utility functions (e.g., join()).
7. Removed/Deprecated Functions: A list of functions that have been removed or deprecated, with explanations and alternatives.

]]


---@meta _
---@version 5.2
---@version Moonsharp2.0.0.0

-- =============================================================================
-- 2. Process Execution Types
-- =============================================================================

--- Options for sdk.exec and sdk.execSilent.
---@class ExecOptions
---@field cwd? string The working directory. Must be an allowed path.
---@field env? table<string, string> Environment variable overrides.
---@field new_terminal? boolean If true, attempts to launch in a new terminal window.
---@field keep_open? boolean (Used with new_terminal) Keeps the terminal open after exit.
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

---@class SdkHash
---@field md5 fun(text: string): string
---@field sha1_file fun(path: string): string|nil
local SdkHash = {}
