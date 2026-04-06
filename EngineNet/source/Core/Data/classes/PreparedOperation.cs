namespace EngineNet.Core.Data;

/// <summary>
/// Represents a prepared operation with resolved metadata.
/// </summary>
public sealed class PreparedOperation {
    public Dictionary<string, object?> Operation { get; }
    public string DisplayName { get; }
    public long? OperationId { get; }
    public bool HasDuplicateId { get; }
    public bool HasInvalidId { get; }
    public string? ScriptPath { get; }
    public string? ScriptType { get; }

    public PreparedOperation(
        Dictionary<string, object?> operation,
        string displayName,
        long? operationId,
        bool hasDuplicateId,
        bool hasInvalidId,
        string? scriptPath,
        string? scriptType
    ) {
        this.Operation = operation;
        this.DisplayName = displayName;
        this.OperationId = operationId;
        this.HasDuplicateId = hasDuplicateId;
        this.HasInvalidId = hasInvalidId;
        this.ScriptPath = scriptPath;
        this.ScriptType = scriptType;
    }
}
