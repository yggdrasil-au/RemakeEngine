using MoonSharp.Interpreter;


using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// Shim modules for Lua compatibility (lfs, dkjson, debug).
/// Provides minimal implementations for legacy Lua scripts.
/// </summary>
internal static class LuaShimModules {
    internal static void PreloadShimModules(Script lua, string scriptPath) {
        // Ensure package.loaded exists
        Table package = lua.Globals.Get("package").IsNil() ? new Table(lua) : lua.Globals.Get("package").Table;
        if (package.Get("loaded").IsNil()) {
            package["loaded"] = new Table(lua);
        }

        Table loaded = package.Get("loaded").Table;

        // Minimal 'require' shim: return preloaded modules from package.loaded
        if (lua.Globals.Get("require").IsNil()) {
            lua.Globals["require"] = (System.Func<string, DynValue>)(name => {
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
        lfs["currentdir"] = () => System.Environment.CurrentDirectory;

        // lfs.mkdir(path) -> true on success, nil on failure (minimal behavior)
        lfs["mkdir"] = (System.Func<string, DynValue>)((path) => {
            try {
                System.IO.Directory.CreateDirectory(path);
                return DynValue.True;
            } catch (System.Exception) {
                // Return nil to indicate failure; message not used by current scripts
                return DynValue.Nil;
            }
        });

        lfs["attributes"] = (System.Func<string, DynValue>)((path) => {
            if (System.IO.Directory.Exists(path)) {
                Table t = new Table(lua);
                t["mode"] = "directory";
                return DynValue.NewTable(t);
            }
            if (System.IO.File.Exists(path)) {
                System.IO.FileInfo info = new System.IO.FileInfo(path);
                Table t = new Table(lua);
                t["mode"] = "file";
                t["size"] = info.Length;
                t["modtime"] = info.LastWriteTimeUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                return DynValue.NewTable(t);
            }
            return DynValue.Nil;
        });

        lfs["dir"] = (System.Func<string, DynValue>)((path) => {
            // Return an iterator function like lfs.dir
            IEnumerable<string> Enumerate() {
                // In real lfs, '.' and '..' are included; we'll include them for compatibility
                yield return ".";
                yield return "..";
                if (System.IO.Directory.Exists(path)) {
                    foreach (string entry in System.IO.Directory.EnumerateFileSystemEntries(path)) {
                        yield return System.IO.Path.GetFileName(entry);
                    }
                }
            }
            IEnumerator<string> enumerator = Enumerate().GetEnumerator();
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
        dkjson["encode"] = (System.Func<DynValue, DynValue, string>)((val, opts) => {
            bool indent = false;
            if (opts.Type == DataType.Table) {
                DynValue indentVal = opts.Table.Get("indent");
                indent = indentVal.Type == DataType.Boolean && indentVal.Boolean;
            }
            object? obj = LuaUtilities.FromDynValue(val);
            System.Text.Json.JsonSerializerOptions jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = indent };
            return System.Text.Json.JsonSerializer.Serialize(obj, jsonOpts);
        });

        dkjson["decode"] = (System.Func<string, DynValue>)((json) => {
            try {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                return LuaUtilities.JsonElementToDynValue(lua, doc.RootElement);
            } catch {
                return DynValue.Nil; // caller will treat as error
            }
        });

        loaded["dkjson"] = DynValue.NewTable(dkjson);
    }

    private static void CreateDebugShim(Script lua, string scriptPath) {
        // debug shim: provide getinfo with .source used by modules to find their file path
        Table debugTbl = lua.Globals.Get("debug").IsNil() ? new Table(lua) : lua.Globals.Get("debug").Table;
        debugTbl["getinfo"] = (System.Func<DynValue, DynValue, DynValue>)((level, what) => {
            Table t = new Table(lua);
            // Lua expects '@' prefix for file paths
            t["source"] = "@" + scriptPath;
            return DynValue.NewTable(t);
        });
        lua.Globals["debug"] = debugTbl;
    }
}