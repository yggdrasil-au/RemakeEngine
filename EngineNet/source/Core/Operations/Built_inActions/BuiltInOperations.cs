using EngineNet.Shared.Serialization.Toml;

namespace EngineNet.Core.Operations.Built_inActions;

using Data;
using Utils;

internal class BuiltInOperations {

    internal static bool config(
        IDictionary<string, object?> op,
        string currentGame,
        Core.Data.GameModules games
    ) {
        // Parse arguments
        List<string> argsList = op.TryGetValue(key: "args", out object? argsObj) && argsObj is IList<object?> list
            ? list.Select(selector: x => x?.ToString() ?? "").ToList()
            : new List<string>();

        ConfigHelpers.ConfigOptions opts = Utils.ConfigHelpers.ParseArgs(args: argsList);

        string? configPath = opts.ConfigPath;
        if (string.IsNullOrEmpty(configPath)) {
            // Try to resolve Game Root
            if (!string.IsNullOrEmpty(currentGame) && games.TryGetValue(key: currentGame, out GameModuleInfo? gameInfo)) {
                configPath = System.IO.Path.Combine(path1: gameInfo.GameRoot, path2: "config.toml");
            } else {
                // Fallback
                configPath = System.IO.Path.Combine(path1: EngineNet.Shared.State.RootPath, path2: "config.toml");
            }
        }

        // Ensure absolute path
        if (!System.IO.Path.IsPathRooted(path: configPath)) {
            configPath = System.IO.Path.GetFullPath(path: configPath);
        }

        // --list functionality
        if (opts.List) {
            if (!System.IO.File.Exists(path: configPath)) {
                IO.Error($"Config file does not exist at {configPath}");
                return false;
            }
            try {
                // Parse and re-serialize to show structure (matching behavior of listing the TOML structure)
                object docObj = TomlHelpers.ParseFileToPlainObject(path: configPath);
                string dump = TomlHelpers.WriteDocument(data: docObj);
                IO.writeLine($"Config file: {configPath}");
                IO.writeLine(dump);
                return true;
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"Failed to read config structure: {ex.Message}");
                return false;
            }
        }

        // Check file existence
        if (!System.IO.File.Exists(path: configPath)) {
            // create if missing
            Shared.IO.Diagnostics.Trace($"Config file does not exist at {configPath}, creating new.");
            System.IO.File.Create(path: configPath).Close();
        }

