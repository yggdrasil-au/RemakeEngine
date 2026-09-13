using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {
    private static void AddFileSystemOperations(LuaWorld _LuaWorld) {
        AddPathAndInfoOperations(_LuaWorld: _LuaWorld);
        AddDirectoryOperations(_LuaWorld: _LuaWorld);
        AddFileOperations(_LuaWorld: _LuaWorld);
        AddLinkOperations(_LuaWorld: _LuaWorld);
    }

    private static void AddPathAndInfoOperations(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.Table[key: "find_subdir"] = (string baseDir, string name) => {
            if (Security.IsAllowedPath(path: baseDir))
                return ScriptEngines.Global.SdkModule.FileSystemUtils.FindSubdir(baseDir: baseDir, name: name);
            Shared.IO.UI.EngineSdk.Error($"Access denied: find_subdir baseDir is outside allowed areas ('{baseDir}')");
            return null;
        };

        _LuaWorld.Sdk.Table[key: "has_all_subdirs"] = (string baseDir, Table names) => {
            try {
                if (!Security.IsAllowedPath(path: baseDir)) {
                    Shared.IO.UI.EngineSdk.Error(
                        $"Access denied: has_all_subdirs baseDir is outside allowed areas ('{baseDir}')");
                    return false;
                }

                List<string> list = Lua.Globals.Utils.TableToStringList(t: names);
                return ScriptEngines.Global.SdkModule.FileSystemUtils.HasAllSubdirs(baseDir: baseDir, names: list);
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "has_all_subdirs failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "path_exists"] = (string path) => {
            return Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath) && ScriptEngines.Global.SdkModule.FileSystemUtils.PathExists(path: safePath);
        };

        _LuaWorld.Sdk.Table[key: "lexists"] = (string path) => {
            return Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath) && ScriptEngines.Global.SdkModule.FileSystemUtils.PathExistsIncludingLinks(path: safePath);
        };

        _LuaWorld.Sdk.Table[key: "absolute_path"] = (string path) => {
            if (string.IsNullOrEmpty(path)) return path;

            // 1. Try realpath first (handles symlinks etc)
            string? resolved = null;
            if (Security.IsAllowedPath(path: path)) {
                resolved = ScriptEngines.Global.SdkModule.FileSystemUtils.RealPath(path: path);
            }

            string result = path;
            if (!string.IsNullOrEmpty(resolved)) {
                result = resolved;
            } else {
                // 2. Check if absolute using is_absolute logic
                bool isAbsolute = false;
                bool isWindows =
                    System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(osPlatform: System.Runtime.InteropServices
                        .OSPlatform.Windows);

                if (isWindows) {
                    switch (path.Length) {
                        // Windows drive: C:\
                        case >= 3 when char.IsLetter(c: path[index: 0]) && path[index: 1] == ':' && (path[index: 2] == '/' || path[index: 2] == '\\'):
                        // Windows UNC: \\host
                        case >= 2 when path[index: 0] == '\\' && path[index: 1] == '\\':
                        // Windows root: \
                        case >= 1 when path[index: 0] == '\\':
                        // Unix-style root on Windows: /
                        case >= 1 when path[index: 0] == '/':
                            isAbsolute = true;
                            break;
                    }
                } else {
                    // Unix root: /
                    if (path.Length >= 1 && path[index: 0] == '/') isAbsolute = true;
                }

                if (!isAbsolute) {
                    string cwd = System.IO.Directory.GetCurrentDirectory();
                    result = System.IO.Path.Combine(path1: cwd, path2: path);
                }
            }

            // 3. Normalize
            char sep = System.IO.Path.DirectorySeparatorChar;
            result = sep == '\\' ? result.Replace(oldChar: '/', newChar: '\\') : result.Replace(oldChar: '\\', newChar: '/');

            // 4. Windows Long Path Support (\\\\?\\ prefix)
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(osPlatform: System.Runtime.InteropServices
                    .OSPlatform.Windows)) return result;
            if (result.Length > 255 && result.Length >= 2 && char.IsLetter(c: result[index: 0]) && result[index: 1] == ':' &&
                !result.StartsWith(@"\\?\")) {
                result = @"\\?\" + result;
            }

            return result;
        };

        _LuaWorld.Sdk.Table[key: "realpath"] = (string path) =>
            Security.IsAllowedPath(path: path) ? ScriptEngines.Global.SdkModule.FileSystemUtils.RealPath(path: path) : null;

        _LuaWorld.Sdk.Table[key: "attributes"] = (string path) => {
            // Call the shared logic
            Dictionary<string, object>? resultDict = ScriptEngines.Global.SdkModule.Helpers.AddFileSystemOperations.FileAttributes(path: path);

            if (resultDict == null) {
                return DynValue.Nil;
            }

            // Convert C# Dictionary to MoonSharp Table
            Table table = new Table(owner: _LuaWorld.LuaScript);
            foreach (KeyValuePair<string, object> kvp in resultDict) {
                // We use DynValue.FromObject to handle the conversion of strings/doubles/longs automatically
                table[key: kvp.Key] = DynValue.FromObject(script: _LuaWorld.LuaScript, obj: kvp.Value);
            }

            return DynValue.NewTable(table: table);
        };

        _LuaWorld.Sdk.Table[key: "is_absolute"] = (string path) => {
            if (string.IsNullOrEmpty(path)) return false;

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(osPlatform: System.Runtime.InteropServices.OSPlatform
                    .Windows)) {
                switch (path.Length) {
                    // Windows drive: ^%a:[/\\]
                    case >= 3 when char.IsLetter(c: path[index: 0]) && path[index: 1] == ':' && (path[index: 2] == '/' || path[index: 2] == '\\'):
                    // Windows UNC: ^\\\\
                    case >= 2 when path[index: 0] == '\\' && path[index: 1] == '\\':
                    // Unix root / or Windows root \
                    case >= 1 when (path[index: 0] == '/' || path[index: 0] == '\\'):
                        return true;
                }
            } else {
                // Unix root /
                if (path.Length >= 1 && path[index: 0] == '/') return true;
            }

            return false;
        };
    }

    private static void AddDirectoryOperations(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.Table[key: "mkdir"] = (string path) => {
            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)) {
                return false;
            }

            try {
                System.IO.Directory.CreateDirectory(path: safePath);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "mkdir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "ensure_dir"] = (string path) => {
            try {
                if (!Security.IsAllowedPath(path: path)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: ensure_dir path is outside allowed areas ('{path}')");
                    return false;
                }

                System.IO.Directory.CreateDirectory(path: path);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "ensure_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "is_dir"] = (string path) => {
            return Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)
                   && System.IO.Directory.Exists(path: safePath);
        };

        _LuaWorld.Sdk.Table[key: "copy_dir"] = (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate paths
                if (!Security.IsAllowedPath(path: src) || !Security.IsAllowedPath(path: dst)) {
                    Shared.IO.UI.EngineSdk.Error(
                        $"Access denied: copy_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }

                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ScriptEngines.Global.SdkModule.FileSystemUtils.CopyDirectory(sourceDir: src, destDir: dst, overwrite: ow);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "copy_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "move_dir"] = (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate paths
                if (!Security.IsAllowedPath(path: src) || !Security.IsAllowedPath(path: dst)) {
                    Shared.IO.UI.EngineSdk.Error(
                        $"Access denied: move_dir src or dst is outside allowed areas (src='{src}', dst='{dst}')");
                    return false;
                }

                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ScriptEngines.Global.SdkModule.FileSystemUtils.MoveDirectory(sourceDir: src, destDir: dst, overwrite: ow);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "move_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "remove_dir"] = (string path) => {
            try {
                // Security: Validate path prior to deletion
                if (!Security.IsAllowedPath(path: path)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: remove_dir path is outside allowed areas ('{path}')");
                    return false;
                }

                if (System.IO.Directory.Exists(path: path)) {
                    System.IO.Directory.Delete(path: path, recursive: true);
                }

                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "remove_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "currentdir"] = () => {
            // old, get current directory of process, not caller script file
            return System.IO.Directory.GetCurrentDirectory();
        };

        _LuaWorld.Sdk.Table[key: "current_dir"] = () => {
            // new, get current directory of caller script file
            return System.IO.Path.GetDirectoryName(path: _LuaWorld.LuaScriptPath);
        };

        _LuaWorld.Sdk.Table[key: "list_dir"] = (string path) => {
            Table table = new Table(owner: _LuaWorld.LuaScript);
            List<string>? resultList = ScriptEngines.Global.SdkModule.Helpers.AddFileSystemOperations.List_Dir(path: path);
            if (resultList == null) {
                return table;
            }

            // Convert the List<string> into the Lua Table
            foreach (string name in resultList) {
                table.Append(DynValue.NewString(str: name));
            }

            return table;
        };
    }

    private static void AddFileOperations(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.Table[key: "is_file"] = static (string path) => {
            return Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath) && System.IO.File.Exists(path: safePath);
        };

        _LuaWorld.Sdk.Table[key: "remove_file"] = static (string path) => {
            try {
                // Security: Validate path prior to deletion
                if (!Security.TryGetAllowedCanonicalPath(path: path, canonicalPath: out string safePath)) {
                    Shared.IO.UI.EngineSdk.Error(
                        $"Access denied: remove_file path is outside allowed areas ('{path}')");
                    return false;
                }

                if (!ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path: safePath) &&
                    !System.IO.File.Exists(path: safePath)) return true;
                // Clear read-only if present
                if (System.IO.File.Exists(path: safePath)) {
                    System.IO.FileAttributes attributes = System.IO.File.GetAttributes(path: safePath);
                    if ((attributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly) {
                        System.IO.File.SetAttributes(path: safePath, fileAttributes: attributes & ~System.IO.FileAttributes.ReadOnly);
                    }
                }

                System.IO.File.Delete(path: safePath);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "remove_file failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "copy_file"] = static (string src, string dst, DynValue overwrite) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: src, canonicalPath: out string safeSrc) ||
                    !Security.TryGetAllowedCanonicalPathWithPrompt(path: dst, canonicalPath: out string safeDst)) {
                    return false;
                }

                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                if (ow && System.IO.File.Exists(path: safeDst)) {
                    System.IO.FileAttributes attributes = System.IO.File.GetAttributes(path: safeDst);
                    if ((attributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly) {
                        System.IO.File.SetAttributes(path: safeDst, fileAttributes: attributes & ~System.IO.FileAttributes.ReadOnly);
                    }
                }

                System.IO.File.Copy(sourceFileName: safeSrc, destFileName: safeDst, overwrite: ow);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "copy_file failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "write_file"] = static (string path, string content) => {
            try {
                if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)) {
                    return false;
                }

                string? parent = System.IO.Path.GetDirectoryName(path: safePath);
                if (!string.IsNullOrEmpty(parent)) {
                    System.IO.Directory.CreateDirectory(path: parent);
                }

                System.IO.File.WriteAllText(path: safePath, contents: content);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "write_file failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "read_file"] = (string path) => {
            try {
                if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)) {
                    return null;
                }

                if (!System.IO.File.Exists(path: safePath)) {
                    return null;
                }

                return System.IO.File.ReadAllText(path: safePath);
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.Bug("read_file catch triggered with exception: " + ex);
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "read_file failed with exception: " + ex);
                return null;
            }
        };

        _LuaWorld.Sdk.Table[key: "rename_file"] = (string oldPath, string newPath, bool overwrite = false) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: oldPath, canonicalPath: out string safeOldPath) ||
                    !Security.TryGetAllowedCanonicalPathWithPrompt(path: newPath, canonicalPath: out string safeNewPath)) {
                    return false;
                }

                if (System.IO.File.Exists(path: safeOldPath)) {
                    if (System.IO.File.Exists(path: safeNewPath)) {
                        if (!overwrite) return false;
                        System.IO.File.Delete(path: safeNewPath);
                    }

                    try {
                        System.IO.File.Move(sourceFileName: safeOldPath, destFileName: safeNewPath, overwrite: overwrite);
                    }
                    catch (Exception ex) {
                        Shared.IO.Diagnostics.Bug("rename_file fallback catch triggered with exception: " +
                                     ex);
                        // Fallback for cross-volume moves (or older .NET targets)
                        System.IO.File.Copy(sourceFileName: safeOldPath, destFileName: safeNewPath, overwrite: overwrite);
                        System.IO.File.Delete(path: safeOldPath);
                    }

                    return true;
                } else if (System.IO.Directory.Exists(path: safeOldPath)) {
                    ScriptEngines.Global.SdkModule.FileSystemUtils.MoveDirectory(sourceDir: safeOldPath, destDir: safeNewPath, overwrite: overwrite);
                    return true;
                }

                return false;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "rename_file failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "is_writable"] = (string path) => {
            try {
                if (!Security.TryGetAllowedCanonicalPath(path: path, canonicalPath: out string safePath)) {
                    return false;
                }

                // If it's a file, check file attributes and try to open for writing
                if (File.Exists(path: safePath)) {
                    try {
                        FileInfo fi = new FileInfo(fileName: safePath);
                        if (fi.IsReadOnly) return false;
                        using (File.Open(path: safePath, mode: FileMode.Open, access: FileAccess.Write, share: FileShare.ReadWrite)) {
                            return true;
                        }
                    }
                    catch (Exception ex) {
                        Shared.IO.Diagnostics.Bug("is_writable(file) catch triggered with exception: " +
                                     ex);
                        return false;
                    }
                }

                if (!Directory.Exists(path: safePath))
                    return false;

                // For directories, try to create a temp file
                string testFile = Path.Combine(path1: safePath, path2: Path.GetRandomFileName() + ".tmp");

                try {
                    // Create a zero-byte file and delete it immediately when closed
                    using (File.Create(path: testFile, bufferSize: 1, options: FileOptions.DeleteOnClose)) {
                        return true;
                    }
                } catch (Exception ex) {
                    Shared.IO.Diagnostics.Bug("is_writable(directory) catch triggered with exception: " +
                                 ex);
                    return false;
                }
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.Bug("is_writable outer catch triggered with exception: " + ex);
                return false;
            }
        };
    }

    private static void AddLinkOperations(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.Table[key: "is_symlink"] = (string path) => Security.IsAllowedPath(path: path) && ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path: path);

        // Create hardlink (files only)
        _LuaWorld.Sdk.Table[key: "create_hardlink"] = (string src, string dst) => {
            try {
                if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: src, canonicalPath: out string safeSrc) ||
                    !Security.TryGetAllowedCanonicalPathWithPrompt(path: dst, canonicalPath: out string safeDst)) {
                    return false;
                }

                string destFull = safeDst;
                string srcFull = safeSrc;
                string? parent = System.IO.Path.GetDirectoryName(path: destFull);
                if (!string.IsNullOrEmpty(parent)) {
                    System.IO.Directory.CreateDirectory(path: parent);
                }

                if (!System.IO.File.Exists(path: srcFull)) {
                    return false;
                }

                // Remove existing file or link
                try {
                    if (ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path: destFull) ||
                        System.IO.File.Exists(path: destFull)) {
                        System.IO.File.Delete(path: destFull);
                    }
                } catch (Exception ex) {
                    Shared.IO.Diagnostics.LuaInternalCatch(ex: "Failed to delete existing file or link: " + destFull + " with exception: " + ex);
                    /* ignore */
                }

                try {
                    ScriptEngines.Global.SdkModule.HardLink.Create(existingFile: srcFull, newLinkPath: destFull);
                    return true;
                } catch (Exception ex) {
                    Shared.IO.Diagnostics.LuaInternalCatch(ex: "Failed to create hardlink from " + srcFull + " to " + destFull + " with exception: " + ex);
                    return false;
                }
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.Bug("create_hardlink catch triggered with exception: " + ex);
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "create_hardlink failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "create_symlink"] = (string source, string destination, bool isDirectory, DynValue overwrite) => {
            if (!Security.IsAllowedPath(path: source) || !Security.IsAllowedPath(path: destination)) {
                Shared.IO.UI.EngineSdk.Error(
                    $"Access denied: create_symlink src or dst outside allowed areas (src='{source}', dst='{destination}')");
                return false;
            }

            bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
            return ScriptEngines.Global.SdkModule.SymLink.Create(source: source, destination: destination, isDirectory: isDirectory, overwrite: ow);
        };

        _LuaWorld.Sdk.Table[key: "readlink"] = (string path) =>
            Security.IsAllowedPath(path: path) ? ScriptEngines.Global.SdkModule.FileSystemUtils.ReadLink(path: path) : null;
    }
}