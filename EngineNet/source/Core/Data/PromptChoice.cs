namespace EngineNet.Core.Data;

/// <summary>
/// Encapsulates a selectable prompt choice.
/// </summary>
public sealed class PromptChoice {
    public string Label { get; }
    public bool IsDisabled { get; }

    public PromptChoice(string label, bool isDisabled) {
        Label = label;
        IsDisabled = isDisabled;
    }
}
