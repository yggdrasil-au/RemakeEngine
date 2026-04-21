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

-- Builtins

---@class unknown
---@class any
---@class nil
---@class boolean
---@class true: boolean
---@class false: boolean
---@class number
---@class integer: number
---@class thread
---@class table<K, V>: { [K]: V }
---@class string: stringlib
---@class userdata
---@class lightuserdata
---@class function


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
--- Closes the database connection.
---@overload fun(self: SqliteHandle)
---@overload fun()
function SqliteHandle.dispose(...) end


-- =============================================================================
-- 1a. Standard Built-in Functions
-- =============================================================================

--- Converts the argument to a number.
---@param e any
---@param base? integer
---@return number|nil
function tonumber(e, base) end

--- Returns the type of its only argument, coded as a string.
---@param v any
---@return "nil"|"number"|"string"|"boolean"|"table"|"function"|"thread"|"userdata"
function type(v) end

--- Receives a value of any type and converts it to a string in a human-readable format.
---@param v any
---@return string
function tostring(v) end

--- If t has a metamethod __pairs, calls it with t as argument and returns the first three results from the call.
--- Otherwise, returns three values: the next function, the table t, and nil, so that the construction
--- `for k,v in pairs(t) do body end` will iterate over all key–value pairs of table t.
---@generic K, V
---@param t table<K, V>
---@return fun(table: table<K, V>, index?: K):K, V
---@return table<K, V>
---@return K|nil
function pairs(t) end

--- Returns three values: an iterator function, the table t, and 0, so that the construction
--- `for i,v in ipairs(t) do body end` will iterate over the key–value pairs (1,t[1]), (2,t[2]), ..., up to the first nil value.
---@generic V
---@param t table<integer, V>
---@return fun(table: table<integer, V>, index?: integer):integer, V
---@return table<integer, V>
---@return integer
function ipairs(t) end

--- Sets the error handler and calls function f with the given arguments.
---@param f function
---@param ... any
---@return boolean success
---@return any result_or_error
function pcall(f, ...) end

--- Calls function f with the given arguments in protected mode with a new message handler.
---@param f function
---@param msgh function
---@param ... any
---@return boolean success
---@return any result_or_error
function xpcall(f, msgh, ...) end

--- Issues an error with the given message.
---@param message any
---@param level? integer
function error(message, level) end

--- Loads a chunk from the given string.
---@param chunk string
---@param chunkname? string
---@param mode? "b"|"t"|"bt"
---@param env? table
---@return function|nil
---@return string? error_message
function load(chunk, chunkname, mode, env) end

--- Checks whether v1 is equal to v2, without invoking any metamethod.
---@param v1 any
---@param v2 any
---@return boolean
function rawequal(v1, v2) end

--- Checks whether the value of its first argument is truthy; if it is, returns all its arguments.
--- Otherwise, raises an error; message is the error object; its default value is "assertion failed!".
---@generic T
---@param v T
---@param message? any
---@return T
---@return any ...
function assert(v, message, ...) end

--- Gets the real value of table[index], without invoking any metamethod.
---@param table table
---@param index any
---@return any
function rawget(table, index) end

--- Sets the real value of table[index] to value, without invoking any metamethod.
---@param table table
---@param index any
---@param value any
---@return table
function rawset(table, index, value) end

--- If index is a number, returns all arguments after argument number index.
--- Otherwise, index must be the string "#", and select returns the total number of extra arguments it received.
---@param index integer|"#"
---@param ... any
---@return any
function select(index, ...) end

--- Returns the current metatable of the given object.
---@param object any
---@return table|nil
function getmetatable(object) end

--- Sets the metatable for the given table.
---@param table table
---@param metatable table|nil
---@return table
function setmetatable(table, metatable) end

--- Returns the element of t next to index.
---@generic K, V
---@param t table<K, V>
---@param index? K
---@return K|nil
---@return V|nil
function next(t, index) end


-- =============================================================================
-- 2. Standard Libraries (Modified)
-- =============================================================================

