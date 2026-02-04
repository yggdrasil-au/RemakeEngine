namespace EngineNet.ScriptEngines.Helpers;

/// <summary>
/// Represents a single executable step within a game module.
/// </summary>
internal interface IAction {
    /// <summary>
    /// Executes the action with access to tool resolution services.
    /// </summary>
    /// <param name="tools">Resolver for locating external tools.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    System.Threading.Tasks.Task ExecuteAsync(Core.ExternalTools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default);
}
