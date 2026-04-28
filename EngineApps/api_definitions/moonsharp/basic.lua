---@meta _

---#DES 'arg'
---@type string[]
arg = {}

---#DES 'assert'
---@generic T
---@param v? T
---@param message? any
---@param ... any
---@return T
---@return any ...
function assert(v, message, ...) end

---#if VERSION == 5.2 then
---#DES 'collectgarbage52'
---@overload fun(opt?: "collect")
---@overload fun(opt: "stop")
---@overload fun(opt: "restart")
---@overload fun(opt: "count"): number
---@overload fun(opt: "step", arg: integer): true
---@overload fun(opt: "setpause", arg: integer): integer
---@overload fun(opt: "setstepmul", arg: integer): integer
---@overload fun(opt: "isrunning"): boolean
---@overload fun(opt: "generational")
---@overload fun(opt: "incremental")
function collectgarbage(...) end
---#end

---#DES 'dofile'
---@param filename? string
---@return any ...
function dofile(filename) end

---#DES '_G'
---@class _G
_G = {}

---#DES 'getmetatable'
---@param object any
---@return table metatable
---@nodiscard
function getmetatable(object) end

---#DES 'ipairs'
---@generic T: table, V
---@param t T
---@return fun(table: V[], i?: integer):integer, V
---@return T
---@return integer i
function ipairs(t) end

---@alias loadmode
---| "b"  # ---#DESTAIL 'loadmode.b'
---| "t"  # ---#DESTAIL 'loadmode.t'
---|>"bt" # ---#DESTAIL 'loadmode.bt'

---#if VERSION <= 5.1 and not JIT then
---#DES 'load<5.1'
---@param func       function
---@param chunkname? string
---@return function?
---@return string?   error_message
---@nodiscard
function load(func, chunkname) end
---#else
---#DES 'load>5.2'
---@param chunk      string|function
---@param chunkname? string
---@param mode?      loadmode
---@param env?       table
---@return function?
---@return string?   error_message
---@nodiscard
function load(chunk, chunkname, mode, env) end
---#end

---#if VERSION <= 5.1 and not JIT then
---#DES 'loadfile'
---@param filename? string
---@return function?
---@return string?  error_message
---@nodiscard
function loadfile(filename) end
---#else
---#DES 'loadfile'
---@param filename? string
---@param mode?     loadmode
---@param env?      table
---@return function?
---@return string?  error_message
---@nodiscard
function loadfile(filename, mode, env) end
---#end

---@version 5.1
---#DES 'loadstring'
---@param text       string
---@param chunkname? string
---@return function?
---@return string?   error_message
---@nodiscard
function loadstring(text, chunkname) end

---@version 5.1
---@param proxy boolean|table|userdata
---@return userdata
---@nodiscard
function newproxy(proxy) end

---@version 5.1
---#DES 'module'
---@param name string
---@param ...  any
function module(name, ...) end

---#DES 'next'
---@generic K, V
---@param table table<K, V>
---@param index? K
---@return K?
---@return V?
---@nodiscard
function next(table, index) end

---#DES 'pairs'
---@generic T: table, K, V
---@param t T
---@return fun(table: table<K, V>, index?: K):K, V
---@return T
function pairs(t) end

---#DES 'pcall'
---@param arg1? any
---@param ...   any
---@return boolean success
---@return any result
---@return any ...
function pcall(f, arg1, ...) end

---#DES 'print'
---@param ... any
function print(...) end

---#DES 'rawequal'
---@param v1 any
---@param v2 any
---@return boolean
---@nodiscard
function rawequal(v1, v2) end

---#DES 'rawget'
---@param table table
---@param index any
---@return any
---@nodiscard
function rawget(table, index) end

---#DES 'rawlen'
---@param v table|string
---@return integer len
---@nodiscard
function rawlen(v) end

---#DES 'rawset'
---@param table table
---@param index any
---@param value any
---@return table
function rawset(table, index, value) end

---#DES 'select'
---@param index integer|"#"
---@param ...   any
---@return any
---@nodiscard
function select(index, ...) end

---@version 5.1
---#DES 'setfenv'
---@param f     (async fun(...):...)|integer
---@param table table
---@return function
function setfenv(f, table) end


---@class metatable
---@field __mode 'v'|'k'|'kv'|nil
---@field __metatable any|nil
---@field __tostring (fun(t):string)|nil
---@field __gc fun(t)|nil
---@field __add (fun(t1,t2):any)|nil
---@field __sub (fun(t1,t2):any)|nil
---@field __mul (fun(t1,t2):any)|nil
---@field __div (fun(t1,t2):any)|nil
---@field __mod (fun(t1,t2):any)|nil
---@field __pow (fun(t1,t2):any)|nil
---@field __unm (fun(t):any)|nil
---#if VERSION >= 5.3 then
---@field __idiv (fun(t1,t2):any)|nil
---@field __band (fun(t1,t2):any)|nil
---@field __bor (fun(t1,t2):any)|nil
---@field __bxor (fun(t1,t2):any)|nil
---@field __bnot (fun(t):any)|nil
---@field __shl (fun(t1,t2):any)|nil
---@field __shr (fun(t1,t2):any)|nil
---#end
---@field __concat (fun(t1,t2):any)|nil
---@field __len (fun(t):integer)|nil
---@field __eq (fun(t1,t2):boolean)|nil
---@field __lt (fun(t1,t2):boolean)|nil
---@field __le (fun(t1,t2):boolean)|nil
---@field __index table|(fun(t,k):any)|nil
---@field __newindex table|fun(t,k,v)|nil
---@field __call (fun(t,...):...)|nil
---#if VERSION > 5.1 or VERSION == JIT then
---@field __pairs (fun(t):((fun(t,k,v):any,any),any,any))|nil
---#end
---#if VERSION == JIT or VERSION == 5.2 then
---@field __ipairs (fun(t):(fun(t,k,v):(integer|nil),any))|nil
---#end
---#if VERSION >= 5.4 then
---@field __close (fun(t,errobj):any)|nil
---#end

