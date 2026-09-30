namespace EngineNet.Interface.Terminal;

/// <summary>
/// Options for inline operation execution.
/// </summary>
public sealed class InlineOperationOptions {
    public string? InternalModuleIdentifier {
        get; set;
    }
    public string? GameIdentifier {
        get; set;
    }
    public string? GameRoot {
        get; set;
    }
    public string? GameName {
        get; set;
    }
    public string? OpsFile {
        get; set;
    }
    public string? Script {
        get; set;
    }
    public string? ScriptType {
        get; set;
    }
    public object? RunOperationSelector {
        get; set;
    }
    public bool RunAll {
        get; set;
    }
    public Dictionary<string, object?> OperationFields { get; } = new(comparer: System.StringComparer.OrdinalIgnoreCase);
    public Core.Data.PromptAnswers PromptAnswers { get; } = new(); // respond to operations.toml prompts
    public Dictionary<string, string> AutoPromptResponses { get; } = new(comparer: System.StringComparer.OrdinalIgnoreCase); // responde to lua prompt() calls

    public readonly List<string> _args = new();
    public bool _argsOverride;


}