--- Modified IO library.
---@class IO_Lib
io = {}

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

--- Restricted OS library.
---@class OS_Lib
os = {}

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
--- Note: Sensitive variables (e.g., USERNAME, TEMP, Path) are blocked for security.
---@param varname string
---@return string|nil value
function os.getenv(varname) end


--- Standard String library.
---@class String_Lib
string = {}

--- Standard Table library.
---@class tablelib
table = {}

--- Standard Package library.
---@class packagelib
---@field config string
package = {}

--- Returns the byte values of the string.
---@param s string|number
---@param i? integer
---@param j? integer
---@return integer ...
---@nodiscard
function string.byte(s, i, j) end

--- Returns a string from the given byte values.
---@param byte integer
---@param ... integer
---@return string
---@nodiscard
function string.char(byte, ...) end

--- Dumps a function as a binary chunk.
---@param f async fun(...):...
---@param strip? boolean
---@return string
---@nodiscard
function string.dump(f, strip) end

--- Returns a copy of the string with all uppercase letters changed to lowercase.
---@param s string
---@return string
function string.lower(s) end

--- Returns the substring of s that starts at i and continues until j; i and j can be negative.
--- If j is absent, it is assumed to be -1 (the end of the string).
---@param s string
---@param i integer
---@param j? integer
---@return string
function string.sub(s, i, j) end

--- Looks for the first match of pattern in the string s.
--- If it finds a match, then find returns the indices of s where this occurrence starts and ends.
---@param s string
---@param pattern string
---@param init? integer
---@param plain? boolean
---@return integer|nil start, integer|nil end, ...
function string.find(s, pattern, init, plain) end

--- Returns the first match of pattern in the string s.
---@param s string|number
---@param pattern string|number
---@param init? integer
---@return any ...
---@nodiscard
function string.match(s, pattern, init) end

--- Formats a string using printf-style placeholders.
---@param s string|number
---@param ... any
---@return string
---@nodiscard
function string.format(s, ...) end

--- Returns an iterator over all matches of the pattern in the string.
---@param s string|number
---@param pattern string|number
---@param init? integer
---@return fun():string, ...
---@nodiscard
function string.gmatch(s, pattern, init) end

--- Returns a copy of s in which all (or the first n, if given) occurrences of the pattern have been replaced by a replacement string/table/function.
---@param s string
---@param pattern string
---@param repl string|table|function
---@param n? integer
---@return string result, integer count
function string.gsub(s, pattern, repl, n) end

--- Returns the length of the string.
---@param s string|number
---@return integer
---@nodiscard
function string.len(s) end

--- Repeats a string a given number of times.
---@param s string|number
---@param n integer
---@param sep? string|number
---@return string
---@nodiscard
function string.rep(s, n, sep) end

--- Reverses a string.
---@param s string|number
---@return string
---@nodiscard
function string.reverse(s) end

--- Returns the string in uppercase.
---@param s string|number
---@return string
---@nodiscard
function string.upper(s) end

---@version >5.3
---#DES 'string.pack'
---@param fmt string
---@param v1 string|number
---@param v2? string|number
---@param ... string|number
---@return string binary
---@nodiscard
function string.pack(fmt, v1, v2, ...) end

---@version >5.3
---#DES 'string.packsize'
---@param fmt string
---@return integer
---@nodiscard
function string.packsize(fmt) end

---@version >5.3
---#DES 'string.unpack'
---@param fmt string
---@param s string
---@param pos? integer
---@return any ...
---@nodiscard
function string.unpack(fmt, s, pos) end

---@version 5.2
--- Returns the unicode codepoint(s) at the given position(s) in the string.
---@param s string
---@param i? integer
---@param j? integer
---@return integer|table<integer, integer>
function string.unicode(s, i, j) end

--- Returns true if str2 is contained in str1.
---@param str1 string
---@param str2 string
---@return boolean
function string.contains(str1, str2) end

--- Returns true if str1 starts with str2.
---@param str1 string
---@param str2 string
---@return boolean
function string.startsWith(str1, str2) end

