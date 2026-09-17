namespace EngineNet.Terminal;

public sealed partial class CLI {

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

    private int ListOps(string game) {
        try {
            // Find the game module
            Core.Data.GameModules modules = Engine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.All);
            if (!modules.TryGetValue(key: game, out Core.Data.GameModuleInfo? mod)) {
                System.Console.WriteLine($"Game '{game}' not found.");
                return 1;
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

            WritePreparedOperationWarnings(preparedOps: preparedOps, game: game, opsFile: opsFile);

            // Print operations
            System.Console.WriteLine($"Operations for game '{game}':");
            foreach (Core.Data.PreparedOperation op in preparedOps.InitOperations) {
                System.Console.WriteLine($"- [init] {FormatPreparedOperation(op: op)}");
            }
            foreach (Core.Data.PreparedOperation op in preparedOps.RegularOperations) {
                System.Console.WriteLine($"- {FormatPreparedOperation(op: op)}");
            }

            return 0;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error listing operations for game '{game}': {ex}");
            return -1;
        }
    }

    private static void PrintHelp() {
        Shared.IO.Diagnostics.Trace("Displaying CLI help.");
        System.Console.WriteLine(@"RemakeEngine
        TUI Usage:
            engine --tui (to launch terminal ui menu)
        CLI Usage:
            engine --list-games (to list available game modules)
            engine --list-ops <game> (to list available operations for a game module)
            engine --game_module <name|id|path> --run_op <name|id> (to run a defined operation from Operations.toml)
            engine --game_module <name|id|path> --run_all (to run the module's configured run-all sequence)
            engine --game_module <name|id|path> --script <action> [--script_type <type>] [--args '""<arg>"",""<arg>""'] (to manually run an operation directly)
        Other commands:
            --root ""PATH""
            --gui
        ");
    }

    private static string GetArg(string[] args, int index, string error) {
        return args.Length <= index ? throw new System.ArgumentException(error) : args[index];
    }

    private static bool TryResolveInlineGame(InlineOperationOptions options, Core.Data.GameModules games, out string? resolvedName) {
        resolvedName = null;
        string GameRoot;

        if (options.GameRoot is null) {
            GameRoot = string.Empty;
        } else {
            GameRoot = options.GameRoot;
        }

        string? identifier = options.GameIdentifier;
        string? preferredRoot = ResolveFullPathSafe(path: GameRoot);

        if (!string.IsNullOrWhiteSpace(identifier)) {
            foreach (KeyValuePair<string, Core.Data.GameModuleInfo> kv in games) {
                if (!string.Equals(a: kv.Key, b: identifier, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                resolvedName = kv.Key;
                ApplyGameOverrides(games: games, gameName: resolvedName, preferredRoot: preferredRoot, opsFile: options.OpsFile);
                return true;
            }

            if (TryResolveGameByRegisteredId(games: games, identifier: identifier, resolvedName: out string? resolvedById)) {
                resolvedName = resolvedById;
                if (resolvedName is null) {
                    return false;
                }
                ApplyGameOverrides(games: games, gameName: resolvedName, preferredRoot: preferredRoot, opsFile: options.OpsFile);
                return true;
            }

            string identifierPath = ResolveFullPathSafe(path: identifier);
            if (!string.IsNullOrWhiteSpace(identifierPath)) {
                foreach (KeyValuePair<string, Core.Data.GameModuleInfo> kv in games) {
                    if (kv.Value.GameRoot is null) {
                        continue;
                    }
                    string existingRoot = ResolveFullPathSafe(path: kv.Value.GameRoot);
                    if (string.IsNullOrWhiteSpace(existingRoot) || !PathsEqual(a: existingRoot, b: identifierPath)) {
                        continue;
                    }
                    resolvedName = kv.Key;
                    ApplyGameOverrides(games: games, gameName: resolvedName, preferredRoot: preferredRoot, opsFile: options.OpsFile);
                    return true;
                }

                if (System.IO.Directory.Exists(path: identifierPath)) {
                    string inferredName = options.GameName ?? new System.IO.DirectoryInfo(path: identifierPath).Name;
                    Core.Data.GameModuleInfo moduleInfo = new()
                    {
                        Id = string.Empty,
                        GameRoot = identifierPath,
                        Name = string.Empty,
                        OpsFile = string.Empty,
                        ExePath = string.Empty,
                        Title = string.Empty,
                        Url = string.Empty
                    };
                    if (!string.IsNullOrWhiteSpace(options.OpsFile)) {
                        moduleInfo.OpsFile = ResolveFullPathSafe(path: options.OpsFile);
                    }
                    games[key: inferredName] = moduleInfo;
                    resolvedName = inferredName;
                    return true;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(preferredRoot) && System.IO.Directory.Exists(path: preferredRoot)) {
            string inferredName = options.GameName ?? new System.IO.DirectoryInfo(path: preferredRoot).Name;
            Core.Data.GameModuleInfo moduleInfo = new()
            {
                Id = string.Empty,
                GameRoot = preferredRoot,
                Name = string.Empty,
                OpsFile = string.Empty,
                ExePath = string.Empty,
                Title = string.Empty,
                Url = string.Empty
            };
            if (!string.IsNullOrWhiteSpace(options.OpsFile)) {
                moduleInfo.OpsFile = ResolveFullPathSafe(path: options.OpsFile);
            }
            games[key: inferredName] = moduleInfo;
            resolvedName = inferredName;
            return true;
        }

        return false;
    }

    private static bool TryResolveGameByRegisteredId(Core.Data.GameModules games, string identifier, out string? resolvedName) {
        resolvedName = null;

        if (!long.TryParse(s: identifier, result: out long requestedId)) {
            return false;
        }

        foreach (KeyValuePair<string, Core.Data.GameModuleInfo> kv in games) {
            Core.Data.GameModuleInfo moduleInfo = kv.Value;
            if (!moduleInfo.IsRegistered) {
                continue;
            }

            if (!long.TryParse(s: moduleInfo.Id, result: out long moduleId)) {
                continue;
            }

            if (moduleId == requestedId) {
                resolvedName = kv.Key;
                return true;
            }
        }

        return false;
    }

    private bool TryLoadPreparedOperations(
        string gameName,
        Core.Data.GameModules games,
        string? opsFileOverride,
        out Core.Data.PreparedOperations? preparedOps,
        out int exitCode
    ) {
        preparedOps = null;
        exitCode = 1;

        if (!games.TryGetValue(key: gameName, out Core.Data.GameModuleInfo? moduleInfo)) {
            WriteUserError($"Game '{gameName}' was not found.");
            exitCode = 1;
            return false;
        }

        string opsFile = !string.IsNullOrWhiteSpace(opsFileOverride)
            ? ResolveFullPathSafe(path: opsFileOverride)
            : moduleInfo.OpsFile;

        if (string.IsNullOrWhiteSpace(opsFile) || !System.IO.File.Exists(path: opsFile)) {
            WriteUserError($"Game '{gameName}' is missing an operations file.");
            exitCode = 1;
            return false;
        }

        preparedOps = Engine.OperationsService_LoadAndPrepare(opsFile: opsFile, currentGame: gameName, games: games, engineConfig: Engine.EngineConfig_Data);
        if (!preparedOps.IsLoaded) {
            WriteUserError(preparedOps.ErrorMessage ?? "Failed to load operations.");
            exitCode = 1;
            return false;
        }

        WritePreparedOperationWarnings(preparedOps: preparedOps, game: gameName, opsFile: opsFile);
        exitCode = 0;
        return true;
    }

    private static bool TryResolvePreparedOperation(
        Core.Data.PreparedOperations preparedOps,
        object? selector,
        out Core.Data.PreparedOperation? selected,
        out string? errorMessage
    ) {
        selected = null;
        errorMessage = null;

        List<(Core.Data.PreparedOperation Operation, bool IsInit)> candidates = GetPreparedOperationCandidates(preparedOps: preparedOps);
        if (candidates.Count == 0) {
            errorMessage = "No operations found.";
            return false;
        }

        string selectorText = selector?.ToString()?.Trim() ?? string.Empty;
        if (selectorText.Length == 0) {
            errorMessage = "--run_op requires an operation name or ID.";
            return false;
        }

        bool selectorIsNumeric = long.TryParse(s: selectorText, result: out long selectorId);

        if (selectorIsNumeric) {
            List<(Core.Data.PreparedOperation Operation, bool IsInit)> idMatches = candidates
                .Where(predicate: entry => entry.Operation.OperationId.HasValue && entry.Operation.OperationId.Value == selectorId)
                .ToList();

            if (idMatches.Count == 1) {
                selected = idMatches[index: 0].Operation;
                return true;
            }

            if (idMatches.Count > 1) {
                selected = PromptForPreparedOperationChoice(matches: idMatches, $"Multiple operations share ID {selectorId}. Select one:");
                if (selected is not null) {
                    return true;
                }

                errorMessage = $"Unable to resolve operation ID {selectorId}.";
                return false;
            }
        }

        List<(Core.Data.PreparedOperation Operation, bool IsInit)> nameMatches = candidates
            .Where(predicate: entry => string.Equals(a: entry.Operation.DisplayName, b: selectorText, comparisonType: System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nameMatches.Count == 1) {
            selected = nameMatches[index: 0].Operation;
            return true;
        }

        if (nameMatches.Count > 1) {
            selected = PromptForPreparedOperationChoice(matches: nameMatches, $"Multiple operations share the name '{selectorText}'. Select one:");
            if (selected is not null) {
                return true;
            }

            errorMessage = $"Unable to resolve operation name '{selectorText}'.";
            return false;
        }

        errorMessage = selectorIsNumeric
            ? $"Operation ID {selectorId} was not found."
            : $"Operation '{selectorText}' was not found.";
        return false;
    }

    private static void WriteUserError(string message) {
        System.Console.Error.WriteLine($"ERROR: {message}");
        Shared.IO.Diagnostics.Log($"ERROR: {message}");
    }

    private static void WriteOperationSelectionHint(string gameName, Core.Data.PreparedOperations preparedOps) {
        System.Console.Error.WriteLine($"Use --list-ops {gameName} to inspect the available operations.");

        List<string> options = GetPreparedOperationCandidates(preparedOps: preparedOps)
            .Select(selector: entry => FormatPreparedOperationChoice(op: entry.Operation, isInit: entry.IsInit))
            .ToList();

        if (options.Count == 0) {
            System.Console.Error.WriteLine("No operations are available for this module.");
            return;
        }

        System.Console.Error.WriteLine("Available operations:");
        foreach (string option in options.Take(count: 10)) {
            System.Console.Error.WriteLine($"  - {option}");
        }

        if (options.Count > 10) {
            System.Console.Error.WriteLine($"  ... and {options.Count - 10} more");
        }
    }

    private static Core.Data.PreparedOperation? PromptForPreparedOperationChoice(
        IReadOnlyList<(Core.Data.PreparedOperation Operation, bool IsInit)> matches,
        string message
    ) {
        if (matches.Count == 0) {
            return null;
        }

        if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected) {
            System.Console.WriteLine(message);
            for (int index = 0; index < matches.Count; index++) {
                System.Console.WriteLine($"  {index + 1}. {FormatPreparedOperationChoice(op: matches[index: index].Operation, isInit: matches[index: index].IsInit)}");
            }
            return null;
        }

        while (true) {
            System.Console.WriteLine(message);
            for (int index = 0; index < matches.Count; index++) {
                System.Console.WriteLine($"  {index + 1}. {FormatPreparedOperationChoice(op: matches[index: index].Operation, isInit: matches[index: index].IsInit)}");
            }

            System.Console.Write("Selection (blank to cancel): ");
            string? input = System.Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) {
                return null;
            }

            if (int.TryParse(s: input.Trim(), result: out int choice) && choice >= 1 && choice <= matches.Count) {
                return matches[index: choice - 1].Operation;
            }

            System.Console.WriteLine("Invalid selection. Please enter a valid number.");
        }
    }

    private static List<(Core.Data.PreparedOperation Operation, bool IsInit)> GetPreparedOperationCandidates(Core.Data.PreparedOperations preparedOps) {
        List<(Core.Data.PreparedOperation Operation, bool IsInit)> candidates = new();
        candidates.AddRange(collection: preparedOps.InitOperations.Select(selector: op => (op, true)));
        candidates.AddRange(collection: preparedOps.RegularOperations.Select(selector: op => (op, false)));
        return candidates;
    }

    private static string FormatPreparedOperation(Core.Data.PreparedOperation op) {
        return FormatPreparedOperationChoice(op: op, isInit: false);
    }

    private static string FormatPreparedOperationChoice(Core.Data.PreparedOperation op, bool isInit) {
        string phasePrefix = isInit ? "[init] " : string.Empty;
        string idText = op.OperationId.HasValue ? $"[id={op.OperationId.Value}] " : "[id=?] ";
        string statePrefix = op.HasDuplicateId ? "[dup-id] " : op.HasInvalidId ? "[invalid-id] " : string.Empty;
        return $"{phasePrefix}{statePrefix}{idText}{op.DisplayName}";
    }

    private static void WritePreparedOperationWarnings(Core.Data.PreparedOperations preparedOps, string game, string opsFile) {
        if (preparedOps.Warnings.Count == 0) {
            return;
        }

        foreach (string warning in preparedOps.Warnings) {
            Shared.IO.Diagnostics.Log($"Warning for '{game}' ({opsFile}): {warning}");
        }

        System.Console.ForegroundColor = System.ConsoleColor.Yellow;
        System.Console.WriteLine("Validation Warnings:");
        foreach (string warning in preparedOps.Warnings) {
            System.Console.WriteLine($"  • {warning}");
        }
        System.Console.ResetColor();
        System.Console.WriteLine();
    }

    private static void ApplyGameOverrides(Core.Data.GameModules games, string gameName, string? preferredRoot, string? opsFile) {
        if (!games.TryGetValue(key: gameName, out Core.Data.GameModuleInfo? moduleInfo)) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(preferredRoot)) {
            moduleInfo.GameRoot = preferredRoot;
        }

        if (!string.IsNullOrWhiteSpace(opsFile)) {
            moduleInfo.OpsFile = ResolveFullPathSafe(path: opsFile);
        }
    }

    private static string ResolveFullPathSafe(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (System.IO.File.Exists(path: path) || System.IO.Directory.Exists(path: path)) {
            try {
                return System.IO.Path.GetFullPath(path: path);
            } catch {
                return path;
            }
        } else {
            return path;
        }
    }

    private static bool PathsEqual(string a, string b) {
        string normalizedA = NormalizePath(path: a);
        string normalizedB = NormalizePath(path: b);
        return System.OperatingSystem.IsWindows()
            ? string.Equals(a: normalizedA, b: normalizedB, comparisonType: System.StringComparison.OrdinalIgnoreCase)
            : string.Equals(a: normalizedA, b: normalizedB, comparisonType: System.StringComparison.Ordinal);
    }

    private static string NormalizePath(string path) {
        string full = ResolveFullPathSafe(path: path);
        return full.TrimEnd(trimChars: [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
    }

    private static string GetOptionKey(string token) {
        string trimmed = token.StartsWith("--", comparisonType: System.StringComparison.Ordinal) ? token.Substring(startIndex: 2) : token;
        int eq = trimmed.IndexOf('=');
        return eq >= 0 ? trimmed.Substring(startIndex: 0, length: eq) : trimmed;
    }

    private static string NormalizeOptionKey(string key) {
        return key.Replace(oldChar: '-', newChar: '_').Trim().ToLowerInvariant();
    }

    private static object? ParseValueToken(string value) {
        string trimmed = value.Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }

        if (string.Equals(a: trimmed, b: "null", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (bool.TryParse(trimmed, result: out bool boolValue)) {
            return boolValue;
        }

        if (long.TryParse(s: trimmed, style: System.Globalization.NumberStyles.Integer, provider: System.Globalization.CultureInfo.InvariantCulture, result: out long longValue)) {
            return longValue;
        }

        if (double.TryParse(s: trimmed, style: System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, provider: System.Globalization.CultureInfo.InvariantCulture, result: out double doubleValue)) {
            return doubleValue;
        }

        if ((trimmed.StartsWith("[", comparisonType: System.StringComparison.Ordinal) && trimmed.EndsWith("]", comparisonType: System.StringComparison.Ordinal)) ||
            (trimmed.StartsWith("{", comparisonType: System.StringComparison.Ordinal) && trimmed.EndsWith("}", comparisonType: System.StringComparison.Ordinal))) {
            try {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json: trimmed);
                return FromJsonElement(element: doc.RootElement);
            } catch (System.Text.Json.JsonException) {
                // fall back to string literal
            }
        }

        if ((trimmed.StartsWith("\"", comparisonType: System.StringComparison.Ordinal) && trimmed.EndsWith("\"", comparisonType: System.StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", comparisonType: System.StringComparison.Ordinal) && trimmed.EndsWith("'", comparisonType: System.StringComparison.Ordinal))) {
            return trimmed.Substring(startIndex: 1, length: trimmed.Length - 2);
        }

        return trimmed;
    }

    private static object? FromJsonElement(System.Text.Json.JsonElement element) {
        return element.ValueKind switch {
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(keySelector: p => p.Name, elementSelector: p => FromJsonElement(element: p.Value), comparer: System.StringComparer.OrdinalIgnoreCase),
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(selector: FromJsonElement).ToList(),
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

        if (trimmed.StartsWith("[", comparisonType: System.StringComparison.Ordinal) && trimmed.EndsWith("]", comparisonType: System.StringComparison.Ordinal)) {
            foreach (string item in ParseArgsJson(json: trimmed)) {
                yield return item;
            }
            yield break;
        }

        List<string> parsed = ParseArgsJson(json: $"[{trimmed}]").ToList();
        if (parsed.Count > 0) {
            foreach (string item in parsed) {
                yield return item;
            }
            yield break;
        }

        string[] commaSplit = trimmed.Split(separator: ',', options: System.StringSplitOptions.RemoveEmptyEntries);
        if (commaSplit.Length > 0) {
            foreach (string value in commaSplit.Select(selector: segment => StripEnclosingQuotes(segment.Trim())).Where(predicate: value => value.Length > 0)) {
                yield return value;
            }
            yield break;
        }

        yield return StripEnclosingQuotes(trimmed);
    }

    private static IEnumerable<string> ParseArgsJson(string json) {
        try {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json: json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) {
                return System.Array.Empty<string>();
            }

            List<string> values = new();
            foreach (System.Text.Json.JsonElement element in doc.RootElement.EnumerateArray()) {
                values.Add(item: element.ToString());
            }
            return values;
        } catch (System.Text.Json.JsonException) {
            return System.Array.Empty<string>();
        }
    }

    private static string StripEnclosingQuotes(string value) {
        if ((value.StartsWith("\"", comparisonType: System.StringComparison.Ordinal) && value.EndsWith("\"", comparisonType: System.StringComparison.Ordinal)) ||
            (value.StartsWith("'", comparisonType: System.StringComparison.Ordinal) && value.EndsWith("'", comparisonType: System.StringComparison.Ordinal))) {
            return value.Length >= 2 ? value.Substring(startIndex: 1, length: value.Length - 2) : string.Empty;
        }
        return value;
    }

    private static (string key, object? value) ParseKeyValue(string input) {
        int idx = input.IndexOf('=');
        if (idx < 0) {
            throw new System.ArgumentException($"Expected KEY=VALUE pair but received '{input}'.");
        }

        string key = input.Substring(startIndex: 0, length: idx).Trim();
        string raw = input.Substring(startIndex: idx + 1).Trim();
        return (key, ParseValueToken(raw));
    }

    private static string NormalizeOperationKey(string key) {
        return key.Replace(oldChar: '-', newChar: '_').Trim();
    }

    private static bool IsTruthy(object? value) {
        return value switch {
            null => false,
            bool b => b,
            string s => bool.TryParse(s, result: out bool parsed) && parsed,
            long l => l != 0,
            int i => i != 0,
            short s => s != 0,
            byte b => b != 0,
            _ => true
        };
    }

}
