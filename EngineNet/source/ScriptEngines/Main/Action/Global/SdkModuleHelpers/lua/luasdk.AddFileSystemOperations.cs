using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {

    private static void AddFileSystemOperations(LuaWorld _LuaWorld) {
        _LuaWorld.sdk["copy_dir"] = (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate paths
                if (!Security.IsAllowedPath(src) || !Security.IsAllowedPath(dst)) {
                    Core.UI.EngineSdk.Error($"Access denied: copy_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                Helpers.ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("copy_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.sdk["move_dir"] = (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate paths
                if (!Security.IsAllowedPath(src) || !Security.IsAllowedPath(dst)) {
                    Core.UI.EngineSdk.Error($"Access denied: move_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                Helpers.ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("move_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.sdk["find_subdir"] = (string baseDir, string name) => {
            if (!Security.IsAllowedPath(baseDir)) {
                Core.UI.EngineSdk.Error($"Access denied: find_subdir baseDir is outside allowed areas ('{baseDir}')");
                return null;
            }
            return Helpers.ConfigHelpers.FindSubdir(baseDir, name);
        };

        _LuaWorld.sdk["has_all_subdirs"] = (string baseDir, Table names) => {
            try {
                if (!Security.IsAllowedPath(baseDir)) {
                    Core.UI.EngineSdk.Error($"Access denied: has_all_subdirs baseDir is outside allowed areas ('{baseDir}')");
                    return false;
                }
                List<string> list = Lua.Globals.Utils.TableToStringList(names);
                return Helpers.ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("has_all_subdirs failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.sdk["ensure_dir"] = (string path) => {
            try {
                if (!Security.IsAllowedPath(path)) {
                    Core.UI.EngineSdk.Error($"Access denied: ensure_dir path is outside allowed areas ('{path}')");
                    return false;
                }
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("ensure_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.sdk["path_exists"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && ScriptEngines.Global.SdkModule.FileSystemUtils.PathExists(path);
        };
        _LuaWorld.sdk["lexists"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && ScriptEngines.Global.SdkModule.FileSystemUtils.PathExistsIncludingLinks(path);
        };
        _LuaWorld.sdk["is_dir"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && System.IO.Directory.Exists(path);
        };
        _LuaWorld.sdk["is_file"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && System.IO.File.Exists(path);
        };

        _LuaWorld.sdk["is_writable"] = (string path) => {
            try {
                if (!Security.IsAllowedPath(path)) {
                    return false;
                }
                if (!Directory.Exists(path))
                    return false;

                string testFile = Path.Combine(path, Path.GetRandomFileName());

                // Create a zero-byte file and delete it immediately when closed
                using (File.Create(testFile, 1, FileOptions.DeleteOnClose)) {
                }

                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("is_writable failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.sdk["remove_dir"] = (string path) => {
            try {
                // Security: Validate path prior to deletion
                if (!Security.IsAllowedPath(path)) {
                    Core.UI.EngineSdk.Error($"Access denied: remove_dir path is outside allowed areas ('{path}')");
                    return false;
                }
                if (System.IO.Directory.Exists(path)) {
                    System.IO.Directory.Delete(path, true);
                }
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("remove_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.sdk["remove_file"] = (System.Func<string, bool>)(path => {
            try {
                // Security: Validate path prior to deletion
                if (!Security.IsAllowedPath(path)) {
                    Core.UI.EngineSdk.Error($"Access denied: remove_file path is outside allowed areas ('{path}')");
                    return false;
                }
                if (ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path) || System.IO.File.Exists(path)) {
                    System.IO.File.Delete(path);
                }
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("remove_file failed with exception: " + ex);
                return false;
            }
        });

        _LuaWorld.sdk["copy_file"] = (System.Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!Security.EnsurePathAllowedWithPrompt(src) || !Security.EnsurePathAllowedWithPrompt(dst)) {
                    return false;
                }

                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                System.IO.File.Copy(src, dst, ow);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("copy_file failed with exception: " + ex);
                return false;
            }
        });

        _LuaWorld.sdk["write_file"] = (System.Func<string, string, bool>)((path, content) => {
            try {
                if (!Security.EnsurePathAllowedWithPrompt(path)) {
                    return false;
                }
                string? parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent)) {
                    System.IO.Directory.CreateDirectory(parent);
                }
                System.IO.File.WriteAllText(path, content ?? string.Empty);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("write_file failed with exception: " + ex);
                return false;
            }
        });

        _LuaWorld.sdk["read_file"] = (System.Func<string, string?>)(path => {
            try {
                if (!Security.EnsurePathAllowedWithPrompt(path)) {
                    return null;
                }
                if (!System.IO.File.Exists(path)) {
                    return null;
                }
                return System.IO.File.ReadAllText(path);
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("read_file failed with exception: " + ex);
                return null;
            }
        });

        _LuaWorld.sdk["rename_file"] = (System.Func<string, string, bool>)((oldPath, newPath) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!Security.EnsurePathAllowedWithPrompt(oldPath) || !Security.EnsurePathAllowedWithPrompt(newPath)) {
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
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("rename_file failed with exception: " + ex);
                return false;
            }
        });

        _LuaWorld.sdk["create_symlink"] = (System.Func<string, string, bool, bool>)((source, destination, isDirectory) => {
            if (!Security.IsAllowedPath(source) || !Security.IsAllowedPath(destination)) {
                Core.UI.EngineSdk.Error($"Access denied: create_symlink src or dst outside allowed areas (src='{source}', dst='{destination}')");
                return false;
            }
            return ScriptEngines.Global.SdkModule.SymLink.Create(source, destination, isDirectory);
        });
        _LuaWorld.sdk["is_symlink"] = (System.Func<string, bool>)(path => Security.IsAllowedPath(path) && ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path));
        _LuaWorld.sdk["realpath"] = (System.Func<string, string?>)(path => Security.IsAllowedPath(path) ? ScriptEngines.Global.SdkModule.FileSystemUtils.RealPath(path) : null);
        _LuaWorld.sdk["readlink"] = (System.Func<string, string?>)(path => Security.IsAllowedPath(path) ? ScriptEngines.Global.SdkModule.FileSystemUtils.ReadLink(path) : null);

        // Create hardlink (files only)
        _LuaWorld.sdk["create_hardlink"] = (System.Func<string, string, bool>)((src, dst) => {
            try {
                if (!Security.EnsurePathAllowedWithPrompt(src) || !Security.EnsurePathAllowedWithPrompt(dst)) {
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
                    if (ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(destFull) || System.IO.File.Exists(destFull)) {
                        System.IO.File.Delete(destFull);
                    }
                } catch (Exception ex) {
                    Core.Diagnostics.luaInternalCatch("Failed to delete existing file or link: " + destFull + " with exception: " + ex);
                    /* ignore */
                }
                try {
                    ScriptEngines.Global.SdkModule.HardLink.Create(srcFull, destFull);
                    return true;
                } catch (Exception ex) {
                    Core.Diagnostics.luaInternalCatch("Failed to create hardlink from " + srcFull + " to " + destFull + " with exception: " + ex);
                    return false;
                }
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("create_hardlink failed with exception: " + ex);
                return false;
            }
        });

        // old, get current directory of process, not caller script file
        _LuaWorld.sdk["currentdir"] = () => {
            return System.IO.Directory.GetCurrentDirectory();
        };
        // new, get current directory of caller script file
        _LuaWorld.sdk["current_dir"] = () => {
            // get current directory of caller script file
            return System.IO.Path.GetDirectoryName(_LuaWorld.LuaScriptPath);
        };

        _LuaWorld.sdk["mkdir"] = (System.Func<string, bool>)(path => {
            if (!Security.EnsurePathAllowedWithPrompt(path)) {
                return false;
            }
            try {
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("mkdir failed with exception: " + ex);
                return false;
            }
        });

        _LuaWorld.sdk["attributes"] = (string path) => {
            // Call the shared logic
            var resultDict = ScriptEngines.Global.SdkModule.Helpers.AddFileSystemOperations.FileAttributes(path);

            if (resultDict == null) {
                return DynValue.Nil;
            }

            // Convert C# Dictionary to MoonSharp Table
            Table table = new Table(_LuaWorld.LuaScript);
            foreach (var kvp in resultDict) {
                // We use DynValue.FromObject to handle the conversion of strings/doubles/longs automatically
                table[kvp.Key] = DynValue.FromObject(_LuaWorld.LuaScript, kvp.Value);
            }

            return DynValue.NewTable(table);
        };

        // Lists files and directories in a given path. Returns a table of strings (names only, not full paths).
        _LuaWorld.sdk["list_dir"] = (string path) => {
            Table table = new Table(_LuaWorld.LuaScript);
            List<string>? resultList = ScriptEngines.Global.SdkModule.Helpers.AddFileSystemOperations.List_Dir(path);
            if (resultList == null) {
                return table;
            }
            // Convert the List<string> into the Lua Table
            foreach (string name in resultList) {
                table.Append(DynValue.NewString(name));
            }
            return table;
        };


        // SHA1 hash of a file (lowercase hex)
        _LuaWorld.sdk["sha1_file"] = (System.Func<string, string?>)(path => {
            return ScriptEngines.Global.SdkModule.Helpers.AddFileSystemOperations.sha1_file(path);
        });

        _LuaWorld.sdk["md5"] = (string text) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddFileSystemOperations.Md5Hash(text);
        };

        _LuaWorld.sdk["sleep"] = (double seconds) => {
            ScriptEngines.Global.SdkModule.Helpers.AddFileSystemOperations.Sleep(seconds);
        };
    }

}