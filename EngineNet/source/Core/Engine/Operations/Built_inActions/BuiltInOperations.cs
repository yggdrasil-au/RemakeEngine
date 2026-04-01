using EngineNet.Core.Serialization.Toml;

namespace EngineNet.Core.Engine.operations.Built_inActions;

internal class BuiltInOperations {

    internal static bool config(
        IDictionary<string, object?> op,
        string currentGame,
        Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games
    ) {
        // Parse arguments
        var argsList = op.TryGetValue("args", out object? argsObj) && argsObj is IList<object?> list
            ? list.Select(x => x?.ToString() ?? "").ToList()
            : new List<string>();

        var opts = ConfigHelpers.ParseArgs(argsList);

        string? configPath = opts.ConfigPath;
        if (string.IsNullOrEmpty(configPath)) {
            // Try to resolve Game Root
            if (!string.IsNullOrEmpty(currentGame) && games.TryGetValue(currentGame, out var gameInfo)) {
                configPath = System.IO.Path.Combine(gameInfo.GameRoot, "config.toml");
            } else {
                // Fallback
                configPath = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, "config.toml");
            }
        }

        // Ensure absolute path
        if (!System.IO.Path.IsPathRooted(configPath)) {
            configPath = System.IO.Path.GetFullPath(configPath);
        }

        // --list functionality
        if (opts.List) {
            if (!System.IO.File.Exists(configPath)) {
                Core.UI.EngineSdk.Error($"Config file does not exist at {configPath}");
                return false;
            }
            try {
                // Parse and re-serialize to show structure (matching behavior of listing the TOML structure)
                object docObj = TomlHelpers.ParseFileToPlainObject(configPath);
                string dump = TomlHelpers.WriteDocument(docObj);
                Core.UI.EngineSdk.PrintLine($"Config file: {configPath}");
                Core.UI.EngineSdk.PrintLine(dump);
                return true;
            } catch (System.Exception ex) {
                Core.Diagnostics.Bug($"Failed to read config structure: {ex.Message}");
                return false;
            }
        }

        // Check file existence
        if (!System.IO.File.Exists(configPath)) {
            // create if missing
            Core.Diagnostics.Trace($"Config file does not exist at {configPath}, creating new.");
            System.IO.File.Create(configPath).Close();
        }

        try {
            // Read existing document
            object docObj = TomlHelpers.ParseFileToPlainObject(configPath);
            IDictionary<string, object?> doc;
            if (docObj is IDictionary<string, object?> dict) {
                doc = dict;
            } else {
                Core.Diagnostics.Trace($"Creating new config structure for {configPath}");
                doc = new Dictionary<string, object?>();
            }

            // Handle Multi-set
            if (opts.Sets.Count > 0) {
                foreach (var set in opts.Sets) {
                    ConfigHelpers.ApplyUpdate(doc, opts.Group, opts.Index, set.Key, set.Value, set.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{set.Key} = {ConfigHelpers.ConvertValue(set.Value, set.TypeHint)}";
                    Core.UI.EngineSdk.PrintLine(msg, System.ConsoleColor.Green);
                }
            } else {
                // Single set
                if (string.IsNullOrEmpty(opts.Key) || opts.Value == null) {
                    // Check if we are just lacking args but not in list mode
                    // Lua checks: if not opts.group or not opts.key then return 1
                    if (string.IsNullOrEmpty(opts.Group) || string.IsNullOrEmpty(opts.Key)) {
                        Core.UI.EngineSdk.Error("Missing --group/--key for set operation");
                        return false;
                    }
                    if (opts.Value == null) {
                        Core.UI.EngineSdk.Error("Missing --value for set operation");
                        return false;
                    }
                } else {
                    ConfigHelpers.ApplyUpdate(doc, opts.Group, opts.Index, opts.Key, opts.Value, opts.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{opts.Key} = {ConfigHelpers.ConvertValue(opts.Value, opts.TypeHint)}";
                    Core.UI.EngineSdk.PrintLine(msg, System.ConsoleColor.Green);
                }
            }

            // Write back
            TomlHelpers.WriteTomlFile(configPath, doc);
            // Core.UI.EngineSdk.PrintLine($"Updated config at {configPath}", System.ConsoleColor.Green);
            // Lua prints the specific updates. The above loops print the updates.
            return true;

        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"Failed to update config: {ex.Message}");
            return false;
        }
    }


