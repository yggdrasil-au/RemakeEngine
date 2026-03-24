namespace EngineNet.Core.Data;

/// <summary>
/// Prepared operations data for UI consumption.
/// </summary>
public sealed class PreparedOperations {
    internal bool IsLoaded { get; set; }
    internal string? ErrorMessage { get; set; }
    internal List<PreparedOperation> InitOperations { get; } = new List<PreparedOperation>();
    internal List<PreparedOperation> RegularOperations { get; } = new List<PreparedOperation>();
    internal bool HasRunAll { get; set; }
    internal List<string> Warnings { get; } = new List<string>();
}
