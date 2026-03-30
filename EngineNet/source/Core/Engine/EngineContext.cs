
using EngineNet.Core.Services;
using EngineNet.Core.Data;

namespace EngineNet.Core.Engine;

/// <summary>
/// Encapsulates the core services required for executing engine operations.
/// </summary>
internal record EngineContext(
    GameRegistry GameRegistry,
    CommandService CommandService,
    ExternalTools.JsonToolResolver ToolResolver,


    EngineConfig EngineConfig,

    OperationContext OperationContext
);

// operation context record
internal record OperationContext(
    Core.Services.OperationsService OperationsService,
    Core.Services.OperationsLoader OperationsLoader,
    Core.Engine.Operations.Single Single
);
