--[[
Automated Lua API validation for the RemakeEngine demo module.
This script is intentionally non-interactive and performs pass/fail checks
across exposed globals, modules, functions, and expected behaviors.
--]]

---@class TestState
---@field Passed integer
---@field Failed integer
---@field Skipped integer
---@field Failures table<integer, string>

---@class ArgOptions
---@field scratch? string
---@field module? string
---@field extras table<integer, string>

---@param value any
---@return string
local function ToText(value)
	if value == nil then
		return "nil"
	end
	return tostring(value)
end

---@param color string
---@param message string
local function Emit(color, message)
	if type(sdk) == "table" and type(sdk.color_print) == "function" then
		sdk.color_print(color, message)
	else
		io.write(message .. "\n")
	end
end

---@param list table<integer, string>
---@return ArgOptions
local function ParseArgs(list)
	---@type ArgOptions
	local opts = { extras = {} }
	local i = 1
	while i <= #list do
		local key = list[i]
		if key == "--scratch" and list[i + 1] then
			opts.scratch = list[i + 1]
			i = i + 2
		elseif (key == "--module" or key == "--Game_Root") and list[i + 1] then
			opts.module = list[i + 1]
			i = i + 2
		else
			table.insert(opts.extras, key)
			i = i + 1
		end
	end
	return opts
end

---@param state TestState
---@param name string
---@param condition boolean
---@param details string?
local function Check(state, name, condition, details)
	if condition then
		state.Passed = state.Passed + 1
		Emit("green", "[PASS] " .. name)
	else
		state.Failed = state.Failed + 1
		local msg = "[FAIL] " .. name
		if details and #details > 0 then
			msg = msg .. " :: " .. details
		end
		table.insert(state.Failures, msg)
		Emit("red", msg)
	end
end

---@param state TestState
---@param name string
---@param fn function
---@param ... any
---@return boolean, any, any, any, any
local function SafeCall(state, name, fn, ...)
	local ok, a, b, c, d = pcall(fn, ...)
	Check(state, name, ok, ok and nil or ToText(a))
	return ok, a, b, c, d
end

---@param value any
---@return boolean
local function IsBool(value)
	return type(value) == "boolean"
end

---@param value any
---@return boolean
local function IsNonEmptyString(value)
	return type(value) == "string" and #value > 0
end

---@param value any
---@return boolean
local function IsTable(value)
	return type(value) == "table"
end

---@param text string
---@param needle string
---@return boolean
local function Contains(text, needle)
	return type(text) == "string" and type(needle) == "string" and string.find(text, needle, 1, true) ~= nil
end

---@param path string
local function EnsureCleanPath(path)
	if type(sdk) == "table" then
		if type(sdk.remove_file) == "function" then
			pcall(sdk.remove_file, path)
		end
		if type(sdk.remove_dir) == "function" then
			pcall(sdk.remove_dir, path)
		end
	end
end

---@param opts ArgOptions
---@return string
local function ResolveScratchRoot(opts)
	local moduleRoot = opts.module or Game_Root or "."
	local chosen = opts.scratch
	if type(chosen) == "string" and #chosen > 0 then
		return chosen
	end
	return join(moduleRoot, "TMP", "lua-api-test")
end

---@type table<integer, string>
local InputArgs = argv or { ... }
local Opts = ParseArgs(InputArgs)
local ScratchRoot = ResolveScratchRoot(Opts)

---@type TestState
local State = {
	Passed = 0,
	Failed = 0,
	Skipped = 0,
	Failures = {}
}

Emit("white", "=== RemakeEngine Lua API Automated Test ===")
Emit("cyan", "ScratchRoot: " .. ScratchRoot)

EnsureCleanPath(ScratchRoot)
if type(sdk) == "table" and type(sdk.ensure_dir) == "function" then
	sdk.ensure_dir(ScratchRoot)
end

-- --------------------------------------------------------------------------
-- Global symbols and module availability
-- --------------------------------------------------------------------------

Check(State, "global Game_Root", IsNonEmptyString(Game_Root), ToText(Game_Root))
Check(State, "global Project_Root", IsNonEmptyString(Project_Root), ToText(Project_Root))
Check(State, "global script_dir", IsNonEmptyString(script_dir), ToText(script_dir))
Check(State, "global argv", IsTable(argv), ToText(argv))
Check(State, "global argc", type(argc) == "number", ToText(argc))
Check(State, "global UIMode", type(UIMode) == "string", ToText(UIMode))
Check(State, "global DEBUG", IsBool(DEBUG), ToText(DEBUG))
Check(State, "global cpu_count", type(cpu_count) == "number" and cpu_count >= 1, ToText(cpu_count))

