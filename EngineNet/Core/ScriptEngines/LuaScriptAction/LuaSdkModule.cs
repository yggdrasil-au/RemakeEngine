using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MoonSharp.Interpreter;
using EngineNet.Core.ScriptEngines.Helpers;
using EngineNet.Core.Sys;
using EngineNet.Tools;
using System.Runtime.InteropServices;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// SDK module providing file operations, archive handling, and system utilities for Lua scripts.
/// </summary>
internal static class LuaSdkModule {
    public static Table CreateSdkModule(Script lua, IToolResolver tools) {
        Table sdk = new Table(lua);
        
        // Color/colour print functionality
        AddColorPrintFunctions(sdk, lua);
        
        // Configuration helpers
        AddConfigurationHelpers(sdk);
        
        // File system operations
        AddFileSystemOperations(sdk, lua);
        
        // Archive operations
        LuaSdkModuleExtensions.AddArchiveOperations(sdk);
        
        // TOML helpers
        LuaSdkModuleExtensions.AddTomlHelpers(sdk);
        
        // Process execution
        LuaProcessExecution.AddProcessExecution(sdk, lua, tools);

        // Expose CPU count as a numeric value for Lua scripts to choose sensible defaults
        try {
            // Environment.ProcessorCount is safe and fast
            sdk.Set("cpu_count", DynValue.NewNumber(Environment.ProcessorCount));
        } catch {
            // Ignore if DynValue isn't available for any reason; scripts will fallback to 1
        }

        return sdk;
    }

    private static void AddColorPrintFunctions(Table sdk, Script lua) {
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        CallbackFunction colorPrintFunc = new CallbackFunction((ctx, args) => {
            String? color = null;
            String message = String.Empty;
            Boolean newline = true;
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
            EngineSdk.Print(message, color, newline);
            return DynValue.Nil;
        });
        sdk["color_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["colour_print"] = DynValue.NewCallback(colorPrintFunc);
    }

    private static void AddConfigurationHelpers(Table sdk) {
        sdk["ensure_project_config"] = (Func<String, String>)((root) => ConfigHelpers.EnsureProjectConfig(root));
        sdk["validate_source_dir"] = (Func<String, Boolean>)((dir) => {
            try {
                ConfigHelpers.ValidateSourceDir(dir);
                return true;
            } catch { 
                return false; 
            }
        });
    }

    private static void AddFileSystemOperations(Table sdk, Script lua) {
        sdk["copy_dir"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch { 
                return false; 
            }
        });

        sdk["move_dir"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch { 
                return false; 
            }
        });

        sdk["find_subdir"] = (Func<String, String, String?>)((baseDir, name) => ConfigHelpers.FindSubdir(baseDir, name));
        