        try {
            // Read existing document
            object docObj = TomlHelpers.ParseFileToPlainObject(path: configPath);
            IDictionary<string, object?> doc;
            if (docObj is IDictionary<string, object?> dict) {
                doc = dict;
            } else {
                Shared.IO.Diagnostics.Trace($"Creating new config structure for {configPath}");
                doc = new Dictionary<string, object?>();
            }

            // Handle Multi-set
            if (opts.Sets.Count > 0) {
                foreach (ConfigHelpers.SetToken set in opts.Sets) {
                    Utils.ConfigHelpers.ApplyUpdate(doc: doc, group: opts.Group, index: opts.Index, key: set.Key, set.Value, typeHint: set.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{set.Key} = {Utils.ConfigHelpers.ConvertValue(raw: set.Value, hint: set.TypeHint)}";
                    IO.writeLine(msg, color: System.ConsoleColor.Green);
                }
            } else {
                // Single set
                if (string.IsNullOrEmpty(opts.Key) || opts.Value == null) {
                    // Check if we are just lacking args but not in list mode
                    // Lua checks: if not opts.group or not opts.key then return 1
                    if (string.IsNullOrEmpty(opts.Group) || string.IsNullOrEmpty(opts.Key)) {
                        IO.Error("Missing --group/--key for set operation");
                        return false;
                    }
                    if (opts.Value == null) {
                        IO.Error("Missing --value for set operation");
                        return false;
                    }
                } else {
                    Utils.ConfigHelpers.ApplyUpdate(doc: doc, group: opts.Group, index: opts.Index, key: opts.Key, opts.Value, typeHint: opts.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{opts.Key} = {Utils.ConfigHelpers.ConvertValue(raw: opts.Value, hint: opts.TypeHint)}";
                    IO.writeLine(msg, color: System.ConsoleColor.Green);
                }
            }

            // Write back
            TomlHelpers.WriteTomlFile(path: configPath, data: doc);
            // IO.writeLine($"Updated config at {configPath}", System.ConsoleColor.Green);
            // Lua prints the specific updates. The above loops print the updates.
            return true;

        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Failed to update config: {ex.Message}");
            return false;
        }
    }


    internal static bool format_convert(Operations.helpers.OperationArgs operationArgs) {
        Shared.IO.Diagnostics.Log("] format-convert");

        // 1. Determine tool - check both 'tool' field and '-m'/'--mode' in args
        string? tool = operationArgs.op.TryGetValue(key: "tool", out object? ft)
            ? ft?.ToString()?.ToLowerInvariant() : null;

    #if DEBUG
        if (operationArgs.op.TryGetValue(key: "args", out object? argsDebugObj)) {
            Shared.IO.Diagnostics.Log($"] format-convert: args type = {argsDebugObj?.GetType().FullName ?? "null"}");
            if (argsDebugObj is System.Collections.IList argsDebugList) {
                Shared.IO.Diagnostics.Log($"] format-convert: args count = {argsDebugList.Count}");
                for (int i = 0; i < argsDebugList.Count; i++) {
                    Shared.IO.Diagnostics.Log($"] format-convert: args[{i}] = '{argsDebugList[index: i]}'");
                }
            }
        }
    #endif

        // 2. If tool not specified, try to extract from args
        if (string.IsNullOrWhiteSpace(tool) && operationArgs.op.TryGetValue(key: "args", out object? argsObj)
                                                   && (argsObj is System.Collections.IList argsList)) {
            for (int i = 0; i < argsList.Count - 1; i++) {
                string arg = argsList[index: i]?.ToString() ?? string.Empty;
                if (arg != "-m" && arg != "--mode") continue;
                tool = argsList[index: i + 1]?.ToString()?.ToLowerInvariant();
                Shared.IO.Diagnostics.Log($"] format-convert: extracted tool from args: '{tool}'");
                break;
            }
        }


        Shared.IO.Diagnostics.Log($"] format-convert: final tool = '{tool}'");

        // 3. Prepare Execution Context
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(context: operationArgs.context, currentGame: operationArgs.currentGame, games: operationArgs.games);
        List<string> args = Utils.Helpers.ResolveOperationArgs(op: operationArgs.op, ctx: ctx);

        // 4. Execute via Switch
        switch (tool) {
            case "ffmpeg":
            case "vgmstream":
                IO.writeLine("\n>>> Built-in media conversion");
                Shared.IO.Diagnostics.Log($"] format-convert: running media conversion with args: {string.Join(separator: ' ', values: args)}");
                return Core.Media.AvTools.Run(toolResolver: operationArgs.context.ToolResolver, args: args, cancellationToken: operationArgs.cancellationToken);

            case "imagemagick":
                IO.writeLine("\n>>> Built-in image conversion");
                Shared.IO.Diagnostics.Log($"] format-convert: running image conversion with args: {string.Join(separator: ' ', values: args)}");
                return Core.Media.ImageMagickConverter.Run(toolResolver: operationArgs.context.ToolResolver, args: args, cancellationToken: operationArgs.cancellationToken);

            case "p3d":
                IO.writeLine("\n>>> Built-in p3d conversion");
                Shared.IO.Diagnostics.Log($"] format-convert: running p3d conversion with args: {string.Join(separator: ' ', values: args)}");
                return EngineNet.GameFormats.p3d.P3dExtractor.Run(args: args, cancellationToken: operationArgs.cancellationToken);

            default:
                Shared.IO.Diagnostics.Log($"] format-convert: unknown tool '{tool}'");
                IO.writeLine($"ERROR: format-convert requires a valid tool. Found: '{tool ?? "(null)"}'");
                IO.writeLine("Supported tools: ffmpeg, vgmstream, ImageMagick, p3d");
                IO.writeLine("Specify tool with --tool parameter or -m/--mode in args.");
                return false;
        }
    }

    internal static async System.Threading.Tasks.Task<bool> DownloadTools(
        Operations.helpers.OperationArgs operationArgs
    ) {
        // Expect a 'tools_manifest' value (path), or fallback to first arg
        string? manifest = Utils.Helpers.GetFieldOrFirstArgRawValue(op: operationArgs.op, fieldName: "tools_manifest");

        if (string.IsNullOrWhiteSpace(manifest)) {
            return false;
        }
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(context: operationArgs.context, currentGame: operationArgs.currentGame, games: operationArgs.games);
        string resolvedManifest = Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "tools_manifest", ctx: ctx, fallbackToRawValue: true)
            ?? Core.Utils.Placeholders.Resolve(manifest, context: ctx)?.ToString()
            ?? manifest;

        bool force = false;
        if (operationArgs.promptAnswers.TryGetValue(key: "force download", out object? fd) && fd is bool b1) {
            force = b1;
        }
        if (operationArgs.promptAnswers.TryGetValue(key: "force_download", out object? fd2) && fd2 is bool b2) {
            force = b2;
        }

        // execute
        await ExternalTools.ToolsDownloader.ProcessAsync(moduleTomlPath: resolvedManifest, rootPath: EngineNet.Shared.State.RootPath,force: force, context: ctx, cancellationToken: operationArgs.cancellationToken);
        return true;
    }

