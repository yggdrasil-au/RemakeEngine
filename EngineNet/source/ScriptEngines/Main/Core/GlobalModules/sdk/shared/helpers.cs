
namespace EngineNet.ScriptEngines.Global.SdkModule;

internal static class Helpers {

    //AddConfigurationHelpers
    internal static class AddConfigurationHelpers {
        internal static bool Validate_Source_Dir(string dir) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(dir)) {
                    Shared.UI.EngineSdk.Error($"Access denied: validate_source_dir path is outside allowed areas ('{dir}')");
                    return false;
                }
                ValidateSourceDir(dir);
                return true;
            } catch (Exception ex) {
                Shared.Diagnostics.LuaInternalCatch("validate_source_dir failed with exception: " + ex);
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

        if (!System.IO.Directory.Exists(dir)) {
            throw new System.IO.DirectoryNotFoundException($"Source directory not found: {dir}");
        }
        // Basic access check: attempt to enumerate one entry (if any)
        try {
            using IEnumerator<string> _ = System.IO.Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
        } catch (System.Exception ex) {
            throw new System.IO.IOException($"Cannot access source directory '{dir}': {ex.Message}", ex);
        }
    }


    //AddFileSystemOperations
    internal static class AddFileSystemOperations {
        internal static Dictionary<string, object>? FileAttributes(string path) {
            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path, out string safePath)) {
                return null;
            }

            try {
                var attrs = new Dictionary<string, object>();

                if (System.IO.Directory.Exists(safePath)) {
                    var dirInfo = new System.IO.DirectoryInfo(safePath);
                    attrs["mode"] = "directory";
                    attrs["modification"] = (double)new System.DateTimeOffset(dirInfo.LastWriteTime).ToUnixTimeSeconds();
                    return attrs;
                }

                if (System.IO.File.Exists(safePath)) {
                    var fileInfo = new System.IO.FileInfo(safePath);
                    attrs["mode"] = "file";
                    attrs["size"] = fileInfo.Length;
                    attrs["modification"] = (double)new System.DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds();
                    return attrs;
                }

                return null;
            } catch (Exception ex) {
                Shared.Diagnostics.LuaInternalCatch("FileAttributes helper failed with exception: " + ex);
                return null;
            }
        }

        internal static List<string>? List_Dir(string path) {
            // 1. Security check
            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path, out string safePath)) {
                return null;
            }

            try {
                // 2. Logic: Get all files and directories
                // This returns the full paths initially
                string[] entries = System.IO.Directory.GetFileSystemEntries(safePath);

                // 3. Transformation: Convert full paths to just names
                List<string> names = new List<string>();
                foreach (string entry in entries) {
                    names.Add(System.IO.Path.GetFileName(entry));
                }

                return names;
            } catch (Exception ex) {
                Shared.Diagnostics.LuaInternalCatch("list_dir helper failed with exception: " + ex);
                return null;
            }
        }

    }

    // add hashing functions like sha1_file and md5
    internal static class AddHashMethods {

        internal static dynamic? sha1_file(string path) {
            try {
                if (!Security.TryGetAllowedCanonicalPathWithPrompt(path, out string safePath)) {
                    return null;
                }
                using System.IO.FileStream fs = System.IO.File.OpenRead(safePath);
                byte[] hash = System.Security.Cryptography.SHA1.HashData(fs);
                return System.Convert.ToHexString(hash).ToLowerInvariant();
            } catch (Exception ex) {
                Shared.Diagnostics.LuaInternalCatch("sha1_file failed with exception: " + ex);
                return null;
            }
        }

        internal static string Md5Hash(string text) {
            try {
                byte[] data = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
                return System.Convert.ToHexString(data).ToLowerInvariant();
            } catch (Exception ex) {
                Shared.Diagnostics.LuaInternalCatch("md5 failed with exception: " + ex);
                return string.Empty;
            }
        }
    }

    internal static void Sleep(double seconds) {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) {
            return;
        }

        try {
            System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(seconds));
        }  catch (Exception ex) {
            Shared.Diagnostics.LuaInternalCatch("sleep failed with exception: " + ex);
        }
    }


    //AddArchiveOperations
    internal static class AddArchiveOperations {
        internal static bool Extract_Archive(string archivePath, string destDir) {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!Security.IsAllowedPath(archivePath) || !Security.IsAllowedPath(destDir)) {
                    Shared.UI.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {archivePath} -> {destDir}");
                    return false;
                }

                string ext = System.IO.Path.GetExtension(archivePath).ToLowerInvariant();
                if (ext == ".zip") {
                    System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destDir);
                    return true;
                }
                // For other formats, suggest using approved tools
                Shared.UI.EngineSdk.Error($"Unsupported archive format '{ext}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Shared.UI.EngineSdk.Error($"Archive extraction failed: {ex.Message}"); // output directly to UI, consider returning error to lua instead
                Shared.Diagnostics.LuaInternalCatch("extract_archive failed with exception: " + ex);
                return false;
            }
        }

        internal static bool Create_Archive(string srcPath, string archivePath, string type) {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!Security.IsAllowedPath(srcPath) || !Security.IsAllowedPath(archivePath)) {
                    Shared.UI.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {srcPath} -> {archivePath}");
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
                Shared.UI.EngineSdk.Error($"Unsupported archive type '{type}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Shared.UI.EngineSdk.Error($"Archive creation failed: {ex.Message}");
                Shared.Diagnostics.LuaInternalCatch("create_archive failed with exception: " + ex);
                return false;
            }
        }
    }

    //AddTomlHelpers

    internal static class AddTomlHelpers {
        internal static object? Toml_Read_File(string path) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path)) {
                    Shared.UI.EngineSdk.Error($"Access denied: toml_read_file path is outside allowed areas ('{path}')");
                    return null; //DynValue.Nil;
                }
                object? obj = Shared.Serialization.Toml.TomlHelpers.ParseFileToPlainObject(path);
                return obj;
            } catch (System.Exception ex) {
                Shared.UI.EngineSdk.Error($"TOML read failed: {ex.Message}");
                Shared.Diagnostics.LuaInternalCatch("toml_read_file failed with exception: " + ex);
                return null; //DynValue.Nil;
            }
        }

        internal static void Toml_Write_File(string path, object? obj) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path)) {
                    Shared.UI.EngineSdk.Error($"Access denied: toml_write_file path is outside allowed areas ('{path}')");
                    return;
                }
                Shared.Serialization.Toml.TomlHelpers.WriteTomlFile(path, obj);
            } catch (System.Exception ex) {
                Shared.Diagnostics.Bug("[helpers.cs::Toml_Write_File] catch triggered with exception: " + ex);
                Shared.UI.EngineSdk.Error($"TOML write failed: {ex.Message}");
                Shared.Diagnostics.LuaInternalCatch("toml_write_file failed with exception: " + ex);
            }
        }

    }

    internal static class AddYamlHelpers {
        internal static object? Yaml_Read_File(string path) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path)) {
                    Shared.UI.EngineSdk.Error($"Access denied: yaml_read_file path is outside allowed areas ('{path}')");
                    return null;
                }

                object obj = Shared.Serialization.Yaml.YamlHelpers.ParseFileToPlainObject(path);
                return obj;
            } catch (System.Exception ex) {
                Shared.UI.EngineSdk.Error($"YAML read failed: {ex.Message}");
                Shared.Diagnostics.LuaInternalCatch("yaml_read_file failed with exception: " + ex);
                return null;
            }
        }

        internal static void Yaml_Write_File(string path, object? obj) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path)) {
                    Shared.UI.EngineSdk.Error($"Access denied: yaml_write_file path is outside allowed areas ('{path}')");
                    return;
                }

                Shared.Serialization.Yaml.YamlHelpers.WriteYamlFile(path, obj);
            } catch (System.Exception ex) {
                Shared.UI.EngineSdk.Error($"YAML write failed: {ex.Message}");
                Shared.Diagnostics.LuaInternalCatch("yaml_write_file failed with exception: " + ex);
            }
        }
    }
}
