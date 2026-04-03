
using EngineNet.Core.Data;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

/// <summary>
/// Provides shared operations loading, validation, and prompt-flow logic for all interfaces.
/// </summary>
internal sealed class OperationsService {

    /* :: :: Vars :: START :: */
    private readonly OperationsLoader _loader;
    private readonly GameRegistry _gameRegistry;
    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    internal OperationsService(OperationsLoader loader, GameRegistry gameRegistry) {
        _loader = loader;
        _gameRegistry = gameRegistry;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Loads operations from an ops file and prepares structured metadata for UI consumption.
    /// </summary>
    /// <param name="opsFile"></param>
    /// <returns></returns>
    internal PreparedOperations LoadAndPrepare(
        string opsFile,
        string? currentGame = null,
        Dictionary<string, Data.GameModuleInfo>? games = null,
        IDictionary<string, object?>? engineConfig = null
    ) {
        PreparedOperations result = new PreparedOperations();
        if (string.IsNullOrWhiteSpace(opsFile) || !System.IO.File.Exists(opsFile)) {
            result.IsLoaded = false;
            result.ErrorMessage = "Operations file is missing.";
            return result;
        }

        List<Dictionary<string, object?>>? allOps = _loader.LoadOperations(opsFile);
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
                ctx = Core.Utils.ExecutionContextBuilder.Build(currentGame, games, engineConfig);
            } catch (System.Exception ex) {
                Core.Diagnostics.Bug($"[OperationsService::LoadAndPrepare()] Failed building context for game '{currentGame}'.", ex);
                /* ignore context build failure for menu rendering */
            }
        }

        Dictionary<long, int> idCounts = new Dictionary<long, int>();
        HashSet<Dictionary<string, object?>> invalidIdOps = new HashSet<Dictionary<string, object?>>();

        foreach (Dictionary<string, object?> op in allOps) {
            if (TryGetLong(op, out long idValue)) {
                idCounts[idValue] = idCounts.TryGetValue(idValue, out int count) ? count + 1 : 1;
            } else if (op.ContainsKey("id")) {
                invalidIdOps.Add(op);
            }
        }

        HashSet<long> duplicateIds = idCounts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet();
        if (duplicateIds.Count > 0) {
            result.Warnings.Add($"Duplicate operation IDs found: {string.Join(", ", duplicateIds.OrderBy(x => x))}");
        }
        if (invalidIdOps.Count > 0) {
            result.Warnings.Add("One or more operations contain invalid IDs.");
        }

        foreach (Dictionary<string, object?> op in allOps) {
            // Resolve placeholders for THIS operation for menu display
            Dictionary<string, object?> resolvedOp = op;
            if (ctx != null) {
                if (Core.Utils.Placeholders.Resolve(op, ctx) is Dictionary<string, object?> resolved) {
                    resolvedOp = resolved;
                }
            }

            bool isInit = TryGetBool(resolvedOp, out bool initValue, "init") && initValue;
            bool hasDuplicateId = false;
            bool hasInvalidId = invalidIdOps.Contains(op);
            long? id = null;

            if (TryGetLong(resolvedOp, out long idValue)) {
                id = idValue;
                hasDuplicateId = duplicateIds.Contains(idValue);
            }

            string displayName = ResolveOperationDisplayName(resolvedOp);
            string? scriptPath = GetString(resolvedOp, "script");
            string? scriptType = GetString(resolvedOp, "script_type", "scriptType");

            PreparedOperation prepared = new PreparedOperation(
                operation: resolvedOp,
                displayName: displayName,
                operationId: id,
                hasDuplicateId: hasDuplicateId,
                hasInvalidId: hasInvalidId,
                scriptPath: scriptPath,
                scriptType: scriptType
            );

            if (isInit) {
                result.InitOperations.Add(prepared);
            } else {
                result.RegularOperations.Add(prepared);
            }
        }

        result.HasRunAll = allOps.Any(op =>
            (TryGetBool(op, out bool runAllDash, "run-all") && runAllDash) ||
            (TryGetBool(op, out bool runAllUnderscore, "run_all") && runAllUnderscore)
        );