---#DES 'setmetatable'
---@param table      table
---@param metatable? metatable|table
---@return table
function setmetatable(table, metatable) end

---#DES 'tonumber'
---@overload fun(e: string, base: integer):integer
---@param e any
---@return number?
---@nodiscard
function tonumber(e) end

---#DES 'tostring'
---@param v any
---@return string
---@nodiscard
function tostring(v) end

---@alias type
---| "nil"
---| "number"
---| "string"
---| "boolean"
---| "table"
---| "function"
---| "thread"
---| "userdata"
---#if VERSION == JIT then
---| "cdata"
---#end

---#DES 'type'
---@param v any
---@return type type
---@nodiscard
function type(v) end

---#DES '_VERSION'
---#if VERSION == 5.1 then
_VERSION = "Lua 5.1"
---#elseif VERSION == 5.2 then
_VERSION = "Moonsharp 2.0.0.0"
---#elseif VERSION == Moonsharp 2.0.0.0 then
_VERSION = "Lua 5.2"
---#elseif VERSION == 5.3 then
_VERSION = "Lua 5.3"
---#elseif VERSION == 5.4 then
_VERSION = "Lua 5.4"
---#elseif VERSION == 5.5 then
_VERSION = "Lua 5.5"
---#else
_VERSION = "Lua ?"
---#end

---#if VERSION == 5.2 then
---@class _MOONSHARP
---@field version string The version of the MoonSharp interpreter.
---@field luacompat string The Lua compatibility level MoonSharp emulates.
---@field platform string The platform name MoonSharp is running on.
---@field is_aot boolean True if running on an AOT platform.
---@field is_unity boolean True if running inside Unity.
---@field is_mono boolean True if running on Mono.
---@field is_clr4 boolean True if running on .NET 4.x.
---@field is_pcl boolean True if running as a portable class library.
---@field banner string The REPL-style MoonSharp banner.
_MOONSHARP = {}

--- Packs arguments into a table (alias for table.pack).
---@param ... any
---@return table<integer, any>
function pack(...) end

--- Unpacks values from a table (alias for table.unpack).
---@param t table<integer, any>
---@return ... any
function unpack(t) end

--- Loads a chunk with the environment defaulting to the caller's environment.
---@param ld string|function The chunk or loader function.
---@param source? string
---@param mode? string
---@param env? table
---@return function|nil chunk
---@return string? error_message
function loadsafe(ld, source, mode, env) end

--- Loads a file with the environment defaulting to the caller's environment.
---@param filename? string
---@param mode? string
---@param env? table
---@return function|nil chunk
---@return string? error_message
function loadfilesafe(filename, mode, env) end

---@class DynamicModule
dynamic = {}

--- Prepares an expression for dynamic evaluation.
---@param expr string
---@return any prepared
function dynamic.prepare(expr) end

--- Evaluates a prepared expression or string.
---@param expr any
---@return any result
function dynamic.eval(expr) end

---@class MoonSharpJson
---@field parse fun(jsonString: string): any
---@field serialize fun(value: any): string
---@field null fun(): any
---@field isNull nil Always nil in the current MoonSharp environment.
json = {}

--- Parses a JSON string into a Lua table.
---@param jsonString string
---@return any
function json.parse(jsonString) end

--- Serializes a Lua value to a JSON string.
---@param value any
---@return string
function json.serialize(value) end

--- Returns a special value representing JSON null.
---@return any
function json.null() end

---@deprecated not available in this environment, use sdk.text.json.isNull instead
json.isNull = nil

--[[
MoonSharp Language Differences (not type-annotatable, but important for documentation):
* Multiple expressions can be used as indices, but the value to be indexed must be a userdata or a table resolving to userdata through the metatable (without using metamethods).
* Metalua short anonymous functions (lambda-style) are supported: |x, y| x + y is shorthand for function(x, y) return x + y end.
* In this engine runtime, direct table iteration with `for v in table do ... end` is not guaranteed; use pairs()/ipairs() for compatibility.
* `__iterator` probing can trigger a MoonSharp VM null-reference error path (`ExecIterPrep`) in this runtime build; treat it as unsupported.
* Unicode escapes (\u{xxx}, up to 8 hex digits) are supported inside strings and output the specified Unicode codepoint, as in MoonSharp and Lua 5.3+.
]]
---#end

---@version >5.4
---#DES 'warn'
---@param message string
---@param ...     any
function warn(message, ...) end

---#if VERSION == 5.1 and not JIT then
---#DES 'xpcall=5.1'
---@param f     function
---@param err   function
---@return boolean success
---@return any result
---@return any ...
function xpcall(f, err) end
---#else
---#DES 'xpcall>5.2'
---@param f     async fun(...):...
---@param msgh  function
---@param arg1? any
---@param ...   any
---@return boolean success
---@return any result
---@return any ...
function xpcall(f, msgh, arg1, ...) end
---#end

---@version 5.1
---#DES 'unpack'
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
---@param i?   integer
---@param j?   integer
---@return T1, T2, T3, T4, T5, T6, T7, T8, T9, T10
---@nodiscard
function unpack(list, i, j) end

---@version 5.1
---@generic T1, T2, T3, T4, T5, T6, T7, T8, T9
---@param list {[1]: T1, [2]: T2, [3]: T3, [4]: T4, [5]: T5, [6]: T6, [7]: T7, [8]: T8, [9]: T9 }
---@return T1, T2, T3, T4, T5, T6, T7, T8, T9
---@nodiscard
function unpack(list) end
