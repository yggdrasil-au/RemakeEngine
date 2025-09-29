using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EngineNet.Interface.CLI;

public partial class CliApp {
    private Int32 RunInlineOperation(String[] args) {
        InlineOperationOptions options;
        try {
            options = InlineOperationOptions.Parse(args);
        } catch (ArgumentException ex) {
            Console.Error.WriteLine($"options ERROR: {ex.Message}");
            return 2;
        }

        if (String.IsNullOrWhiteSpace(options.GameIdentifier) && String.IsNullOrWhiteSpace(options.GameRoot)) {
            Console.Error.WriteLine("ERROR: --game_module/--game (or --game-root) is required for inline execution.");
            return 2;
        }

        if (String.IsNullOrWhiteSpace(options.Script) && !options.OperationFields.ContainsKey("script")) {
            Console.Error.WriteLine("ERROR: --script must be provided for inline execution.");
            return 2;
        }

        Dictionary<String, Object?> games = _engine.ListGames();
        if (!TryResolveInlineGame(options, games, out String? gameName)) {
            Console.Error.WriteLine("ERROR: Unable to resolve the specified game/module.");
            return 1;
        }

        Dictionary<String, Object?> op = options.BuildOperation();
        if (!op.TryGetValue("script", out Object? scriptObj) || scriptObj is null || String.IsNullOrWhiteSpace(scriptObj.ToString())) {
            Console.Error.WriteLine("ERROR: Inline operation is missing a script path or identifier.");
            return 2;
        }

        Boolean ok = ExecuteOp(gameName!, games, op, options.PromptAnswers, options.AutoPromptResponses);
        return ok ? 0 : 1;
    }

    private static Boolean IsInlineOperationInvocation(String[] args) {
        Boolean sawGame = false;
        Boolean sawScript = false;

        foreach (String token in args) {
            if (!token.StartsWith("--", StringComparison.Ordinal)) {
                continue;
            }

            String key = NormalizeOptionKey(GetOptionKey(token));
            if (key is "game" or "game_module" or "module" or "gameid" or "game_name" or "game_root") {
                sawGame = true;
            }

            if (key == "script") {
                sawScript = true;
            }
        }

        return sawGame && sawScript;
    }

