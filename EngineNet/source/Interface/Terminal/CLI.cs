
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Interface.Terminal;

internal partial class CLI {

    /* :: :: Constructor, Var :: START :: */
    private readonly Core.Engine _engine;
    internal CLI(Core.Engine engine) {
        _engine = engine;
    }

    /// <summary>
    /// Run the CLI
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    internal int Run(string[] args) {
        try {
            // Strip global flags that Program.cs already handled, like --root PATH
            if (args.Length > 0) {
                List<string> list = new List<string>(args);
                for (int i = 0; i < list.Count;) {
                    if (list[i] == "--root") {
                        list.RemoveAt(i);
                        if (i < list.Count) {
                            list.RemoveAt(i); // remove path following --root
                        }

                        continue;
                    }
                    i++;
                }
                args = list.ToArray();
            }

            // Check for inline operation invocation
            if (IsInlineOperationInvocation(args)) {
                // Run operation directly from command-line args
                return RunInlineOperation(args);
            }

            string cmd = args[0].ToLowerInvariant();
            switch (cmd) {
                case "help":
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                case "--list-games":
                    return ListGames();
                case "--list-ops":
                    return ListOps(GetArg(args, 1, "<game> required for list-ops"));
                default:
                    System.Console.WriteLine($"Unknown command '{args[0]}'.");
                    PrintHelp();
                    return 2;
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"CLI Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Run an operation based on command-line arguments.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    internal int RunInlineOperation(string[] args) {
        // Parse inline operation options
        InlineOperationOptions options;

        options = InlineOperationOptions.Parse(args);

        // Validate required options
        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot)) {
            Core.Diagnostics.Log("ERROR: --game_module/--game (or --game-root) is required for inline execution.");
            return 2;
        }

        // Validate script option
        if (string.IsNullOrWhiteSpace(options.Script) && !options.OperationFields.ContainsKey("script")) {
            Core.Diagnostics.Log("ERROR: --script must be provided for inline execution.");
            return 2;
        }

        // Find game modules
        Dictionary<string, Core.Utils.GameModuleInfo> games = _engine.Modules(Core.Utils.ModuleFilter.All);
        if (!TryResolveInlineGame(options, games, out string? gameName)) {
            Core.Diagnostics.Log("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        // Build operation dictionary
        Dictionary<string, object?> op = options.BuildOperation();
        if (!op.TryGetValue("script", out object? scriptObj) || scriptObj is null || string.IsNullOrWhiteSpace(scriptObj.ToString())) {
            Core.Diagnostics.Log("ERROR: Inline operation is missing a script path or identifier.");
            return 2;
        }

        // Execute the operation
        bool ok = new Utils().ExecuteOp(_engine, gameName!, games, op, options.PromptAnswers, options.AutoPromptResponses);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Determine if the command-line arguments indicate an inline operation invocation.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    internal static bool IsInlineOperationInvocation(string[] args) {
        bool sawGame = false;
        bool sawScript = false;

        foreach (string token in args) {
            if (!token.StartsWith("--", System.StringComparison.Ordinal)) {
                continue;
            }

            string key = NormalizeOptionKey(GetOptionKey(token));
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
    internal class InlineOperationOptions {
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
        internal string? ScriptType {
            get; private set;
        }
        internal Dictionary<string, object?> OperationFields { get; } = new(System.StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, object?> PromptAnswers { get; } = new(System.StringComparer.OrdinalIgnoreCase); // respond to operations.toml prompts
        internal Dictionary<string, string> AutoPromptResponses { get; } = new(System.StringComparer.OrdinalIgnoreCase); // responde to lua prompt() calls

        private readonly List<string> _args = new();
        private bool _argsOverride;

        /// <summary>
        /// Parse command-line arguments into inline operation options.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentException"></exception>
        internal static InlineOperationOptions Parse(string[] args) {
            InlineOperationOptions options = new InlineOperationOptions();

            for (int index = 0; index < args.Length; index++) {
                string token = args[index];
                if (!token.StartsWith("--", System.StringComparison.Ordinal)) {
                    continue;
                }

                string key = token.Substring(2);
                string? value = null;
                if (key.Contains('=', System.StringComparison.Ordinal)) {
                    string[] kv = key.Split('=', 2);
                    key = kv[0];
                    value = kv[1];
                } else if (index + 1 < args.Length && !args[index + 1].StartsWith("--", System.StringComparison.Ordinal)) {
                    value = args[++index];
                }

                string normalized = NormalizeOptionKey(key);
                Core.Diagnostics.Log($"DEBUG: Parsing option --{key} (normalized: {normalized}) with value '{value}'");
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
                    case "script_type":
                    case "type":
                        if (!string.IsNullOrWhiteSpace(value)) {
                            // Handle common typos and aliases
                            string normalizedType = value.ToLowerInvariant();
                            options.ScriptType = normalizedType switch {
                                "lau" => "lua",  // Common typo
                                "js" => "javascript",
                                _ => value
                            };
                        }
                        break;
                    case "arg":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--arg' requires a value.");
                        }
                        options._args.Add(value);
                        break;
                    case "args":
                        if (value is null) {
                            Core.Diagnostics.Log("DEBUG: --args missing value");
                            throw new System.ArgumentException("Option '--args' requires a value.");
                        }
                        foreach (string item in ParseArgsList(value)) {
                            options._args.Add(item);
                        }
#if DEBUG
                        Core.Diagnostics.Log($"DEBUG: --args parsed {options._args.Count} items");
                        Core.Diagnostics.Log($"DEBUG: --args items: {string.Join(", ", options._args)}");
                        Core.Diagnostics.Log($"DEBUG: --args raw value: {value}");
#endif
                        break;
                    case "answer":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--answer' requires KEY=VALUE.");
                        }
                        (string answerKey, object? answerValue) = ParseKeyValue(value);
                        options.PromptAnswers[answerKey] = answerValue;
                        break;
                    case "auto_prompt":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--auto_prompt' requires PROMPT_ID=RESPONSE.");
                        }
                        (string promptId, object? promptResponse) = ParseKeyValue(value);
                        options.AutoPromptResponses[promptId] = promptResponse?.ToString() ?? string.Empty;
                        break;
                    case "set":
                        if (value is null) {
                            throw new System.ArgumentException("Option '--set' requires KEY=VALUE.");
                        }
                        (string setKey, object? setValue) = ParseKeyValue(value);
                        string normalizedSetKey = NormalizeOptionKey(setKey);
                        if (normalizedSetKey == "args") {
                            options._argsOverride = true;
                        }
                        options.OperationFields[NormalizeOperationKey(setKey)] = setValue;
                        break;
                    default:
                        if (value is null) {
                            options.OperationFields[NormalizeOperationKey(key)] = true;
                        } else {
                            if (NormalizeOptionKey(key) == "args") {
                                options._argsOverride = true;
                            }
                            options.OperationFields[NormalizeOperationKey(key)] = ParseValueToken(value);
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
            Dictionary<string, object?> op = new Dictionary<string, object?>(OperationFields, System.StringComparer.OrdinalIgnoreCase);

            if (!op.ContainsKey("script_type") && !string.IsNullOrWhiteSpace(ScriptType)) {
                op["script_type"] = ScriptType;
            }

            if (!string.IsNullOrWhiteSpace(Script)) {
                op["script"] = Script;
            }

            if (!op.ContainsKey("args") && !_argsOverride && _args.Count > 0) {
                op["args"] = _args.Select(a => a).ToList();
            }

            return op;
        }
    }

}
