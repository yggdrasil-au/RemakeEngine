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