Check(State, "global join", type(join) == "function", ToText(join))
Check(State, "global ResolveToolPath", type(ResolveToolPath) == "function", ToText(ResolveToolPath))
Check(State, "global tool", type(tool) == "function", ToText(tool))
Check(State, "global import", type(import) == "function", ToText(import))
Check(State, "global require", type(require) == "function", ToText(require))
Check(State, "global warn", type(warn) == "function", ToText(warn))
Check(State, "global error", type(error) == "function", ToText(error))
Check(State, "global prompt", type(prompt) == "function", ToText(prompt))
Check(State, "global color_prompt", type(color_prompt) == "function", ToText(color_prompt))
Check(State, "global colour_prompt", type(colour_prompt) == "function", ToText(colour_prompt))

Check(State, "global Diagnostics table", IsTable(Diagnostics), ToText(Diagnostics))
if IsTable(Diagnostics) then
	Check(State, "Diagnostics.Log", type(Diagnostics.Log) == "function", ToText(Diagnostics.Log))
	Check(State, "Diagnostics.Trace", type(Diagnostics.Trace) == "function", ToText(Diagnostics.Trace))
end

Check(State, "global sdk table", IsTable(sdk), ToText(sdk))
Check(State, "global sqlite table", IsTable(sqlite), ToText(sqlite))
Check(State, "global progress table", IsTable(progress), ToText(progress))
Check(State, "global io table", IsTable(io), ToText(io))
Check(State, "global os table", IsTable(os), ToText(os))

Check(State, "loadfile removed", loadfile == nil, ToText(loadfile))
Check(State, "dofile removed", dofile == nil, ToText(dofile))

if IsTable(io) then
	Check(State, "io.open available", type(io.open) == "function", ToText(io.open))
	Check(State, "io.write available", type(io.write) == "function", ToText(io.write))
	Check(State, "io.flush removed", io.flush == nil, ToText(io.flush))
	Check(State, "io.read removed", io.read == nil, ToText(io.read))
	Check(State, "io.popen removed", io.popen == nil, ToText(io.popen))
end

if IsTable(os) then
	Check(State, "os.date available", type(os.date) == "function", ToText(os.date))
	Check(State, "os.time available", type(os.time) == "function", ToText(os.time))
	Check(State, "os.clock available", type(os.clock) == "function", ToText(os.clock))
	Check(State, "os.getenv available", type(os.getenv) == "function", ToText(os.getenv))
	Check(State, "os.exit available", type(os.exit) == "function", ToText(os.exit))
	Check(State, "os.execute removed", os.execute == nil, ToText(os.execute))
end

-- --------------------------------------------------------------------------
-- MoonSharp baseline checks
-- --------------------------------------------------------------------------

Check(State, "_MOONSHARP table", IsTable(_MOONSHARP), ToText(_MOONSHARP))
if IsTable(_MOONSHARP) then
	Check(State, "_MOONSHARP.version baseline", _MOONSHARP.version == "2.0.0.0", ToText(_MOONSHARP.version))
	Check(State, "_MOONSHARP.luacompat baseline", _MOONSHARP.luacompat == "5.2", ToText(_MOONSHARP.luacompat))
	Check(State, "_MOONSHARP.platform", IsNonEmptyString(_MOONSHARP.platform), ToText(_MOONSHARP.platform))
	Check(State, "_MOONSHARP.is_aot", IsBool(_MOONSHARP.is_aot), ToText(_MOONSHARP.is_aot))
	Check(State, "_MOONSHARP.is_unity", IsBool(_MOONSHARP.is_unity), ToText(_MOONSHARP.is_unity))
	Check(State, "_MOONSHARP.is_mono", IsBool(_MOONSHARP.is_mono), ToText(_MOONSHARP.is_mono))
	Check(State, "_MOONSHARP.is_clr4", IsBool(_MOONSHARP.is_clr4), ToText(_MOONSHARP.is_clr4))
	Check(State, "_MOONSHARP.is_pcl", IsBool(_MOONSHARP.is_pcl), ToText(_MOONSHARP.is_pcl))
	Check(State, "_MOONSHARP.banner", Contains(ToText(_MOONSHARP.banner), "MoonSharp"), ToText(_MOONSHARP.banner))
end

Check(State, "global pack", type(pack) == "function", ToText(pack))
Check(State, "global unpack", type(unpack) == "function", ToText(unpack))
Check(State, "global loadsafe", type(loadsafe) == "function", ToText(loadsafe))
Check(State, "global loadfilesafe", type(loadfilesafe) == "function", ToText(loadfilesafe))
if type(pack) == "function" and type(unpack) == "function" then
	local Packed = pack(1, 2, 3)
	Check(State, "pack/unpack behavior", IsTable(Packed) and Packed[1] == 1 and Packed[2] == 2 and Packed[3] == 3, ToText(Packed))
