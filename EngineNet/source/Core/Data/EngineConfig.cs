namespace EngineNet.Core.Data;

/*
the EngineConfig class serves as a central container for engine-wide configuration data and variables. Here is a breakdown of its uses:
    Data Storage: It wraps a case-insensitive dictionary (IDictionary<string, object?> Data) that holds arbitrary configuration keys and values.
    Execution Context: When the engine runs an operation, it uses context.EngineConfig.Data alongside game module info to build the overall "Execution Context" (via ExecutionContextBuilder.Build). This context is then used to resolve variables and placeholders within the operation scripts.
    Command Building: It is passed into the CommandService.BuildCommand method to help format and construct the final command-line arguments and paths required to execute external tools or scripts.
    Shared Dependency: Rather than being passed around individually, it is injected into the EngineContext record. This makes the global configuration easily accessible to any engine service (like Runner, OperationExecution, or the CommandService) that needs to read engine-level settings without relying on a static class.

it acts as the global settings dictionary that provides variables for script execution and command building across the engine

*/


/// <summary>
/// Configuration class for the Engine, allowing storage of arbitrary key-value pairs.
/// </summary>
internal sealed class EngineConfig {
    /// <summary>
    /// A case-insensitive dictionary for storing engine configuration data.
    /// This can include any settings or variables that need to be accessed globally across the engine, such as paths, flags, or other parameters.
    /// The Engine and its services can read from this dictionary to resolve variables when building execution contexts or commands.
    /// </summary>
    internal IDictionary<string, object?> Data => this._data;
    private readonly Dictionary<string, object?> _data = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
}
