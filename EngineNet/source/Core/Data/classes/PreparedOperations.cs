namespace EngineNet.Core.Data;

/// <summary>
/// Prepared operations data for UI consumption.
/// </summary>
public sealed class PreparedOperations {
    public bool IsLoaded { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PreparedOperation> InitOperations { get; } = new();
    public List<PreparedOperation> RegularOperations { get; } = new();
    public List<PreparedOperation> RunAllOperations { get; } = new();
    public bool HasRunAll { get; set; }
    public List<string> Warnings { get; } = new();
}
