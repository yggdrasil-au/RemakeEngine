namespace EngineNet.Core.Data;

/// <summary>
/// Represents the UI response for a prompt request.
/// </summary>
public sealed class PromptResponse {
    internal bool IsCancelled { get; }
    internal bool UseDefault { get; }
    internal object? Value { get; }

    private PromptResponse(bool isCancelled, bool useDefault, object? value) {
        this.IsCancelled = isCancelled;
        this.UseDefault = useDefault;
        this.Value = value;
    }

    public static PromptResponse Cancelled() {
        return new PromptResponse(isCancelled: true, useDefault: false, null);
    }

    public static PromptResponse UseDefaultValue() {
        return new PromptResponse(isCancelled: false, useDefault: true, null);
    }

    public static PromptResponse FromValue(object? value) {
        return new PromptResponse(isCancelled: false, useDefault: false, value);
    }
}
