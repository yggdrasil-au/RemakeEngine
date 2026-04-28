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



