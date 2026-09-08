namespace EngineNet.Core.Data;

/// <summary>
/// Represents a prepared operation with resolved metadata.
/// </summary>
public sealed class PreparedOperation(
        Dictionary<string, object?> operation,
        string displayName,
        long? operationId,
        bool hasDuplicateId,
        bool hasInvalidId,
        string? scriptPath,
        string? scriptType
    ) {
    public Dictionary<string, object?> Operation { get; } = operation;
    public string DisplayName { get; } = displayName;
    public long? OperationId { get; } = operationId;
    public bool HasDuplicateId { get; } = hasDuplicateId;
    public bool HasInvalidId { get; } = hasInvalidId;
    public string? ScriptPath { get; } = scriptPath;
    public string? ScriptType { get; } = scriptType;
}