--- Returns true if str1 ends with str2.
---@param str1 string
---@param str2 string
---@return boolean
function string.endsWith(str1, str2) end

--- Concatenates a list into a string.
---@param list table
---@param sep? string
---@param i? integer
---@param j? integer
---@return string
---@nodiscard
function table.concat(list, sep, i, j) end

--- Inserts a value into a table.
---@overload fun(list: table, value: any)
---@param list table
---@param pos integer
---@param value any
function table.insert(list, pos, value) end

--- Removes and returns a value from a table.
---@param list table
---@param pos? integer
---@return any
function table.remove(list, pos) end

--- Sorts a table in-place.
---@generic T
---@param list T[]
---@param comp? fun(a: T, b: T):boolean
function table.sort(list, comp) end

--- Unpacks a table into multiple return values.
---@generic T1, T2, T3, T4, T5, T6, T7, T8, T9, T10
---@param list {
--- [1]?: T1,
--- [2]?: T2,
--- [3]?: T3,
--- [4]?: T4,
--- [5]?: T5,
--- [6]?: T6,
--- [7]?: T7,
--- [8]?: T8,
--- [9]?: T9,
--- [10]?: T10,
---}
---@param i? integer
---@param j? integer
---@return T1, T2, T3, T4, T5, T6, T7, T8, T9, T10
---@nodiscard
function table.unpack(list, i, j) end

---@version <5.1
---#DES 'table.maxn'
---@param table table
---@return integer
---@nodiscard
function table.maxn(table) end

---@version >5.3, JIT
---#DES 'table.move'
---@param a1 table
---@param f integer
---@param e integer
---@param t integer
---@param a2? table
---@return table a2
function table.move(a1, f, e, t, a2) end

---@version >5.2, JIT
---#DES 'table.pack'
---@return table
---@nodiscard
function table.pack(...) end

---@version <5.1, JIT
---#DES 'table.foreach'
---@generic T
---@param list any
---@param callback fun(key: string, value: any):T|nil
---@return T|nil
---@deprecated
function table.foreach(list, callback) end

---@version <5.1, JIT
---#DES 'table.foreachi'
---@generic T
---@param list any
---@param callback fun(key: string, value: any):T|nil
---@return T|nil
---@deprecated
function table.foreachi(list, callback) end

---@version <5.1, JIT
---#DES 'table.getn'
---@generic T
---@param list T[]
---@return integer
---@nodiscard
---@deprecated
function table.getn(list) end

--- Returns the current package path configuration.
package.config = [[
/
;
?
!
-]]

--- Compatibility alias for Lua 5.1 loaders.
package.loaders = {}

--- Returns the package-specific loader table.
package.searchers = {}

--- Cache of loaded modules.
package.loaded = {}

--- Preload table for modules.
package.preload = {}

--- Searches for a module in the package path.
---@param name string
---@param path string
---@param sep? string
---@param rep? string
---@return string? filename
---@return string? errmsg
---@nodiscard
function package.searchpath(name, path, sep, rep) end

--- Loads a shared library module.
---@param libname string
---@param funcname string
---@return any
function package.loadlib(libname, funcname) end

--- Legacy helper for Lua 5.0 style module injection.
---@param module table
function package.seeall(module) end

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



-- =============================================================================
-- 4. Global Functions
-- =============================================================================

--- Resolves the absolute path to an external tool.
---@param id string The tool ID (e.g., "ffmpeg").
---@param version? string The optional tool version.
---@return string path The resolved file path.
function ResolveToolPath(id, version) end

--- Alias for ResolveToolPath.
--- Resolves the absolute path to an external tool.
---@param id string The tool ID (e.g., "ffmpeg").
---@param version? string The optional tool version.
---@return string path The resolved file path.
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
--- Prompts the user for input with a colored message.
---@param message string The question to ask.
---@param color string The color of the prompt text.
---@param id? string An ID for this prompt.
---@param secret? boolean If true, hides the input.
---@return string response
function colour_prompt(message, color, id, secret) end

