namespace EngineNet.Interface.Terminal;

/// <summary>
/// Provides methods to execute Remake Engine Module operations, both inline and predefined.
/// </summary>
internal static class ExecuteOperations {

    /// <summary>
    /// Run an operation based on command-line arguments.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal static async System.Threading.Tasks.Task<int> RunInlineOperationAsync(CLI.InlineOperationOptions options, MiniEngineFace Engine, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        // Validate required options
        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot) && string.IsNullOrWhiteSpace(options.InternalModuleIdentifier)) {
            Shared.IO.Diagnostics.Log("ERROR: --game_module/--game (or --game-root) or --internal is required.");
            return 2;
        }

        // Validate script option
        if (string.IsNullOrWhiteSpace(options.Script) && !options.OperationFields.ContainsKey(key: "script")) {
            Shared.IO.Diagnostics.Log("ERROR: --script must be provided for inline execution.");
            return 2;
        }

        // Conditionally select the ModuleFilter and find game modules
        bool isInternal = !string.IsNullOrWhiteSpace(options.InternalModuleIdentifier);
        Core.Data.GameModules games = Engine.GameRegistry_GetModules(filter: isInternal ? Core.Data.ModuleFilter.Internal : Core.Data.ModuleFilter.All);
        if (!StaticHelpers.TryResolveInlineGame(options: options, games: games, resolvedName: out string? gameName)) {
            Shared.IO.Diagnostics.Log("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        // Build operation dictionary
        Dictionary<string, object?> op = options.BuildOperation();
        if (!op.TryGetValue(key: "script", out object? scriptObj) || scriptObj is null || string.IsNullOrWhiteSpace(scriptObj.ToString())) {
            Shared.IO.Diagnostics.Log("ERROR: Inline operation is missing a script path or identifier.");
            return 2;
        }

        // Execute the operation
        bool ok = await new Utils().ExecuteOpAsync(Engine: Engine, game: gameName!, games: games, op: op, promptAnswers: options.PromptAnswers, autoPromptResponses: options.AutoPromptResponses, cancellationToken: cancellationToken);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Run a predefined operation selected by name or ID.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal static async System.Threading.Tasks.Task<int> RunSelectedOperationAsync(CLI.InlineOperationOptions options, MiniEngineFace Engine, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        if (options.RunOperationSelector is null) {
            Shared.IO.Diagnostics.Log("ERROR: --run_op requires an operation name or ID.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot) && string.IsNullOrWhiteSpace(options.InternalModuleIdentifier)) {
            Shared.IO.Diagnostics.Log("ERROR: --game_module/--game (or --game-root) or --internal is required.");
            return 2;
        }

        bool isInternal = !string.IsNullOrWhiteSpace(options.InternalModuleIdentifier);
        Core.Data.GameModules games = Engine.GameRegistry_GetModules(filter: isInternal ? Core.Data.ModuleFilter.Internal : Core.Data.ModuleFilter.All);
        if (!StaticHelpers.TryResolveInlineGame(options: options, games: games, resolvedName: out string? gameName)) {
            Shared.IO.Diagnostics.Log("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        if (!CLI.TryLoadPreparedOperations(gameName: gameName!, games: games, opsFileOverride: options.OpsFile, _engine: Engine, preparedOps: out Core.Data.PreparedOperations? preparedOps, exitCode: out int loadCode)) {
            return loadCode;
        }

        if (!StaticHelpers.TryResolvePreparedOperation(preparedOps: preparedOps!, selector: options.RunOperationSelector, selected: out Core.Data.PreparedOperation? selectedOp, errorMessage: out string? errorMessage)) {
            if (!string.IsNullOrWhiteSpace(errorMessage)) {
                await System.Console.Error.WriteLineAsync($"ERROR: {errorMessage}");
                Shared.IO.Diagnostics.Log($"ERROR: {errorMessage}");
            }

            if (preparedOps is not null) {
                StaticHelpers.WriteOperationSelectionHint(gameName: gameName!, preparedOps: preparedOps);
            }
            return 1;
        }

        Dictionary<string, object?> finalOp;
        if (isInternal) {
            // Internal operations rely on prompt answers (--answer) rather than raw field overrides (--set/--args)
            finalOp = selectedOp!.Operation;
        } else {
            // Merge command-line overrides (like --args, --set) into the selected operation for standard scripts
            finalOp = new(dictionary: selectedOp!.Operation, comparer: System.StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object?> overrideField in options.BuildOperation()) {
                finalOp[key: overrideField.Key] = overrideField.Value;
            }
        }

        Core.Data.PromptAnswers promptAnswers = options.PromptAnswers;
        bool ok = await new Utils().ExecuteOpAsync(Engine: Engine, game: gameName!, games: games, op: finalOp, promptAnswers: promptAnswers, autoPromptResponses: options.AutoPromptResponses, cancellationToken: cancellationToken);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Run the module's configured run-all sequence.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal static async System.Threading.Tasks.Task<int> RunAllOperationsAsync(CLI.InlineOperationOptions options, MiniEngineFace Engine, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot) && string.IsNullOrWhiteSpace(options.InternalModuleIdentifier)) {
            Shared.IO.Diagnostics.Log("ERROR: --game_module/--game (or --game-root) or --internal is required.");
            return 2;
        }

        bool isInternal = !string.IsNullOrWhiteSpace(options.InternalModuleIdentifier);
        Core.Data.GameModules games = Engine.GameRegistry_GetModules(filter: isInternal ? Core.Data.ModuleFilter.Internal : Core.Data.ModuleFilter.All);
        if (!StaticHelpers.TryResolveInlineGame(options: options, games: games, resolvedName: out string? gameName)) {
            Shared.IO.Diagnostics.Log("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        if (!CLI.TryLoadPreparedOperations(gameName: gameName!, games: games, opsFileOverride: options.OpsFile, _engine: Engine, preparedOps: out _, exitCode: out int loadCode)) {
            return loadCode;
        }

        try {
            Core.Operations.RunAllResult result = await Engine.RunAllAsync(
                gameName: gameName!,
                onOutput: Utils.OnOutput,
                onEvent: Utils.OnEvent,
                stdinProvider: static () => System.Console.ReadLine() ?? string.Empty,
                cancellationToken: cancellationToken
            );

            Shared.IO.Diagnostics.Log($"Completed run-all for '{result.Game}' with {result.SucceededOperations}/{result.TotalOperations} successful operations.");
            return result.Success ? 0 : 1;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"CLI RunAll Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}");
            return -1;
        }
    }
}
