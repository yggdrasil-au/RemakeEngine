namespace EngineNet.Core.Data;

/// <summary>
/// Prepared operations data for UI consumption.
/// </summary>
public sealed class PreparedOperations {
    public bool IsLoaded { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PreparedOperation> InitOperations { get; } = new List<PreparedOperation>();
    public List<PreparedOperation> RegularOperations { get; } = new List<PreparedOperation>();
    public bool HasRunAll { get; set; }
    public List<string> Warnings { get; } = new List<string>();
}
