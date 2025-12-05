
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Interface.Terminal;

internal partial class CLI {

    private int ListGames() {
        try {
            Dictionary<string, Core.Utils.GameModuleInfo> modules = _engine.Modules(Core.Utils.ModuleFilter.All);
            if (modules.Count == 0) {
                System.Console.WriteLine("No modules found.");
                return 0;
            }
            foreach (KeyValuePair<string, Core.Utils.GameModuleInfo> kv in modules) {
                Core.Utils.GameModuleInfo m = kv.Value;
                string state = m.DescribeState();
                System.Console.WriteLine($"- {m.Name}  (state: {state}; root: {m.GameRoot})");
            }
            return 0;
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"Error listing games: {ex}");
            return -1;
        }
    }

    private int ListOps(string game) {
        try {
            Dictionary<string, Core.Utils.GameModuleInfo> modules = _engine.Modules(Core.Utils.ModuleFilter.All);
            if (!modules.TryGetValue(game, out Core.Utils.GameModuleInfo? mod)) {
                System.Console.WriteLine($"Game '{game}' not found.");
                return 1;
            }
            string? opsFile = mod.OpsFile;
            if (string.IsNullOrWhiteSpace(opsFile) || !System.IO.File.Exists(opsFile)) {
                throw new System.ArgumentException($"Game '{game}' missing ops_file.");
            }
            List<Dictionary<string, object?>>? doc = Core.Engine.LoadOperationsList(opsFile);
            if (doc is null || doc.Count == 0) {
                System.Console.WriteLine($"No operations found for game '{game}'.");
                Core.Diagnostics.Log($"No operations found in ops_file '{opsFile}' for game '{game}'.");
                return 0;
            }
            System.Console.WriteLine($"Operations for game '{game}':");
            foreach (Dictionary<string, object?> op in doc) {
                if (op.TryGetValue("name", out object? nameObj) && nameObj is string name) {
                    System.Console.WriteLine($"- {name}");
                }
            }

            return 0;
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"Error listing operations for game '{game}': {ex}");
            return -1;
        }
    }

    private static void PrintHelp() {
        System.Console.WriteLine(@"RemakeEngine
        TUI Usage:
            engine --tui (to launch terminal ui menu)
        CLI Usage:
            engine --list-games (to list available game modules)
            engine --list-ops <game> (to list available operations for a game module)
            engine --game_module <name|path> --script <action> [--script_type <type>] [--args ""...""] (to manually run an operation directly)
        Other commands:
            --root ""PATH""
            --gui
        ");
    }

    private static string GetArg(string[] args, int index, string error) {
        return args.Length <= index ? throw new System.ArgumentException(error) : args[index];
    }

    private static bool TryResolveInlineGame(InlineOperationOptions options, Dictionary<string, Core.Utils.GameModuleInfo> games, out string? resolvedName) {
        resolvedName = null;
        string GameRoot;

        if (options.GameRoot is null) {
            GameRoot = string.Empty;
        } else {
            GameRoot = options.GameRoot;
        }

        string? identifier = options.GameIdentifier;
        string? preferredRoot = ResolveFullPathSafe(GameRoot);

        if (!string.IsNullOrWhiteSpace(identifier)) {
            foreach (KeyValuePair<string, Core.Utils.GameModuleInfo> kv in games) {
                if (string.Equals(kv.Key, identifier, System.StringComparison.OrdinalIgnoreCase)) {
                    resolvedName = kv.Key;
                    ApplyGameOverrides(games, resolvedName, preferredRoot, options.OpsFile);
                    return true;
                }
            }

            string? identifierPath = ResolveFullPathSafe(identifier);
            if (!string.IsNullOrWhiteSpace(identifierPath)) {
                foreach (KeyValuePair<string, Core.Utils.GameModuleInfo> kv in games) {
                    if (kv.Value.GameRoot is not null) {
                        string? existingRoot = ResolveFullPathSafe(kv.Value.GameRoot);
                        if (!string.IsNullOrWhiteSpace(existingRoot) && PathsEqual(existingRoot, identifierPath)) {
                            resolvedName = kv.Key;
                            ApplyGameOverrides(games, resolvedName, preferredRoot, options.OpsFile);
                            return true;
                        }
                    }
                }

                if (System.IO.Directory.Exists(identifierPath)) {
                    string inferredName = options.GameName ?? new System.IO.DirectoryInfo(identifierPath).Name;
                    Core.Utils.GameModuleInfo moduleInfo = new Core.Utils.GameModuleInfo {
                        Id = string.Empty,
                        GameRoot = identifierPath,
                        Name = string.Empty,
                        OpsFile = string.Empty,
                        ExePath = string.Empty,
                        Title = string.Empty,
                        Url = string.Empty
                    };
                    if (!string.IsNullOrWhiteSpace(options.OpsFile)) {
                        moduleInfo.OpsFile = ResolveFullPathSafe(options.OpsFile);
                    }
                    games[inferredName] = moduleInfo;
                    resolvedName = inferredName;
                    return true;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(preferredRoot) && System.IO.Directory.Exists(preferredRoot)) {
            string inferredName = options.GameName ?? new System.IO.DirectoryInfo(preferredRoot).Name;
            Core.Utils.GameModuleInfo moduleInfo = new Core.Utils.GameModuleInfo {
                Id = string.Empty,
                GameRoot = preferredRoot,
                Name = string.Empty,
                OpsFile = string.Empty,
                ExePath = string.Empty,
                Title = string.Empty,
                Url = string.Empty
            };
            if (!string.IsNullOrWhiteSpace(options.OpsFile)) {
                moduleInfo.OpsFile = ResolveFullPathSafe(options.OpsFile);
            }
            games[inferredName] = moduleInfo;
            resolvedName = inferredName;
            return true;
        }

        return false;
    }

    private static void ApplyGameOverrides(Dictionary<string, Core.Utils.GameModuleInfo> games, string gameName, string? preferredRoot, string? opsFile) {
        if (!games.TryGetValue(gameName, out Core.Utils.GameModuleInfo? moduleInfo)) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(preferredRoot)) {
            moduleInfo.GameRoot = preferredRoot;
        }

        if (!string.IsNullOrWhiteSpace(opsFile)) {
            moduleInfo.OpsFile = ResolveFullPathSafe(opsFile);
        }
    }

    private static string ResolveFullPathSafe(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path)) {
            try {
                return System.IO.Path.GetFullPath(path);
            } catch {
                return path;
            }
        } else {
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
