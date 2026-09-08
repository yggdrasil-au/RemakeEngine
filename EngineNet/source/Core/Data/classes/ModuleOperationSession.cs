namespace EngineNet.Core.Data;

/// <summary>
/// Represents the latest persisted execution state of an operation.
/// </summary>
public enum OperationExecutionStatus {
    NotRun,
    Succeeded,
    Failed
}

/// <summary>
/// Represents a prepared operation and its persisted execution state for UI rendering.
/// </summary>
public sealed class SessionOperation(PreparedOperation operation, OperationExecutionStatus status) {
    /// <summary>
    /// Gets the prepared operation metadata and execution payload.
    /// </summary>
    public PreparedOperation Operation { get; } = operation;

    /// <summary>
    /// Gets the latest recorded execution status.
    /// </summary>
    public OperationExecutionStatus Status { get; } = status;
}

/// <summary>
/// Represents all manifest-defined operations and capabilities available for one module.
/// </summary>
public sealed class ModuleOperationSession(
    string gameName,
    GameModuleInfo? module,
    PreparedOperations preparedOperations,
    IReadOnlyList<SessionOperation> initOperations,
    IReadOnlyList<SessionOperation> regularOperations
) {
    /// <summary>
    /// Gets the selected module name.
    /// </summary>
    public string GameName { get; } = gameName;

    /// <summary>
    /// Gets the selected module metadata when the module resolved successfully.
    /// </summary>
    public GameModuleInfo? Module { get; } = module;

    /// <summary>
    /// Gets the prepared manifest operations, warnings, and load errors.
    /// </summary>
    public PreparedOperations PreparedOperations { get; } = preparedOperations;

    /// <summary>
    /// Gets initialization operations with their persisted execution statuses.
    /// </summary>
    public IReadOnlyList<SessionOperation> InitOperations { get; } = initOperations;

    /// <summary>
    /// Gets the menu-visible non-initialization operations with persisted status.
    /// </summary>
    public IReadOnlyList<SessionOperation> RegularOperations { get; } = regularOperations;

    /// <summary>
    /// Gets whether the selected module can be launched.
    /// </summary>
    public bool CanLaunch => Module?.IsBuilt == true;

    /// <summary>
    /// Gets whether the selected module exposes a run-all sequence.
    /// </summary>
    public bool CanRunAll => Module?.IsInternal == false && PreparedOperations.HasRunAll;
}