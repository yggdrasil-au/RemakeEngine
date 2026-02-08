
namespace EngineNet.ScriptEngines.Global.SdkModule;

public static class Helpers {

    //AddConfigurationHelpers
    public static class AddConfigurationHelpers {
        public static bool Validate_Source_Dir(string dir) {
        try {
            // Security: Validate path is within allowed areas
            if (!Security.IsAllowedPath(dir)) {
                Core.UI.EngineSdk.Error($"Access denied: validate_source_dir path is outside allowed areas ('{dir}')");
                return false;
            }
            ScriptEngines.Helpers.ConfigHelpers.ValidateSourceDir(dir);
            return true;
        } catch (Exception ex) {
            Core.Diagnostics.luaInternalCatch("validate_source_dir failed with exception: " + ex);
            return false;
        }
    }
    }

    //AddFileSystemOperations
    public static class AddFileSystemOperations {
        public static Dictionary<string, object>? FileAttributes(string path) {
            if (!Security.EnsurePathAllowedWithPrompt(path)) {
                return null;
            }

            try {
                var attrs = new Dictionary<string, object>();

                if (System.IO.Directory.Exists(path)) {
                    var dirInfo = new System.IO.DirectoryInfo(path);
                    attrs["mode"] = "directory";
                    attrs["modification"] = (double)new System.DateTimeOffset(dirInfo.LastWriteTime).ToUnixTimeSeconds();
                    return attrs;
                }

                if (System.IO.File.Exists(path)) {
                    var fileInfo = new System.IO.FileInfo(path);
                    attrs["mode"] = "file";
                    attrs["size"] = fileInfo.Length;
                    attrs["modification"] = (double)new System.DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds();
                    return attrs;
                }

                return null;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("FileAttributes helper failed with exception: " + ex);
                return null;
            }
        }

        public static List<string>? List_Dir(string path) {
            // 1. Security check
            if (!Security.EnsurePathAllowedWithPrompt(path)) {
                return null;
            }

            try {
                // 2. Logic: Get all files and directories
                // This returns the full paths initially
                string[] entries = System.IO.Directory.GetFileSystemEntries(path);

                // 3. Transformation: Convert full paths to just names
                List<string> names = new List<string>();
                foreach (string entry in entries) {
                    names.Add(System.IO.Path.GetFileName(entry));
                }

                return names;
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("list_dir helper failed with exception: " + ex);
                return null;
            }
        }

    }

    // add hashing functions like sha1_file and md5
    public static class AddHashMethods {
        
        public static dynamic? sha1_file(string path) {
            try {
                if (!Security.EnsurePathAllowedWithPrompt(path)) {
                    return null;
                }
                using System.IO.FileStream fs = System.IO.File.OpenRead(path);
                byte[] hash = System.Security.Cryptography.SHA1.HashData(fs);
                return System.Convert.ToHexString(hash).ToLowerInvariant();
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("sha1_file failed with exception: " + ex);
                return null;
            }
        }

        public static string Md5Hash(string text) {
            try {
                byte[] data = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
                return System.Convert.ToHexString(data).ToLowerInvariant();
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("md5 failed with exception: " + ex);
                return string.Empty;
            }
        }
    }

    public static void Sleep(double seconds) {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) {
            return;
        }

        try {
            System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(seconds));
        }  catch (Exception ex) {
            Core.Diagnostics.luaInternalCatch("sleep failed with exception: " + ex);
        }
    }


    //AddArchiveOperations
    public static class AddArchiveOperations {
        public static bool Extract_Archive(string archivePath, string destDir) {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!Security.IsAllowedPath(archivePath) || !Security.IsAllowedPath(destDir)) {
                    Core.UI.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {archivePath} -> {destDir}");
                    return false;
                }

                string ext = System.IO.Path.GetExtension(archivePath).ToLowerInvariant();
                if (ext == ".zip") {
                    System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destDir);
                    return true;
                }
                // For other formats, suggest using approved tools
                Core.UI.EngineSdk.Error($"Unsupported archive format '{ext}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Core.UI.EngineSdk.Error($"Archive extraction failed: {ex.Message}"); // output directly to UI, consider returning error to lua instead
                Core.Diagnostics.luaInternalCatch("extract_archive failed with exception: " + ex);
                return false;
            }
        }

        public static bool Create_Archive(string srcPath, string archivePath, string type) {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!Security.IsAllowedPath(srcPath) || !Security.IsAllowedPath(archivePath)) {
                    Core.UI.EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {srcPath} -> {archivePath}");
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
                Core.UI.EngineSdk.Error($"Unsupported archive type '{type}'. Use 7z tool from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" for other formats.");
                return false;
            } catch (System.Exception ex) {
                Core.UI.EngineSdk.Error($"Archive creation failed: {ex.Message}");
                Core.Diagnostics.luaInternalCatch("create_archive failed with exception: " + ex);
                return false;
            }
        }
    }

    //AddTomlHelpers

    public static class AddTomlHelpers {
        public static object? Toml_Read_File(string path) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path)) {
                    Core.UI.EngineSdk.Error($"Access denied: toml_read_file path is outside allowed areas ('{path}')");
                    return null; //DynValue.Nil;
                }
                object? obj = Core.Serialization.Toml.TomlHelpers.ParseFileToPlainObject(path);
                return obj;
            } catch (System.Exception ex) {
                Core.UI.EngineSdk.Error($"TOML read failed: {ex.Message}");
                Core.Diagnostics.luaInternalCatch("toml_read_file failed with exception: " + ex);
                return null; //DynValue.Nil;
            }
        }

        public static void Toml_Write_File(string path, object? obj) {
            try {
                // Security: Validate path is within allowed areas
                if (!Security.IsAllowedPath(path)) {
                    Core.UI.EngineSdk.Error($"Access denied: toml_write_file path is outside allowed areas ('{path}')");
                    return;
                }
                Core.Serialization.Toml.TomlHelpers.WriteTomlFile(path, obj);
            } catch (System.Exception ex) {
                Core.UI.EngineSdk.Error($"TOML write failed: {ex.Message}");
                Core.Diagnostics.luaInternalCatch("toml_write_file failed with exception: " + ex);
            }
        }

    }
}
