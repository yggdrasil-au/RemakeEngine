namespace EngineNet.Core.Data;

/// <summary>
/// Represents the UI response for a prompt request.
/// </summary>
public sealed class PromptResponse {
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
