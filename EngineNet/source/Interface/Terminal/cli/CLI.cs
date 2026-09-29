namespace EngineNet.Interface.Terminal;

using Interface;

public sealed class CLI {

    /* :: :: Constructor, Var :: START :: */
    private readonly MiniEngineFace Engine;

    public CLI(MiniEngineFace engine) {
        Engine = engine;
    }

    /// <summary>
    /// Run the CLI
    /// </summary>
    /// <param name="args"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async System.Threading.Tasks.Task<int> RunAsync(string[] args, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            // if no arguments provided, display help and exit
            if (args.Length == 0) {
                StaticHelpers.PrintHelp();
                return 0;
            }

            // Parse inline operation options from command-line arguments
            InlineOperationOptions options = InlineOperationOptions.Parse(args: args);

            if (options.RunAll) {
                Shared.IO.Diagnostics.Trace("Detected run-all operation invocation.");
                if (options.RunOperationSelector is null) {
                    return await ExecuteOperations.RunAllOperationsAsync(options: options, Engine: Engine, cancellationToken: cancellationToken);
                }
                Shared.IO.Diagnostics.Log("ERROR: --run_op cannot be combined with --run_all.");
                return 2;
            }

            if (options.RunOperationSelector is not null) {
                Shared.IO.Diagnostics.Trace("Detected named operation invocation.");
                return await ExecuteOperations.RunSelectedOperationAsync(options: options, Engine: Engine, cancellationToken: cancellationToken);
            }

            // Check for inline operation invocation
            if (StaticHelpers.IsInlineOperationInvocation(args: args)) {
                Shared.IO.Diagnostics.Trace("Detected inline operation invocation.");
                // Run operation directly from command-line args
                return await ExecuteOperations.RunInlineOperationAsync(options: options, Engine: Engine, cancellationToken: cancellationToken);
            }

