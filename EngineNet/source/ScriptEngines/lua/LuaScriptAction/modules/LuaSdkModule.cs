using MoonSharp.Interpreter;

using System.IO;
using System.Collections.Generic;
using EngineNet.Core.Utils;

namespace EngineNet.ScriptEngines.lua.LuaModules;

/// <summary>
/// SDK module providing file operations, archive handling, and system utilities for Lua scripts.
/// </summary>
internal static class LuaSdkModule {
    internal static Table CreateSdkModule(LuaWorld LuaEnvObj, Core.Tools.IToolResolver tools) {
        // Color/colour print functionality
        AddColorPrintFunctions(LuaEnvObj);

        // Configuration helpers
        AddConfigurationHelpers(LuaEnvObj);

        // File system operations
        AddFileSystemOperations(LuaEnvObj);

        // Archive operations
        AddArchiveOperations(LuaEnvObj);

        // TOML helpers
        AddTomlHelpers(LuaEnvObj);

        // todo: remove old shim stuff and use only sdk
        PreloadShimModules(LuaEnvObj);


        // Process execution helpers
        Utils.LuaProcessExecution.AddProcessExecution(LuaEnvObj, tools);

        // Expose CPU count as a numeric value for Lua scripts to choose sensible defaults
        LuaEnvObj.sdk.Set("cpu_count", DynValue.NewNumber(System.Environment.ProcessorCount));

        return LuaEnvObj.sdk;
    }

    private static void AddColorPrintFunctions(LuaWorld LuaEnvObj) {
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        CallbackFunction colorPrintFunc = new CallbackFunction((ctx, args) => {
            string? color = null;
            string message = string.Empty;
            bool newline = true;
            if (args.Count >= 2 && (args[0].Type == DataType.String || args[0].Type == DataType.UserData)) {
                // color, message, [newline]
                color = args[0].ToPrintString();
                message = args[1].Type == DataType.String ? args[1].String : args[1].ToPrintString();
                if (args.Count >= 3 && args[2].Type == DataType.Boolean) {
                    newline = args[2].Boolean;
                }
            } else if (args.Count >= 1 && args[0].Type == DataType.Table) {
                Table t = args[0].Table;
                DynValue c = t.Get("color");
                if (c.IsNil()) {
                    c = t.Get("colour");
                }

                if (!c.IsNil()) {
                    color = c.Type == DataType.String ? c.String : c.ToPrintString();
                }

                DynValue m = t.Get("message");
                if (!m.IsNil()) {
                    message = m.Type == DataType.String ? m.String : m.ToPrintString();
                }

                DynValue nl = t.Get("newline");
                if (!nl.IsNil() && nl.Type == DataType.Boolean) {
                    newline = nl.Boolean;
                }
            }
            Core.Utils.EngineSdk.Print(message, color, newline);
            return DynValue.Nil;
        });
        LuaEnvObj.sdk["color_print"] = DynValue.NewCallback(colorPrintFunc);
        LuaEnvObj.sdk["colour_print"] = DynValue.NewCallback(colorPrintFunc);
    }

    private static void AddConfigurationHelpers(LuaWorld LuaEnvObj) {
        LuaEnvObj.sdk["validate_source_dir"] = (System.Func<string, bool>)((dir) => {
            try {
                // Security: Validate path is within allowed areas
                if (!LuaSecurity.IsAllowedPath(dir)) {
                    Core.Utils.EngineSdk.Error($"Access denied: validate_source_dir path is outside allowed areas ('{dir}')");
                    return false;
                }
                Helpers.ConfigHelpers.ValidateSourceDir(dir);
                return true;
            } catch {
                return false;
            }
        });
    }

