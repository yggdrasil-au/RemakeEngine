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
            } case "iso": {
                return extract_iso(operationArgs: operationArgs);
            } default: {
                IO.writeLine($"ERROR: format-extract does not support format '{format}'");
                IO.writeLine("Supported formats: p3d, txd, iso");
                return false;
            }
        }
    }

    internal static bool extract_iso(
        Operations.helpers.OperationArgs operationArgs,
        bool inspectOnly = false
    ) {
        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(context: operationArgs.context, currentGame: operationArgs.currentGame, games: operationArgs.games);
        List<string> args = Utils.Helpers.ResolveOperationArgs(op: operationArgs.op, ctx: ctx);

        string? inputVal = Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "input", ctx: ctx);
        if (!string.IsNullOrWhiteSpace(inputVal) && !args.Contains(item: "--input") && !args.Contains(item: "-i") && !args.Contains(item: "--source")) {
            args.Add(item: "--input");
            args.Add(item: inputVal);
        }

        string? outputVal = Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "output", ctx: ctx);
        if (!string.IsNullOrWhiteSpace(outputVal) && !args.Contains(item: "--output") && !args.Contains(item: "-o") && !args.Contains(item: "--out")) {
            args.Add(item: "--output");
            args.Add(item: outputVal);
        }

        string? filterVal = Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "filter", ctx: ctx);
        if (!string.IsNullOrWhiteSpace(filterVal) && !args.Contains(item: "--filter") && !args.Contains(item: "-f") && !args.Contains(item: "--pattern")) {
            args.Add(item: "--filter");
            args.Add(item: filterVal);
        }

        if (operationArgs.op.TryGetValue(key: "overwrite", out object? ovObj) && ovObj is not null && !args.Contains(item: "--overwrite")) {
            if (ovObj is bool ovBool && ovBool) {
                args.Add(item: "--overwrite");
            } else if (bool.TryParse(value: ovObj.ToString(), result: out bool parsedOv) && parsedOv) {
                args.Add(item: "--overwrite");
            }
        }

        if (inspectOnly && !args.Contains(item: "--inspect") && !args.Contains(item: "--list")) {
            args.Add(item: "--inspect");
        }

        IO.writeLine(inspectOnly ? "\n>>> Built-in ISO inspection" : "\n>>> Built-in ISO extraction");
        IO.writeLine($"with args: {string.Join(separator: ' ', values: args)}");
        return DiscImages.IsoFactory.RunCli(args: args, cancellationToken: operationArgs.cancellationToken);
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

    private static readonly System.Threading.AsyncLocal<System.Collections.Generic.HashSet<string>?> ActiveModuleOperations = new();

    /// <summary>
    /// Executes an explicitly selected operation across target modules resolved within the requested scope
    /// (<c>submodules</c> or <c>all</c>), resolving module selectors by Registry Name, Registry Id, or Module Folder Name.
    /// </summary>
    /// <param name="operationArgs">Execution arguments for the engine operation.</param>
    /// <returns><c>true</c> if all targeted module operations completed successfully; otherwise <c>false</c>.</returns>
    internal static async System.Threading.Tasks.Task<bool> RunModuleOperationsAsync(
        Operations.helpers.OperationArgs operationArgs
    ) {
        if (operationArgs.operationContext is null) {
            IO.Error("run-module-operations requires an active IOperationContext.");
            return false;
        }

        string currentModuleRoot = ResolveCurrentModuleRoot(operationArgs: operationArgs);
        if (string.IsNullOrWhiteSpace(value: currentModuleRoot)) {
            IO.Error($"Unable to resolve GameRoot directory for current module '{operationArgs.currentGame}'.");
            return false;
        }

        Dictionary<string, object?> ctx = Utils.Helpers.BuildOperationContext(
            context: operationArgs.context,
            currentGame: operationArgs.currentGame,
            games: operationArgs.games
        );
        List<string> resolvedArgs = Utils.Helpers.ResolveOperationArgs(op: operationArgs.op, ctx: ctx);

        string scope = Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "scope", ctx: ctx)
            ?? GetArgOptionValue(args: resolvedArgs, optionName: "--scope")
            ?? "submodules";

        List<string> requestedModules = ParseRequestedModules(op: operationArgs.op, ctx: ctx, args: resolvedArgs);

        if (!TryParseTargetOpId(op: operationArgs.op, ctx: ctx, args: resolvedArgs, targetOpId: out long? targetOpId, error: out string? idParseError)) {
            IO.Error(idParseError!);
            return false;
        }

        string? targetOpName = Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "target_op_name", ctx: ctx)
            ?? Utils.Helpers.ResolveOperationValue(op: operationArgs.op, key: "op_name", ctx: ctx)
            ?? GetArgOptionValue(args: resolvedArgs, optionName: "--op-name")
            ?? GetArgOptionValue(args: resolvedArgs, optionName: "--target-op-name");

        if (!targetOpId.HasValue && string.IsNullOrWhiteSpace(value: targetOpName)) {
            IO.Error("run-module-operations requires an explicit operation selector ('target_op_id' / 'target_op_name' or '--op-id' / '--op-name').");
            return false;
        }

        bool continueOnError = ParseBoolOption(op: operationArgs.op, key: "continue_on_error", args: resolvedArgs, flagName: "--continue-on-error");

        IReadOnlyList<GameModuleInfo> targetModules;
        try {
            targetModules = operationArgs.context.GameRegistry.ResolveModules(
                currentModuleRoot: currentModuleRoot,
                scope: scope,
                requestedModules: requestedModules.Count > 0 ? requestedModules : null
            );
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"Invalid run-module-operations arguments: {ex.Message}");
            IO.Error(ex.Message);
            return false;
        } catch (System.Collections.Generic.KeyNotFoundException ex) {
            Shared.IO.Diagnostics.Bug($"Unresolved module selector: {ex.Message}");
            IO.Error(ex.Message);
            return false;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug($"Module resolution error: {ex.Message}");
            IO.Error(ex.Message);
            return false;
        }

        if (targetModules.Count == 0) {
            IO.Warn($"No valid target modules found in scope '{scope}' for '{currentModuleRoot}'.");
            return true;
        }

        Core.Data.GameModules childGames = new();
        foreach (KeyValuePair<string, GameModuleInfo> kv in operationArgs.games) {
            childGames[key: kv.Key] = kv.Value;
        }

        string targetLabel = targetOpId.HasValue
            ? $"op #{targetOpId.Value}{(string.IsNullOrWhiteSpace(value: targetOpName) ? string.Empty : $" ('{targetOpName}')")}"
            : $"op '{targetOpName}'";
        IO.writeLine($"\n>>> Running {targetLabel} across {targetModules.Count} module(s) [scope: {scope}]");

        bool overallSuccess = true;
        foreach (GameModuleInfo targetModule in targetModules) {
            if (operationArgs.cancellationToken.IsCancellationRequested) {
                return false;
            }

            string childKey = RegisterChildModuleKey(childGames: childGames, targetModule: targetModule);
            Core.Data.PreparedOperations prepared = operationArgs.operationContext.OperationsService.LoadAndPrepare(
                opsFile: targetModule.OpsFile,
                currentGame: childKey,
                games: childGames,
                engineConfig: operationArgs.context.EngineConfig.Data
            );
            if (!prepared.IsLoaded) {
                string msg = $"Failed to load operations for module '{targetModule.Name}' ({targetModule.OpsFile}): {prepared.ErrorMessage}";
                IO.Error(msg);
                overallSuccess = false;
                if (!continueOnError) {
                    return false;
                }
                continue;
            }

            if (!TryMatchOperation(prepared: prepared, opsFile: targetModule.OpsFile, targetOpId: targetOpId, targetOpName: targetOpName, matchedOp: out Core.Data.PreparedOperation? matchedOp, error: out string? matchError)) {
                IO.Error($"Module '{targetModule.Name}': {matchError}");
                overallSuccess = false;
                if (!continueOnError) {
                    return false;
                }
                continue;
            }

            string normalizedRoot = System.IO.Path.TrimEndingDirectorySeparator(path: System.IO.Path.GetFullPath(path: targetModule.GameRoot));
            string cycleKey = $"{normalizedRoot}|{matchedOp!.OperationId?.ToString() ?? matchedOp.DisplayName}";
            System.Collections.Generic.HashSet<string>? previousStack = ActiveModuleOperations.Value;
            System.Collections.Generic.HashSet<string> nextStack = previousStack is null
                ? new System.Collections.Generic.HashSet<string>(comparer: System.StringComparer.OrdinalIgnoreCase)
                : new System.Collections.Generic.HashSet<string>(collection: previousStack, comparer: System.StringComparer.OrdinalIgnoreCase);

            if (!nextStack.Add(item: cycleKey)) {
                IO.Error($"Recursive run-module-operations cycle detected for '{targetModule.Name}' ({cycleKey}).");
                return false;
            }

            IO.writeLine($"\n--- Module '{targetModule.Name}' -> {matchedOp.DisplayName} (id: {matchedOp.OperationId?.ToString() ?? "none"}) ---");
            ActiveModuleOperations.Value = nextStack;
            bool ok;
            try {
                Core.Data.PromptAnswers childAnswers = All.BuildPromptDefaults(op: matchedOp.Operation);
                ok = await operationArgs.operationContext.Single.RunAsync(
                    currentGame: childKey,
                    games: childGames,
                    op: matchedOp.Operation,
                    promptAnswers: childAnswers,
                    Context: operationArgs.context,
                    OperationContext: operationArgs.operationContext,
                    cancellationToken: operationArgs.cancellationToken
                );
            } finally {
                ActiveModuleOperations.Value = previousStack;
            }

            if (!ok) {
                IO.Error($"Module '{targetModule.Name}' operation '{matchedOp.DisplayName}' failed.");
                overallSuccess = false;
                if (!continueOnError) {
                    return false;
                }
            }
        }

        return overallSuccess;
    }

    private static string ResolveCurrentModuleRoot(Operations.helpers.OperationArgs operationArgs) {
        if (!string.IsNullOrWhiteSpace(value: operationArgs.currentGame)
            && operationArgs.games.TryGetValue(key: operationArgs.currentGame, out GameModuleInfo? currentInfo)
            && !string.IsNullOrWhiteSpace(value: currentInfo.GameRoot)) {
            return System.IO.Path.GetFullPath(path: currentInfo.GameRoot);
        }

        string? resolved = !string.IsNullOrWhiteSpace(value: operationArgs.currentGame)
            ? operationArgs.context.GameRegistry.GetGamePath(name: operationArgs.currentGame)
            : null;
        if (!string.IsNullOrWhiteSpace(value: resolved)) {
            return System.IO.Path.GetFullPath(path: resolved);
        }

        return string.Empty;
    }

    private static string RegisterChildModuleKey(Core.Data.GameModules childGames, GameModuleInfo targetModule) {
        string normalizedTargetRoot = System.IO.Path.TrimEndingDirectorySeparator(path: System.IO.Path.GetFullPath(path: targetModule.GameRoot));
        if (!childGames.TryGetValue(key: targetModule.Name, out GameModuleInfo? existing)) {
            childGames[key: targetModule.Name] = targetModule;
            return targetModule.Name;
        }

        if (!string.IsNullOrWhiteSpace(value: existing.GameRoot)) {
            string normalizedExisting = System.IO.Path.TrimEndingDirectorySeparator(path: System.IO.Path.GetFullPath(path: existing.GameRoot));
            System.StringComparison cmp = System.OperatingSystem.IsWindows() ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
            if (string.Equals(a: normalizedExisting, b: normalizedTargetRoot, comparisonType: cmp)) {
                return targetModule.Name;
            }
        }

        string uniqueKey = $"{targetModule.Name}::{normalizedTargetRoot}";
        childGames[key: uniqueKey] = targetModule;
        return uniqueKey;
    }

    private static List<string> ParseRequestedModules(
        IDictionary<string, object?> op,
        IDictionary<string, object?> ctx,
        IReadOnlyList<string> args
    ) {
        List<string> modules = [];
        AddModuleField(op: op, key: "modules", ctx: ctx, dest: modules);
        AddModuleField(op: op, key: "module", ctx: ctx, dest: modules);

        for (int i = 0; i < args.Count; i++) {
            string arg = args[index: i];
            if ((string.Equals(a: arg, b: "--modules", comparisonType: System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(a: arg, b: "--module", comparisonType: System.StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Count) {
                SplitModuleTokens(raw: args[index: ++i], dest: modules);
            } else if (arg.StartsWith("--modules=", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                SplitModuleTokens(raw: arg.Substring(startIndex: "--modules=".Length), dest: modules);
            } else if (arg.StartsWith("--module=", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                SplitModuleTokens(raw: arg.Substring(startIndex: "--module=".Length), dest: modules);
            }
        }

        return modules;
    }

    private static void AddModuleField(
        IDictionary<string, object?> op,
        string key,
        IDictionary<string, object?> ctx,
        List<string> dest
    ) {
        if (!op.TryGetValue(key: key, out object? raw) || raw is null) {
            return;
        }

        object? resolved = Core.Utils.Placeholders.Resolve(raw, context: ctx);
        if (resolved is System.Collections.IList list) {
            foreach (object? item in list) {
                if (item is not null) {
                    SplitModuleTokens(raw: item.ToString(), dest: dest);
                }
            }
        } else {
            SplitModuleTokens(raw: resolved?.ToString(), dest: dest);
        }
    }

    private static void SplitModuleTokens(string? raw, List<string> dest) {
        if (string.IsNullOrWhiteSpace(value: raw)) {
            return;
        }
        foreach (string part in raw.Split(separator: ',', options: System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)) {
            if (!string.IsNullOrWhiteSpace(value: part)) {
                dest.Add(item: part);
            }
        }
    }

    private static string? GetArgOptionValue(IReadOnlyList<string> args, string optionName) {
        string prefix = optionName + "=";
        for (int i = 0; i < args.Count; i++) {
            string arg = args[index: i];
            if (string.Equals(a: arg, b: optionName, comparisonType: System.StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) {
                return args[index: i + 1];
            }
            if (arg.StartsWith(prefix, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                return arg.Substring(startIndex: prefix.Length);
            }
        }
        return null;
    }

    private static bool TryParseTargetOpId(
        IDictionary<string, object?> op,
        IDictionary<string, object?> ctx,
        IReadOnlyList<string> args,
        out long? targetOpId,
        out string? error
    ) {
        targetOpId = null;
        error = null;

        string? rawId = Utils.Helpers.ResolveOperationValue(op: op, key: "target_op_id", ctx: ctx)
            ?? Utils.Helpers.ResolveOperationValue(op: op, key: "op_id", ctx: ctx)
            ?? GetArgOptionValue(args: args, optionName: "--op-id")
            ?? GetArgOptionValue(args: args, optionName: "--target-op-id");

        if (string.IsNullOrWhiteSpace(value: rawId)) {
            return true;
        }

        if (long.TryParse(s: rawId.Trim(), style: System.Globalization.NumberStyles.Integer, provider: System.Globalization.CultureInfo.InvariantCulture, out long parsed)) {
            targetOpId = parsed;
            return true;
        }

        error = $"Invalid target_op_id '{rawId}'. Expected an integer operation ID.";
        return false;
    }

    private static bool ParseBoolOption(
        IDictionary<string, object?> op,
        string key,
        IReadOnlyList<string> args,
        string flagName
    ) {
        if (op.TryGetValue(key: key, out object? raw)) {
            if (raw is bool b) {
                return b;
            }
            if (raw is not null && bool.TryParse(value: raw.ToString(), result: out bool parsed)) {
                return parsed;
            }
        }

        return args.Any(predicate: a => string.Equals(a: a, b: flagName, comparisonType: System.StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryMatchOperation(
        Core.Data.PreparedOperations prepared,
        string opsFile,
        long? targetOpId,
        string? targetOpName,
        out Core.Data.PreparedOperation? matchedOp,
        out string? error
    ) {
        matchedOp = null;
        error = null;

        List<Core.Data.PreparedOperation> allOps = [.. prepared.InitOperations, .. prepared.RegularOperations];
        List<Core.Data.PreparedOperation> matches = [];

        foreach (Core.Data.PreparedOperation candidate in allOps) {
            if (targetOpId.HasValue) {
                long? candidateId = candidate.OperationId;
                if (!candidateId.HasValue
                    && candidate.Operation.TryGetValue(key: "id", out object? idObj)
                    && idObj is not null
                    && long.TryParse(s: idObj.ToString(), out long parsedId)) {
                    candidateId = parsedId;
                }
                if (candidateId != targetOpId.Value) {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(value: targetOpName)) {
                string? rawName = candidate.Operation.TryGetValue(key: "Name", out object? nameObj) ? nameObj?.ToString() : null;
                bool nameMatches = string.Equals(a: candidate.DisplayName, b: targetOpName, comparisonType: System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a: rawName, b: targetOpName, comparisonType: System.StringComparison.OrdinalIgnoreCase);
                if (!nameMatches) {
                    continue;
                }
            }

            matches.Add(item: candidate);
        }

        if (matches.Count == 1) {
            matchedOp = matches[index: 0];
            return true;
        }

        string selectorLabel = targetOpId.HasValue
            ? $"id '{targetOpId.Value}'{(string.IsNullOrWhiteSpace(value: targetOpName) ? string.Empty : $" and name '{targetOpName}'")}"
            : $"name '{targetOpName}'";

        if (matches.Count == 0) {
            error = $"Target operation with {selectorLabel} was not found in '{opsFile}'.";
            return false;
        }

        error = $"Multiple operations ({matches.Count}) matched {selectorLabel} in '{opsFile}'.";
        return false;
    }

}
