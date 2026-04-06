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
        this.Name = name;
        this.Type = type;
        this.Title = title;
        this.DefaultValue = defaultValue;
        this.Choices = choices;
        this.IsSecret = isSecret;
    }
}