    private static void AddFileSystemOperations(LuaWorld LuaEnvObj) {
        LuaEnvObj.sdk["copy_dir"] = (System.Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                // Security: Validate paths
                if (!LuaSecurity.IsAllowedPath(src) || !LuaSecurity.IsAllowedPath(dst)) {
                    Core.Utils.EngineSdk.Error($"Access denied: copy_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                Helpers.ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["move_dir"] = (System.Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                // Security: Validate paths
                if (!LuaSecurity.IsAllowedPath(src) || !LuaSecurity.IsAllowedPath(dst)) {
                    Core.Utils.EngineSdk.Error($"Access denied: move_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                Helpers.ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["find_subdir"] = (System.Func<string, string, string?>)((baseDir, name) => {
            if (!LuaSecurity.IsAllowedPath(baseDir)) {
                Core.Utils.EngineSdk.Error($"Access denied: find_subdir baseDir is outside allowed areas ('{baseDir}')");
                return null;
            }
            return Helpers.ConfigHelpers.FindSubdir(baseDir, name);
        });

        LuaEnvObj.sdk["has_all_subdirs"] = (System.Func<string, Table, bool>)((baseDir, names) => {
            try {
                if (!LuaSecurity.IsAllowedPath(baseDir)) {
                    Core.Utils.EngineSdk.Error($"Access denied: has_all_subdirs baseDir is outside allowed areas ('{baseDir}')");
                    return false;
                }
                List<string> list = Utils.LuaUtilities.TableToStringList(names);
                return Helpers.ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["ensure_dir"] = (System.Func<string, bool>)(path => {
            try {
                if (!LuaSecurity.IsAllowedPath(path)) {
                    Core.Utils.EngineSdk.Error($"Access denied: ensure_dir path is outside allowed areas ('{path}')");
                    return false;
                }
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["path_exists"] = (System.Func<string, bool>)(path => LuaSecurity.EnsurePathAllowedWithPrompt(path) && Utils.LuaFileSystemUtils.PathExists(path));
        LuaEnvObj.sdk["lexists"] = (System.Func<string, bool>)(path => LuaSecurity.EnsurePathAllowedWithPrompt(path) && Utils.LuaFileSystemUtils.PathExistsIncludingLinks(path));
        LuaEnvObj.sdk["is_dir"] = (System.Func<string, bool>)(path => LuaSecurity.EnsurePathAllowedWithPrompt(path) && System.IO.Directory.Exists(path));
        LuaEnvObj.sdk["is_file"] = (System.Func<string, bool>)(path => LuaSecurity.EnsurePathAllowedWithPrompt(path) && System.IO.File.Exists(path));

        LuaEnvObj.sdk["is_writable"] = (System.Func<string, bool>)(path => {
            try {
                if (!LuaSecurity.IsAllowedPath(path)) {
                    return false;
                }
                if (!Directory.Exists(path))
                    return false;

                string testFile = Path.Combine(path, Path.GetRandomFileName());

                // Create a zero-byte file and delete it immediately when closed
                using (File.Create(testFile, 1, FileOptions.DeleteOnClose)) {
                }

                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["remove_dir"] = (System.Func<string, bool>)(path => {
            try {
                // Security: Validate path prior to deletion
                if (!LuaSecurity.IsAllowedPath(path)) {
                    Core.Utils.EngineSdk.Error($"Access denied: remove_dir path is outside allowed areas ('{path}')");
                    return false;
                }
                if (System.IO.Directory.Exists(path)) {
                    System.IO.Directory.Delete(path, true);
                }
                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["remove_file"] = (System.Func<string, bool>)(path => {
            try {
                // Security: Validate path prior to deletion
                if (!LuaSecurity.IsAllowedPath(path)) {
                    Core.Utils.EngineSdk.Error($"Access denied: remove_file path is outside allowed areas ('{path}')");
                    return false;
                }
                if (Utils.LuaFileSystemUtils.IsSymlink(path) || System.IO.File.Exists(path)) {
                    System.IO.File.Delete(path);
                }
                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["copy_file"] = (System.Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(src) || !LuaSecurity.EnsurePathAllowedWithPrompt(dst)) {
                    return false;
                }

                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                System.IO.File.Copy(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["rename_file"] = (System.Func<string, string, bool>)((oldPath, newPath) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(oldPath) || !LuaSecurity.EnsurePathAllowedWithPrompt(newPath)) {
                    return false;
                }

                if (System.IO.File.Exists(oldPath)) {
                    System.IO.File.Move(oldPath, newPath);
                    return true;
                } else if (System.IO.Directory.Exists(oldPath)) {
                    System.IO.Directory.Move(oldPath, newPath);
                    return true;
                }
                return false;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["create_symlink"] = (System.Func<string, string, bool, bool>)((src, dst, isDir) => {
            if (!LuaSecurity.IsAllowedPath(src) || !LuaSecurity.IsAllowedPath(dst)) {
                Core.Utils.EngineSdk.Error($"Access denied: create_symlink src or dst outside allowed areas (src='{src}', dst='{dst}')");
                return false;
            }
            return Utils.LuaFileSystemUtils.CreateSymlink(src, dst, isDir);
        });
        LuaEnvObj.sdk["is_symlink"] = (System.Func<string, bool>)(path => LuaSecurity.IsAllowedPath(path) && Utils.LuaFileSystemUtils.IsSymlink(path));
        LuaEnvObj.sdk["realpath"] = (System.Func<string, string?>)(path => LuaSecurity.IsAllowedPath(path) ? Utils.LuaFileSystemUtils.RealPath(path) : null);
        LuaEnvObj.sdk["readlink"] = (System.Func<string, string?>)(path => LuaSecurity.IsAllowedPath(path) ? Utils.LuaFileSystemUtils.ReadLink(path) : null);

        // Create hardlink (files only)
        LuaEnvObj.sdk["create_hardlink"] = (System.Func<string, string, bool>)((src, dst) => {
            try {
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(src) || !LuaSecurity.EnsurePathAllowedWithPrompt(dst)) {
                    return false;
                }
                string destFull = System.IO.Path.GetFullPath(dst);
                string srcFull = System.IO.Path.GetFullPath(src);
                string? parent = System.IO.Path.GetDirectoryName(destFull);
                if (!string.IsNullOrEmpty(parent)) {
                    System.IO.Directory.CreateDirectory(parent);
                }
                if (!System.IO.File.Exists(srcFull)) {
                    return false;
                }
                // Remove existing file or link
                try {
                    if (Utils.LuaFileSystemUtils.IsSymlink(destFull) || System.IO.File.Exists(destFull)) {
                        System.IO.File.Delete(destFull);
                    }
                } catch {
                    Core.Diagnostics.Bug("Failed to delete existing file or link: " + destFull);
                    /* ignore */
                }
                try {
                    Helpers.HardLink.Create(srcFull, destFull);
                    return true;
                } catch {
                    return false;
                }
            } catch {
                return false;
            }
        });

        // SHA1 hash of a file (lowercase hex)
        LuaEnvObj.sdk["sha1_file"] = (System.Func<string, string?>)(path => {
            try {
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                    return null;
                }
                using System.IO.FileStream fs = System.IO.File.OpenRead(path);
                byte[] hash = System.Security.Cryptography.SHA1.HashData(fs);
                return System.Convert.ToHexString(hash).ToLowerInvariant();
            } catch {
                return null;
            }
        });

        // Additional file system operations for compatibility with existing scripts
        // currentdir would be called from a lua script file, so it should reflect the current working directory of the file not the engine's working directory
        // TODO: update
        LuaEnvObj.sdk["currentdir"] = (System.Func<string>)(() => System.IO.Directory.GetCurrentDirectory());

        LuaEnvObj.sdk["mkdir"] = (System.Func<string, bool>)(path => {
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return false;
            }
            try {
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch {
                return false;
            }
        });

        LuaEnvObj.sdk["attributes"] = (System.Func<string, DynValue>)(path => {
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return DynValue.Nil;
            }
            try {
                if (System.IO.Directory.Exists(path)) {
                    Table attrs = new Table(LuaEnvObj.LuaScript);
                    attrs["mode"] = "directory";
                    System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(path);
                    attrs["modification"] = (double)new System.DateTimeOffset(dirInfo.LastWriteTime).ToUnixTimeSeconds();
                    return DynValue.NewTable(attrs);
                }
                if (System.IO.File.Exists(path)) {
                    Table attrs = new Table(LuaEnvObj.LuaScript);
                    attrs["mode"] = "file";
                    System.IO.FileInfo fileInfo = new System.IO.FileInfo(path);
                    attrs["size"] = fileInfo.Length;
                    attrs["modification"] = (double) new System.DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds();
                    return DynValue.NewTable(attrs);
                }
                return DynValue.Nil;
            } catch {
                return DynValue.Nil;
            }
        });

        /// <summary>
        /// Lists files and directories in a given path.
        /// Returns a table of strings (names only, not full paths).
        /// Does not include "." or "..".
        /// </summary>
        LuaEnvObj.sdk["list_dir"] = (System.Func<string, Table>)((path) => {
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return new Table(LuaEnvObj.LuaScript);
            }
            try {
                if (!System.IO.Directory.Exists(path)) {
                    return new Table(LuaEnvObj.LuaScript);
                }
                Table list = new Table(LuaEnvObj.LuaScript);
                string[] entries = System.IO.Directory.GetFileSystemEntries(path);
                foreach (string entry in entries) {
                    list.Append(DynValue.NewString(System.IO.Path.GetFileName(entry)));
                }
                return list;
            } catch {
                return new Table(LuaEnvObj.LuaScript);
            }
        });

        LuaEnvObj.sdk["md5"] = (System.Func<string, string>)(text => {
            try {
                byte[] data = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
                return System.Convert.ToHexString(data).ToLowerInvariant();
            } catch {
                return string.Empty;
            }
        });

        LuaEnvObj.sdk["sleep"] = (System.Action<double>)(seconds => {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) {
                return;
            }

            try {
                System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(seconds));
            }  catch {
            Core.Diagnostics.Bug($"Error .....'");
        }
        });
    }

    private static void AddArchiveOperations(LuaWorld LuaEnvObj) {
        // Archive operations (using system's built-in capabilities)
        LuaEnvObj.sdk["extract_archive"] = (System.Func<string, string, bool>)((archivePath, destDir) => {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!LuaSecurity.IsAllowedPath(archivePath) || !LuaSecurity.IsAllowedPath(destDir)) {
                    Core.Utils.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {archivePath} -> {destDir}");
                    return false;
                }

                string ext = System.IO.Path.GetExtension(archivePath).ToLowerInvariant();
                if (ext == ".zip") {
                    System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destDir);
                    return true;
                }
                // For other formats, suggest using approved tools
                Core.Utils.EngineSdk.Error($"Unsupported archive format '{ext}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Core.Utils.EngineSdk.Error($"Archive extraction failed: {ex.Message}");
                return false;
            }
        });

        LuaEnvObj.sdk["create_archive"] = (System.Func<string, string, string, bool>)((srcPath, archivePath, type) => {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!LuaSecurity.IsAllowedPath(srcPath) || !LuaSecurity.IsAllowedPath(archivePath)) {
                    Core.Utils.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {srcPath} -> {archivePath}");
                    return false;
                }

                if (type.Equals("zip", System.StringComparison.OrdinalIgnoreCase)) {
                    if (System.IO.Directory.Exists(srcPath)) {
                        System.IO.Compression.ZipFile.CreateFromDirectory(srcPath, archivePath);
                    } else if (System.IO.File.Exists(srcPath)) {
                        // Create zip with single file
                        using var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create);
                        var entry = archive.CreateEntry(System.IO.Path.GetFileName(srcPath));
                        using var entryStream = entry.Open();
                        using var fileStream = System.IO.File.OpenRead(srcPath);
                        fileStream.CopyTo(entryStream);
                    } else {
                        return false;
                    }
                    return true;
                }
                // For other formats, suggest using approved tools
                Core.Utils.EngineSdk.Error($"Unsupported archive type '{type}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Core.Utils.EngineSdk.Error($"Archive creation failed: {ex.Message}");
                return false;
            }
        });
    }

    private static void AddTomlHelpers(LuaWorld LuaEnvObj) {
        // TOML helpers
        LuaEnvObj.sdk["toml_read_file"] = (System.Func<string, DynValue>)(path => {
            try {
                // Security: Validate path is within allowed areas
                if (!LuaSecurity.IsAllowedPath(path)) {
                    Core.Utils.EngineSdk.Error($"Access denied: toml_read_file path is outside allowed areas ('{path}')");
                    return DynValue.Nil;
                }
                object obj = TomlHelpers.ParseFileToPlainObject(path);
                return Utils.LuaUtilities.ToDynValue(GetScriptFromTable(LuaEnvObj.sdk), obj);
            } catch (System.Exception ex) {
                Core.Utils.EngineSdk.Error($"TOML read failed: {ex.Message}");
                return DynValue.Nil;
            }
        });

        LuaEnvObj.sdk["toml_write_file"] = (System.Action<string, DynValue>)((path, value) => {
            try {
                // Security: Validate path is within allowed areas
                if (!LuaSecurity.IsAllowedPath(path)) {
                    Core.Utils.EngineSdk.Error($"Access denied: toml_write_file path is outside allowed areas ('{path}')");
                    return;
                }
                object? obj = Utils.LuaUtilities.FromDynValue(value);
                TomlHelpers.WriteTomlFile(path, obj);
            } catch (System.Exception ex) {
                Core.Utils.EngineSdk.Error($"TOML write failed: {ex.Message}");
            }
        });
    }

    private static Script GetScriptFromTable(Table table) {
        // This is a workaround - in practice we'll pass the script reference
        return table.OwnerScript;
    }


    internal static void PreloadShimModules(LuaWorld LuaEnvObj) {
        // Ensure package.loaded exists
        Table package = LuaEnvObj.LuaScript.Globals.Get("package").IsNil() ? new Table(LuaEnvObj.LuaScript) : LuaEnvObj.LuaScript.Globals.Get("package").Table;
        if (package.Get("loaded").IsNil()) {
            package["loaded"] = new Table(LuaEnvObj.LuaScript);
        }

        Table loaded = package.Get("loaded").Table;

        // Minimal 'require' shim: return preloaded modules from package.loaded
        if (LuaEnvObj.LuaScript.Globals.Get("require").IsNil()) {
            LuaEnvObj.LuaScript.Globals["require"] = (System.Func<string, DynValue>)(name => {
                DynValue mod = loaded.Get(name);
                return !mod.IsNil() ? mod : throw new ScriptRuntimeException($"module '{name}' not found (only preloaded modules available)");
            });
        }

        /*
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
        */

        // dkjson shim: provides encode(value, opts?) and decode(string)
        Table dkjson = new Table(LuaEnvObj.LuaScript);
        dkjson["encode"] = (System.Func<DynValue, DynValue, string>)((val, opts) => {
            bool indent = false;
            if (opts.Type == DataType.Table) {
                DynValue indentVal = opts.Table.Get("indent");
                indent = indentVal.Type == DataType.Boolean && indentVal.Boolean;
            }
            object? obj = Utils.LuaUtilities.FromDynValue(val);
            System.Text.Json.JsonSerializerOptions jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = indent };
            return System.Text.Json.JsonSerializer.Serialize(obj, jsonOpts);
        });

        Table textTable = new Table(LuaEnvObj.LuaScript);
        Table jsonTable = new Table(LuaEnvObj.LuaScript);

        LuaEnvObj.sdk["text"] = textTable;
        textTable["json"] = jsonTable;
        jsonTable["encode"] = dkjson["encode"];

        dkjson["decode"] = (System.Func<string, DynValue>)((json) => {
            try {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                return Utils.LuaUtilities.JsonElementToDynValue(LuaEnvObj.LuaScript, doc.RootElement);
            } catch {
                return DynValue.Nil; // caller will treat as error
            }
        });

        // now that dkjson.decode exists, expose it under sdk.text.json
        jsonTable["decode"] = dkjson["decode"];

        loaded["dkjson"] = DynValue.NewTable(dkjson);

        // debug shim: provide getinfo with .source used by modules to find their file path
        /*Table debugTbl = lua.Globals.Get("debug").IsNil() ? new Table(lua) : lua.Globals.Get("debug").Table;
        debugTbl["getinfo"] = (System.Func<DynValue, DynValue, DynValue>)((level, what) => {
            Table t = new Table(lua);
            // Lua expects '@' prefix for file paths
            t["source"] = "@" + scriptPath;
            return DynValue.NewTable(t);
        });
        lua.Globals["debug"] = debugTbl;*/

        // publish back package (in case it didn't exist)
        LuaEnvObj.LuaScript.Globals["package"] = package;
    }

}
