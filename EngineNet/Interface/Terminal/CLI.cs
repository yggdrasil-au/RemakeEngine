

using System.Linq;
using System.Collections.Generic;
using Tomlyn;


namespace EngineNet.Interface.Terminal;

internal partial class CLI {
    private readonly Core.Engine _engine;

    internal CLI(Core.Engine engine) => _engine = engine;

    internal async System.Threading.Tasks.Task<int> Run(string[] args) {
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

        if (args.Length == 0) {
            throw new System.NotImplementedException();
            //return await new Interface.Terminal.TUI(_engine).RunInteractiveMenuAsync();
        }

        if (IsInlineOperationInvocation(args)) {
            return RunInlineOperation(args);
        }

        string cmd = args[0].ToLowerInvariant();
        switch (cmd) {
            case "help":
            case "-h":
            case "--help":
                PrintHelp();
                return 0;
            case "--tui":
            case "--menu":
                throw new System.NotImplementedException();
                //return await new TUI(_engine).RunInteractiveMenuAsync();
            case "--list-games":
                return ListGames();
            case "--list-ops":
                return ListOps(GetArg(args, 1, "<game> required for list-ops"));
            default:
                System.Console.WriteLine($"Unknown command '{args[0]}'.");
                PrintHelp();
                return 2;
        }
    }

    private int ListGames() {
        Dictionary<string, object?> games = _engine.ListGames();
        if (games.Count == 0) {
            System.Console.WriteLine("No games found in EngineApps/Games.");
            return 0;
        }
        foreach ((string name, object? obj) in games) {
            if (obj is Dictionary<string, object?> dict && dict.TryGetValue("game_root", out object? root)) {
                System.Console.WriteLine($"- {name}  (root: {root})");
            }
        }
        return 0;
    }

    private int ListOps(string game) {
        Dictionary<string, object?> games = _engine.ListGames();
        if (!games.TryGetValue(game, out object? g)) {
            System.Console.WriteLine($"Game '{game}' not found.");
            return 1;
        }
        string opsFile = (g is Dictionary<string, object?> gdict && gdict.TryGetValue("ops_file", out object? of) && of is string s) ? s : throw new System.ArgumentException($"Game '{game}' missing ops_file.");
        List<Dictionary<string, object?>> doc = Core.Engine.LoadOperationsList(opsFile);
        System.Console.WriteLine($"Operations for game '{game}':");
        foreach (Dictionary<string, object?> op in doc) {
            if (op.TryGetValue("name", out object? nameObj) && nameObj is string name) {
                System.Console.WriteLine($"- {name}");
            }
        }

        return 0;
    }

