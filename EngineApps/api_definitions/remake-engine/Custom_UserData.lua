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

--- Sets the total number of steps for the progress bar.
---@param total integer
function PanelProgress:SetTotal(total) end

--- Sets the label shown for the progress bar.
---@param label string
function PanelProgress:SetLabel(label) end

--- Marks the progress as complete and closes the panel.
function PanelProgress:Complete() end

--- Disposes the progress handle (same as Complete()).
function PanelProgress:Dispose() end

--- Represents a fully controllable console progress panel.
---@class ConsoleProgress
---@field Id string The progress id.
---@field Current integer The current processed count.
---@field Total integer The total number of steps.
---@field Ok integer The ok count.
---@field Skip integer The skip count.
---@field Err integer The error count.
---@field Label string|nil The progress label.
local ConsoleProgress = {}

--- Returns the progress id.
---@return string
function ConsoleProgress:GetId() end

--- Returns the total count.
---@return integer
function ConsoleProgress:GetTotal() end

--- Returns the processed count.
---@return integer
function ConsoleProgress:GetProcessed() end

--- Returns the ok count.
---@return integer
function ConsoleProgress:GetOk() end

--- Returns the skip count.
---@return integer
function ConsoleProgress:GetSkip() end

--- Returns the error count.
---@return integer
function ConsoleProgress:GetErr() end

--- Returns the current label.
---@return string
function ConsoleProgress:GetLabel() end

--- Sets the total number of steps for the progress bar.
---@param total integer
function ConsoleProgress:SetTotal(total) end

--- Sets the label shown for the progress bar.
---@param label string
function ConsoleProgress:SetLabel(label) end

--- Sets all counter stats at once.
---@param processed integer
---@param ok integer
---@param skip integer
---@param err integer
function ConsoleProgress:SetStats(processed, ok, skip, err) end

--- Updates the progress counters by the provided increments.
---@param processed? integer
---@param ok? integer
---@param skip? integer
---@param err? integer
function ConsoleProgress:Update(processed, ok, skip, err) end

--- Adds a job entry to the active list.
---@param tool string
---@param file string
function ConsoleProgress:AddJob(tool, file) end

--- Removes a job entry by file name.
---@param file string
function ConsoleProgress:RemoveJob(file) end

--- Marks the progress as complete and closes the panel.
function ConsoleProgress:Complete() end

--- Disposes the progress handle (same as Complete()).
function ConsoleProgress:Dispose() end

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


--- Represents a single file or directory entry inside an optical disc image (.iso).
---@class IsoEntry
---@field path string Normalized internal path inside the disc (e.g. "PS3_GAME/PARAM.SFO" or "default.xex").
---@field name string Entry file or directory name.
---@field size integer Uncompressed file size in bytes (0 for directories).
---@field is_dir boolean True if the entry is a directory.
---@field sector integer Starting 2048-byte sector index relative to partition base.
---@field offset integer Absolute byte offset within the underlying .iso file.

--- Options controlling batch extraction of files from an optical disc image.
---@class IsoExtractOptions
---@field destination string Output directory path on the host filesystem.
---@field pattern? string Optional glob pattern (e.g. "USRDIR/*", "*.xex", "GAME*.DAT").
---@field filter? string Alias for pattern.
---@field overwrite? boolean Overwrite existing files (default false).
---@field progress_panel? PanelProgress|ConsoleProgress Optional engine progress panel handle to update during extraction.

--- Summary result returned after extracting files from an optical disc image.
---@class IsoExtractResult
---@field success boolean True if extraction finished without fatal errors.
---@field files_extracted integer Number of files written to disk.
---@field files_skipped integer Number of existing files skipped when overwrite is false.
---@field bytes_extracted integer Total bytes written to disk.
---@field error? string Error message if extraction failed.

--- Optical Disc Image (.iso) Module ( Xbox Original XISO/XGD1, Xbox 360 XGD2/XGD3, PS3 ISO9660/Joliet/UDF, PS2 ISO9660 ).
---@class IsoModule
iso = {}

--- Opens a console optical disc image (.iso) for inspection, virtual streaming, or extraction.
---@param path string Host path to the .iso file.
---@return IsoReader|nil reader The opened disc reader handle, or nil on failure.
---@return string|nil error Error message if opening failed.
function iso.open(path) end

