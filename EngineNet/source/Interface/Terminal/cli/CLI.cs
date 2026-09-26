
namespace EngineNet.Interface.Terminal;

using Interface;

public sealed partial class CLI {

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
            if (args.Length == 0) {
                PrintHelp();
                return 0;
            }

            InlineOperationOptions options = InlineOperationOptions.Parse(args: args);

            if (options.RunAll) {
                Shared.IO.Diagnostics.Trace("Detected run-all operation invocation.");
                if (options.RunOperationSelector is null){
                    return await RunAllOperationsAsync(options: options, cancellationToken: cancellationToken);
                }
                Shared.IO.Diagnostics.Log("ERROR: --run_op cannot be combined with --run_all.");
                return 2;

            }

            if (options.RunOperationSelector is not null) {
                Shared.IO.Diagnostics.Trace("Detected named operation invocation.");
                return await RunSelectedOperationAsync(options: options, cancellationToken: cancellationToken);
            }

            // Check for inline operation invocation
            if (IsInlineOperationInvocation(args: args)) {
                Shared.IO.Diagnostics.Trace("Detected inline operation invocation.");
                // Run operation directly from command-line args
                return await RunInlineOperationAsync(options: options, cancellationToken: cancellationToken);
            }

            string cmd = args[0].ToLowerInvariant();
            Shared.IO.Diagnostics.Trace($"CLI Command: {cmd}");
            switch (cmd) {
                case "help":
                case "-h":
                case "--help":
                    PrintHelp();
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
                case "--list-ops":
                    return ListOps(game: GetArg(args: args, index: 1, error: "<game> required for list-ops"));
                default:
                    System.Console.WriteLine($"Unknown command '{args[0]}'.");
                    PrintHelp();
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
    /// Run an operation based on command-line arguments.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal async System.Threading.Tasks.Task<int> RunInlineOperationAsync(InlineOperationOptions options, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        // Validate required options
        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot)) {
            Shared.IO.Diagnostics.Log("ERROR: --game_module/--game (or --game-root) is required for inline execution.");
            return 2;
        }

        // Validate script option
        if (string.IsNullOrWhiteSpace(options.Script) && !options.OperationFields.ContainsKey(key: "script")) {
            Shared.IO.Diagnostics.Log("ERROR: --script must be provided for inline execution.");
            return 2;
        }

        // Find game modules
        Core.Data.GameModules games = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.All);
        if (!TryResolveInlineGame(options: options, games: games, resolvedName: out string? gameName)) {
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
    internal async System.Threading.Tasks.Task<int> RunSelectedOperationAsync(InlineOperationOptions options, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        if (options.RunOperationSelector is null) {
            Shared.IO.Diagnostics.Log("ERROR: --run_op requires an operation name or ID.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot)) {
            Shared.IO.Diagnostics.Log("ERROR: --game_module/--game (or --game-root) is required for --run_op.");
            return 2;
        }

        Core.Data.GameModules games = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.All);
        if (!TryResolveInlineGame(options: options, games: games, resolvedName: out string? gameName)) {
            Shared.IO.Diagnostics.Log("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        if (!TryLoadPreparedOperations(gameName: gameName!, games: games, opsFileOverride: options.OpsFile, preparedOps: out Core.Data.PreparedOperations? preparedOps, exitCode: out int loadCode)) {
            return loadCode;
        }

        if (!TryResolvePreparedOperation(preparedOps: preparedOps!, selector: options.RunOperationSelector, selected: out Core.Data.PreparedOperation? selectedOp, errorMessage: out string? errorMessage)) {
            if (!string.IsNullOrWhiteSpace(errorMessage)) {
                await System.Console.Error.WriteLineAsync($"ERROR: {errorMessage}");
                Shared.IO.Diagnostics.Log($"ERROR: {errorMessage}");
            }

            if (preparedOps is not null) {
                WriteOperationSelectionHint(gameName: gameName!, preparedOps: preparedOps);
            }
            return 1;
        }

        Core.Data.PromptAnswers promptAnswers = options.PromptAnswers;
        bool ok = await new Utils().ExecuteOpAsync(Engine: Engine, game: gameName!, games: games, op: selectedOp!.Operation, promptAnswers: promptAnswers, autoPromptResponses: options.AutoPromptResponses, cancellationToken: cancellationToken);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Run the module's configured run-all sequence.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async System.Threading.Tasks.Task<int> RunAllOperationsAsync(InlineOperationOptions options, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot)) {
            Shared.IO.Diagnostics.Log("ERROR: --game_module/--game (or --game-root) is required for --run_all.");
            return 2;
        }

        Core.Data.GameModules games = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.All);
        if (!TryResolveInlineGame(options: options, games: games, resolvedName: out string? gameName)) {
            Shared.IO.Diagnostics.Log("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        if (!TryLoadPreparedOperations(gameName: gameName!, games: games, opsFileOverride: options.OpsFile, preparedOps: out _, exitCode: out int loadCode)) {
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

    /// <summary>
    /// Determine if the command-line arguments indicate an inline operation invocation.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private static bool IsInlineOperationInvocation(string[] args) {
        bool sawGame = false;
        bool sawScript = false;

        foreach (string token in args) {
            if (!token.StartsWith("--", comparisonType: System.StringComparison.Ordinal)) {
                continue;
            }

            string key = NormalizeOptionKey(key: GetOptionKey(token: token));
            if (key is "game" or "game_module" or "module" or "gameid" or "game_name" or "game_root") {
                // Indicate that a game/module was specified
                sawGame = true;
            }

            if (key == "script") {
                // Indicate that a script was specified
                sawScript = true;
            }
        }

        return sawGame && sawScript;
    }

    /// <summary>
    /// Options for inline operation execution.
    /// </summary>
    internal sealed class InlineOperationOptions {
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

                string normalized = NormalizeOptionKey(key: key);
                Shared.IO.Diagnostics.Log($"DEBUG: Parsing option --{key} (normalized: {normalized}) with value '{value}'");
                switch (normalized) {
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
                        options.RunOperationSelector = ParseValueToken(value);
                        break;
                    case "run_all":
                        if (value is null) {
                            options.RunAll = true;
                        } else {
                            options.RunAll = IsTruthy(ParseValueToken(value));
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
                        foreach (string item in ParseArgsList(raw: value)) {
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
                        (string answerKey, object? answerValue) = ParseKeyValue(input: value);
                        options.PromptAnswers[key: answerKey] = answerValue;
                        break;
                    case "auto_prompt":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--auto_prompt' requires PROMPT_ID=RESPONSE.");
                        }
                        (string promptId, object? promptResponse) = ParseKeyValue(input: value);
                        options.AutoPromptResponses[key: promptId] = promptResponse?.ToString() ?? string.Empty;
                        break;
                    case "set":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--set' requires KEY=VALUE.");
                        }
                        (string setKey, object? setValue) = ParseKeyValue(input: value);
                        string normalizedSetKey = NormalizeOptionKey(key: setKey);
                        if (normalizedSetKey == "args") {
                            options._argsOverride = true;
                        }
                        options.OperationFields[key: NormalizeOperationKey(key: setKey)] = setValue;
                        break;
                    default:
                        if (value is null) {
                            options.OperationFields[key: NormalizeOperationKey(key: key)] = true;
                        } else {
                            if (NormalizeOptionKey(key: key) == "args") {
                                options._argsOverride = true;
                            }
                            options.OperationFields[key: NormalizeOperationKey(key: key)] = ParseValueToken(value);
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

}