    private static void PrintHelp() {
            System.Console.WriteLine(@"RemakeEngine
        TUI Usage:
            engine [--root PATH] --tui (to launch terminal ui menu)
        CLI Usage:
            engine [--root PATH] --list-games (to list available game modules)
            engine [--root PATH] --list-ops <game> (to list available operations for a game module)
            engine [--root PATH] --game_module <name|path> --script <action> [--script_type <type>] [--args ""...""] (to manually run an operation directly)
        Other commands:
            engine [--root PATH] --gui (to launch GUI application)
        ");
    }

    private static string GetArg(string[] args, int index, string error) {
        return args.Length <= index ? throw new System.ArgumentException(error) : args[index];
    }

    internal int RunInlineOperation(string[] args) {
        InlineOperationOptions options;
        try {
            options = InlineOperationOptions.Parse(args);
        } catch (System.ArgumentException ex) {
            System.Console.Error.WriteLine($"options ERROR: {ex.Message}");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.GameIdentifier) && string.IsNullOrWhiteSpace(options.GameRoot)) {
            System.Console.Error.WriteLine("ERROR: --game_module/--game (or --game-root) is required for inline execution.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.Script) && !options.OperationFields.ContainsKey("script")) {
            System.Console.Error.WriteLine("ERROR: --script must be provided for inline execution.");
            return 2;
        }

        Dictionary<string, object?> games = _engine.ListGames();
        if (!TryResolveInlineGame(options, games, out string? gameName)) {
            System.Console.Error.WriteLine("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        Dictionary<string, object?> op = options.BuildOperation();
        if (!op.TryGetValue("script", out object? scriptObj) || scriptObj is null || string.IsNullOrWhiteSpace(scriptObj.ToString())) {
            System.Console.Error.WriteLine("ERROR: Inline operation is missing a script path or identifier.");
            return 2;
        }

        bool ok = new Utils().ExecuteOp(_engine, gameName!, games, op, options.PromptAnswers, options.AutoPromptResponses);
        return ok ? 0 : 1;
    }

    internal static bool IsInlineOperationInvocation(string[] args) {
        bool sawGame = false;
        bool sawScript = false;

        foreach (string token in args) {
            if (!token.StartsWith("--", System.StringComparison.Ordinal)) {
                continue;
            }

            string key = NormalizeOptionKey(GetOptionKey(token));
            if (key is "game" or "game_module" or "module" or "gameid" or "game_name" or "game_root") {
                sawGame = true;
            }

            if (key == "script") {
                sawScript = true;
            }
        }

        return sawGame && sawScript;
    }

    private static bool TryResolveInlineGame(InlineOperationOptions options, Dictionary<string, object?> games, out string? resolvedName) {
        resolvedName = null;

        string? identifier = options.GameIdentifier;
        string? preferredRoot = ResolveFullPathSafe(options.GameRoot);

        if (!string.IsNullOrWhiteSpace(identifier)) {
            foreach (KeyValuePair<string, object?> kv in games) {
                if (string.Equals(kv.Key, identifier, System.StringComparison.OrdinalIgnoreCase)) {
                    resolvedName = kv.Key;
                    ApplyGameOverrides(games, resolvedName, preferredRoot, options.OpsFile);
                    return true;
                }
            }

            string? identifierPath = ResolveFullPathSafe(identifier);
            if (!string.IsNullOrWhiteSpace(identifierPath)) {
                foreach (KeyValuePair<string, object?> kv in games) {
                    if (kv.Value is Dictionary<string, object?> info && info.TryGetValue("game_root", out object? rootObj) && rootObj is not null) {
                        string? existingRoot = ResolveFullPathSafe(rootObj.ToString());
                        if (!string.IsNullOrWhiteSpace(existingRoot) && PathsEqual(existingRoot, identifierPath)) {
                            resolvedName = kv.Key;
                            ApplyGameOverrides(games, resolvedName, preferredRoot, options.OpsFile);
                            return true;
                        }
                    }
                }

                if (System.IO.Directory.Exists(identifierPath)) {
                    string inferredName = options.GameName ?? new System.IO.DirectoryInfo(identifierPath).Name;
                    Dictionary<string, object?> info = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                        ["game_root"] = identifierPath
                    };
                    if (!string.IsNullOrWhiteSpace(options.OpsFile)) {
                        info["ops_file"] = ResolveFullPathSafe(options.OpsFile);
                    }
                    games[inferredName] = info;
                    resolvedName = inferredName;
                    return true;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(preferredRoot) && System.IO.Directory.Exists(preferredRoot)) {
            string inferredName = options.GameName ?? new System.IO.DirectoryInfo(preferredRoot).Name;
            Dictionary<string, object?> info = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                ["game_root"] = preferredRoot
            };
            if (!string.IsNullOrWhiteSpace(options.OpsFile)) {
                info["ops_file"] = ResolveFullPathSafe(options.OpsFile);
            }
            games[inferredName] = info;
            resolvedName = inferredName;
            return true;
        }

        return false;
    }

    private static void ApplyGameOverrides(Dictionary<string, object?> games, string gameName, string? preferredRoot, string? opsFile) {
        if (!games.TryGetValue(gameName, out object? infoObj) || infoObj is not Dictionary<string, object?> info) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(preferredRoot)) {
            info["game_root"] = preferredRoot;
        }

        if (!string.IsNullOrWhiteSpace(opsFile)) {
            info["ops_file"] = ResolveFullPathSafe(opsFile);
        }
    }

    private static string? ResolveFullPathSafe(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        try {
            return System.IO.Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static bool PathsEqual(string a, string b) {
        string normalizedA = NormalizePath(a);
        string normalizedB = NormalizePath(b);
        return System.OperatingSystem.IsWindows()
            ? string.Equals(normalizedA, normalizedB, System.StringComparison.OrdinalIgnoreCase)
            : string.Equals(normalizedA, normalizedB, System.StringComparison.Ordinal);
    }

    private static string NormalizePath(string path) {
        string full = ResolveFullPathSafe(path) ?? path;
        return full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private static string GetOptionKey(string token) {
        string trimmed = token.StartsWith("--", System.StringComparison.Ordinal) ? token.Substring(2) : token;
        int eq = trimmed.IndexOf('=');
        return eq >= 0 ? trimmed.Substring(0, eq) : trimmed;
    }

    private static string NormalizeOptionKey(string key) {
        return key.Replace('-', '_').Trim().ToLowerInvariant();
    }

    private static object? ParseValueToken(string value) {
        string trimmed = value.Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }

        if (string.Equals(trimmed, "null", System.StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (bool.TryParse(trimmed, out bool boolValue)) {
            return boolValue;
        }

        if (long.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long longValue)) {
            return longValue;
        }

        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out double doubleValue)) {
            return doubleValue;
        }

        if ((trimmed.StartsWith("[", System.StringComparison.Ordinal) && trimmed.EndsWith("]", System.StringComparison.Ordinal)) ||
            (trimmed.StartsWith("{", System.StringComparison.Ordinal) && trimmed.EndsWith("}", System.StringComparison.Ordinal))) {
            try {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(trimmed);
                return FromJsonElement(doc.RootElement);
            } catch (System.Text.Json.JsonException) {
                // fall back to string literal
            }
        }

        if ((trimmed.StartsWith("\"", System.StringComparison.Ordinal) && trimmed.EndsWith("\"", System.StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", System.StringComparison.Ordinal) && trimmed.EndsWith("'", System.StringComparison.Ordinal))) {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static object? FromJsonElement(System.Text.Json.JsonElement element) {
        return element.ValueKind switch {
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => FromJsonElement(p.Value), System.StringComparer.OrdinalIgnoreCase),
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(FromJsonElement).ToList(),
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.TryGetDouble(out double d) ? d : element.GetRawText(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null
        };
    }

    private static IEnumerable<string> ParseArgsList(string raw) {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0) {
            yield break;
        }

        if (trimmed.StartsWith("[", System.StringComparison.Ordinal) && trimmed.EndsWith("]", System.StringComparison.Ordinal)) {
            foreach (string item in ParseArgsJson(trimmed)) {
                yield return item;
            }
            yield break;
        }

        List<string> parsed = ParseArgsJson($"[{trimmed}]").ToList();
        if (parsed.Count > 0) {
            foreach (string item in parsed) {
                yield return item;
            }
            yield break;
        }

        string[] commaSplit = trimmed.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
        if (commaSplit.Length > 0) {
            foreach (string segment in commaSplit) {
                string value = StripEnclosingQuotes(segment.Trim());
                if (value.Length > 0) {
                    yield return value;
                }
            }
            yield break;
        }

        yield return StripEnclosingQuotes(trimmed);
    }

    private static IEnumerable<string> ParseArgsJson(string json) {
        try {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) {
                return System.Array.Empty<string>();
            }

            List<string> values = new List<string>();
            foreach (System.Text.Json.JsonElement element in doc.RootElement.EnumerateArray()) {
                values.Add(element.ToString());
            }
            return values;
        } catch (System.Text.Json.JsonException) {
            return System.Array.Empty<string>();
        }
    }

    private static string StripEnclosingQuotes(string value) {
        if ((value.StartsWith("\"", System.StringComparison.Ordinal) && value.EndsWith("\"", System.StringComparison.Ordinal)) ||
            (value.StartsWith("'", System.StringComparison.Ordinal) && value.EndsWith("'", System.StringComparison.Ordinal))) {
            return value.Length >= 2 ? value.Substring(1, value.Length - 2) : string.Empty;
        }
        return value;
    }

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
                            throw new System.ArgumentException("Option '--args' requires a value.");
                        }
                        foreach (string item in ParseArgsList(value)) {
                            options._args.Add(item);
                        }
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

    private static (string key, object? value) ParseKeyValue(string input) {
        int idx = input.IndexOf('=');
        if (idx < 0) {
            throw new System.ArgumentException($"Expected KEY=VALUE pair but received '{input}'.");
        }

        string key = input.Substring(0, idx).Trim();
        string raw = input.Substring(idx + 1).Trim();
        return (key, ParseValueToken(raw));
    }

    private static string NormalizeOperationKey(string key) {
        return key.Replace('-', '_').Trim();
    }

}
