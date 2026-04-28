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

