namespace EngineNet.Core.Data;

/// <summary>
/// Encapsulates a prompt request for the UI.
/// </summary>
internal sealed class PromptRequest {
    internal string Name { get; }
    internal string Type { get; }
    internal string Title { get; }
    internal object? DefaultValue { get; }
    internal IReadOnlyList<PromptChoice> Choices { get; }
    internal bool IsSecret { get; }

    internal PromptRequest(
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