    internal static bool format_extract(
        Operations.helpers.OperationArgs operationArgs
    ) {
        // Determine input file format
        string? format = operationArgs.op.TryGetValue(key: "format", out object? ft)
            ? ft?.ToString()?.ToLowerInvariant() : null;

        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(context: operationArgs.context, currentGame: operationArgs.currentGame, games: operationArgs.games);
        List<string> args = Utils.Helpers.ResolveOperationArgs(op: operationArgs.op, ctx: ctx);

        // execute
        switch (format) {
            case "p3d": {
                // in future will be specifically for converting p3d into there core component files (meshes, textures, shaders, etc)
                IO.writeLine("\n>>> Built-in P3D extraction");
                IO.writeLine($"with args: {string.Join(separator: ' ', values: args)}");
                return EngineNet.GameFormats.p3d.P3dExtractor.Run(args: args, cancellationToken: operationArgs.cancellationToken);
            } case "txd": {
                IO.writeLine("\n>>> Built-in TXD extraction");
                IO.writeLine($"with args: {string.Join(separator: ' ', values: args)}");
                return EngineNet.GameFormats.txd.Extractor.Run(args: args, cancellationToken: operationArgs.cancellationToken);
            } default: {
                IO.writeLine($"ERROR: format-extract does not support format '{format}'");
                IO.writeLine("Supported formats: p3d, txd");
                return false;
            }
        }
    }

    internal static bool rename_folders(
        Operations.helpers.OperationArgs operationArgs
    ) {
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(context: operationArgs.context, currentGame: operationArgs.currentGame, games: operationArgs.games);
        List<string> args = Utils.Helpers.ResolveOperationArgs(op: operationArgs.op, ctx: ctx);

        // execute
        IO.writeLine("\n>>> Built-in folder rename");

        IO.writeLine($"with args: {string.Join(separator: ' ', values: args)}");
        bool ok = Utils.FolderRenamer.Run(args: args, cancellationToken: operationArgs.cancellationToken);
        return ok;
    }

    internal static bool validate_files(
        Operations.helpers.OperationArgs operationArgs
    ) {
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(context: operationArgs.context, currentGame: operationArgs.currentGame, games: operationArgs.games);
        string? resolvedDbPath = Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "db", ctx: ctx);

        // create args list
        List<string> args = new();
        // if a db path was resolved and is not already in args, add it as the first arg
        if (!string.IsNullOrWhiteSpace(resolvedDbPath)) {
            args.Add(item: resolvedDbPath);
        }
        List<string> resolvedArgs = Utils.Helpers.ResolveOperationArgs(op: operationArgs.op, ctx: ctx);
        for (int i = 0; i < resolvedArgs.Count; i++) {
            string value = resolvedArgs[index: i];
            if (!string.IsNullOrWhiteSpace(resolvedDbPath) && args.Count == 1 && i == 0 && string.Equals(a: args[index: 0], b: value, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            args.Add(item: value);
        }
        // if less than 2 args, print message and return false
        if (args.Count < 2) {
            IO.writeLine("validate-files requires a database path and base directory.");
            return false;
        }



        // execute
        IO.writeLine("\n>>> Built-in file validation");
        IO.writeLine($"with args: {string.Join(separator: ' ', values: args)}");
        bool ok = Utils.FileValidator.Run(args: args, cancellationToken: operationArgs.cancellationToken);
        return ok;
    }

}
