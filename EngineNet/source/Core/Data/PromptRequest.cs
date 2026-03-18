namespace EngineNet.Core.Data;

/// <summary>
/// Encapsulates a prompt request for the UI.
/// </summary>
public sealed class PromptRequest {
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