end

Check(State, "string.unicode", type(string.unicode) == "function", ToText(string.unicode))
Check(State, "string.contains", type(string.contains) == "function", ToText(string.contains))
Check(State, "string.startsWith", type(string.startsWith) == "function", ToText(string.startsWith))
Check(State, "string.endsWith", type(string.endsWith) == "function", ToText(string.endsWith))
if type(string.unicode) == "function" then
	Check(State, "string.unicode('A')", string.unicode("A") == 65, ToText(string.unicode("A")))
end
if type(string.contains) == "function" then
	Check(State, "string.contains behavior", string.contains("Hello World", "World") == true, ToText(string.contains("Hello World", "World")))
end
if type(string.startsWith) == "function" then
	Check(State, "string.startsWith behavior", string.startsWith("Hello World", "Hello") == true, ToText(string.startsWith("Hello World", "Hello")))
end
if type(string.endsWith) == "function" then
	Check(State, "string.endsWith behavior", string.endsWith("Hello World", "World") == true, ToText(string.endsWith("Hello World", "World")))
end

Check(State, "global dynamic module", IsTable(dynamic), ToText(dynamic))
if IsTable(dynamic) then
	Check(State, "dynamic.prepare", type(dynamic.prepare) == "function", ToText(dynamic.prepare))
	Check(State, "dynamic.eval", type(dynamic.eval) == "function", ToText(dynamic.eval))
	if type(dynamic.prepare) == "function" and type(dynamic.eval) == "function" then
		local DynamicPrepared = dynamic.prepare("1 + 2")
		local DynamicEval = dynamic.eval(DynamicPrepared)
		Check(State, "dynamic eval behavior", DynamicEval == 3, ToText(DynamicEval))
	end
end

Check(State, "global json module", IsTable(json), ToText(json))
if IsTable(json) then
	Check(State, "json.serialize", type(json.serialize) == "function", ToText(json.serialize))
	Check(State, "json.parse", type(json.parse) == "function", ToText(json.parse))
	Check(State, "json.null", type(json.null) == "function", ToText(json.null))
	Check(State, "json.isNull optional nil", json.isNull == nil or type(json.isNull) == "function", ToText(json.isNull))
	if type(json.serialize) == "function" and type(json.parse) == "function" and type(json.null) == "function" then
		local JsonStatus, JsonPayload = pcall(function()
			return json.serialize({ a = 1, b = json.null() })
		end)
		Check(State, "json.serialize behavior", JsonStatus and IsNonEmptyString(JsonPayload), JsonStatus and nil or ToText(JsonPayload))
		if JsonStatus then
			local JsonParseOk, JsonParsed = pcall(function()
				return json.parse(JsonPayload)
			end)
			Check(State, "json.parse behavior", JsonParseOk and IsTable(JsonParsed), JsonParseOk and nil or ToText(JsonParsed))
		end
	end
end

local Euro = "\u{20AC}"
Check(State, "unicode escape behavior", type(Euro) == "string" and type(string.unicode) == "function" and string.unicode(Euro) == 8364, ToText(Euro))

local LambdaOk, LambdaLoader = pcall(function()
	return load("return |x, y| x + y")
end)
Check(State, "lambda loader accepted", LambdaOk and type(LambdaLoader) == "function", LambdaOk and nil or ToText(LambdaLoader))
if LambdaOk and type(LambdaLoader) == "function" then
	local LambdaCtorOk, LambdaFunc = pcall(LambdaLoader)
	Check(State, "lambda constructor call", LambdaCtorOk and type(LambdaFunc) == "function", LambdaCtorOk and nil or ToText(LambdaFunc))
	if LambdaCtorOk and type(LambdaFunc) == "function" then
		local LambdaValue = LambdaFunc(5, 10)
		Check(State, "lambda behavior", LambdaValue == 15, ToText(LambdaValue))
	end
end

-- --------------------------------------------------------------------------
-- Global behavior checks
-- --------------------------------------------------------------------------

local Joined = join("C:/Base/", "/SubDir", "\\File.dat")
Check(State, "join behavior", Joined == "C:/Base/SubDir/File.dat", Joined)

local ToolOk, ToolPath = pcall(function()
	return tool("ffmpeg")
end)
Check(State, "tool('ffmpeg') call", ToolOk, ToolOk and nil or ToText(ToolPath))
if ToolOk then
	Check(State, "tool('ffmpeg') value", type(ToolPath) == "string", ToText(ToolPath))
end

SafeCall(State, "warn call", warn, "lua_api_test warning channel")
SafeCall(State, "error call", error, "lua_api_test error channel")
if IsTable(Diagnostics) then
	SafeCall(State, "Diagnostics.Log call", Diagnostics.Log, "lua_api_test Diagnostics.Log")
	SafeCall(State, "Diagnostics.Trace call", Diagnostics.Trace, "lua_api_test Diagnostics.Trace")
