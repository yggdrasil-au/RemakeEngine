using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.LuaModules;

/// <summary>
/// SDK module extensions for archive operations and TOML handling.
/// </summary>
internal static class LuaSdkModuleExtensions {
    internal static void AddArchiveOperations(Table sdk) {
        // Archive operations (using system's built-in capabilities)
        sdk["extract_archive"] = (System.Func<string, string, bool>)((archivePath, destDir) => {
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

        sdk["create_archive"] = (System.Func<string, string, string, bool>)((srcPath, archivePath, type) => {
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

    internal static void AddTomlHelpers(Table sdk) {
        // TOML helpers
        sdk["toml_read_file"] = (System.Func<string, DynValue>)(path => {
            try {
                // Security: Validate path is within allowed areas
                if (!LuaSecurity.IsAllowedPath(path)) {
                    Core.Utils.EngineSdk.Error($"Access denied: toml_read_file path is outside allowed areas ('{path}')");
                    return DynValue.Nil;
                }
                object obj = Helpers.TomlHelpers.ParseFileToPlainObject(path);
                return LuaUtilities.ToDynValue(GetScriptFromTable(sdk), obj);
            } catch (System.Exception ex) {
                Core.Utils.EngineSdk.Error($"TOML read failed: {ex.Message}");
                return DynValue.Nil;
            }
        });

        sdk["toml_write_file"] = (System.Action<string, DynValue>)((path, value) => {
            try {
                // Security: Validate path is within allowed areas
                if (!LuaSecurity.IsAllowedPath(path)) {
                    Core.Utils.EngineSdk.Error($"Access denied: toml_write_file path is outside allowed areas ('{path}')");
                    return;
                }
                object? obj = LuaUtilities.FromDynValue(value);
                Helpers.TomlHelpers.WriteTomlFile(path, obj);
            } catch (System.Exception ex) {
                Core.Utils.EngineSdk.Error($"TOML write failed: {ex.Message}");
            }
        });
    }

    private static Script GetScriptFromTable(Table table) {
        // This is a workaround - in practice we'll pass the script reference
        return table.OwnerScript;
    }
}