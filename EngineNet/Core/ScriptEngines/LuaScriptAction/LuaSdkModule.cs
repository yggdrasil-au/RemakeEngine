using MoonSharp.Interpreter;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// SDK module providing file operations, archive handling, and system utilities for Lua scripts.
/// </summary>
internal static class LuaSdkModule {
    public static Table CreateSdkModule(Script lua, Tools.IToolResolver tools) {
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
            // System.Environment.ProcessorCount is safe and fast
            sdk.Set("cpu_count", DynValue.NewNumber(System.Environment.ProcessorCount));
        } catch {
            // Ignore if DynValue isn't available for any reason; scripts will fallback to 1
        }

        return sdk;
    }

    private static void AddColorPrintFunctions(Table sdk, Script lua) {
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        CallbackFunction colorPrintFunc = new CallbackFunction((ctx, args) => {
            string? color = null;
            string message = string.Empty;
            bool newline = true;
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
            Helpers.EngineSdk.Print(message, color, newline);
            return DynValue.Nil;
        });
        sdk["color_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["colour_print"] = DynValue.NewCallback(colorPrintFunc);
    }

    private static void AddConfigurationHelpers(Table sdk) {
        sdk["ensure_project_config"] = (System.Func<string, string>)((root) => Helpers.ConfigHelpers.EnsureProjectConfig(root));
        sdk["validate_source_dir"] = (System.Func<string, bool>)((dir) => {
            try {
                Helpers.ConfigHelpers.ValidateSourceDir(dir);
                return true;
            } catch {
                return false;
            }
        });
    }

    private static void AddFileSystemOperations(Table sdk, Script lua) {
        sdk["copy_dir"] = (System.Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                Helpers.ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        });

        sdk["move_dir"] = (System.Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                Helpers.ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        });

        sdk["find_subdir"] = (System.Func<string, string, string?>)((baseDir, name) => Helpers.ConfigHelpers.FindSubdir(baseDir, name));

        sdk["has_all_subdirs"] = (System.Func<string, Table, bool>)((baseDir, names) => {
            try {
                List<string> list = LuaUtilities.TableToStringList(names);
                return Helpers.ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch {
                return false;
            }
        });

        sdk["ensure_dir"] = (System.Func<string, bool>)(path => {
            try {
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch {
                return false;
            }
        });

        sdk["path_exists"] = (System.Func<string, bool>)LuaFileSystemUtils.PathExists;
        sdk["lexists"] = (System.Func<string, bool>)LuaFileSystemUtils.PathExistsIncludingLinks;
        sdk["is_dir"] = (System.Func<string, bool>)System.IO.Directory.Exists;
        sdk["is_file"] = (System.Func<string, bool>)System.IO.File.Exists;

        sdk["remove_dir"] = (System.Func<string, bool>)(path => {
            try {
                if (System.IO.Directory.Exists(path)) {
                    System.IO.Directory.Delete(path, true);
                }
                return true;
            } catch {
                return false;
            }
        });

        sdk["remove_file"] = (System.Func<string, bool>)(path => {
            try {
                if (LuaFileSystemUtils.IsSymlink(path) || System.IO.File.Exists(path)) {
                    System.IO.File.Delete(path);
                }
                return true;
            } catch {
                return false;
            }
        });

        sdk["copy_file"] = (System.Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(src) || !LuaSecurity.EnsurePathAllowedWithPrompt(dst)) {
                    return false;
                }

                bool ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                System.IO.File.Copy(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        });

        sdk["rename_file"] = (System.Func<string, string, bool>)((oldPath, newPath) => {
            try {
                // Security: Validate or prompt-approve paths
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(oldPath) || !LuaSecurity.EnsurePathAllowedWithPrompt(newPath)) {
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
            } catch {
                return false;
            }
        });

        sdk["create_symlink"] = (System.Func<string, string, bool, bool>)LuaFileSystemUtils.CreateSymlink;
        sdk["is_symlink"] = (System.Func<string, bool>)LuaFileSystemUtils.IsSymlink;
        sdk["realpath"] = (System.Func<string, string?>)LuaFileSystemUtils.RealPath;
        sdk["readlink"] = (System.Func<string, string?>)LuaFileSystemUtils.ReadLink;

        // Create hardlink (files only)
        sdk["create_hardlink"] = (System.Func<string, string, bool>)((src, dst) => {
            try {
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(src) || !LuaSecurity.EnsurePathAllowedWithPrompt(dst)) {
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
                    if (LuaFileSystemUtils.IsSymlink(destFull) || System.IO.File.Exists(destFull)) {
                        System.IO.File.Delete(destFull);
                    }
                } catch { /* ignore */ }
                try {
                    Helpers.HardLink.Create(srcFull, destFull);
                    return true;
                } catch {
                    return false;
                }
            } catch {
                return false;
            }
        });

        // SHA1 hash of a file (lowercase hex)
        sdk["sha1_file"] = (System.Func<string, string?>)(path => {
            try {
                if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                    return null;
                }
                using System.IO.FileStream fs = System.IO.File.OpenRead(path);
                byte[] hash = System.Security.Cryptography.SHA1.HashData(fs);
                return System.Convert.ToHexString(hash).ToLowerInvariant();
            } catch {
                return null;
            }
        });

        // Additional file system operations for compatibility with existing scripts
        sdk["currentdir"] = (System.Func<string>)(() => System.IO.Directory.GetCurrentDirectory());

        sdk["mkdir"] = (System.Func<string, bool>)(path => {
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return false;
            }
            try {
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch {
                return false;
            }
        });

        sdk["attributes"] = (System.Func<string, DynValue>)(path => {
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return DynValue.Nil;
            }
            try {
                if (System.IO.Directory.Exists(path)) {
                    Table attrs = new Table(lua);
                    attrs["mode"] = "directory";
                    System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(path);
                    attrs["modification"] = (double)new System.DateTimeOffset(dirInfo.LastWriteTime).ToUnixTimeSeconds();
                    return DynValue.NewTable(attrs);
                }
                if (System.IO.File.Exists(path)) {
                    Table attrs = new Table(lua);
                    attrs["mode"] = "file";
                    System.IO.FileInfo fileInfo = new System.IO.FileInfo(path);
                    attrs["size"] = fileInfo.Length;
                    attrs["modification"] = (double) new System.DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds();
                    return DynValue.NewTable(attrs);
                }
                return DynValue.Nil;
            } catch {
                return DynValue.Nil;
            }
        });

        sdk["md5"] = (System.Func<string, string>)(text => {
            try {
                byte[] data = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
                return System.Convert.ToHexString(data).ToLowerInvariant();
            } catch {
                return string.Empty;
            }
        });

        sdk["sleep"] = (System.Action<double>)(seconds => {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) {
                return;
            }

            try {
                System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(seconds));
            }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"Error .....'");
#endif
        }
        });
    }
}