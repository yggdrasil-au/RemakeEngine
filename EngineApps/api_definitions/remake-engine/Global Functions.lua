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
--- the start() method is specifically for script wide progress indicating start to end progress
--- the new() method is for creating independent progress bars that can be stepped and updated separately, useful for nested operations or tracking multiple tasks simultaneously
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
--- internally the engine will normalize the output of join using the Normalize() method
--- Example: join("a", "/b", "c") -> "a/b/c"
---@param ... string|number Path segments to join.
---@return string The joined path.
function join(...) end
