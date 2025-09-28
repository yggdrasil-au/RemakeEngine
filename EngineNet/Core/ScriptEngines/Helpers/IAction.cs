using System.Threading.Tasks;
using System.Threading;

namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Represents a single executable step within a game module.
/// </summary>
public interface IAction {
    /// <summary>
    /// Executes the action with access to tool resolution services.
    /// </summary>
    /// <param name="tools">Resolver for locating external tools.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task ExecuteAsync(Tools.IToolResolver tools, CancellationToken cancellationToken = default);
}
