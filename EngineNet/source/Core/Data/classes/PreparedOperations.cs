namespace EngineNet.Core.Data;

/// <summary>
/// Prepared operations data for UI consumption.
/// </summary>
public sealed class PreparedOperations {
    public bool IsLoaded { get; internal set; }
    public string? ErrorMessage { get; internal set; }
    public List<PreparedOperation> InitOperations { get; } = new List<PreparedOperation>();
    public List<PreparedOperation> RegularOperations { get; } = new List<PreparedOperation>();
    public List<PreparedOperation> RunAllOperations { get; } = new List<PreparedOperation>();
    public bool HasRunAll { get; internal set; }
    public List<string> Warnings { get; } = new List<string>();
}
