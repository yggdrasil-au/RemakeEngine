namespace EngineNet.Core.Data;

/// <summary>
/// Encapsulates a prompt request for the UI.
/// </summary>
public sealed class PromptRequest(
        string name,
        string type,
        string title,
        object? defaultValue,
        IReadOnlyList<PromptChoice> choices,
        bool isSecret
    ) {
    public string Name { get; } = name;
    public string Type { get; } = type;
    public string Title { get; } = title;
    public object? DefaultValue { get; } = defaultValue;
    public IReadOnlyList<PromptChoice> Choices { get; } = choices;
    public bool IsSecret { get; } = isSecret;
}