        sdk["has_all_subdirs"] = (Func<String, Table, Boolean>)((baseDir, names) => {
            try {
                List<String> list = LuaUtilities.TableToStringList(names);
                return ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch { 
                return false; 
            }
        });

        sdk["ensure_dir"] = (Func<String, Boolean>)(path => {
            try {
                Directory.CreateDirectory(path);
                return true;
            } catch { 
                return false; 
            }
        });

        sdk["path_exists"] = (Func<String, Boolean>)LuaFileSystemUtils.PathExists;
        sdk["lexists"] = (Func<String, Boolean>)LuaFileSystemUtils.PathExistsIncludingLinks;
        sdk["is_dir"] = (Func<String, Boolean>)Directory.Exists;
        sdk["is_file"] = (Func<String, Boolean>)File.Exists;

        sdk["remove_dir"] = (Func<String, Boolean>)(path => {
            try {
                if (Directory.Exists(path)) {
                    Directory.Delete(path, true);
                }
                return true;
            } catch { 
                return false; 
            }
        });

        sdk["remove_file"] = (Func<String, Boolean>)(path => {
            try {
                if (LuaFileSystemUtils.IsSymlink(path) || File.Exists(path)) {
                    File.Delete(path);
                }
                return true;
            } catch { 
                return false; 
            }
        });

        sdk["copy_file"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(src) || !LuaSecurity.EnsurePathAllowedWithPrompt(dst)) {
                    return false;
                }
                
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                File.Copy(src, dst, ow);
                return true;
            } catch { 
                return false; 
            }
        });

        sdk["rename_file"] = (Func<String, String, Boolean>)((oldPath, newPath) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(oldPath) || !LuaSecurity.EnsurePathAllowedWithPrompt(newPath)) {
                    return false;
                }
                
                if (File.Exists(oldPath)) {
                    File.Move(oldPath, newPath);
                    return true;
                } else if (Directory.Exists(oldPath)) {
                    Directory.Move(oldPath, newPath);
                    return true;
                }
                return false;
            } catch { 
                return false; 
            }
        });

        sdk["create_symlink"] = (Func<String, String, Boolean, Boolean>)LuaFileSystemUtils.CreateSymlink;
        sdk["is_symlink"] = (Func<String, Boolean>)LuaFileSystemUtils.IsSymlink;
        sdk["realpath"] = (Func<String, String?>)LuaFileSystemUtils.RealPath;
        sdk["readlink"] = (Func<String, String?>)LuaFileSystemUtils.ReadLink;

        // Create hardlink (files only)
        sdk["create_hardlink"] = (Func<String, String, Boolean>)((src, dst) => {
            try {
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(src) || !LuaSecurity.EnsurePathAllowedWithPrompt(dst)) {
                    return false;
                }
                String destFull = Path.GetFullPath(dst);
                String srcFull = Path.GetFullPath(src);
                String? parent = Path.GetDirectoryName(destFull);
                if (!String.IsNullOrEmpty(parent)) {
                    Directory.CreateDirectory(parent);
                }
                if (!File.Exists(srcFull)) {
                    return false;
                }
                // Remove existing file or link
                try {
                    if (LuaFileSystemUtils.IsSymlink(destFull) || File.Exists(destFull)) {
                        File.Delete(destFull);
                    }
                } catch { /* ignore */ }
                try {
                    HardLink.Create(srcFull, destFull);
                    return true;
                } catch {
                    return false;
                }
            } catch {
                return false;
            }
        });

        // SHA1 hash of a file (lowercase hex)
        sdk["sha1_file"] = (Func<String, String?>)(path => {
            try {
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                    return null;
                }
                using FileStream fs = File.OpenRead(path);
                Byte[] hash = SHA1.HashData(fs);
                return Convert.ToHexString(hash).ToLowerInvariant();
            } catch {
                return null;
            }
        });

        // Additional file system operations for compatibility with existing scripts
        sdk["currentdir"] = (Func<String>)(() => Directory.GetCurrentDirectory());
        
        sdk["mkdir"] = (Func<String, Boolean>)(path => {
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return false;
            }
            try {
                Directory.CreateDirectory(path);
                return true;
            } catch { 
                return false; 
            }
        });

        sdk["attributes"] = (Func<String, DynValue>)(path => {
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return DynValue.Nil;
            }
            try {
                if (Directory.Exists(path)) {
                    Table attrs = new Table(lua);
                    attrs["mode"] = "directory";
                    DirectoryInfo dirInfo = new DirectoryInfo(path);
                    attrs["modification"] = (Double)new DateTimeOffset(dirInfo.LastWriteTime).ToUnixTimeSeconds();
                    return DynValue.NewTable(attrs);
                }
                if (File.Exists(path)) {
                    Table attrs = new Table(lua);
                    attrs["mode"] = "file";
                    FileInfo fileInfo = new FileInfo(path);
                    attrs["size"] = fileInfo.Length;
                    attrs["modification"] = (Double)new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds();
                    return DynValue.NewTable(attrs);
                }
                return DynValue.Nil;
            } catch {
                return DynValue.Nil;
            }
        });

        sdk["md5"] = (Func<String, String>)(text => {
            try {
                Byte[] data = MD5.HashData(Encoding.UTF8.GetBytes(text ?? String.Empty));
                return Convert.ToHexString(data).ToLowerInvariant();
            } catch {
                return String.Empty;
            }
        });

        sdk["sleep"] = (Action<Double>)(seconds => {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0) {
                return;
            }

            try {
                Thread.Sleep(TimeSpan.FromSeconds(seconds));
            } catch { }
        });
    }
}