
using EngineNet.Core.Data;

namespace EngineNet.Core.Services;

/// <summary>
/// Provides shared operations loading, validation, and prompt-flow logic for all interfaces.
/// </summary>
public sealed class OperationsService {

    /* :: :: Vars :: START :: */
    private readonly OperationsLoader _loader;
    private readonly GameRegistry _gameRegistry;
    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    public OperationsService(OperationsLoader loader, GameRegistry gameRegistry) {
        _loader = loader;
        _gameRegistry = gameRegistry;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Loads the operations session for a module, including menu capabilities and latest execution states.
    /// </summary>
    /// <param name="gameName"></param>
    /// <param name="games"></param>
    /// <param name="engineConfig"></param>
    /// <returns></returns>
    public ModuleOperationSession LoadModuleSession(
        string gameName,
        GameModules games,
        IDictionary<string, object?> engineConfig
    ) {
        if (!games.TryGetValue(key: gameName, out GameModuleInfo? module)) {
            PreparedOperations missingModule = new PreparedOperations {
                IsLoaded = false,
                ErrorMessage = $"Game '{gameName}' was not found."
            };
            return new ModuleOperationSession(gameName: gameName, module: null, preparedOperations: missingModule, initOperations: Array.Empty<SessionOperation>(), regularOperations: Array.Empty<SessionOperation>());
        }

        if (string.IsNullOrWhiteSpace(module.OpsFile)) {
            PreparedOperations missingOpsFile = new PreparedOperations {
                IsLoaded = false,
                ErrorMessage = "Selected game is missing operations file."
            };
            return new ModuleOperationSession(gameName: gameName, module: module, preparedOperations: missingOpsFile, initOperations: Array.Empty<SessionOperation>(), regularOperations: Array.Empty<SessionOperation>());
        }

        PreparedOperations prepared = LoadAndPrepare(opsFile: module.OpsFile, currentGame: gameName, games: games, engineConfig: engineConfig);
        IReadOnlyDictionary<long, OperationExecutionStatus> statuses = LoadLatestExecutionStatuses(gameRoot: module.GameRoot);
        List<SessionOperation> initOperations = BuildSessionOperations(preparedOperations: prepared.InitOperations, statuses: statuses);
        List<SessionOperation> regularOperations = BuildSessionOperations(preparedOperations: prepared.RegularOperations, statuses: statuses);

        return new ModuleOperationSession(gameName: gameName, module: module, preparedOperations: prepared, initOperations: initOperations, regularOperations: regularOperations);
    }

    private static List<SessionOperation> BuildSessionOperations(
        IReadOnlyList<PreparedOperation> preparedOperations,
        IReadOnlyDictionary<long, OperationExecutionStatus> statuses
    ) {
        List<SessionOperation> sessionOperations = new List<SessionOperation>(capacity: preparedOperations.Count);

        foreach (PreparedOperation operation in preparedOperations) {
            OperationExecutionStatus status = operation.OperationId.HasValue
                && statuses.TryGetValue(key: operation.OperationId.Value, out OperationExecutionStatus recordedStatus)
                ? recordedStatus
                : OperationExecutionStatus.NotRun;
            sessionOperations.Add(item: new SessionOperation(operation: operation, status: status));
        }

        return sessionOperations;
    }

    /// <summary>
    /// Loads operations from an ops file and prepares structured metadata for UI consumption.
    /// </summary>
    /// <param name="opsFile"></param>
    /// <param name="currentGame"></param>
    /// <param name="games"></param>
    /// <param name="engineConfig"></param>
    /// <returns></returns>
    public PreparedOperations LoadAndPrepare(
        string opsFile,
        string? currentGame = null,
        Core.Data.GameModules? games = null,
        IDictionary<string, object?>? engineConfig = null
    ) {
        PreparedOperations result = new PreparedOperations();
        if (string.IsNullOrWhiteSpace(opsFile) || !System.IO.File.Exists(path: opsFile)) {
            result.IsLoaded = false;
            result.ErrorMessage = "Operations file is missing.";
            return result;
        }

        List<Dictionary<string, object?>>? allOps = _loader.LoadOperations(opsFile: opsFile);
        if (allOps is null) {
            result.IsLoaded = false;
            result.ErrorMessage = "Failed to load operations file.";
            return result;
        }

        result.IsLoaded = true;

        // Build context once if game info is provided
        Dictionary<string, object?>? ctx = null;
        if (!string.IsNullOrEmpty(currentGame) && games != null && engineConfig != null) {
            try {
                ctx = Core.Utils.ExecutionContextBuilder.Build(currentGame: currentGame, games: games, engineConfig: engineConfig);
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"Failed building context for game '{currentGame}'.", ex: ex);
                /* ignore context build failure for menu rendering */
            }
        }

        Dictionary<long, int> idCounts = new Dictionary<long, int>();
        HashSet<Dictionary<string, object?>> invalidIdOps = new HashSet<Dictionary<string, object?>>();

        foreach (Dictionary<string, object?> op in allOps) {
            if (TryGetLong(data: op, out long idValue)) {
                idCounts[key: idValue] = idCounts.TryGetValue(key: idValue, out int count) ? count + 1 : 1;
            } else if (op.ContainsKey(key: "id")) {
                invalidIdOps.Add(item: op);
            }
        }

        HashSet<long> duplicateIds = idCounts.Where(predicate: kv => kv.Value > 1).Select(selector: kv => kv.Key).ToHashSet();
        if (duplicateIds.Count > 0) {
            result.Warnings.Add(item: $"Duplicate operation IDs found: {string.Join(separator: ", ", values: duplicateIds.OrderBy(keySelector: x => x))}");
        }
        if (invalidIdOps.Count > 0) {
            result.Warnings.Add(item: "One or more operations contain invalid IDs.");
        }

        foreach (Dictionary<string, object?> op in allOps) {
            // Resolve placeholders for UI display only; execution should use raw ops for fresh config values.
            Dictionary<string, object?> resolvedOp = op;
            if (ctx != null) {
                if (Core.Utils.Placeholders.Resolve(op, context: ctx) is Dictionary<string, object?> resolved) {
                    resolvedOp = resolved;
                }
            }

            bool isInit = TryGetBool(data: op, out bool initValue, keys: "init") && initValue;
            bool hasDuplicateId = false;
            bool hasInvalidId = invalidIdOps.Contains(item: op);
            long? id = null;

            if (TryGetLong(data: op, out long idValue)) {
                id = idValue;
                hasDuplicateId = duplicateIds.Contains(item: idValue);
            }

            string displayName = ResolveOperationDisplayName(op: resolvedOp);
            string? scriptPath = GetString(data: resolvedOp, keys: "script");
            string? scriptType = GetString(data: resolvedOp, keys: ["script_type", "scriptType"]);

            PreparedOperation prepared = new PreparedOperation(
                operation: op,
                displayName: displayName,
                operationId: id,
                hasDuplicateId: hasDuplicateId,
                hasInvalidId: hasInvalidId,
                scriptPath: scriptPath,
                scriptType: scriptType
            );

            if (isInit) {
                result.InitOperations.Add(item: prepared);
            } else {
                result.RegularOperations.Add(item: prepared);
            }

            bool isRunAll = (TryGetBool(data: op, out bool runAllDash, keys: "run-all") && runAllDash)
                || (TryGetBool(data: op, out bool runAllUnderscore, keys: "run_all") && runAllUnderscore);
            if (isRunAll) {
                result.RunAllOperations.Add(item: prepared);
            }
        }

        result.HasRunAll = result.RunAllOperations.Count > 0;

        return result;
    }

