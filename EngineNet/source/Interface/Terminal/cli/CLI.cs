namespace EngineNet.Interface.Terminal;

using Interface;

public sealed class CLI {

    private readonly MiniEngineFace Engine;

    public CLI(MiniEngineFace engine) {
        Engine = engine;
    }

    /// <summary>
    /// Run the CLI
    /// </summary>
    public async System.Threading.Tasks.Task<int> RunAsync(string[] args, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            if (args.Length == 0) {
                StaticHelpers.PrintHelp();
                return 0;
            }

            InlineOperationOptions options = Parse(args: args);

            if (!string.IsNullOrWhiteSpace(options.PrimaryCommand)) {
                Shared.IO.Diagnostics.Trace($"CLI Command: {options.PrimaryCommand}");
                switch (options.PrimaryCommand) {
                    case "help":
                    case "h":
                        StaticHelpers.PrintHelp();
                        return 0;
                    case "version":
                    case "v":
                        System.Console.WriteLine($"Remake Engine");
                        System.Console.WriteLine($"");
                        System.Console.WriteLine($"Build: {EngineBuildInfo.BuildNumber}");
                        System.Console.WriteLine($"Version: {EngineBuildInfo.ProjectVersion}");
                        System.Console.WriteLine($"Commit: {EngineBuildInfo.GitCommitHash}");
                        return 0;
                    case "list_games":
                        return StaticHelpers.ListGames(Engine);
                    case "list_internal":
                        return StaticHelpers.ListInternal(Engine);
                    case "list_ops":
                        return StaticHelpers.ListOps(game: options.PrimaryCommandArg!, Engine);
                }
            }

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

            if (StaticHelpers.IsInlineOperationInvocation(args: args)) {
                Shared.IO.Diagnostics.Trace("Detected inline operation invocation.");
                return await ExecuteOperations.RunInlineOperationAsync(options: options, Engine: Engine, cancellationToken: cancellationToken);
            }

            System.Console.WriteLine($"Unknown operation execution format.");
            StaticHelpers.PrintHelp();
            return 2;

        } catch (System.OperationCanceledException) {
            System.Console.WriteLine("\nOperation cancelled by user.");
            throw;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"CLI Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Parse command-line arguments into inline operation options.
    /// </summary>
    public static InlineOperationOptions Parse(string[] args) {
        InlineOperationOptions options = new();

        for (int index = 0; index < args.Length; index++) {
            string token = args[index];
            if (!token.StartsWith("--", comparisonType: System.StringComparison.Ordinal)) {
                System.Console.WriteLine($"WARNING: Potentially unsupported argument '{token}' with incorrect format or simply unknown. It won't be handled/used.");
                continue;
            }

            string key = token.Substring(startIndex: 2);
            string? value = null;
            bool hasEquals = false;

            if (key.Contains('=', comparisonType: System.StringComparison.Ordinal)) {
                string[] kv = key.Split(separator: '=', count: 2);
                key = kv[0];
                value = kv[1];
                hasEquals = true;
            }

            string normalized = StaticHelpers.NormalizeOptionKey(key: key);
            Shared.IO.Diagnostics.Log($"DEBUG: Parsing option --{key} (normalized: {normalized}) with value '{value}'");

            string? ConsumeValue() {
                if (hasEquals) {
                    return value;
                }
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", comparisonType: System.StringComparison.Ordinal)) {
                    return args[++index];
                }
                return null;
            }

            switch (normalized) {
                case "help":
                case "h":
                case "version":
                case "v":
                case "list_games":
                case "list_internal":
                    options.PrimaryCommand = normalized;
                    break;
                case "list_ops":
                    options.PrimaryCommand = normalized;
                    options.PrimaryCommandArg = ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value.");
                    break;
                case "internal":
                    options.InternalModuleIdentifier = ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value.");
                    break;
                case "game":
                case "game_module":
                case "module":
                case "gameid":
                    options.GameIdentifier = ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value.");
                    break;
                case "game_root":
                    options.GameRoot = ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a directory path.");
                    break;
                case "game_name":
                    options.GameName = ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value.");
                    break;
                case "ops_file":
                    options.OpsFile = ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value.");
                    break;
                case "script":
                    options.Script = ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value.");
                    break;
                case "run_op":
                    options.RunOperationSelector = StaticHelpers.ParseValueToken(ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires an operation name or ID."));
                    break;
                case "run_all":
                    string? runAllVal = ConsumeValue();
                    options.RunAll = runAllVal is null || StaticHelpers.IsTruthy(StaticHelpers.ParseValueToken(runAllVal));
                    break;
                case "script_type":
                case "type":
                    string? typeVal = ConsumeValue();
                    if (!string.IsNullOrWhiteSpace(typeVal)) {
                        string normalizedType = typeVal.ToLowerInvariant();
                        options.ScriptType = normalizedType switch {
                            "lau" => "lua",
                            "javascript" => "js",
                            _ => typeVal
                        };
                    }
                    break;
                case "arg":
                    options._args.Add(item: ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value."));
                    break;
                case "args":
                    foreach (string item in StaticHelpers.ParseArgsList(raw: ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires a value."))) {
                        options._args.Add(item: item);
                    }
                    break;
                case "answer":
                    (string answerKey, object? answerValue) = StaticHelpers.ParseKeyValue(input: ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires KEY=VALUE."));
                    options.PromptAnswers[key: answerKey] = answerValue;
                    break;
                case "auto_prompt":
                    (string promptId, object? promptResponse) = StaticHelpers.ParseKeyValue(input: ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires PROMPT_ID=RESPONSE."));
                    options.AutoPromptResponses[key: promptId] = promptResponse?.ToString() ?? string.Empty;
                    break;
                case "set":
                    (string setKey, object? setValue) = StaticHelpers.ParseKeyValue(input: ConsumeValue() ?? throw new System.ArgumentException($"Option '--{key}' requires KEY=VALUE."));
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