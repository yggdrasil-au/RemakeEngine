
using EngineNet.Core.Services;

namespace EngineNet.Core.Engine;

/// <summary>
/// Encapsulates the core services required for executing engine operations.
/// </summary>
public record EngineContext(
    GameRegistry GameRegistry,
    CommandService CommandService,
    ExternalTools.JsonToolResolver ToolResolver,

    GitService GitService,

    EngineConfig EngineConfig,

    OperationExecution OperationExecution
);