--- Probes a file to check whether it is a supported optical disc image without throwing.
---@param path string Host path to the candidate file.
---@return boolean is_disc True if a supported optical disc header was detected.
---@return string|nil format Detected format ("XGD2", "XGD3", "XGD1", "XISO", "PS3_HYBRID", "PS2_ISO9660", "STANDARD_ISO9660").
function iso.is_iso(path) end

--- One-step batch extraction helper for optical disc images.
---@param iso_path string Host path to the .iso file.
---@param dest_dir string Target directory on the host disk.
---@param options? IsoExtractOptions|string Optional options table or glob filter pattern.
---@return boolean success True if extraction succeeded.
---@return string|nil error Error message if extraction failed.
function iso.extract(iso_path, dest_dir, options) end

--- Represents an active optical disc image reader returned by iso.open().
---@class IsoReader
local IsoReader = {}

--- Returns the detected disc filesystem format ("XGD2", "XGD3", "XGD1", "XISO", "PS3_HYBRID", "PS2_ISO9660", "STANDARD_ISO9660").
---@overload fun(self: IsoReader): string
---@overload fun(): string
---@return string format
function IsoReader.format(...) end

--- Returns the disc volume label (e.g. "PS3VOLUME", "PLAYSTATION", "MICROSOFT*XBOX*MEDIA").
---@overload fun(self: IsoReader): string
---@overload fun(): string
---@return string volume_id
function IsoReader.volume_id(...) end

--- Returns the total byte length of the underlying .iso file.
---@overload fun(self: IsoReader): integer
---@overload fun(): integer
---@return integer total_size
function IsoReader.total_size(...) end

--- Returns the byte offset of the active game partition (e.g. 0x0FD90000 for XGD2, 0x02080000 for XGD3, 0 for ISO9660/XISO).
---@overload fun(self: IsoReader): integer
---@overload fun(): integer
---@return integer partition_offset
function IsoReader.partition_offset(...) end

--- Checks whether a file or directory exists inside the disc image (case-insensitive).
---@overload fun(self: IsoReader, internal_path: string): boolean
---@overload fun(internal_path: string): boolean
---@return boolean exists
function IsoReader.exists(...) end

--- Retrieves metadata for a single file or directory inside the disc image (case-insensitive).
---@overload fun(self: IsoReader, internal_path: string): IsoEntry|nil
---@overload fun(internal_path: string): IsoEntry|nil
---@return IsoEntry|nil entry
function IsoReader.get_entry(...) end

--- Lists entries within the disc image, optionally scoped to a subdirectory.
---@overload fun(self: IsoReader, subpath?: string, recursive?: boolean): table<integer, IsoEntry>
---@overload fun(subpath?: string, recursive?: boolean): table<integer, IsoEntry>
---@return table<integer, IsoEntry> entries
function IsoReader.list_files(...) end

--- Reads a file's raw binary content directly into memory without extracting to disk.
---@overload fun(self: IsoReader, internal_path: string, max_bytes?: integer): string|nil, string|nil
---@overload fun(internal_path: string, max_bytes?: integer): string|nil, string|nil
---@return string|nil content Raw binary byte string (Latin1 0..255), or nil on failure.
---@return string|nil error Error message on failure.
function IsoReader.read_file(...) end

--- Extracts a single internal file directly to a host destination path.
---@overload fun(self: IsoReader, internal_path: string, host_destination: string, overwrite?: boolean): boolean, string|nil
---@overload fun(internal_path: string, host_destination: string, overwrite?: boolean): boolean, string|nil
---@return boolean success
---@return string|nil error
function IsoReader.extract_file(...) end

--- Extracts multiple files matching options to a target directory.
---@overload fun(self: IsoReader, options: IsoExtractOptions|string, filter?: string, overwrite?: boolean): IsoExtractResult
---@overload fun(options: IsoExtractOptions|string, filter?: string, overwrite?: boolean): IsoExtractResult
---@return IsoExtractResult result
function IsoReader.extract(...) end

--- Closes the disc reader handle and releases file locks.
---@overload fun(self: IsoReader)
---@overload fun()
function IsoReader.close(...) end

--- Alias for close().
---@overload fun(self: IsoReader)
---@overload fun()
function IsoReader.dispose(...) end


