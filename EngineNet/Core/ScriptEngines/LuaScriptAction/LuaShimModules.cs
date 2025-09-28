using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using MoonSharp.Interpreter;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// Shim modules for Lua compatibility (lfs, dkjson, debug).
/// Provides minimal implementations for legacy Lua scripts.
/// </summary>
internal static class LuaShimModules {
    public static void PreloadShimModules(Script lua, String scriptPath) {
        // Ensure package.loaded exists
        Table package = lua.Globals.Get("package").IsNil() ? new Table(lua) : lua.Globals.Get("package").Table;
        if (package.Get("loaded").IsNil()) {
            package["loaded"] = new Table(lua);
        }

        Table loaded = package.Get("loaded").Table;

        // Minimal 'require' shim: return preloaded modules from package.loaded
        if (lua.Globals.Get("require").IsNil()) {
            lua.Globals["require"] = (Func<String, DynValue>)(name => {
                DynValue mod = loaded.Get(name);
                return !mod.IsNil() ? mod : throw new ScriptRuntimeException($"module '{name}' not found (only preloaded modules available)");
            });
        }

        // lfs shim
        CreateLfsShim(lua, loaded);
        
        // dkjson shim
        CreateDkJsonShim(lua, loaded);

        // debug shim
        CreateDebugShim(lua, scriptPath);

        // publish back package (in case it didn't exist)
        lua.Globals["package"] = package;
    }

    private static void CreateLfsShim(Script lua, Table loaded) {
        Table lfs = new Table(lua);
        lfs["currentdir"] = () => Environment.CurrentDirectory;
        
        // lfs.mkdir(path) -> true on success, nil on failure (minimal behavior)
        lfs["mkdir"] = (Func<String, DynValue>)((path) => {
            try {
                Directory.CreateDirectory(path);
                return DynValue.True;
            } catch (Exception) {
                // Return nil to indicate failure; message not used by current scripts
                return DynValue.Nil;
            }
        });
        
        lfs["attributes"] = (Func<String, DynValue>)((path) => {
            if (Directory.Exists(path)) {
                Table t = new Table(lua);
                t["mode"] = "directory";
                return DynValue.NewTable(t);
            }
            if (File.Exists(path)) {
                FileInfo info = new FileInfo(path);
                Table t = new Table(lua);
                t["mode"] = "file";
                t["size"] = info.Length;
                t["modtime"] = info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
                return DynValue.NewTable(t);
            }
            return DynValue.Nil;
        });
        
        lfs["dir"] = (Func<String, DynValue>)((path) => {
            // Return an iterator function like lfs.dir
            IEnumerable<String> Enumerate() {
                // In real lfs, '.' and '..' are included; we'll include them for compatibility
                yield return ".";
                yield return "..";
                if (Directory.Exists(path)) {
                    foreach (String entry in Directory.EnumerateFileSystemEntries(path)) {
                        yield return Path.GetFileName(entry);
                    }
                }
            }
            IEnumerator<String> enumerator = Enumerate().GetEnumerator();
            CallbackFunction iterator = new CallbackFunction((ctx, args) => {
                return enumerator.MoveNext() ? DynValue.NewString(enumerator.Current) : DynValue.Nil;
            });
            return DynValue.NewCallback(iterator);
        });
        
        loaded["lfs"] = DynValue.NewTable(lfs);
    }

    private static void CreateDkJsonShim(Script lua, Table loaded) {
        // dkjson shim: provides encode(value, opts?) and decode(string)
        Table dkjson = new Table(lua);
        dkjson["encode"] = (Func<DynValue, DynValue, String>)((val, opts) => {
            Boolean indent = false;
            if (opts.Type == DataType.Table) {
                DynValue indentVal = opts.Table.Get("indent");
                indent = indentVal.Type == DataType.Boolean && indentVal.Boolean;
            }
            Object? obj = LuaUtilities.FromDynValue(val);
            JsonSerializerOptions jsonOpts = new JsonSerializerOptions { WriteIndented = indent };
            return JsonSerializer.Serialize(obj, jsonOpts);
        });
        
        dkjson["decode"] = (Func<String, DynValue>)((json) => {
            try {
                using JsonDocument doc = JsonDocument.Parse(json);
                return LuaUtilities.JsonElementToDynValue(lua, doc.RootElement);
            } catch {
                return DynValue.Nil; // caller will treat as error
            }
        });
        
        loaded["dkjson"] = DynValue.NewTable(dkjson);
    }

    private static void CreateDebugShim(Script lua, String scriptPath) {
        // debug shim: provide getinfo with .source used by modules to find their file path
        Table debugTbl = lua.Globals.Get("debug").IsNil() ? new Table(lua) : lua.Globals.Get("debug").Table;
        debugTbl["getinfo"] = (Func<DynValue, DynValue, DynValue>)((level, what) => {
            Table t = new Table(lua);
            // Lua expects '@' prefix for file paths
            t["source"] = "@" + scriptPath;
            return DynValue.NewTable(t);
        });
        lua.Globals["debug"] = debugTbl;
    }
}