            string cmd = args[0].ToLowerInvariant();
            Shared.IO.Diagnostics.Trace($"CLI Command: {cmd}");
            switch (cmd) {
                case "help":
                case "-h":
                case "--help":
                    StaticHelpers.PrintHelp();
                    return 0;
                case "--version":
                case "-v":
                case "--v":
                    // Display basic project and build info
                    System.Console.WriteLine($"Remake Engine");
                    System.Console.WriteLine($"");
                    System.Console.WriteLine($"Build: {EngineBuildInfo.BuildNumber}");
                    System.Console.WriteLine($"Version: {EngineBuildInfo.ProjectVersion}");
                    System.Console.WriteLine($"Commit: {EngineBuildInfo.GitCommitHash}");
                    return 0;
                case "--list-games":
                    return ListGames();
                case "--list-internal":
                    return ListInternal();
                case "--list-ops":
                    return ListOps(game: StaticHelpers.GetArg(args: args, index: 1, error: "<game> required for list-ops"));
                default:
                    System.Console.WriteLine($"Unknown command '{args[0]}'.");
                    StaticHelpers.PrintHelp();
                    return 2;
            }
        } catch (System.OperationCanceledException) {
            System.Console.WriteLine("\nOperation cancelled by user.");
            return 1;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"CLI Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}");
            return -1;
        }
    }


    /// <summary>
    /// Options for inline operation execution.
    /// </summary>
    public sealed class InlineOperationOptions {
        internal string? InternalModuleIdentifier {
            get; private set;
        }
        internal string? GameIdentifier {
            get; private set;
        }
        internal string? GameRoot {
            get; private set;
        }
        internal string? GameName {
            get; private set;
        }
        internal string? OpsFile {
            get; private set;
        }
        internal string? Script {
            get; private set;
        }
        private string? ScriptType {
            get; set;
        }
        internal object? RunOperationSelector {
            get; private set;
        }
        internal bool RunAll {
            get; private set;
        }
        internal Dictionary<string, object?> OperationFields { get; } = new(comparer: System.StringComparer.OrdinalIgnoreCase);
        internal Core.Data.PromptAnswers PromptAnswers { get; } = new(); // respond to operations.toml prompts
        internal Dictionary<string, string> AutoPromptResponses { get; } = new(comparer: System.StringComparer.OrdinalIgnoreCase); // responde to lua prompt() calls

        private readonly List<string> _args = new();
        private bool _argsOverride;

        /// <summary>
        /// Parse command-line arguments into inline operation options.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentException"></exception>
        public static InlineOperationOptions Parse(string[] args) {
            InlineOperationOptions options = new();

            for (int index = 0; index < args.Length; index++) {
                string token = args[index];
                if (!token.StartsWith("--", comparisonType: System.StringComparison.Ordinal)) {
                    continue;
                }

                string key = token.Substring(startIndex: 2);
                string? value = null;
                if (key.Contains('=', comparisonType: System.StringComparison.Ordinal)) {
                    string[] kv = key.Split(separator: '=', count: 2);
                    key = kv[0];
                    value = kv[1];
                } else if (index + 1 < args.Length && !args[index + 1].StartsWith("--", comparisonType: System.StringComparison.Ordinal)) {
                    value = args[++index];
                }

                string normalized = StaticHelpers.NormalizeOptionKey(key: key);
                Shared.IO.Diagnostics.Log($"DEBUG: Parsing option --{key} (normalized: {normalized}) with value '{value}'");
                switch (normalized) {
                    case "internal":
                        if (string.IsNullOrWhiteSpace(value)) {
                            throw new System.ArgumentException($"Option '--{key}' requires a value.");
                        }
                        options.InternalModuleIdentifier = value;
                        break;
                    case "game":
                    case "game_module":
                    case "module":
                    case "gameid":
                        if (string.IsNullOrWhiteSpace(value)) {
                            throw new System.ArgumentException($"Option '--{key}' requires a value.");
                        }
                        options.GameIdentifier = value;
                        break;
                    case "game_root":
                        if (string.IsNullOrWhiteSpace(value)) {
                            throw new System.ArgumentException("Option '--game-root' requires a directory path.");
                        }
                        options.GameRoot = value;
                        break;
                    case "game_name":
                        if (string.IsNullOrWhiteSpace(value)) {
                            throw new System.ArgumentException("Option '--game-name' requires a value.");
                        }
                        options.GameName = value;
                        break;
                    case "ops_file":
                        if (string.IsNullOrWhiteSpace(value)) {
                            throw new System.ArgumentException("Option '--ops-file' requires a value.");
                        }
                        options.OpsFile = value;
                        break;
                    case "script":
                        if (string.IsNullOrWhiteSpace(value)) {
                            throw new System.ArgumentException("Option '--script' requires a value.");
                        }
                        options.Script = value;
                        break;
                    case "run_op":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--run-op' requires an operation name or ID.");
                        }
                        options.RunOperationSelector = StaticHelpers.ParseValueToken(value);
                        break;
                    case "run_all":
                        if (value is null) {
                            options.RunAll = true;
                        } else {
                            options.RunAll = StaticHelpers.IsTruthy(StaticHelpers.ParseValueToken(value));
                        }
                        break;
                    case "script_type":
                    case "type":
                        if (!string.IsNullOrWhiteSpace(value)) {
                            // Handle common typos and aliases
                            string normalizedType = value.ToLowerInvariant();
                            options.ScriptType = normalizedType switch {
                                "lau" => "lua",  // Common typo
                                "javascript" => "js",
                                _ => value
                            };
                        }
                        break;
                    case "arg":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--arg' requires a value.");
                        }
                        options._args.Add(item: value);
                        break;
                    case "args":
                        if (value is null) {
                            Shared.IO.Diagnostics.Log("DEBUG: --args missing value");
                            throw new System.ArgumentException("Option '--args' requires a value.");
                        }
                        foreach (string item in StaticHelpers.ParseArgsList(raw: value)) {
                            options._args.Add(item: item);
                        }
#if DEBUG
                        Shared.IO.Diagnostics.Log($"DEBUG: --args parsed {options._args.Count} items");
                        Shared.IO.Diagnostics.Log($"DEBUG: --args items: {string.Join(separator: ", ", values: options._args)}");
                        Shared.IO.Diagnostics.Log($"DEBUG: --args raw {value}");
#endif
                        break;
                    case "answer":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--answer' requires KEY=VALUE.");
                        }
                        (string answerKey, object? answerValue) = StaticHelpers.ParseKeyValue(input: value);
                        options.PromptAnswers[key: answerKey] = answerValue;
                        break;
                    case "auto_prompt":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--auto_prompt' requires PROMPT_ID=RESPONSE.");
                        }
                        (string promptId, object? promptResponse) = StaticHelpers.ParseKeyValue(input: value);
                        options.AutoPromptResponses[key: promptId] = promptResponse?.ToString() ?? string.Empty;
                        break;
                    case "set":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--set' requires KEY=VALUE.");
                        }
                        (string setKey, object? setValue) = StaticHelpers.ParseKeyValue(input: value);
                        string normalizedSetKey = StaticHelpers.NormalizeOptionKey(key: setKey);
                        if (normalizedSetKey == "args") {
                            options._argsOverride = true;
                        }
                        options.OperationFields[key: StaticHelpers.NormalizeOperationKey(key: setKey)] = setValue;
                        break;
                    default:
                        if (value is null) {
                            options.OperationFields[key: StaticHelpers.NormalizeOperationKey(key: key)] = true;
                        } else {
                            if (StaticHelpers.NormalizeOptionKey(key: key) == "args") {
                                options._argsOverride = true;
                            }
                            options.OperationFields[key: StaticHelpers.NormalizeOperationKey(key: key)] = StaticHelpers.ParseValueToken(value);
                        }
                        break;
                }
            }

            return options;
        }

        /// <summary>
        /// Build the operation dictionary from the parsed options.
        /// </summary>
        /// <returns></returns>
        internal Dictionary<string, object?> BuildOperation() {
            Dictionary<string, object?> op = new(dictionary: OperationFields, comparer: System.StringComparer.OrdinalIgnoreCase);

            if (!op.ContainsKey(key: "script_type") && !string.IsNullOrWhiteSpace(ScriptType)) {
                op[key: "script_type"] = ScriptType;
            }

            if (!string.IsNullOrWhiteSpace(Script)) {
                op[key: "script"] = Script;
            }

            if (!op.ContainsKey(key: "args") && !_argsOverride && _args.Count > 0) {
                op[key: "args"] = _args.ToList();
            }

            return op;
        }
    }

    private int ListGames() {
        try {
            Core.Data.GameModules modules = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.All);
            if (modules.Count == 0) {
                System.Console.WriteLine("No modules found.");
                return 0;
            }
            foreach ((string Name, string State, string Root) item in modules.Values.Select(selector: m => (Name: m.Name, State: m.DescribeState(), Root: m.GameRoot))) {
                System.Console.WriteLine($"- {item.Name}  (state: {item.State}; root: {item.Root})");
            }
            return 0;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error listing games: {ex}");
            return -1;
        }
    }

    private int ListInternal() {
        try {
            Core.Data.GameModules modules = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.Internal);
            if (modules.Count == 0) {
                System.Console.WriteLine("No internal modules found.");
                return 0;
            }
            System.Console.WriteLine("Internal Modules:");
            foreach ((string Name, string State, string Root) item in modules.Values.Select(selector: m => (Name: m.Name, State: m.DescribeState(), Root: m.GameRoot))) {
                System.Console.WriteLine($"- {item.Name}  (state: {item.State}; root: {item.Root})");
            }
            return 0;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error listing internal modules: {ex}");
            return -1;
        }
    }

    private int ListOps(string game) {
        try {
            // Find the game module
            Core.Data.GameModules modules = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.All);
            if (!modules.TryGetValue(key: game, out Core.Data.GameModuleInfo? mod)) {
                // Fallback to check if it's an internal module
                modules = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.Internal);
                if (!modules.TryGetValue(key: game, out mod)) {
                    System.Console.WriteLine($"Game/Module '{game}' not found.");
                    return 1;
                }
            }
            // Load operations list
            string? opsFile = mod.OpsFile;
            if (string.IsNullOrWhiteSpace(opsFile) || !System.IO.File.Exists(path: opsFile)) {
                throw new System.ArgumentException($"Game '{game}' missing ops_file.");
            }
            // Load and validate operations
            Core.Data.PreparedOperations preparedOps = Engine.OperationsService_LoadAndPrepare(opsFile: opsFile, currentGame: game, games: modules, engineConfig: Engine.EngineConfig_Data);
            if (!preparedOps.IsLoaded) {
                System.Console.WriteLine(preparedOps.ErrorMessage ?? "Failed to load operations.");
                return 1;
            }

            if (preparedOps.InitOperations.Count == 0 && preparedOps.RegularOperations.Count == 0) {
                System.Console.WriteLine($"No operations found for game '{game}'.");
                Shared.IO.Diagnostics.Log($"No operations found in ops_file '{opsFile}' for game '{game}'.");
                return 0;
            }

            StaticHelpers.WritePreparedOperationWarnings(preparedOps: preparedOps, game: game, opsFile: opsFile);

            // Print operations
            System.Console.WriteLine($"Operations for game '{game}':");
            foreach (Core.Data.PreparedOperation op in preparedOps.InitOperations) {
                System.Console.WriteLine($"- [init] {StaticHelpers.FormatPreparedOperation(op: op)}");
            }
            foreach (Core.Data.PreparedOperation op in preparedOps.RegularOperations) {
                System.Console.WriteLine($"- {StaticHelpers.FormatPreparedOperation(op: op)}");
            }

            return 0;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error listing operations for game '{game}': {ex}");
            return -1;
        }
    }

    internal static bool TryLoadPreparedOperations(
        string gameName,
        Core.Data.GameModules games,
        string? opsFileOverride,
        MiniEngineFace _engine,
        out Core.Data.PreparedOperations? preparedOps,
        out int exitCode
    ) {
        preparedOps = null;
        exitCode = 1;

        if (!games.TryGetValue(key: gameName, out Core.Data.GameModuleInfo? moduleInfo)) {
            StaticHelpers.WriteUserError($"Game '{gameName}' was not found.");
            exitCode = 1;
            return false;
        }

        string opsFile = !string.IsNullOrWhiteSpace(opsFileOverride)
            ? StaticHelpers.ResolveFullPathSafe(path: opsFileOverride)
            : moduleInfo.OpsFile;

        if (string.IsNullOrWhiteSpace(opsFile) || !System.IO.File.Exists(path: opsFile)) {
            StaticHelpers.WriteUserError($"Game '{gameName}' is missing an operations file.");
            exitCode = 1;
            return false;
        }

        preparedOps = _engine.OperationsService_LoadAndPrepare(opsFile: opsFile, currentGame: gameName, games: games, engineConfig: _engine.EngineConfig_Data);
        if (!preparedOps.IsLoaded) {
            StaticHelpers.WriteUserError(preparedOps.ErrorMessage ?? "Failed to load operations.");
            exitCode = 1;
            return false;
        }

        StaticHelpers.WritePreparedOperationWarnings(preparedOps: preparedOps, game: gameName, opsFile: opsFile);
        exitCode = 0;
        return true;
    }

}