    internal static bool format_convert(Operations.helpers.OperationArgs operationArgs) {
        Core.Diagnostics.Log("[Engine.private.cs :: Operations()]] format-convert");

        // 1. Determine tool - check both 'tool' field and '-m'/'--mode' in args
        string? tool = operationArgs.op.TryGetValue("tool", out object? ft) 
            ? ft?.ToString()?.ToLowerInvariant() : null;

    #if DEBUG
        if (operationArgs.op.TryGetValue("args", out object? argsDebugObj)) {
            Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args type = {argsDebugObj?.GetType().FullName ?? "null"}");
            if (argsDebugObj is System.Collections.IList argsDebugList) {
                Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args count = {argsDebugList.Count}");
                for (int i = 0; i < argsDebugList.Count; i++) {
                    Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args[{i}] = '{argsDebugList[i]}'");
                }
            }
        }
    #endif

        // 2. If tool not specified, try to extract from args
        if (string.IsNullOrWhiteSpace(tool) && operationArgs.op.TryGetValue("args", out object? argsObj)) {
            if (argsObj is System.Collections.IList argsList) {
                for (int i = 0; i < argsList.Count - 1; i++) {
                    string arg = argsList[i]?.ToString() ?? string.Empty;
                    if (arg == "-m" || arg == "--mode") {
                        tool = argsList[i + 1]?.ToString()?.ToLowerInvariant();
                        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: extracted tool from args: '{tool}'");
                        break;
                    }
                }
            }
        }

        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: final tool = '{tool}'");

        // 3. Prepare Execution Context
        Dictionary<string, object?> ctx = Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        List<string> args = Helpers.ResolveOperationArgs(operationArgs.op, ctx);

        // 4. Execute via Switch
        switch (tool) {
            case "ffmpeg":
            case "vgmstream":
                Core.UI.EngineSdk.PrintLine("\n>>> Built-in media conversion");
                Core.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running media conversion with args: {string.Join(' ', args)}");
                return FileHandlers.MediaConverter.Run(operationArgs.context.ToolResolver, args, operationArgs.cancellationToken);

            case "imagemagick":
                Core.UI.EngineSdk.PrintLine("\n>>> Built-in image conversion");
                Core.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running image conversion with args: {string.Join(' ', args)}");
                return FileHandlers.ImageMagickConverter.Run(operationArgs.context.ToolResolver, args, operationArgs.cancellationToken);

            case "p3d":
                Core.UI.EngineSdk.PrintLine("\n>>> Built-in p3d conversion");
                Core.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running p3d conversion with args: {string.Join(' ', args)}");
                return FileHandlers.Formats.p3d.Main.Run(args, operationArgs.cancellationToken);

            default:
                Core.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: unknown tool '{tool}'");
                Core.UI.EngineSdk.PrintLine($"ERROR: format-convert requires a valid tool. Found: '{tool ?? "(null)"}'");
                Core.UI.EngineSdk.PrintLine("Supported tools: ffmpeg, vgmstream, ImageMagick, p3d");
                Core.UI.EngineSdk.PrintLine("Specify tool with --tool parameter or -m/--mode in args.");
                return false;
        }
    }

