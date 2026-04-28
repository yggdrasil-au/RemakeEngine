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
-- 3. SDK Modules
-- =============================================================================

---@class SDK
---@field IO IO_Lib Access to IO functions (same as global 'io').
---@field Hash SdkHash Hash helpers under sdk.Hash.
---@field cpu_count integer The number of logical processors available.
---@field text SdkText JSON/text helpers.
sdk = {}

--- Prints colored text to the engine console/UI.
--- Accepts either (color, message[, newline]) or a table { color/colour, message, newline }.
---@param color string|table
---@param message? string
---@param newline? boolean
function sdk.color_print(color, message, newline) end

--- builtin lua print, does not print to sdk.print, and will only work in terminal output mode
---@param ... any
---@return nil
function print(...) end

--- Alias for sdk.color_print (AU/UK spelling).
--- Prints colored text to the engine console/UI.
--- Accepts either (color, message[, newline]) or a table { color/colour, message, newline }.
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
--- Supports cross-volume moves and merging directories.
---@param old_path string
---@param new_path string
---@param overwrite? boolean
---@return boolean ok
function sdk.rename_file(old_path, new_path, overwrite) end

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

--- Checks whether a path is an absolute path.
---@param path string
---@return boolean ok
function sdk.is_absolute(path) end

--- Resolves a path to its absolute path with long path support on Windows.
---@param path string
---@return string absolute_path
function sdk.absolute_path(path) end

--- Checks whether a file or directory is writable.
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

--- Reads a YAML file into a Lua table.
---@param path string
---@return table|nil data
function sdk.yaml_read_file(path) end

--- Writes a Lua table to a YAML file.
---@param path string
---@param value table
function sdk.yaml_write_file(path, value) end

--- TOML helpers in sdk.text.toml.
---@class SdkTextToml
---@field read_file? fun(path: string): table|nil
---@field write_file? fun(path: string, value: table)
local SdkTextToml = {}

--- JSON helpers in sdk.text.json.
---@class SdkTextJson
---@field encode? fun(value: any, opts?: JsonEncodeOptions): string
---@field decode? fun(json: string): any
---@field isNull? fun(val: any): boolean
local SdkTextJson = {}

--- Encodes a Lua value to JSON.
---@param value any
---@param opts? JsonEncodeOptions
---@return string json
function SdkTextJson.encode(value, opts) end

--- Decodes JSON into Lua values.
---@param json string?
---@return any value
function SdkTextJson.decode(json) end

--- YAML helpers in sdk.text.yaml.
---@class SdkTextYaml
---@field encode? fun(value: any, opts?: JsonEncodeOptions): string
---@field decode? fun(yaml: string): any
---@field read_file? fun(path: string): table|nil
---@field write_file? fun(path: string, value: table)
local SdkTextYaml = {}

--- Encodes a Lua value to YAML.
---@param value any
---@param opts? JsonEncodeOptions
---@return string yaml
function SdkTextYaml.encode(value, opts) end

--- Decodes YAML into Lua values.
---@param yaml string
---@return any value
function SdkTextYaml.decode(yaml) end

---@class SdkText
---@field json SdkTextJson
---@field toml SdkTextToml
---@field yaml SdkTextYaml
local SdkText = {}

sdk.text = {}
sdk.text.json = {}
sdk.text.toml = {}
sdk.text.yaml = {}

--- Encodes a Lua value to JSON.
---@param value any
---@param opts? JsonEncodeOptions
---@return string json
function sdk.text.json.encode(value, opts) end

--- Decodes JSON into Lua values.
---@param json string?
---@return any|nil value
function sdk.text.json.decode(json) end

--- Checks if a value is null (nil).
---@param val any
---@return boolean
function sdk.text.json.isNull(val) end

--- Reads a TOML file into a Lua table.
---@param path string
---@return table|nil data
function sdk.text.toml.read_file(path) end

--- Writes a Lua table to a TOML file.
---@param path string
---@param value table
function sdk.text.toml.write_file(path, value) end

--- Encodes a Lua value to YAML.
---@param value any
---@param opts? JsonEncodeOptions
---@return string yaml
function sdk.text.yaml.encode(value, opts) end

--- Decodes YAML into Lua values.
---@param yaml string
---@return any value
function sdk.text.yaml.decode(yaml) end

--- Reads a YAML file into a Lua table.
---@param path string
---@return table|nil data
function sdk.text.yaml.read_file(path) end

--- Writes a Lua table to a YAML file.
---@param path string
---@param value table
function sdk.text.yaml.write_file(path, value) end

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
--- Note: Current C# implementation returns a status snapshot using poll semantics.
--- The timeout argument is accepted for API compatibility but is currently ignored.
---@param pid integer The process ID returned by spawn_process.
---@param timeout_ms? integer Optional timeout in milliseconds.
---@return ProcessStatus status Information about the process state.
function sdk.wait_process(pid, timeout_ms) end

--- Forcefully closes/kills a background process.
---@param pid integer The process ID returned by spawn_process.
---@return boolean success True if closed successfully.
function sdk.close_process(pid) end

