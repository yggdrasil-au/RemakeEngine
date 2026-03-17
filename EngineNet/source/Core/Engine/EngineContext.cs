using EngineNet.Core.Abstractions;
using EngineNet.Core.ExternalTools;
using EngineNet.Core.Services;

namespace EngineNet.Core.Engine;

/// <summary>
/// Encapsulates the core services required for executing engine operations.
/// </summary>
public record EngineContext(
    EngineConfig EngineConfig,
    IToolResolver ToolResolver,
    GitService GitService,
    IGameRegistry GameRegistry,
    ICommandService CommandService,
    OperationExecution OperationExecution
);
