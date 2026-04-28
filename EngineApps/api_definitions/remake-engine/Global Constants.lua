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

