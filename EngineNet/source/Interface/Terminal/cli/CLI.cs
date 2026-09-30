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
            InlineOperationOptions options = Parse(args: args);

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

            string cmd = args[0].TrimStart('-').ToLowerInvariant();
            Shared.IO.Diagnostics.Trace($"CLI Command: {cmd}");
            switch (cmd) {
                case "help":
                case "h":
                    StaticHelpers.PrintHelp();
                    return 0;
                case "version":
                case "v":
                    // Display basic project and build info
                    System.Console.WriteLine($"Remake Engine");
                    System.Console.WriteLine($"");
                    System.Console.WriteLine($"Build: {EngineBuildInfo.BuildNumber}");
                    System.Console.WriteLine($"Version: {EngineBuildInfo.ProjectVersion}");
                    System.Console.WriteLine($"Commit: {EngineBuildInfo.GitCommitHash}");
                    return 0;
                case "list-games":
                    return StaticHelpers.ListGames(Engine);
                case "list-internal":
                    return StaticHelpers.ListInternal(Engine);
                case "list-ops":
                    return StaticHelpers.ListOps(game: StaticHelpers.GetArg(args: args, index: 1, error: "<game> required for list-ops"), Engine);
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
    /// Parse command-line arguments into inline operation options.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    /// <exception cref="System.ArgumentException"></exception>
    public static InlineOperationOptions Parse(string[] args) {
        InlineOperationOptions options = new();

        // Identify if the execution is targeting a primary CLI command to prevent
        // valid positional arguments (like <game> in list-ops) from triggering format warnings.
        bool isCliCommand = false;
        if (args.Length > 0) {
            string firstArg = args[0].TrimStart('-').ToLowerInvariant();
            if (firstArg is "help" or "h" or "version" or "v" or "list-games" or "list-internal" or "list-ops") {
                isCliCommand = true;
            }
        }

        for (int index = 0; index < args.Length; index++) {
            string token = args[index];
            if (!token.StartsWith("--", comparisonType: System.StringComparison.Ordinal)) {
                if (!isCliCommand) {
                    System.Console.WriteLine($"WARNING: Potentially unsupported argument '{token}' with incorrect format or simply unknown. It won't be handled/used.");
                }
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
                        string normalizedType = value.ToLowerInvariant();
                        options.ScriptType = normalizedType switch {
                            "lau" => "lua",
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
                    System.Console.WriteLine($"WARNING: Unknown potentially unsupported argument '--{key}'. It won't be handled/used.");
                    break;
            }
        }

        return options;
    }


}