        return result;
    }

    /// <summary>
    /// Resolves a consistent display name for an operation using common keys.
    /// </summary>
    /// <param name="op"></param>
    /// <returns></returns>
    internal static string ResolveOperationDisplayName(IDictionary<string, object?> op) {
        string? name = GetString(op, "Name", "name");
        if (!string.IsNullOrWhiteSpace(name)) {
            return name;
        }

        string? title = GetString(op, "Title", "title");
        if (!string.IsNullOrWhiteSpace(title)) {
            return title;
        }

        string? script = GetString(op, "script");
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
    /// <returns></returns>
    internal async Task<bool> CollectAnswersAsync(
        Dictionary<string, object?> op,
        PromptAnswers answers,
        PromptHandler promptHandler,
        bool defaultsOnly = false
    ) {
        if (!op.TryGetValue("prompts", out object? promptsObj) || promptsObj is not IList<object?> prompts) {
            return true;
        }

        foreach (object? p in prompts) {
            if (p is not Dictionary<string, object?> prompt) {
                continue;
            }

            if (!TryGetPromptName(prompt, out string name)) {
                continue;
            }

            string type = NormalizePromptType(GetString(prompt, "type", "Type"));
            object? defaultValue = GetPromptDefault(prompt);

            if (TryGetString(prompt, out string? conditionName, "condition") && !string.IsNullOrWhiteSpace(conditionName)) {
                EnsureConditionDefault(prompts, answers, conditionName);
                if (!answers.TryGetValue(conditionName, out object? condVal) || condVal is not bool cb || !cb) {
                    answers[name] = EmptyValueForType(type);
                    continue;
                }
            }

            if (defaultsOnly) {
                answers[name] = defaultValue ?? EmptyValueForType(type);
                continue;
            }

            string title = ResolvePromptTitle(prompt, name);
            bool isSecret = TryGetBool(prompt, out bool secretValue, "secret", "Secret") && secretValue;
            IReadOnlyList<PromptChoice> choices = ResolvePromptChoices(prompt, type);

            PromptRequest request = new PromptRequest(
                name: name,
                type: type,
                title: title,
                defaultValue: defaultValue,
                choices: choices,
                isSecret: isSecret
            );

            PromptResponse response = await promptHandler(request).ConfigureAwait(false);
            if (response.IsCancelled) {
                return false;
            }

            if (response.UseDefault) {
                answers[name] = defaultValue ?? EmptyValueForType(type);
            } else {
                answers[name] = response.Value;
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
        string? title = GetString(prompt, "message", "Message", "prompt", "Prompt");
        return string.IsNullOrWhiteSpace(title) ? fallbackName : title;
    }

    private IReadOnlyList<PromptChoice> ResolvePromptChoices(IDictionary<string, object?> prompt, string type) {
        List<PromptChoice> choices = new List<PromptChoice>();

        if (TryGetString(prompt, out string? provider, "choices_provider") && provider == "registry_modules") {
            Dictionary<string, Data.GameModuleInfo> registered = _gameRegistry.GetModules(ModuleFilter.Registered);
            Dictionary<string, Data.GameModuleInfo> installed = _gameRegistry.GetModules(ModuleFilter.Installed);
            choices.AddRange(registered.Keys.Select(key => new PromptChoice(label: key, isDisabled: installed.ContainsKey(key))));
            return choices;
        }

        if (TryGetList(prompt, out IList<object?>? rawChoices, "choices", "Choices") && rawChoices is not null) {
            choices.AddRange(rawChoices
                .Select(choice => choice?.ToString() ?? string.Empty)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => new PromptChoice(label: label, isDisabled: false)));
        }

        return choices;
    }

    private static void EnsureConditionDefault(IList<object?> prompts, PromptAnswers answers, string conditionName) {
        if (answers.ContainsKey(conditionName)) {
            return;
        }

        foreach (object? candidate in prompts) {
            if (candidate is not Dictionary<string, object?> prompt) {
                continue;
            }

            if (TryGetPromptName(prompt, out string name) && name == conditionName && prompt.TryGetValue("default", out object? def)) {
                answers[conditionName] = def;
                return;
            }
        }
    }

    private static bool TryGetPromptName(IDictionary<string, object?> prompt, out string name) {
        name = GetString(prompt, "Name", "name") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name)) {
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static object? GetPromptDefault(IDictionary<string, object?> prompt) {
        if (prompt.TryGetValue("default", out object? def)) {
            return def;
        }

        if (prompt.TryGetValue("Default", out object? defAlt)) {
            return defAlt;
        }

        return null;
    }

    private static object? EmptyValueForType(string type) {
        return type switch {
            "confirm" => false,
            "checkbox" => new List<object?>(),
            "select" => null,
            _ => null
        };
    }

    private static bool TryGetBool(IDictionary<string, object?> data, out bool value, params string[] keys) {
        value = false;
        foreach (string key in keys) {
            if (data.TryGetValue(key, out object? raw) && raw is bool b) {
                value = b;
                return true;
            }
        }
        return false;
    }

    private static bool TryGetLong(IDictionary<string, object?> data, out long value) {
        value = 0;
        if (!data.TryGetValue("id", out object? raw) || raw is null) {
            return false;
        }

        try {
            value = System.Convert.ToInt64(raw);
            return true;
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[OperationsService::TryGetLong()] Failed to parse operation id value '{raw}'.", ex);
            return false;
        }
    }

    private static string? GetString(IDictionary<string, object?> data, params string[] keys) {
        foreach (string key in keys) {
            if (data.TryGetValue(key, out object? raw) && raw is not null) {
                string text = raw.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text)) {
                    return text;
                }
            }
        }
        return null;
    }

    private static bool TryGetString(IDictionary<string, object?> data, out string? value, params string[] keys) {
        value = GetString(data, keys);
        return value is not null;
    }

    private static bool TryGetList(IDictionary<string, object?> data, out IList<object?>? values, params string[] keys) {
        values = null;
        foreach (string key in keys) {
            if (data.TryGetValue(key, out object? raw) && raw is IList<object?> list) {
                values = list;
                return true;
            }
        }
        return false;
    }
    /* :: :: Helpers :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// UI prompt callback signature.
    /// </summary>
    public delegate Task<PromptResponse> PromptHandler(PromptRequest request);

    /* :: :: Nested Types :: END :: */
    // //
}
