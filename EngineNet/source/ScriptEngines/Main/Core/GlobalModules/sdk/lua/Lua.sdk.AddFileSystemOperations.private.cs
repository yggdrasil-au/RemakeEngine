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
            return EngineNet.Shared.IO.LongPathIO.GetAbsolutePath(path: path);
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
            Table table = new(owner: _LuaWorld.LuaScript);
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
                EngineNet.Shared.IO.LongPathIO.CreateDirectory(path: safePath);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "mkdir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "ensure_dir"] = (string path) => {
            try {
                if (!Security.TryGetAllowedCanonicalPath(path: path, canonicalPath: out string safePath)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: ensure_dir path is outside allowed areas ('{path}')");
                    return false;
                }

                EngineNet.Shared.IO.LongPathIO.CreateDirectory(path: safePath);
                return true;
            }
            catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "ensure_dir failed with exception: " + ex);
                return false;
            }
        };

        _LuaWorld.Sdk.Table[key: "is_dir"] = (string path) => {
            return Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)
                   && EngineNet.Shared.IO.LongPathIO.DirectoryExists(path: safePath);
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

                if (EngineNet.Shared.IO.LongPathIO.DirectoryExists(path: path)) {
                    EngineNet.Shared.IO.LongPathIO.DeleteDirectory(path: path, recursive: true);
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
            Table table = new(owner: _LuaWorld.LuaScript);
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
            return Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath) && EngineNet.Shared.IO.LongPathIO.FileExists(path: safePath);
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
                    !EngineNet.Shared.IO.LongPathIO.FileExists(path: safePath)) return true;
                // Clear read-only if present
                if (EngineNet.Shared.IO.LongPathIO.FileExists(path: safePath)) {
                    System.IO.FileAttributes attributes = EngineNet.Shared.IO.LongPathIO.GetAttributes(path: safePath);
                    if ((attributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly) {
                        EngineNet.Shared.IO.LongPathIO.SetAttributes(path: safePath, attributes: attributes & ~System.IO.FileAttributes.ReadOnly);
                    }
                }

                EngineNet.Shared.IO.LongPathIO.DeleteFile(path: safePath);
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
                if (ow && EngineNet.Shared.IO.LongPathIO.FileExists(path: safeDst)) {
                    System.IO.FileAttributes attributes = EngineNet.Shared.IO.LongPathIO.GetAttributes(path: safeDst);
                    if ((attributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly) {
                        EngineNet.Shared.IO.LongPathIO.SetAttributes(path: safeDst, attributes: attributes & ~System.IO.FileAttributes.ReadOnly);
                    }
                }

                EngineNet.Shared.IO.LongPathIO.CopyFile(sourcePath: safeSrc, destinationPath: safeDst, overwrite: ow);
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
                    EngineNet.Shared.IO.LongPathIO.CreateDirectory(path: parent);
                }

                EngineNet.Shared.IO.LongPathIO.WriteAllText(path: safePath, contents: content);
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

                if (!EngineNet.Shared.IO.LongPathIO.FileExists(path: safePath)) {
                    return null;
                }

                return EngineNet.Shared.IO.LongPathIO.ReadAllText(path: safePath);
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

                if (EngineNet.Shared.IO.LongPathIO.FileExists(path: safeOldPath)) {
                    if (EngineNet.Shared.IO.LongPathIO.FileExists(path: safeNewPath)) {
                        if (!overwrite) return false;
                        EngineNet.Shared.IO.LongPathIO.DeleteFile(path: safeNewPath);
                    }

                    try {
                        EngineNet.Shared.IO.LongPathIO.MoveFile(sourcePath: safeOldPath, destinationPath: safeNewPath, overwrite: overwrite);
                    }
                    catch (Exception ex) {
                        Shared.IO.Diagnostics.Bug("rename_file fallback catch triggered with exception: " +
                                     ex);
                        // Fallback for cross-volume moves (or older .NET targets)
                        EngineNet.Shared.IO.LongPathIO.CopyFile(sourcePath: safeOldPath, destinationPath: safeNewPath, overwrite: overwrite);
                        EngineNet.Shared.IO.LongPathIO.DeleteFile(path: safeOldPath);
                    }

                    return true;
                } else if (EngineNet.Shared.IO.LongPathIO.DirectoryExists(path: safeOldPath)) {
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
                if (EngineNet.Shared.IO.LongPathIO.FileExists(path: safePath)) {
                    try {
                        FileInfo fi = new(fileName: EngineNet.Shared.IO.LongPathIO.NormalizeForIO(path: safePath));
                        if (fi.IsReadOnly) return false;
                        using (EngineNet.Shared.IO.LongPathIO.OpenFile(path: safePath, mode: FileMode.Open, access: FileAccess.Write, share: FileShare.ReadWrite)) {
                            return true;
                        }
                    }
                    catch (Exception ex) {
                        Shared.IO.Diagnostics.Bug("is_writable(file) catch triggered with exception: " +
                                     ex);
                        return false;
                    }
                }

                if (!EngineNet.Shared.IO.LongPathIO.DirectoryExists(path: safePath))
                    return false;

                // For directories, try to create a temp file
                string testFile = Path.Combine(path1: safePath, path2: Path.GetRandomFileName() + ".tmp");

                try {
                    // Create a zero-byte file and delete it immediately when closed
                    using (EngineNet.Shared.IO.LongPathIO.OpenFile(path: testFile, mode: FileMode.CreateNew, access: FileAccess.Write, options: FileOptions.DeleteOnClose)) {
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
                    EngineNet.Shared.IO.LongPathIO.CreateDirectory(path: parent);
                }

                if (!EngineNet.Shared.IO.LongPathIO.FileExists(path: srcFull)) {
                    return false;
                }

                // Remove existing file or link
                try {
                    if (ScriptEngines.Global.SdkModule.FileSystemUtils.IsSymlink(path: destFull) ||
                        EngineNet.Shared.IO.LongPathIO.FileExists(path: destFull)) {
                        EngineNet.Shared.IO.LongPathIO.DeleteFile(path: destFull);
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