using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EngineNet.Core.Abstractions;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

/// <summary>
/// Provides shared operations loading, validation, and prompt-flow logic for all interfaces.
/// </summary>
internal sealed class OperationsService {

    /* :: :: Vars :: START :: */
    private readonly IOperationsLoader _loader;
    private readonly IGameRegistry _gameRegistry;
    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    internal OperationsService(IOperationsLoader loader, IGameRegistry gameRegistry) {
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
    internal PreparedOperations LoadAndPrepare(string opsFile) {
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
            bool isInit = TryGetBool(op, out bool initValue, "init") && initValue;
            bool hasDuplicateId = false;
            bool hasInvalidId = invalidIdOps.Contains(op);
            long? id = null;

            if (TryGetLong(op, out long idValue)) {
                id = idValue;
                hasDuplicateId = duplicateIds.Contains(idValue);
            }

            string displayName = ResolveOperationDisplayName(op);
            string? scriptPath = GetString(op, "script");
            string? scriptType = GetString(op, "script_type", "scriptType");

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
        Dictionary<string, object?> answers,
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
            Dictionary<string, GameModuleInfo> registered = _gameRegistry.GetModules(ModuleFilter.Registered);
            Dictionary<string, GameModuleInfo> installed = _gameRegistry.GetModules(ModuleFilter.Installed);
            foreach (string key in registered.Keys) {
                bool isDisabled = installed.ContainsKey(key);
                choices.Add(new PromptChoice(label: key, isDisabled: isDisabled));
            }
            return choices;
        }

        if (TryGetList(prompt, out IList<object?>? rawChoices, "choices", "Choices") && rawChoices is not null) {
            foreach (object? choice in rawChoices) {
                string label = choice?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(label)) {
                    choices.Add(new PromptChoice(label: label, isDisabled: false));
                }
            }
        }

        return choices;
    }

    private static void EnsureConditionDefault(IList<object?> prompts, Dictionary<string, object?> answers, string conditionName) {
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
        } catch {
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
    /// Prepared operations data for UI consumption.
    /// </summary>
    internal sealed class PreparedOperations {
        public bool IsLoaded { get; internal set; }
        public string? ErrorMessage { get; internal set; }
        public List<PreparedOperation> InitOperations { get; } = new List<PreparedOperation>();
        public List<PreparedOperation> RegularOperations { get; } = new List<PreparedOperation>();
        public bool HasRunAll { get; internal set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Represents a prepared operation with resolved metadata.
    /// </summary>
    internal sealed class PreparedOperation {
        public Dictionary<string, object?> Operation { get; }
        public string DisplayName { get; }
        public long? OperationId { get; }
        public bool HasDuplicateId { get; }
        public bool HasInvalidId { get; }
        public string? ScriptPath { get; }
        public string? ScriptType { get; }

        internal PreparedOperation(
            Dictionary<string, object?> operation,
            string displayName,
            long? operationId,
            bool hasDuplicateId,
            bool hasInvalidId,
            string? scriptPath,
            string? scriptType
        ) {
            Operation = operation;
            DisplayName = displayName;
            OperationId = operationId;
            HasDuplicateId = hasDuplicateId;
            HasInvalidId = hasInvalidId;
            ScriptPath = scriptPath;
            ScriptType = scriptType;
        }
    }

    /// <summary>
    /// Encapsulates a prompt request for the UI.
    /// </summary>
    internal sealed class PromptRequest {
        public string Name { get; }
        public string Type { get; }
        public string Title { get; }
        public object? DefaultValue { get; }
        public IReadOnlyList<PromptChoice> Choices { get; }
        public bool IsSecret { get; }

        public PromptRequest(
            string name,
            string type,
            string title,
            object? defaultValue,
            IReadOnlyList<PromptChoice> choices,
            bool isSecret
        ) {
            Name = name;
            Type = type;
            Title = title;
            DefaultValue = defaultValue;
            Choices = choices;
            IsSecret = isSecret;
        }
    }

    /// <summary>
    /// Represents the UI response for a prompt request.
    /// </summary>
    internal sealed class PromptResponse {
        public bool IsCancelled { get; }
        public bool UseDefault { get; }
        public object? Value { get; }

        private PromptResponse(bool isCancelled, bool useDefault, object? value) {
            IsCancelled = isCancelled;
            UseDefault = useDefault;
            Value = value;
        }

        public static PromptResponse Cancelled() => new PromptResponse(isCancelled: true, useDefault: false, value: null);
        public static PromptResponse UseDefaultValue() => new PromptResponse(isCancelled: false, useDefault: true, value: null);
        public static PromptResponse FromValue(object? value) => new PromptResponse(isCancelled: false, useDefault: false, value: value);
    }

    /// <summary>
    /// Encapsulates a selectable prompt choice.
    /// </summary>
    internal sealed class PromptChoice {
        public string Label { get; }
        public bool IsDisabled { get; }

        public PromptChoice(string label, bool isDisabled) {
            Label = label;
            IsDisabled = isDisabled;
        }
    }

    /// <summary>
    /// UI prompt callback signature.
    /// </summary>
    internal delegate Task<PromptResponse> PromptHandler(PromptRequest request);

    /* :: :: Nested Types :: END :: */
    // //
}
