namespace EngineNet.Core.Data;

/// <summary>
/// Represents the UI response for a prompt request.
/// </summary>
internal sealed class PromptResponse {
    internal bool IsCancelled { get; }
    internal bool UseDefault { get; }
    internal object? Value { get; }

    private PromptResponse(bool isCancelled, bool useDefault, object? value) {
        this.IsCancelled = isCancelled;
        this.UseDefault = useDefault;
        this.Value = value;
    }
    
    internal static PromptResponse Cancelled() => new PromptResponse(isCancelled: true, useDefault: false, value: null);
    internal static PromptResponse UseDefaultValue() => new PromptResponse(isCancelled: false, useDefault: true, value: null);
    internal static PromptResponse FromValue(object? value) => new PromptResponse(isCancelled: false, useDefault: false, value: value);
}