--- Loads and executes a Lua file relative to the current script's directory.
--- Like standard 'dofile', this evaluates the file every time it is called and does not cache results.
--- If the path does not end in '.lua', it is appended automatically.
--- when importing a file use --type <filename> to enable IDE support for the imported file, otherwise it will be treated as unknown
--- Any root level code in the imported file will be executed immediately upon import unless contained within a function.
---@param path string The relative or absolute path to the Lua file.
---@return unknown result The value returned by the executed Lua script (if any).
function import(path) end

--- Alias for import.
--- Note: In RemakeEngine, 'require' is a direct alias for 'import'.
--- Unlike standard Lua 'require', it does NOT cache modules in 'package.loaded'
--- and will re-evaluate the file on every call.
---@param path string The relative or absolute path to the Lua file.
---@return unknown result The value returned by the executed Lua script (if any).
function require(path) end

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


--- Normalizes path separators to the host OS default.
---@param path any Path text or any value convertible to text.
---@return any Normalized path, or nil when the input is nil.
function normalize(path) end

--- AU/UK spelling alias for normalize().
---@param path any Path text or any value convertible to text.
---@return any Normalized path, or nil when the input is nil.
function normalise(path) end

--- Compatibility alias for normalize().
---@param path any Path text or any value convertible to text.
---@return any Normalized path, or nil when the input is nil.
function Normalize(path) end

--- Joins multiple path segments into a single path using forward slashes.
--- This performs a "soft" join where leading slashes on subsequent parts are ignored
--- (they are treated as relative segments) to ensure concatenation.
--- Example: join("a", "/b", "c") -> "a/b/c"
---@param ... string|number Path segments to join.
---@return string The joined path.
function join(...) end


-- =============================================================================
-- 5. Global Constants / Environment Variables
-- =============================================================================

--- Arguments passed to the script (1-based index).
---@type table<integer, string>
argv = {}

--- The global environment table.
---@type table<string, any>
_G = {}

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

--- The number of logical processors available.
---@type integer
cpu_count = 1

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




-- =============================================================================
-- // removed //
-- =============================================================================


--- debug not included in moonsharp default preset
---@deprecated Debug library is not available in this environment.
function debug() end

--- Removed from the global scope for security.
---@deprecated Use import() for internal script loading.
function loadfile() end

--- Removed from the global scope for security.
---@deprecated Use import() for internal script loading.
function dofile(...) end

--- Removed from the engine version of the IO library.
---@deprecated Use file:read() on an open handle instead of reading from stdin.
function io.read(...) end

--- Removed from the engine version of the IO library.
---@deprecated Use sdk.exec or sdk.run_process instead.
function io.popen(...) end

--- Removed from the engine version of the IO library.
---@deprecated Use file:flush() on an open handle.
function io.flush() end

--- Removed from the engine version of the IO library.
---@deprecated Default input handles are managed internally.
function io.input(...) end

--- Removed from the engine version of the IO library.
---@deprecated Default output handles are managed internally.
function io.output(...) end

--- Removed from the engine version of the IO library.
---@deprecated Use io.open with a valid workspace path.
function io.tmpfile() end

--- Removed from the engine version of the IO library.
---@deprecated Use type() or check handles directly.
function io.type(...) end

--- Removed from the engine version of the IO library.
---@deprecated Use file:read("*l") in a loop.
function io.lines(...) end

--- Removed from the engine version of the OS library for security.
---@deprecated Use sdk.exec or sdk.run_process instead.
function os.execute(...) end

--- Removed from the engine version of the OS library.
---@deprecated Use sdk.remove_file instead.
function os.remove(...) end

--- Removed from the engine version of the OS library.
---@deprecated Use sdk.rename_file or sdk.move_dir instead.
function os.rename(...) end

--- Removed from the engine version of the OS library.
---@deprecated Temporary file paths are not exposed directly.
function os.tmpname() end

--- Removed from the engine version of the OS library.
---@deprecated Locale settings are managed by the host application.
function os.setlocale(...) end

-- // end //



