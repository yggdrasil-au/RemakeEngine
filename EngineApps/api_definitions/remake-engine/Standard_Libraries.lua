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
-- 2. Standard Libraries
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


