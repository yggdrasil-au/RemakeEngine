namespace EngineNet.Core.Data;

/// <summary>
/// Encapsulates a selectable prompt choice.
/// </summary>
internal sealed class PromptChoice {
    internal string Label { get; }
    internal bool IsDisabled { get; }

    internal PromptChoice(string label, bool isDisabled) {
        this.Label = label;
        this.IsDisabled = isDisabled;
    }
}
