using System;
using System.IO;
using System.IO.Compression;
using MoonSharp.Interpreter;
using EngineNet.Core.ScriptEngines.Helpers;
using EngineNet.Core.Sys;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// SDK module extensions for archive operations and TOML handling.
/// </summary>
internal static class LuaSdkModuleExtensions {
    public static void AddArchiveOperations(Table sdk) {
        // Archive operations (using system's built-in capabilities)
        sdk["extract_archive"] = (Func<String, String, Boolean>)((archivePath, destDir) => {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!LuaSecurity.IsAllowedPath(archivePath) || !LuaSecurity.IsAllowedPath(destDir)) {
                    EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {archivePath} -> {destDir}");
                    return false;
                }
                
                String ext = Path.GetExtension(archivePath).ToLowerInvariant();
                if (ext == ".zip") {
                    ZipFile.ExtractToDirectory(archivePath, destDir);
                    return true;
                }
                // For other formats, suggest using approved tools
                EngineSdk.Error($"Unsupported archive format '{ext}'. Use 7z tool from Tools.json for other formats.");
                return false;
            } catch (Exception ex) {
                EngineSdk.Error($"Archive extraction failed: {ex.Message}");
                return false;
            }
        });
        
        sdk["create_archive"] = (Func<String, String, String, Boolean>)((srcPath, archivePath, type) => {
            try {
                // Security: Validate paths are within allowed workspace areas
                if (!LuaSecurity.IsAllowedPath(srcPath) || !LuaSecurity.IsAllowedPath(archivePath)) {
                    EngineSdk.Error($"Access denied: Archive operations restricted to workspace areas. Attempted: {srcPath} -> {archivePath}");
                    return false;
                }
                
                if (type.Equals("zip", StringComparison.OrdinalIgnoreCase)) {
                    if (Directory.Exists(srcPath)) {
                        ZipFile.CreateFromDirectory(srcPath, archivePath);
                    } else if (File.Exists(srcPath)) {
                        // Create zip with single file
                        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                        var entry = archive.CreateEntry(Path.GetFileName(srcPath));
                        using var entryStream = entry.Open();
                        using var fileStream = File.OpenRead(srcPath);
                        fileStream.CopyTo(entryStream);
                    } else {
                        return false;
                    }
                    return true;
                }
                // For other formats, suggest using approved tools
                EngineSdk.Error($"Unsupported archive type '{type}'. Use 7z tool from Tools.json for other formats.");
                return false;
            } catch (Exception ex) {
                EngineSdk.Error($"Archive creation failed: {ex.Message}");
                return false;
            }
        });
    }

    public static void AddTomlHelpers(Table sdk) {
        // TOML helpers
        sdk["toml_read_file"] = (Func<String, DynValue>)(path => {
            try {
                Object obj = TomlHelpers.ParseFileToPlainObject(path);
                return LuaUtilities.ToDynValue(GetScriptFromTable(sdk), obj);
            } catch (Exception ex) {
                EngineSdk.Error($"TOML read failed: {ex.Message}");
                return DynValue.Nil;
            }
        });
        
        sdk["toml_write_file"] = (Action<String, DynValue>)((path, value) => {
            try {
                Object? obj = LuaUtilities.FromDynValue(value);
                TomlHelpers.WriteTomlFile(path, obj);
            } catch (Exception ex) {
                EngineSdk.Error($"TOML write failed: {ex.Message}");
            }
        });
    }

    private static Script GetScriptFromTable(Table table) {
        // This is a workaround - in practice we'll pass the script reference
        return table.OwnerScript;
    }
}