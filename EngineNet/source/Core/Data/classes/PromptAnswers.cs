namespace EngineNet.Core.Data;

/// <summary>
/// Represents a case-insensitive map of prompt answers for an operation.
/// </summary>
public sealed class PromptAnswers : Dictionary<string, object?> {
    public PromptAnswers() : base(comparer: System.StringComparer.OrdinalIgnoreCase) {
    }

    public PromptAnswers(IDictionary<string, object?> values) : base(dictionary: values, comparer: System.StringComparer.OrdinalIgnoreCase) {
    }
}
