namespace EngineNet.Core.Data;

/// <summary>
/// Represents a prepared operation with resolved metadata.
/// </summary>
internal sealed class PreparedOperation {
    internal Dictionary<string, object?> Operation { get; }
    internal string DisplayName { get; }
    internal long? OperationId { get; }
    internal bool HasDuplicateId { get; }
    internal bool HasInvalidId { get; }
    internal string? ScriptPath { get; }
    internal string? ScriptType { get; }

    internal PreparedOperation(
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