    internal static async System.Threading.Tasks.Task<bool> DownloadTools(
        Operations.helpers.OperationArgs operationArgs
    ) {
        // Expect a 'tools_manifest' value (path), or fallback to first arg
        string? manifest = Helpers.GetFieldOrFirstArgRawValue(operationArgs.op, "tools_manifest");

        if (string.IsNullOrWhiteSpace(manifest)) {
            return false;
        }
        Dictionary<string, object?> ctx = Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        string resolvedManifest = Helpers.ResolveOperationValue(operationArgs.op, "tools_manifest", ctx, fallbackToRawValue: true)
            ?? Core.Utils.Placeholders.Resolve(manifest, ctx)?.ToString()
            ?? manifest;

        bool force = false;
        if (operationArgs.promptAnswers.TryGetValue("force download", out object? fd) && fd is bool b1) {
            force = b1;
        }
        if (operationArgs.promptAnswers.TryGetValue("force_download", out object? fd2) && fd2 is bool b2) {
            force = b2;
        }

        // execute
        ExternalTools.ToolsDownloader dl = new ExternalTools.ToolsDownloader(EngineNet.Core.Main.RootPath, "");
        await dl.ProcessAsync(resolvedManifest, force, ctx, operationArgs.cancellationToken);
        return true;
    }

    internal static bool format_extract(
        Operations.helpers.OperationArgs operationArgs
    ) {
        // Determine input file format
        string? format = operationArgs.op.TryGetValue("format", out object? ft)
            ? ft?.ToString()?.ToLowerInvariant() : null;

        Dictionary<string, object?> ctx = Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        List<string> args = Helpers.ResolveOperationArgs(operationArgs.op, ctx);

        // execute
        switch (format) {
            case "p3d": {
                // in future will be specifically for converting p3d into there core component files (meshes, textures, shaders, etc)
                Core.UI.EngineSdk.PrintLine("\n>>> Built-in P3D extraction");
                Core.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
                return FileHandlers.Formats.p3d.Main.Run(args, operationArgs.cancellationToken);
            } case "txd": {
                Core.UI.EngineSdk.PrintLine("\n>>> Built-in TXD extraction");
                Core.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
                return FileHandlers.Formats.txd.TxdExtractor.Run(args, operationArgs.cancellationToken);
            } default: {
                Core.UI.EngineSdk.PrintLine($"ERROR: format-extract does not support format '{format}'");
                Core.UI.EngineSdk.PrintLine("Supported formats: p3d, txd");
                return false;
            }
        }
    }

    internal static bool rename_folders(
        Operations.helpers.OperationArgs operationArgs
    ) {
        Dictionary<string, object?> ctx = Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        List<string> args = Helpers.ResolveOperationArgs(operationArgs.op, ctx);

        // execute
        Core.UI.EngineSdk.PrintLine("\n>>> Built-in folder rename");

        Core.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
        bool ok = FileHandlers.FolderRenamer.Run(args, operationArgs.cancellationToken);
        return ok;
    }

    internal static bool validate_files(
        Operations.helpers.OperationArgs operationArgs
    ) {
        Dictionary<string, object?> ctx = Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        string? resolvedDbPath = Helpers.ResolveOperationValue(operationArgs.op, "db", ctx);

        // create args list
        List<string> args = new List<string>();
        // if a db path was resolved and is not already in args, add it as the first arg
        if (!string.IsNullOrWhiteSpace(resolvedDbPath)) {
            args.Add(resolvedDbPath);
        }
        List<string> resolvedArgs = Helpers.ResolveOperationArgs(operationArgs.op, ctx);
        for (int i = 0; i < resolvedArgs.Count; i++) {
            string value = resolvedArgs[i];
            if (!string.IsNullOrWhiteSpace(resolvedDbPath) && args.Count == 1 && i == 0 && string.Equals(args[0], value, System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            args.Add(value);
        }
        // if less than 2 args, print message and return false
        if (args.Count < 2) {
            Core.UI.EngineSdk.PrintLine("validate-files requires a database path and base directory.");
            return false;
        }



        // execute
        Core.UI.EngineSdk.PrintLine("\n>>> Built-in file validation");
        Core.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
        bool ok = helpers.FileValidator.Run(args, operationArgs.cancellationToken);
        return ok;
    }

}
