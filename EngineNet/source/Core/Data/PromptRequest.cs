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
        this.Name = name;
        this.Type = type;
        this.Title = title;
        this.DefaultValue = defaultValue;
        this.Choices = choices;
        this.IsSecret = isSecret;
    }
}