    private static Boolean TryResolveInlineGame(InlineOperationOptions options, Dictionary<String, Object?> games, out String? resolvedName) {
        resolvedName = null;

        String? identifier = options.GameIdentifier;
        String? preferredRoot = ResolveFullPathSafe(options.GameRoot);

        if (!String.IsNullOrWhiteSpace(identifier)) {
            foreach (KeyValuePair<String, Object?> kv in games) {
                if (String.Equals(kv.Key, identifier, StringComparison.OrdinalIgnoreCase)) {
                    resolvedName = kv.Key;
                    ApplyGameOverrides(games, resolvedName, preferredRoot, options.OpsFile);
                    return true;
                }
            }

            String? identifierPath = ResolveFullPathSafe(identifier);
            if (!String.IsNullOrWhiteSpace(identifierPath)) {
                foreach (KeyValuePair<String, Object?> kv in games) {
                    if (kv.Value is Dictionary<String, Object?> info && info.TryGetValue("game_root", out Object? rootObj) && rootObj is not null) {
                        String existingRoot = ResolveFullPathSafe(rootObj.ToString());
                        if (!String.IsNullOrWhiteSpace(existingRoot) && PathsEqual(existingRoot, identifierPath)) {
                            resolvedName = kv.Key;
                            ApplyGameOverrides(games, resolvedName, preferredRoot, options.OpsFile);
                            return true;
                        }
                    }
                }

                if (Directory.Exists(identifierPath)) {
                    String inferredName = options.GameName ?? new DirectoryInfo(identifierPath).Name;
                    Dictionary<String, Object?> info = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase) {
                        ["game_root"] = identifierPath
                    };
                    if (!String.IsNullOrWhiteSpace(options.OpsFile)) {
                        info["ops_file"] = ResolveFullPathSafe(options.OpsFile);
                    }
                    games[inferredName] = info;
                    resolvedName = inferredName;
                    return true;
                }
            }
        }
        if (!String.IsNullOrWhiteSpace(preferredRoot) && Directory.Exists(preferredRoot)) {
            String inferredName = options.GameName ?? new DirectoryInfo(preferredRoot).Name;
            Dictionary<String, Object?> info = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase) {
                ["game_root"] = preferredRoot
            };
            if (!String.IsNullOrWhiteSpace(options.OpsFile)) {
                info["ops_file"] = ResolveFullPathSafe(options.OpsFile);
            }
            games[inferredName] = info;
            resolvedName = inferredName;
            return true;
        }

        return false;
    }

    private static void ApplyGameOverrides(Dictionary<String, Object?> games, String gameName, String? preferredRoot, String? opsFile) {
        if (!games.TryGetValue(gameName, out Object? infoObj) || infoObj is not Dictionary<String, Object?> info) {
            return;
        }

        if (!String.IsNullOrWhiteSpace(preferredRoot)) {
            info["game_root"] = preferredRoot;
        }

        if (!String.IsNullOrWhiteSpace(opsFile)) {
            info["ops_file"] = ResolveFullPathSafe(opsFile);
        }
    }

    private static String? ResolveFullPathSafe(String? path) {
        if (String.IsNullOrWhiteSpace(path)) {
            return null;
        }

        try {
            return Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static Boolean PathsEqual(String a, String b) {
        String normalizedA = NormalizePath(a);
        String normalizedB = NormalizePath(b);
        return OperatingSystem.IsWindows()
            ? String.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase)
            : String.Equals(normalizedA, normalizedB, StringComparison.Ordinal);
    }

    private static String NormalizePath(String path) {
        String full = ResolveFullPathSafe(path) ?? path;
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static String GetOptionKey(String token) {
        String trimmed = token.StartsWith("--", StringComparison.Ordinal) ? token.Substring(2) : token;
        Int32 eq = trimmed.IndexOf('=');
        return eq >= 0 ? trimmed.Substring(0, eq) : trimmed;
    }

    private static String NormalizeOptionKey(String key) {
        return key.Replace('-', '_').Trim().ToLowerInvariant();
    }

    private static Object? ParseValueToken(String value) {
        String trimmed = value.Trim();
        if (trimmed.Length == 0) {
            return String.Empty;
        }

        if (String.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (Boolean.TryParse(trimmed, out Boolean boolValue)) {
            return boolValue;
        }

        if (Int64.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int64 longValue)) {
            return longValue;
        }

        if (Double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out Double doubleValue)) {
            return doubleValue;
        }

        if ((trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))) {
            try {
                using JsonDocument doc = JsonDocument.Parse(trimmed);
                return FromJsonElement(doc.RootElement);
            } catch (JsonException) {
                // fall back to string literal
            }
        }

        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal))) {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static Object? FromJsonElement(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => FromJsonElement(p.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(FromJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out Int64 l) ? l : element.TryGetDouble(out Double d) ? d : element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static IEnumerable<String> ParseArgsList(String raw) {
        String trimmed = raw.Trim();
        if (trimmed.Length == 0) {
            yield break;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
            foreach (String item in ParseArgsJson(trimmed)) {
                yield return item;
            }
            yield break;
        }

        List<String> parsed = ParseArgsJson($"[{trimmed}]").ToList();
        if (parsed.Count > 0) {
            foreach (String item in parsed) {
                yield return item;
            }
            yield break;
        }

        String[] commaSplit = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (commaSplit.Length > 0) {
            foreach (String segment in commaSplit) {
                String value = StripEnclosingQuotes(segment.Trim());
                if (value.Length > 0) {
                    yield return value;
                }
            }
            yield break;
        }

        yield return StripEnclosingQuotes(trimmed);
    }

    private static IEnumerable<String> ParseArgsJson(String json) {
        try {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                return Array.Empty<String>();
            }

            List<String> values = new List<String>();
            foreach (JsonElement element in doc.RootElement.EnumerateArray()) {
                values.Add(element.ToString());
            }
            return values;
        } catch (JsonException) {
            return Array.Empty<String>();
        }
    }

    private static String StripEnclosingQuotes(String value) {
        if ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
            (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))) {
            return value.Length >= 2 ? value.Substring(1, value.Length - 2) : String.Empty;
        }
        return value;
    }

    public class InlineOperationOptions {
        public String? GameIdentifier { get; private set; }
        public String? GameRoot { get; private set; }
        public String? GameName { get; private set; }
        public String? OpsFile { get; private set; }
        public String? Script { get; private set; }
        public String? ScriptType { get; private set; }
        public Dictionary<String, Object?> OperationFields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<String, Object?> PromptAnswers { get; } = new(StringComparer.OrdinalIgnoreCase); // respond to operations.toml prompts
        public Dictionary<String, String> AutoPromptResponses { get; } = new(StringComparer.OrdinalIgnoreCase); // responde to lua prompt() calls

        private readonly List<String> _args = new();
        private Boolean _argsOverride;

        public static InlineOperationOptions Parse(String[] args) {
            InlineOperationOptions options = new InlineOperationOptions();

            for (Int32 index = 0; index < args.Length; index++) {
                String token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal)) {
                    continue;
                }

                String key = token.Substring(2);
                String? value = null;
                if (key.Contains('=', StringComparison.Ordinal)) {
                    String[] kv = key.Split('=', 2);
                    key = kv[0];
                    value = kv[1];
                } else if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
                    value = args[++index];
                }

                String normalized = NormalizeOptionKey(key);

                switch (normalized) {
                    case "game":
                    case "game_module":
                    case "module":
                    case "gameid":
                        if (String.IsNullOrWhiteSpace(value)) {
                            throw new ArgumentException($"Option '--{key}' requires a value.");
                        }
                        options.GameIdentifier = value;
                        break;
                    case "game_root":
                        if (String.IsNullOrWhiteSpace(value)) {
                            throw new ArgumentException("Option '--game-root' requires a directory path.");
                        }
                        options.GameRoot = value;
                        break;
                    case "game_name":
                        if (String.IsNullOrWhiteSpace(value)) {
                            throw new ArgumentException("Option '--game-name' requires a value.");
                        }
                        options.GameName = value;
                        break;
                    case "ops_file":
                        if (String.IsNullOrWhiteSpace(value)) {
                            throw new ArgumentException("Option '--ops-file' requires a value.");
                        }
                        options.OpsFile = value;
                        break;
                    case "script":
                        if (String.IsNullOrWhiteSpace(value)) {
                            throw new ArgumentException("Option '--script' requires a value.");
                        }
                        options.Script = value;
                        break;
                    case "script_type":
                    case "type":
                        if (!String.IsNullOrWhiteSpace(value)) {
                            // Handle common typos and aliases
                            String normalizedType = value.ToLowerInvariant();
                            options.ScriptType = normalizedType switch {
                                "lau" => "lua",  // Common typo
                                "js" => "javascript",
                                _ => value
                            };
                        }
                        break;
                    case "arg":
                        if (value is null) {
                            throw new ArgumentException("Option '--arg' requires a value.");
                        }
                        options._args.Add(value);
                        break;
                    case "args":
                        if (value is null) {
                            throw new ArgumentException("Option '--args' requires a value.");
                        }
                        foreach (String item in ParseArgsList(value)) {
                            options._args.Add(item);
                        }
                        break;
                    case "answer":
                        if (value is null) {
                            throw new ArgumentException("Option '--answer' requires KEY=VALUE.");
                        }
                        (String answerKey, Object? answerValue) = ParseKeyValue(value);
                        options.PromptAnswers[answerKey] = answerValue;
                        break;
                    case "auto_prompt":
                        if (value is null) {
                            throw new ArgumentException("Option '--auto_prompt' requires PROMPT_ID=RESPONSE.");
                        }
                        (String promptId, Object? promptResponse) = ParseKeyValue(value);
                        options.AutoPromptResponses[promptId] = promptResponse?.ToString() ?? String.Empty;
                        break;
                    case "set":
                        if (value is null) {
                            throw new ArgumentException("Option '--set' requires KEY=VALUE.");
                        }
                        (String setKey, Object? setValue) = ParseKeyValue(value);
                        String normalizedSetKey = NormalizeOptionKey(setKey);
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

        public Dictionary<String, Object?> BuildOperation() {
            Dictionary<String, Object?> op = new Dictionary<String, Object?>(OperationFields, StringComparer.OrdinalIgnoreCase);

            if (!op.ContainsKey("script_type") && !String.IsNullOrWhiteSpace(ScriptType)) {
                op["script_type"] = ScriptType;
            }

            if (!String.IsNullOrWhiteSpace(Script)) {
                op["script"] = Script;
            }

            if (!op.ContainsKey("args") && !_argsOverride && _args.Count > 0) {
                op["args"] = _args.Select(a => (Object?)a).ToList();
            }

            return op;
        }
    }

    private static (String key, Object? value) ParseKeyValue(String input) {
        Int32 idx = input.IndexOf('=');
        if (idx < 0) {
            throw new ArgumentException($"Expected KEY=VALUE pair but received '{input}'.");
        }

        String key = input.Substring(0, idx).Trim();
        String raw = input.Substring(idx + 1).Trim();
        return (key, ParseValueToken(raw));
    }

    private static String NormalizeOperationKey(String key) {
        return key.Replace('-', '_').Trim();
    }
}
