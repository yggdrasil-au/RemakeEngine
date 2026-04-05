namespace EngineNet.Core.Abstractions;

/// <summary>
/// Represents a script action that can be executed by the engine.
/// Script engine implementations provide this contract so Core can remain agnostic
/// about the concrete runtime (Lua, JavaScript, Python, QuickBMS, and so on).
/// </summary>
public interface IScriptAction {
    /// <summary>
    /// Executes the action with access to tool resolution services and command execution.
    /// </summary>
    /// <param name="tools">Resolver for locating external tools.</param>
    /// <param name="commandService">Centralized command execution service.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task ExecuteAsync(Core.ExternalTools.JsonToolResolver tools, Core.Services.CommandService commandService, CancellationToken cancellationToken = default(CancellationToken));
}