    /// <summary>
    /// Resolves a consistent display name for an operation using common keys.
    /// </summary>
    /// <param name="op"></param>
    /// <returns></returns>
    private static string ResolveOperationDisplayName(IDictionary<string, object?> op) {
        string? name = GetString(data: op, keys: ["Name", "name"]);
        if (!string.IsNullOrWhiteSpace(name)) {
            return name;
        }

        string? title = GetString(data: op, keys: ["Title", "title"]);
        if (!string.IsNullOrWhiteSpace(title)) {
            return title;
        }

        string? script = GetString(data: op, keys: "script");
        if (!string.IsNullOrWhiteSpace(script)) {
            return script;
        }

        return "(unnamed)";
    }

    /// <summary>
    /// Collects prompt answers for an operation using a UI-supplied prompt handler.
    /// </summary>
    /// <param name="op"></param>
    /// <param name="answers"></param>
    /// <param name="promptHandler"></param>
    /// <param name="defaultsOnly"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> CollectAnswersAsync(
        Dictionary<string, object?> op,
        PromptAnswers answers,
        PromptHandler promptHandler,
        bool defaultsOnly = false,
        CancellationToken cancellationToken = default(CancellationToken)
    ) {
        if (!op.TryGetValue(key: "prompts", out object? promptsObj) || promptsObj is not IList<object?> prompts) {
            return true;
        }

        foreach (object? p in prompts) {
            if (p is not Dictionary<string, object?> prompt) {
                continue;
            }

            if (!TryGetPromptName(prompt: prompt, name: out string name)) {
                continue;
            }

            string type = NormalizePromptType(type: GetString(data: prompt, keys: ["type", "Type"]));
            object? defaultValue = GetPromptDefault(prompt: prompt);

            if (TryGetString(data: prompt, out string? conditionName, keys: "condition") && !string.IsNullOrWhiteSpace(conditionName)) {
                EnsureConditionDefault(prompts: prompts, answers: answers, conditionName: conditionName);
                if (!answers.TryGetValue(key: conditionName, out object? condVal) || condVal is not bool cb || !cb) {
                    answers[key: name] = EmptyValueForType(type: type);
                    continue;
                }
            }

            if (defaultsOnly) {
                answers[key: name] = defaultValue ?? EmptyValueForType(type: type);
                continue;
            }

            string title = ResolvePromptTitle(prompt: prompt, fallbackName: name);
            bool isSecret = TryGetBool(data: prompt, out bool secretValue, keys: ["secret", "Secret"]) && secretValue;
            IReadOnlyList<PromptChoice> choices = ResolvePromptChoices(prompt: prompt);

            PromptRequest request = new PromptRequest(
                name: name,
                type: type,
                title: title,
                defaultValue: defaultValue,
                choices: choices,
                isSecret: isSecret
            );

            PromptResponse response = await promptHandler(request: request, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            if (response.IsCancelled) {
                return false;
            }

            if (response.UseDefault) {
                answers[key: name] = defaultValue ?? EmptyValueForType(type: type);
            } else {
                answers[key: name] = response.Value;
            }
        }

        return true;
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Helpers :: START :: */

    private static string NormalizePromptType(string? type) {
        string normalized = string.IsNullOrWhiteSpace(type) ? "text" : type.Trim().ToLowerInvariant();
        return normalized switch {
            "confirm" => "confirm",
            "checkbox" => "checkbox",
            "select" => "select",
            _ => "text"
        };
    }

    private static string ResolvePromptTitle(IDictionary<string, object?> prompt, string fallbackName) {
        string? title = GetString(data: prompt, keys: ["message", "Message", "prompt", "Prompt"]);
        return string.IsNullOrWhiteSpace(title) ? fallbackName : title;
    }

    private IReadOnlyList<PromptChoice> ResolvePromptChoices(IDictionary<string, object?> prompt) {
        List<PromptChoice> choices = new List<PromptChoice>();

        if (TryGetString(data: prompt, out string? provider, keys: "choices_provider") && provider == "registry_modules") {
            Core.Data.GameModules registered = _gameRegistry.GetModules(filter: ModuleFilter.Registered);
            Core.Data.GameModules installed = _gameRegistry.GetModules(filter: ModuleFilter.Installed);
            choices.AddRange(collection: registered.Keys.Select(selector: key => new PromptChoice(label: key, isDisabled: installed.ContainsKey(key: key))));
            return choices;
        }

        if (TryGetList(data: prompt, values: out IList<object?>? rawChoices, keys: ["choices", "Choices"]) && rawChoices is not null) {
            choices.AddRange(collection: rawChoices
                .Select(selector: choice => choice?.ToString() ?? string.Empty)
                .Where(predicate: label => !string.IsNullOrWhiteSpace(label))
                .Select(selector: label => new PromptChoice(label: label, isDisabled: false)));
        }

        return choices;
    }

    private static void EnsureConditionDefault(IList<object?> prompts, PromptAnswers answers, string conditionName) {
        if (answers.ContainsKey(key: conditionName)) {
            return;
        }

        foreach (object? candidate in prompts) {
            if (candidate is not Dictionary<string, object?> prompt) {
                continue;
            }

            if (TryGetPromptName(prompt: prompt, name: out string name) && name == conditionName && prompt.TryGetValue(key: "default", out object? def)) {
                answers[key: conditionName] = def;
                return;
            }
        }
    }

    private static bool TryGetPromptName(IDictionary<string, object?> prompt, out string name) {
        name = GetString(data: prompt, keys: ["Name", "name"]) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name)) {
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static object? GetPromptDefault(IDictionary<string, object?> prompt) {
        if (prompt.TryGetValue(key: "default", out object? def)) {
            return def;
        }

        if (prompt.TryGetValue(key: "Default", out object? defAlt)) {
            return defAlt;
        }

        return null;
    }

    private static object? EmptyValueForType(string type) {
        return type switch {
            "confirm" => false,
            "checkbox" => new List<object?>(),
            //"select" => null,
            _ => null
        };
    }

    private static bool TryGetBool(IDictionary<string, object?> data, out bool value, params string[] keys) {
        value = false;
        foreach (string key in keys) {
            if (data.TryGetValue(key: key, out object? raw) && raw is bool b) {
                value = b;
                return true;
            }
        }
        return false;
    }

    private static bool TryGetLong(IDictionary<string, object?> data, out long value) {
        value = 0;
        if (!data.TryGetValue(key: "id", out object? raw) || raw is null) {
            return false;
        }

        try {
            value = System.Convert.ToInt64(raw);
            return true;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Failed to parse operation id value '{raw}'.", ex: ex);
            return false;
        }
    }

    private static string? GetString(IDictionary<string, object?> data, params string[] keys) {
        foreach (string key in keys) {
            if (data.TryGetValue(key: key, out object? raw) && raw is not null) {
                string text = raw.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text)) {
                    return text;
                }
            }
        }
        return null;
    }

    private static bool TryGetString(IDictionary<string, object?> data, out string? value, params string[] keys) {
        value = GetString(data: data, keys: keys);
        return value is not null;
    }

    private static bool TryGetList(IDictionary<string, object?> data, out IList<object?>? values, params string[] keys) {
        values = null;
        foreach (string key in keys) {
            if (data.TryGetValue(key: key, out object? raw) && raw is IList<object?> list) {
                values = list;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Reads the latest execution result for every numerically identified operation in a module log.
    /// </summary>
    /// <param name="gameRoot"></param>
    /// <returns></returns>
    private static IReadOnlyDictionary<long, OperationExecutionStatus> LoadLatestExecutionStatuses(string gameRoot) {
        Dictionary<long, OperationExecutionStatus> statuses = new Dictionary<long, OperationExecutionStatus>();
        string logPath = System.IO.Path.Combine(path1: gameRoot, path2: "operation_execution.log");
        if (!System.IO.File.Exists(path: logPath)) {
            return statuses;
        }

        try {
            foreach (string line in System.IO.File.ReadLines(path: logPath)) {
                string[] parts = line.Split(separator: " | ");
                if (parts.Length < 3 || !long.TryParse(s: parts[0].Trim(), result: out long operationId)) {
                    continue;
                }

                statuses[key: operationId] = parts[2].Trim() == "SUCCESS"
                    ? OperationExecutionStatus.Succeeded
                    : OperationExecutionStatus.Failed;
            }
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"Failed reading '{logPath}'.", ex: ex);
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"Access denied for '{logPath}'.", ex: ex);
        }

        return statuses;
    }
    /* :: :: Helpers :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// UI prompt callback signature.
    /// </summary>
    public delegate Task<PromptResponse> PromptHandler(PromptRequest request, CancellationToken cancellationToken);

    /* :: :: Nested Types :: END :: */
    // //
}