end

-- Prompt functions are intentionally not invoked to keep the test fully non-interactive.

local DateNoFmt = os.date()
Check(State, "os.date() returns unix timestamp", type(DateNoFmt) == "number" and DateNoFmt > 0, ToText(DateNoFmt))
local DateFmt = os.date("%Y-%m-%d")
Check(State, "os.date(format) returns string", IsNonEmptyString(DateFmt), ToText(DateFmt))
local DateTable = os.date("*t")
Check(State, "os.date('*t') returns table", IsTable(DateTable), ToText(DateTable))
if IsTable(DateTable) then
	Check(State, "os.date('*t').year", type(DateTable.year) == "number", ToText(DateTable.year))
end

local OsTime = os.time()
Check(State, "os.time() returns number", type(OsTime) == "number" and OsTime > 0, ToText(OsTime))
local OsClock = os.clock()
Check(State, "os.clock() returns number", type(OsClock) == "number", ToText(OsClock))
local BlockedPathEnv = os.getenv("Path")
Check(State, "os.getenv('Path') blocked", BlockedPathEnv == nil, ToText(BlockedPathEnv))

-- --------------------------------------------------------------------------
-- io module functional checks
-- --------------------------------------------------------------------------

local IoFile = join(ScratchRoot, "io_test.txt")
EnsureCleanPath(IoFile)

local FileWriteHandle, FileWriteErr = io.open(IoFile, "w")
Check(State, "io.open write handle", FileWriteHandle ~= nil and type(FileWriteHandle.write) == "function", ToText(FileWriteErr))
if FileWriteHandle then
	FileWriteHandle:write("line1\nline2\n")
	FileWriteHandle:flush()
	FileWriteHandle:close()
end

local FileAppendHandle = io.open(IoFile, "a")
Check(State, "io.open append handle", FileAppendHandle ~= nil, ToText(FileAppendHandle))
if FileAppendHandle then
	FileAppendHandle:write("line3\n")
	FileAppendHandle:close()
end

local FileReadHandle = io.open(IoFile, "r")
Check(State, "io.open read handle", FileReadHandle ~= nil, ToText(FileReadHandle))
if FileReadHandle then
	local Whole = FileReadHandle:read("*a")
	Check(State, "io.read('*a')", Contains(Whole, "line1") and Contains(Whole, "line3"), ToText(Whole))
	FileReadHandle:close()
end

