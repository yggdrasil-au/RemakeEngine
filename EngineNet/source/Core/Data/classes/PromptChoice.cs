namespace EngineNet.Core.Data;

/// <summary>
/// Encapsulates a selectable prompt choice.
/// </summary>
public sealed class PromptChoice(string label, bool isDisabled) {
    public string Label { get; } = label;
    public bool IsDisabled { get; } = isDisabled;
}
