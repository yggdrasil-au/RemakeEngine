using EngineNet.Shared.Serialization.Toml;

namespace EngineNet.Core.Operations.Built_inActions;

internal class BuiltInOperations {

    internal static bool config(
        IDictionary<string, object?> op,
        string currentGame,
        Core.Data.GameModules games
    ) {
        // Parse arguments
        var argsList = op.TryGetValue("args", out object? argsObj) && argsObj is IList<object?> list
            ? list.Select(x => x?.ToString() ?? "").ToList()
            : new List<string>();

        var opts = Utils.ConfigHelpers.ParseArgs(argsList);

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
                Shared.IO.UI.EngineSdk.Error($"Config file does not exist at {configPath}");
                return false;
            }
            try {
                // Parse and re-serialize to show structure (matching behavior of listing the TOML structure)
                object docObj = TomlHelpers.ParseFileToPlainObject(configPath);
                string dump = TomlHelpers.WriteDocument(docObj);
                Shared.IO.UI.EngineSdk.PrintLine($"Config file: {configPath}");
                Shared.IO.UI.EngineSdk.PrintLine(dump);
                return true;
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"Failed to read config structure: {ex.Message}");
                return false;
            }
        }

        // Check file existence
        if (!System.IO.File.Exists(configPath)) {
            // create if missing
            Shared.IO.Diagnostics.Trace($"Config file does not exist at {configPath}, creating new.");
            System.IO.File.Create(configPath).Close();
        }

        try {
            // Read existing document
            object docObj = TomlHelpers.ParseFileToPlainObject(configPath);
            IDictionary<string, object?> doc;
            if (docObj is IDictionary<string, object?> dict) {
                doc = dict;
            } else {
                Shared.IO.Diagnostics.Trace($"Creating new config structure for {configPath}");
                doc = new Dictionary<string, object?>();
            }

            // Handle Multi-set
            if (opts.Sets.Count > 0) {
                foreach (var set in opts.Sets) {
                    Utils.ConfigHelpers.ApplyUpdate(doc, opts.Group, opts.Index, set.Key, set.Value, set.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{set.Key} = {Utils.ConfigHelpers.ConvertValue(set.Value, set.TypeHint)}";
                    Shared.IO.UI.EngineSdk.PrintLine(msg, System.ConsoleColor.Green);
                }
            } else {
                // Single set
                if (string.IsNullOrEmpty(opts.Key) || opts.Value == null) {
                    // Check if we are just lacking args but not in list mode
                    // Lua checks: if not opts.group or not opts.key then return 1
                    if (string.IsNullOrEmpty(opts.Group) || string.IsNullOrEmpty(opts.Key)) {
                        Shared.IO.UI.EngineSdk.Error("Missing --group/--key for set operation");
                        return false;
                    }
                    if (opts.Value == null) {
                        Shared.IO.UI.EngineSdk.Error("Missing --value for set operation");
                        return false;
                    }
                } else {
                    Utils.ConfigHelpers.ApplyUpdate(doc, opts.Group, opts.Index, opts.Key, opts.Value, opts.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{opts.Key} = {Utils.ConfigHelpers.ConvertValue(opts.Value, opts.TypeHint)}";
                    Shared.IO.UI.EngineSdk.PrintLine(msg, System.ConsoleColor.Green);
                }
            }

            // Write back
            TomlHelpers.WriteTomlFile(configPath, doc);
            // Shared.IO.UI.EngineSdk.PrintLine($"Updated config at {configPath}", System.ConsoleColor.Green);
            // Lua prints the specific updates. The above loops print the updates.
            return true;

        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Failed to update config: {ex.Message}");
            return false;
        }
    }


    internal static bool format_convert(Operations.helpers.OperationArgs operationArgs) {
        Shared.IO.Diagnostics.Log("[Engine.private.cs :: Operations()]] format-convert");

        // 1. Determine tool - check both 'tool' field and '-m'/'--mode' in args
        string? tool = operationArgs.op.TryGetValue("tool", out object? ft) 
            ? ft?.ToString()?.ToLowerInvariant() : null;

    #if DEBUG
        if (operationArgs.op.TryGetValue("args", out object? argsDebugObj)) {
            Shared.IO.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args type = {argsDebugObj?.GetType().FullName ?? "null"}");
            if (argsDebugObj is System.Collections.IList argsDebugList) {
                Shared.IO.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args count = {argsDebugList.Count}");
                for (int i = 0; i < argsDebugList.Count; i++) {
                    Shared.IO.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args[{i}] = '{argsDebugList[i]}'");
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
                        Shared.IO.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: extracted tool from args: '{tool}'");
                        break;
                    }
                }
            }
        }

        Shared.IO.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: final tool = '{tool}'");

        // 3. Prepare Execution Context
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        List<string> args = Utils.Helpers.ResolveOperationArgs(operationArgs.op, ctx);

        // 4. Execute via Switch
        switch (tool) {
            case "ffmpeg":
            case "vgmstream":
                Shared.IO.UI.EngineSdk.PrintLine("\n>>> Built-in media conversion");
                Shared.IO.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running media conversion with args: {string.Join(' ', args)}");
                return Core.Media.AvTools.Run(operationArgs.context.ToolResolver, args, operationArgs.cancellationToken);

            case "imagemagick":
                Shared.IO.UI.EngineSdk.PrintLine("\n>>> Built-in image conversion");
                Shared.IO.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running image conversion with args: {string.Join(' ', args)}");
                return Core.Media.ImageMagickConverter.Run(operationArgs.context.ToolResolver, args, operationArgs.cancellationToken);

            case "p3d":
                Shared.IO.UI.EngineSdk.PrintLine("\n>>> Built-in p3d conversion");
                Shared.IO.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running p3d conversion with args: {string.Join(' ', args)}");
                return EngineNet.GameFormats.p3d.P3dExtractor.Run(args, operationArgs.cancellationToken);

            default:
                Shared.IO.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: unknown tool '{tool}'");
                Shared.IO.UI.EngineSdk.PrintLine($"ERROR: format-convert requires a valid tool. Found: '{tool ?? "(null)"}'");
                Shared.IO.UI.EngineSdk.PrintLine("Supported tools: ffmpeg, vgmstream, ImageMagick, p3d");
                Shared.IO.UI.EngineSdk.PrintLine("Specify tool with --tool parameter or -m/--mode in args.");
                return false;
        }
    }

    internal static async System.Threading.Tasks.Task<bool> DownloadTools(
        Operations.helpers.OperationArgs operationArgs
    ) {
        // Expect a 'tools_manifest' value (path), or fallback to first arg
        string? manifest = Utils.Helpers.GetFieldOrFirstArgRawValue(operationArgs.op, "tools_manifest");

        if (string.IsNullOrWhiteSpace(manifest)) {
            return false;
        }
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        string resolvedManifest = Utils.Helpers.ResolveOperationValue(operationArgs.op, "tools_manifest", ctx, fallbackToRawValue: true)
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
        await ExternalTools.ToolsDownloader.ProcessAsync(resolvedManifest, EngineNet.Core.Main.RootPath,force, ctx, operationArgs.cancellationToken);
        return true;
    }

    internal static bool format_extract(
        Operations.helpers.OperationArgs operationArgs
    ) {
        // Determine input file format
        string? format = operationArgs.op.TryGetValue("format", out object? ft)
            ? ft?.ToString()?.ToLowerInvariant() : null;

        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        List<string> args = Utils.Helpers.ResolveOperationArgs(operationArgs.op, ctx);

        // execute
        switch (format) {
            case "p3d": {
                // in future will be specifically for converting p3d into there core component files (meshes, textures, shaders, etc)
                Shared.IO.UI.EngineSdk.PrintLine("\n>>> Built-in P3D extraction");
                Shared.IO.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
                return EngineNet.GameFormats.p3d.P3dExtractor.Run(args, operationArgs.cancellationToken);
            } case "txd": {
                Shared.IO.UI.EngineSdk.PrintLine("\n>>> Built-in TXD extraction");
                Shared.IO.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
                return EngineNet.GameFormats.txd.TxdExtractor.Run(args, operationArgs.cancellationToken);
            } default: {
                Shared.IO.UI.EngineSdk.PrintLine($"ERROR: format-extract does not support format '{format}'");
                Shared.IO.UI.EngineSdk.PrintLine("Supported formats: p3d, txd");
                return false;
            }
        }
    }

    internal static bool rename_folders(
        Operations.helpers.OperationArgs operationArgs
    ) {
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        List<string> args = Utils.Helpers.ResolveOperationArgs(operationArgs.op, ctx);

        // execute
        Shared.IO.UI.EngineSdk.PrintLine("\n>>> Built-in folder rename");

        Shared.IO.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
        bool ok = Utils.FolderRenamer.Run(args, operationArgs.cancellationToken);
        return ok;
    }

    internal static bool validate_files(
        Operations.helpers.OperationArgs operationArgs
    ) {
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(operationArgs.context, operationArgs.currentGame, operationArgs.games);
        string? resolvedDbPath = Utils.Helpers.ResolveOperationValue(operationArgs.op, "db", ctx);

        // create args list
        List<string> args = new List<string>();
        // if a db path was resolved and is not already in args, add it as the first arg
        if (!string.IsNullOrWhiteSpace(resolvedDbPath)) {
            args.Add(resolvedDbPath);
        }
        List<string> resolvedArgs = Utils.Helpers.ResolveOperationArgs(operationArgs.op, ctx);
        for (int i = 0; i < resolvedArgs.Count; i++) {
            string value = resolvedArgs[i];
            if (!string.IsNullOrWhiteSpace(resolvedDbPath) && args.Count == 1 && i == 0 && string.Equals(args[0], value, System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            args.Add(value);
        }
        // if less than 2 args, print message and return false
        if (args.Count < 2) {
            Shared.IO.UI.EngineSdk.PrintLine("validate-files requires a database path and base directory.");
            return false;
        }



        // execute
        Shared.IO.UI.EngineSdk.PrintLine("\n>>> Built-in file validation");
        Shared.IO.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
        bool ok = Utils.FileValidator.Run(args, operationArgs.cancellationToken);
        return ok;
    }

}
