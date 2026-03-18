using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {

    private static void AddFileSystemOperations(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.Table["find_subdir"] = (string baseDir, string name) => {
            if (!Security.IsAllowedPath(baseDir)) {
                Core.UI.EngineSdk.Error($"Access denied: find_subdir baseDir is outside allowed areas ('{baseDir}')");
                return null;
            }
            return ScriptEngines.Global.SdkModule.FileSystemUtils.FindSubdir(baseDir, name);
        };
        _LuaWorld.Sdk.Table["has_all_subdirs"] = (string baseDir, Table names) => {
            try {
                if (!Security.IsAllowedPath(baseDir)) {
                    Core.UI.EngineSdk.Error($"Access denied: has_all_subdirs baseDir is outside allowed areas ('{baseDir}')");
                    return false;
                }
                List<string> list = Lua.Globals.Utils.TableToStringList(names);
                return ScriptEngines.Global.SdkModule.FileSystemUtils.HasAllSubdirs(baseDir, list);
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("has_all_subdirs failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["path_exists"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && ScriptEngines.Global.SdkModule.FileSystemUtils.PathExists(path);
        };
        _LuaWorld.Sdk.Table["lexists"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && ScriptEngines.Global.SdkModule.FileSystemUtils.PathExistsIncludingLinks(path);
        };
        _LuaWorld.Sdk.Table["absolute_path"] = (string path) => {
            if (string.IsNullOrEmpty(path)) return path;

            // 1. Try realpath first (handles symlinks etc)
            string? resolved = null;
            if (Security.IsAllowedPath(path)) {
                resolved = ScriptEngines.Global.SdkModule.FileSystemUtils.RealPath(path);
            }

            string result = path;
            if (!string.IsNullOrEmpty(resolved)) {
                result = resolved;
            } else {
                // 2. Check if absolute using is_absolute logic
                bool isAbsolute = false;
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

                if (isWindows) {
                    // Windows drive: C:\
                    if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\')) isAbsolute = true;
                    // Windows UNC: \\host
                    else if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\') isAbsolute = true;
                    // Windows root: \
                    else if (path.Length >= 1 && path[0] == '\\') isAbsolute = true;
                    // Unix-style root on Windows: /
                    else if (path.Length >= 1 && path[0] == '/') isAbsolute = true;
                } else {
                    // Unix root: /
                    if (path.Length >= 1 && path[0] == '/') isAbsolute = true;
                }

                if (!isAbsolute) {
                    string cwd = System.IO.Directory.GetCurrentDirectory();
                    result = System.IO.Path.Combine(cwd, path);
                }
            }

            // 3. Normalize
            char sep = System.IO.Path.DirectorySeparatorChar;
            if (sep == '\\') {
                result = result.Replace('/', '\\');
            } else {
                result = result.Replace('\\', '/');
            }

            // 4. Windows Long Path Support (\\\\?\\ prefix)
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
                if (result.Length > 255 && result.Length >= 2 && char.IsLetter(result[0]) && result[1] == ':' && !result.StartsWith(@"\\?\")) {
                    result = @"\\?\" + result;
                }
            }

            return result;
        };
        _LuaWorld.Sdk.Table["realpath"] = (string path) => Security.IsAllowedPath(path) ? ScriptEngines.Global.SdkModule.FileSystemUtils.RealPath(path) : null;
        _LuaWorld.Sdk.Table["attributes"] = (string path) => {
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

        _LuaWorld.Sdk.Table["mkdir"] = (string path) => {
            if (!Security.EnsurePathAllowedWithPrompt(path)) {
                return false;
            }
            try {
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("mkdir failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["ensure_dir"] = (string path) => {
            try {
                if (!Security.IsAllowedPath(path)) {
                    Core.UI.EngineSdk.Error($"Access denied: ensure_dir path is outside allowed areas ('{path}')");
                    return false;
                }
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("ensure_dir failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["is_dir"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && System.IO.Directory.Exists(path);
        };
        _LuaWorld.Sdk.Table["copy_dir"] = (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate paths
                if (!Security.IsAllowedPath(src) || !Security.IsAllowedPath(dst)) {
                    Core.UI.EngineSdk.Error($"Access denied: copy_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ScriptEngines.Global.SdkModule.FileSystemUtils.CopyDirectory(src, dst, ow);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("copy_dir failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["move_dir"] = (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate paths
                if (!Security.IsAllowedPath(src) || !Security.IsAllowedPath(dst)) {
                    Core.UI.EngineSdk.Error($"Access denied: move_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ScriptEngines.Global.SdkModule.FileSystemUtils.MoveDirectory(src, dst, ow);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("move_dir failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["remove_dir"] = (string path) => {
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
                Core.Diagnostics.LuaInternalCatch("remove_dir failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["currentdir"] = () => {
            // old, get current directory of process, not caller script file
            return System.IO.Directory.GetCurrentDirectory();
        };
        _LuaWorld.Sdk.Table["current_dir"] = () => {
            // new, get current directory of caller script file
            // get current directory of caller script file
            return System.IO.Path.GetDirectoryName(_LuaWorld.LuaScriptPath);
        };
        _LuaWorld.Sdk.Table["list_dir"] = (string path) => {
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

        _LuaWorld.Sdk.Table["is_file"] = (string path) => {
            return Security.EnsurePathAllowedWithPrompt(path) && System.IO.File.Exists(path);
        };
        _LuaWorld.Sdk.Table["is_absolute"] = (string path) => {
            if (string.IsNullOrEmpty(path)) return false;

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
                // Windows drive: ^%a:[/\\]
                if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\')) return true;
                // Windows UNC: ^\\\\
                if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\') return true;
                // Unix root / or Windows root \
                if (path.Length >= 1 && (path[0] == '/' || path[0] == '\\')) return true;
            } else {
                // Unix root /
                if (path.Length >= 1 && path[0] == '/') return true;
            }

            return false;
        };
        _LuaWorld.Sdk.Table["remove_file"] = (string path) => {
            try {
                // Security: Validate path prior to deletion
                if (!Security.IsAllowedPath(path)) {
                    Core.UI.EngineSdk.Error($"Access denied: remove_file path is outside allowed areas ('{path}')");
                    return false;
                }
                if (ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path) || System.IO.File.Exists(path)) {
                    // Clear read-only if present
                    if (System.IO.File.Exists(path)) {
                        System.IO.FileAttributes attributes = System.IO.File.GetAttributes(path);
                        if ((attributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly) {
                            System.IO.File.SetAttributes(path, attributes & ~System.IO.FileAttributes.ReadOnly);
                        }
                    }
                    System.IO.File.Delete(path);
                }
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("remove_file failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["copy_file"] = (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!Security.EnsurePathAllowedWithPrompt(src) || !Security.EnsurePathAllowedWithPrompt(dst)) {
                    return false;
                }

                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                if (ow && System.IO.File.Exists(dst)) {
                    System.IO.FileAttributes attributes = System.IO.File.GetAttributes(dst);
                    if ((attributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly) {
                        System.IO.File.SetAttributes(dst, attributes & ~System.IO.FileAttributes.ReadOnly);
                    }
                }
                System.IO.File.Copy(src, dst, ow);
                return true;
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("copy_file failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["write_file"] = (string path, string content) => {
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
                Core.Diagnostics.LuaInternalCatch("write_file failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["read_file"] = (string path) => {
            try {
                if (!Security.EnsurePathAllowedWithPrompt(path)) {
                    return null;
                }
                if (!System.IO.File.Exists(path)) {
                    return null;
                }
                return System.IO.File.ReadAllText(path);
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("read_file failed with exception: " + ex);
                return null;
            }
        };
        _LuaWorld.Sdk.Table["rename_file"] = (string oldPath, string newPath, bool overwrite = false) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!Security.EnsurePathAllowedWithPrompt(oldPath) || !Security.EnsurePathAllowedWithPrompt(newPath)) {
                    return false;
                }

                if (System.IO.File.Exists(oldPath)) {
                    if (System.IO.File.Exists(newPath)) {
                        if (!overwrite) return false;
                        System.IO.File.Delete(newPath);
                    }
                    try {
                        System.IO.File.Move(oldPath, newPath, overwrite);
                    } catch {
                        // Fallback for cross-volume moves (or older .NET targets)
                        System.IO.File.Copy(oldPath, newPath, overwrite);
                        System.IO.File.Delete(oldPath);
                    }
                    return true;
                } else if (System.IO.Directory.Exists(oldPath)) {
                    ScriptEngines.Global.SdkModule.FileSystemUtils.MoveDirectory(oldPath, newPath, overwrite);
                    return true;
                }
                return false;
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("rename_file failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table["is_writable"] = (string path) => {
            try {
                if (!Security.IsAllowedPath(path)) {
                    return false;
                }

                // If it's a file, check file attributes and try to open for writing
                if (File.Exists(path)) {
                    try {
                        FileInfo fi = new FileInfo(path);
                        if (fi.IsReadOnly) return false;
                        using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) {
                            return true;
                        }
                    } catch {
                        return false;
                    }
                }

                if (!Directory.Exists(path))
                    return false;

                // For directories, try to create a temp file
                string testFile = Path.Combine(path, Path.GetRandomFileName() + ".tmp");

                try {
                    // Create a zero-byte file and delete it immediately when closed
                    using (File.Create(testFile, 1, FileOptions.DeleteOnClose)) {
                        return true;
                    }
                } catch {
                    return false;
                }
            } catch {
                return false;
            }
        };

        _LuaWorld.Sdk.Table["is_symlink"] = (string path) => Security.IsAllowedPath(path) && ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path);
        // Create hardlink (files only)
        _LuaWorld.Sdk.Table["create_hardlink"] = (string src, string dst) => {
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
                    Core.Diagnostics.LuaInternalCatch("Failed to delete existing file or link: " + destFull + " with exception: " + ex);
                    /* ignore */
                }
                try {
                    ScriptEngines.Global.SdkModule.HardLink.Create(srcFull, destFull);
                    return true;
                } catch (Exception ex) {
                    Core.Diagnostics.LuaInternalCatch("Failed to create hardlink from " + srcFull + " to " + destFull + " with exception: " + ex);
                    return false;
                }
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("create_hardlink failed with exception: " + ex);
                return false;
            }
        };
        _LuaWorld.Sdk.Table["create_symlink"] = (string source, string destination, bool isDirectory, DynValue overwrite) => {
            if (!Security.IsAllowedPath(source) || !Security.IsAllowedPath(destination)) {
                Core.UI.EngineSdk.Error($"Access denied: create_symlink src or dst outside allowed areas (src='{source}', dst='{destination}')");
                return false;
            }
            bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
            return ScriptEngines.Global.SdkModule.SymLink.Create(source, destination, isDirectory, ow);
        };
        _LuaWorld.Sdk.Table["readlink"] = (string path) => Security.IsAllowedPath(path) ? ScriptEngines.Global.SdkModule.FileSystemUtils.ReadLink(path) : null;

    }

}
