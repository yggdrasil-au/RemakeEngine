
namespace EngineNet.ScriptEngines.Global.SdkModule;

using System.IO.Compression;

internal static class Helpers {

    //AddConfigurationHelpers
    internal static class AddConfigurationHelpers {
        internal static bool Validate_Source_Dir(string dir) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path: dir)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: validate_source_dir path is outside allowed areas ('{dir}')");
                    return false;
                }
                ValidateSourceDir(dir: dir);
                return true;
            } catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "validate_source_dir failed with exception: " + ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Validates that a source directory exists and is accessible.
    /// Throws if invalid.
    /// </summary>
    private static void ValidateSourceDir(string dir) {
        if (string.IsNullOrWhiteSpace(dir)) {
            throw new System.ArgumentException("Source directory path is empty");
        }

        if (!System.IO.Directory.Exists(path: dir)) {
            throw new System.IO.DirectoryNotFoundException($"Source directory not found: {dir}");
        }
        // Basic access check: attempt to enumerate one entry (if any)
        try {
            using IEnumerator<string> _ = System.IO.Directory.EnumerateFileSystemEntries(path: dir).GetEnumerator();
        } catch (System.Exception ex) {
            throw new System.IO.IOException($"Cannot access source directory '{dir}': {ex.Message}", innerException: ex);
        }
    }


    //AddFileSystemOperations
    internal static class AddFileSystemOperations {
        internal static Dictionary<string, object>? FileAttributes(string path) {
            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)) {
                return null;
            }

            try {
                Dictionary<string, object> attrs = new Dictionary<string, object>();

                if (System.IO.Directory.Exists(path: safePath)) {
                    DirectoryInfo dirInfo = new System.IO.DirectoryInfo(path: safePath);
                    attrs[key: "mode"] = "directory";
                    attrs[key: "modification"] = (double)new System.DateTimeOffset(dateTime: dirInfo.LastWriteTime).ToUnixTimeSeconds();
                    return attrs;
                }

                if (System.IO.File.Exists(path: safePath)) {
                    FileInfo fileInfo = new System.IO.FileInfo(fileName: safePath);
                    attrs[key: "mode"] = "file";
                    attrs[key: "size"] = fileInfo.Length;
                    attrs[key: "modification"] = (double)new System.DateTimeOffset(dateTime: fileInfo.LastWriteTime).ToUnixTimeSeconds();
                    return attrs;
                }

                return null;
            } catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "FileAttributes helper failed with exception: " + ex);
                return null;
            }
        }

        internal static List<string>? List_Dir(string path) {
            // 1. Security check
            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)) {
                return null;
            }

            try {
                // 2. Logic: Get all files and directories
                // This returns the full paths initially
                string[] entries = System.IO.Directory.GetFileSystemEntries(path: safePath);

                // 3. Transformation: Convert full paths to just names
                List<string> names = new List<string>();
                foreach (string entry in entries) {
                    names.Add(item: System.IO.Path.GetFileName(path: entry));
                }

                return names;
            } catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "list_dir helper failed with exception: " + ex);
                return null;
            }
        }

    }

    // add hashing functions like sha1_file and md5
    internal static class AddHashMethods {

        internal static dynamic? sha1_file(string path) {
            try {
                if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)) {
                    return null;
                }
                using System.IO.FileStream fs = System.IO.File.OpenRead(path: safePath);
                byte[] hash = System.Security.Cryptography.SHA1.HashData(source: fs);
                return System.Convert.ToHexString(inArray: hash).ToLowerInvariant();
            } catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "sha1_file failed with exception: " + ex);
                return null;
            }
        }

        internal static string Md5Hash(string text) {
            try {
                byte[] data = System.Security.Cryptography.MD5.HashData(source: System.Text.Encoding.UTF8.GetBytes(s: text ?? string.Empty));
                return System.Convert.ToHexString(inArray: data).ToLowerInvariant();
            } catch (Exception ex) {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "md5 failed with exception: " + ex);
                return string.Empty;
            }
        }
    }

    internal static void Sleep(double seconds) {
        if (double.IsNaN(d: seconds) || double.IsInfinity(d: seconds) || seconds <= 0) {
            return;
        }

        try {
            System.Threading.Thread.Sleep(timeout: System.TimeSpan.FromSeconds(seconds));
        }  catch (Exception ex) {
            Shared.IO.Diagnostics.LuaInternalCatch(ex: "sleep failed with exception: " + ex);
        }
    }


    //AddArchiveOperations
    internal static class AddArchiveOperations {
        internal static bool Extract_Archive(string archivePath, string destDir) {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!Security.IsAllowedPath(path: archivePath) || !Security.IsAllowedPath(path: destDir)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {archivePath} -> {destDir}");
                    return false;
                }

                string ext = System.IO.Path.GetExtension(path: archivePath).ToLowerInvariant();
                if (ext == ".zip") {
                    System.IO.Compression.ZipFile.ExtractToDirectory(sourceArchiveFileName: archivePath, destinationDirectoryName: destDir);
                    return true;
                }
                // For other formats, suggest using approved tools
                Shared.IO.UI.EngineSdk.Error($"Unsupported archive format '{ext}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Shared.IO.UI.EngineSdk.Error($"Archive extraction failed: {ex.Message}"); // output directly to UI, consider returning error to lua instead
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "extract_archive failed with exception: " + ex);
                return false;
            }
        }

        internal static bool Create_Archive(string srcPath, string archivePath, string type) {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!Security.IsAllowedPath(path: srcPath) || !Security.IsAllowedPath(path: archivePath)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {srcPath} -> {archivePath}");
                    return false;
                }

                if (type.Equals("zip", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    if (System.IO.Directory.Exists(path: srcPath)) {
                        System.IO.Compression.ZipFile.CreateFromDirectory(sourceDirectoryName: srcPath, destinationArchiveFileName: archivePath);
                    } else if (System.IO.File.Exists(path: srcPath)) {
                        // Create zip with single file
                        using ZipArchive archive = System.IO.Compression.ZipFile.Open(archiveFileName: archivePath, mode: System.IO.Compression.ZipArchiveMode.Create);
                        ZipArchiveEntry entry = archive.CreateEntry(entryName: System.IO.Path.GetFileName(path: srcPath));
                        using Stream entryStream = entry.Open();
                        using FileStream fileStream = System.IO.File.OpenRead(path: srcPath);
                        fileStream.CopyTo(destination: entryStream);
                    } else {
                        return false;
                    }
                    return true;
                }
                // For other formats, suggest using approved tools
                Shared.IO.UI.EngineSdk.Error($"Unsupported archive type '{type}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Shared.IO.UI.EngineSdk.Error($"Archive creation failed: {ex.Message}");
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "create_archive failed with exception: " + ex);
                return false;
            }
        }
    }

    //AddTomlHelpers

    internal static class AddTomlHelpers {
        internal static object? Toml_Read_File(string path) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path: path)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: toml_read_file path is outside allowed areas ('{path}')");
                    return null; //DynValue.Nil;
                }
                object? obj = Shared.Serialization.Toml.TomlHelpers.ParseFileToPlainObject(path: path);
                return obj;
            } catch (System.Exception ex) {
                Shared.IO.UI.EngineSdk.Error($"TOML read failed: {ex.Message}");
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "toml_read_file failed with exception: " + ex);
                return null; //DynValue.Nil;
            }
        }

        internal static void Toml_Write_File(string path, object? obj) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path: path)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: toml_write_file path is outside allowed areas ('{path}')");
                    return;
                }
                Shared.Serialization.Toml.TomlHelpers.WriteTomlFile(path: path, data: obj);
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug("catch triggered with exception: " + ex);
                Shared.IO.UI.EngineSdk.Error($"TOML write failed: {ex.Message}");
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "toml_write_file failed with exception: " + ex);
            }
        }

    }

    internal static class AddYamlHelpers {
        internal static object? Yaml_Read_File(string path) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path: path)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: yaml_read_file path is outside allowed areas ('{path}')");
                    return null;
                }

                object obj = Shared.Serialization.Yaml.YamlHelpers.ParseFileToPlainObject(path: path);
                return obj;
            } catch (System.Exception ex) {
                Shared.IO.UI.EngineSdk.Error($"YAML read failed: {ex.Message}");
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "yaml_read_file failed with exception: " + ex);
                return null;
            }
        }

        internal static void Yaml_Write_File(string path, object? obj) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path: path)) {
                    Shared.IO.UI.EngineSdk.Error($"Access denied: yaml_write_file path is outside allowed areas ('{path}')");
                    return;
                }

                Shared.Serialization.Yaml.YamlHelpers.WriteYamlFile(path: path, data: obj);
            } catch (System.Exception ex) {
                Shared.IO.UI.EngineSdk.Error($"YAML write failed: {ex.Message}");
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "yaml_write_file failed with exception: " + ex);
            }
        }
    }
}