local FileBinaryHandle = io.open(IoFile, "rb")
Check(State, "io.open binary handle", FileBinaryHandle ~= nil, ToText(FileBinaryHandle))
if FileBinaryHandle then
	local FirstBytes = FileBinaryHandle:read(5)
	Check(State, "io.read(number)", type(FirstBytes) == "string" and #FirstBytes == 5, ToText(FirstBytes))
	local SeekPos = FileBinaryHandle:seek("set", 0)
	Check(State, "io.seek", type(SeekPos) == "number", ToText(SeekPos))
	local FirstLine = FileBinaryHandle:read("*line")
	Check(State, "io.read('*line')", type(FirstLine) == "string", ToText(FirstLine))
	FileBinaryHandle:close()
end

SafeCall(State, "io.write call", io.write, "")

-- --------------------------------------------------------------------------
-- sdk table shape checks
-- --------------------------------------------------------------------------

---@type table<integer, string>
local SdkFunctions = {
	"color_print", "colour_print", "validate_source_dir", "find_subdir", "has_all_subdirs",
	"path_exists", "lexists", "absolute_path", "realpath", "attributes", "mkdir", "ensure_dir",
	"is_dir", "copy_dir", "move_dir", "remove_dir", "currentdir", "current_dir", "list_dir",
	"is_file", "is_absolute", "remove_file", "copy_file", "write_file", "read_file", "rename_file",
	"is_writable", "is_symlink", "create_hardlink", "create_symlink", "readlink",
	"extract_archive", "create_archive", "exec", "execSilent", "run_process", "spawn_process",
	"poll_process", "wait_process", "close_process", "md5", "sha1_file", "sleep", "cpu_count",
	"toml_read_file", "toml_write_file", "yaml_read_file", "yaml_write_file"
}

for _, name in ipairs(SdkFunctions) do
	Check(State, "sdk." .. name .. " available", type(sdk[name]) == "function" or name == "cpu_count", ToText(sdk[name]))
end
Check(State, "sdk.cpu_count mirrors global", sdk.cpu_count == cpu_count, ToText(sdk.cpu_count))

Check(State, "sdk.Hash table", IsTable(sdk.Hash), ToText(sdk.Hash))
if IsTable(sdk.Hash) then
	Check(State, "sdk.Hash.md5", type(sdk.Hash.md5) == "function", ToText(sdk.Hash.md5))
	Check(State, "sdk.Hash.sha1_file", type(sdk.Hash.sha1_file) == "function", ToText(sdk.Hash.sha1_file))
end

Check(State, "sdk.text table", IsTable(sdk.text), ToText(sdk.text))
if IsTable(sdk.text) then
	Check(State, "sdk.text.json table", IsTable(sdk.text.json), ToText(sdk.text.json))
	Check(State, "sdk.text.toml table", IsTable(sdk.text.toml), ToText(sdk.text.toml))
	Check(State, "sdk.text.yaml table", IsTable(sdk.text.yaml), ToText(sdk.text.yaml))
end

SafeCall(State, "sdk.color_print call", sdk.color_print, "white", "")
SafeCall(State, "sdk.colour_print call", sdk.colour_print, { colour = "white", message = "", newline = true })
SafeCall(State, "sdk.sleep call", sdk.sleep, 0.001)

-- --------------------------------------------------------------------------
-- Filesystem, hash, text, archive checks
-- --------------------------------------------------------------------------

local FsRoot = join(ScratchRoot, "fs")
local SourceDir = join(FsRoot, "source")
local DestDir = join(FsRoot, "dest")
local MovedDir = join(FsRoot, "moved")
local SubDir1 = join(FsRoot, "sub1")
local SubDir2 = join(FsRoot, "sub2")
local FileA = join(SourceDir, "a.txt")
local FileB = join(SourceDir, "b.txt")
local FileC = join(SourceDir, "c.txt")

EnsureCleanPath(FsRoot)
sdk.ensure_dir(SourceDir)
sdk.ensure_dir(SubDir1)
sdk.ensure_dir(SubDir2)

local ExistsDir = sdk.path_exists(SourceDir)
local LexistsDir = sdk.lexists(SourceDir)
local IsDirResult = sdk.is_dir(SourceDir)
local ValidateSourceDirResult = sdk.validate_source_dir(SourceDir)
Check(State, "sdk.path_exists dir", ExistsDir == true, ToText(ExistsDir))
Check(State, "sdk.lexists dir", LexistsDir == true, ToText(LexistsDir))
Check(State, "sdk.is_dir", IsDirResult == true, ToText(IsDirResult))
Check(State, "sdk.validate_source_dir", ValidateSourceDirResult == true, ToText(ValidateSourceDirResult))

local FoundSub = sdk.find_subdir(FsRoot, "sub1")
Check(State, "sdk.find_subdir", IsNonEmptyString(FoundSub), ToText(FoundSub))
local HasAllSubdirsResult = sdk.has_all_subdirs(FsRoot, { "sub1", "sub2" })
Check(State, "sdk.has_all_subdirs", HasAllSubdirsResult == true, ToText(HasAllSubdirsResult))

local WriteFileResult = sdk.write_file(FileA, "abc")
Check(State, "sdk.write_file", WriteFileResult == true, ToText(WriteFileResult))
local ReadBack = sdk.read_file(FileA)
Check(State, "sdk.read_file", ReadBack == "abc", ToText(ReadBack))

local Attr = sdk.attributes(FileA)
Check(State, "sdk.attributes returns table", IsTable(Attr), ToText(Attr))
if IsTable(Attr) then
	Check(State, "sdk.attributes.mode", Attr.mode == "file", ToText(Attr.mode))
end

local Listed = sdk.list_dir(FsRoot)
Check(State, "sdk.list_dir returns table", IsTable(Listed), ToText(Listed))

local CurrentDirProcess = sdk.currentdir()
local CurrentDirScript = sdk.current_dir()
local IsFileResult = sdk.is_file(FileA)
local IsAbsoluteResult = sdk.is_absolute(Game_Root)
local AbsolutePathResult = sdk.absolute_path("relative.txt")
local RealPathResult = sdk.realpath(FileA)
local IsWritableResult = sdk.is_writable(SourceDir)
Check(State, "sdk.currentdir", IsNonEmptyString(CurrentDirProcess), ToText(CurrentDirProcess))
Check(State, "sdk.current_dir", IsNonEmptyString(CurrentDirScript), ToText(CurrentDirScript))
Check(State, "sdk.is_file", IsFileResult == true, ToText(IsFileResult))
Check(State, "sdk.is_absolute(Game_Root)", IsAbsoluteResult == true, ToText(IsAbsoluteResult))
Check(State, "sdk.absolute_path", IsNonEmptyString(AbsolutePathResult), ToText(AbsolutePathResult))
Check(State, "sdk.realpath", IsNonEmptyString(RealPathResult), ToText(RealPathResult))
Check(State, "sdk.is_writable(SourceDir)", IsWritableResult == true, ToText(IsWritableResult))

local CopyFileResult = sdk.copy_file(FileA, FileB, true)
local RenameFileResult = sdk.rename_file(FileB, FileC, true)
local RemoveFileResult = sdk.remove_file(FileC)
Check(State, "sdk.copy_file", CopyFileResult == true, ToText(CopyFileResult))
Check(State, "sdk.rename_file", RenameFileResult == true, ToText(RenameFileResult))
Check(State, "sdk.remove_file", RemoveFileResult == true, ToText(RemoveFileResult))

local CopyDirResult = sdk.copy_dir(SourceDir, DestDir, true)
local MoveDirResult = sdk.move_dir(DestDir, MovedDir, true)
local RemoveDirResult = sdk.remove_dir(MovedDir)
Check(State, "sdk.copy_dir", CopyDirResult == true, ToText(CopyDirResult))
Check(State, "sdk.move_dir", MoveDirResult == true, ToText(MoveDirResult))
Check(State, "sdk.remove_dir", RemoveDirResult == true, ToText(RemoveDirResult))

local HardLinkPath = join(SourceDir, "a_hardlink.txt")
local HardLinkResult = sdk.create_hardlink(FileA, HardLinkPath)
Check(State, "sdk.create_hardlink returns bool", IsBool(HardLinkResult), ToText(HardLinkResult))
if HardLinkResult then
	local HardLinkReadBack = sdk.read_file(HardLinkPath)
	Check(State, "hardlink readable", HardLinkReadBack == "abc", ToText(HardLinkReadBack))
end

local SymlinkPath = join(SourceDir, "a_symlink.txt")
EnsureCleanPath(SymlinkPath)
local SymlinkResult = sdk.create_symlink(FileA, SymlinkPath, false, true)
Check(State, "sdk.create_symlink returns bool", IsBool(SymlinkResult), ToText(SymlinkResult))
if SymlinkResult then
	local IsSymlinkResult = sdk.is_symlink(SymlinkPath)
	local ReadLinkResult = sdk.readlink(SymlinkPath)
	Check(State, "sdk.is_symlink true", IsSymlinkResult == true, ToText(IsSymlinkResult))
	Check(State, "sdk.readlink returns string", IsNonEmptyString(ReadLinkResult), ToText(ReadLinkResult))
end

local Md5 = sdk.md5("abc")
Check(State, "sdk.md5", Md5 == "900150983cd24fb0d6963f7d28e17f72", ToText(Md5))
Check(State, "sdk.Hash.md5 parity", sdk.Hash.md5("abc") == Md5, ToText(sdk.Hash.md5("abc")))

local Sha1 = sdk.sha1_file(FileA)
Check(State, "sdk.sha1_file", Sha1 == "a9993e364706816aba3e25717850c26c9cd0d89d", ToText(Sha1))
Check(State, "sdk.Hash.sha1_file parity", sdk.Hash.sha1_file(FileA) == Sha1, ToText(sdk.Hash.sha1_file(FileA)))

local JsonEncoded = sdk.text.json.encode({ alpha = 1, beta = true, gamma = "x" }, { indent = true })
Check(State, "sdk.text.json.encode", IsNonEmptyString(JsonEncoded), ToText(JsonEncoded))
local JsonDecoded = sdk.text.json.decode(JsonEncoded)
Check(State, "sdk.text.json.decode", IsTable(JsonDecoded), ToText(JsonDecoded))
if IsTable(JsonDecoded) then
	Check(State, "json roundtrip alpha", JsonDecoded.alpha == 1, ToText(JsonDecoded.alpha))
	Check(State, "json roundtrip beta", JsonDecoded.beta == true, ToText(JsonDecoded.beta))
end
Check(State, "sdk.text.json.isNull(nil)", sdk.text.json.isNull(nil) == true, ToText(sdk.text.json.isNull(nil)))

local TomlPath = join(ScratchRoot, "sample.toml")
sdk.toml_write_file(TomlPath, { app = { name = "lua_api_test", enabled = true } })
local TomlRead = sdk.toml_read_file(TomlPath)
Check(State, "sdk.toml_read_file", IsTable(TomlRead), ToText(TomlRead))
if IsTable(TomlRead) and IsTable(TomlRead.app) then
	Check(State, "toml roundtrip", TomlRead.app.name == "lua_api_test", ToText(TomlRead.app.name))
end

local TomlPath2 = join(ScratchRoot, "sample2.toml")
sdk.text.toml.write_file(TomlPath2, { app = { name = "lua_api_test_2" } })
local TomlRead2 = sdk.text.toml.read_file(TomlPath2)
Check(State, "sdk.text.toml.read_file", IsTable(TomlRead2), ToText(TomlRead2))

local YamlPath = join(ScratchRoot, "sample.yaml")
sdk.yaml_write_file(YamlPath, { app = { name = "lua_api_test", list = { "a", "b" } } })
local YamlRead = sdk.yaml_read_file(YamlPath)
Check(State, "sdk.yaml_read_file", IsTable(YamlRead), ToText(YamlRead))
if IsTable(YamlRead) and IsTable(YamlRead.app) then
	Check(State, "yaml roundtrip", YamlRead.app.name == "lua_api_test", ToText(YamlRead.app.name))
end

local YamlEncoded = sdk.text.yaml.encode({ a = 1, b = "two" }, {})
Check(State, "sdk.text.yaml.encode", IsNonEmptyString(YamlEncoded), ToText(YamlEncoded))
local YamlDecoded = sdk.text.yaml.decode(YamlEncoded)
Check(State, "sdk.text.yaml.decode", IsTable(YamlDecoded), ToText(YamlDecoded))

local ArchiveSource = join(ScratchRoot, "archive_source")
local ArchiveZip = join(ScratchRoot, "sample.zip")
local ArchiveExtract = join(ScratchRoot, "archive_extract")
EnsureCleanPath(ArchiveSource)
EnsureCleanPath(ArchiveZip)
EnsureCleanPath(ArchiveExtract)
sdk.ensure_dir(ArchiveSource)
sdk.write_file(join(ArchiveSource, "inside.txt"), "zip-content")
local CreateArchiveResult = sdk.create_archive(ArchiveSource, ArchiveZip, "zip")
local ExtractArchiveResult = sdk.extract_archive(ArchiveZip, ArchiveExtract)
local ExtractedFileExists = sdk.path_exists(join(ArchiveExtract, "inside.txt"))
Check(State, "sdk.create_archive(zip)", CreateArchiveResult == true, ToText(CreateArchiveResult))
Check(State, "sdk.extract_archive(zip)", ExtractArchiveResult == true, ToText(ExtractArchiveResult))
Check(State, "archive extracted file", ExtractedFileExists == true, ToText(ExtractedFileExists))

-- --------------------------------------------------------------------------
-- import/require checks
-- --------------------------------------------------------------------------

local ImportTargetBase = join(ScratchRoot, "import_target")
sdk.write_file(ImportTargetBase .. ".lua", "return { ok = true, value = 123 }")

local ImportOk, ImportValue = pcall(function()
	return import(ImportTargetBase)
end)
Check(State, "import() call", ImportOk, ImportOk and nil or ToText(ImportValue))
if ImportOk then
	Check(State, "import() result", IsTable(ImportValue) and ImportValue.ok == true and ImportValue.value == 123, ToText(ImportValue))
end

local RequireOk, RequireValue = pcall(function()
	return require(ImportTargetBase)
end)
Check(State, "require() call", RequireOk, RequireOk and nil or ToText(RequireValue))
if RequireOk then
	Check(State, "require() result", IsTable(RequireValue) and RequireValue.value == 123, ToText(RequireValue))
end

-- --------------------------------------------------------------------------
-- progress and sqlite checks
-- --------------------------------------------------------------------------

local Panel = progress.new(3, "lua-api-test-panel", "panel")
Check(State, "progress.new returns userdata/table", Panel ~= nil, ToText(Panel))
if Panel then
	SafeCall(State, "Panel:Update", function()
		Panel:Update(1)
	end)
	SafeCall(State, "Panel:Complete", function()
		Panel:Complete()
	end)
end

local ScriptProgress = progress.start(2, "lua-api-test-script")
Check(State, "progress.start returns handle", ScriptProgress ~= nil, ToText(ScriptProgress))
SafeCall(State, "progress.step", progress.step, "step-1")
SafeCall(State, "progress.add_steps", progress.add_steps, 1)
SafeCall(State, "progress.step 2", progress.step, "step-2")
SafeCall(State, "progress.finish", progress.finish)

local SqlitePath = join(ScratchRoot, "api_test.sqlite")
EnsureCleanPath(SqlitePath)

local DbOpenOk, Db = pcall(function()
	return sqlite.open(SqlitePath)
end)
Check(State, "sqlite.open", DbOpenOk and Db ~= nil, DbOpenOk and nil or ToText(Db))

if DbOpenOk and Db then
	SafeCall(State, "sqlite.exec drop", function()
		Db:exec("DROP TABLE IF EXISTS items")
	end)
	SafeCall(State, "sqlite.exec create", function()
		Db:exec("CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT, value INTEGER)")
	end)
	local InsertOk, InsertAffected = pcall(function()
		return Db:exec("INSERT INTO items(name, value) VALUES(:name, :value)", { name = "alpha", value = 10 })
	end)
	Check(State, "sqlite.exec insert", InsertOk and InsertAffected == 1, InsertOk and ToText(InsertAffected) or ToText(InsertAffected))

	local QueryOk, Rows = pcall(function()
		return Db:query("SELECT name, value FROM items WHERE name = :name", { name = "alpha" })
	end)
	Check(State, "sqlite.query", QueryOk and IsTable(Rows) and IsTable(Rows[1]), QueryOk and nil or ToText(Rows))
	if QueryOk and IsTable(Rows) and IsTable(Rows[1]) then
		Check(State, "sqlite.query row value", Rows[1].name == "alpha" and Rows[1].value == 10, ToText(Rows[1].name) .. "," .. ToText(Rows[1].value))
	end

	SafeCall(State, "sqlite.begin", function() Db:begin() end)
	SafeCall(State, "sqlite.exec in tx", function() Db:exec("INSERT INTO items(name, value) VALUES('beta', 20)") end)
	SafeCall(State, "sqlite.rollback", function() Db:rollback() end)

	local RollbackCheckOk, RollbackRows = pcall(function()
		return Db:query("SELECT name FROM items WHERE name = 'beta'")
	end)
	Check(State, "sqlite.rollback effective", RollbackCheckOk and IsTable(RollbackRows) and RollbackRows[1] == nil, ToText(RollbackRows))

	SafeCall(State, "sqlite.begin commit", function() Db:begin() end)
	SafeCall(State, "sqlite.exec commit", function() Db:exec("INSERT INTO items(name, value) VALUES('gamma', 30)") end)
	SafeCall(State, "sqlite.commit", function() Db:commit() end)
	local CommitCheckOk, CommitRows = pcall(function()
		return Db:query("SELECT name FROM items WHERE name = 'gamma'")
	end)
	Check(State, "sqlite.commit effective", CommitCheckOk and IsTable(CommitRows) and CommitRows[1] ~= nil, ToText(CommitRows))

	SafeCall(State, "sqlite.close", function() Db:close() end)
end

-- --------------------------------------------------------------------------
-- process execution checks (non-interactive)
-- --------------------------------------------------------------------------

local RunProc = sdk.run_process({ "pwsh", "-NoProfile", "-Command", "Write-Output run_process_ok" }, {
	capture_stdout = true,
	capture_stderr = true,
	timeout_ms = 5000
})
Check(State, "sdk.run_process result table", IsTable(RunProc), ToText(RunProc))
if IsTable(RunProc) then
	Check(State, "sdk.run_process success", RunProc.success == true and RunProc.exit_code == 0, ToText(RunProc.exit_code))
end

local ExecRes = sdk.exec({ "pwsh", "-NoProfile", "-Command", "Write-Output exec_ok" }, {
	wait = true,
	new_terminal = false
})
Check(State, "sdk.exec result table", IsTable(ExecRes), ToText(ExecRes))
if IsTable(ExecRes) then
	Check(State, "sdk.exec success", ExecRes.success == true and ExecRes.exit_code == 0, ToText(ExecRes.exit_code))
end

local ExecSilentRes = sdk.execSilent({ "pwsh", "-NoProfile", "-Command", "Write-Output exec_silent_ok" }, {
	wait = true,
	new_terminal = false
})
Check(State, "sdk.execSilent result table", IsTable(ExecSilentRes), ToText(ExecSilentRes))

local SpawnRes = sdk.spawn_process({ "pwsh", "-NoProfile", "-Command", "Start-Sleep -Milliseconds 200; Write-Output spawned_ok" }, {
	capture_stdout = true,
	capture_stderr = true
})
Check(State, "sdk.spawn_process result table", IsTable(SpawnRes), ToText(SpawnRes))
if IsTable(SpawnRes) and type(SpawnRes.pid) == "number" then
	local PollRes = sdk.poll_process(SpawnRes.pid)
	Check(State, "sdk.poll_process result table", IsTable(PollRes), ToText(PollRes))
	local WaitRes = sdk.wait_process(SpawnRes.pid, 3000)
	Check(State, "sdk.wait_process result table", IsTable(WaitRes), ToText(WaitRes))
	local CloseRes = sdk.close_process(SpawnRes.pid)
	Check(State, "sdk.close_process bool", IsBool(CloseRes), ToText(CloseRes))
end

-- --------------------------------------------------------------------------
-- Summary
-- --------------------------------------------------------------------------

Emit("white", "")
Emit("white", string.format("Total Passed: %d", State.Passed))
Emit("white", string.format("Total Failed: %d", State.Failed))

if State.Failed > 0 then
	Emit("red", "Lua API test failed.")
	for _, failure in ipairs(State.Failures) do
		Emit("red", failure)
	end
	os.exit(1)
else
	Emit("green", "Lua API test passed.")
	os.exit(0)
end

