namespace EngineNet.Core.Abstractions;

/// <summary>
/// Factory contract for embedded and external script actions.
/// The host provides the runtime context and the script-engine assembly returns
/// the correct executable action implementation.
/// </summary>
public interface IScriptActionDispatcher {
    /// <summary>
    /// Creates an embedded script action (Lua, JavaScript, Python).
    /// </summary>
    /// <param name="scriptType">Requested embedded script type.</param>
    /// <param name="scriptPath">Absolute path to the script file.</param>
    /// <param name="args">Arguments to forward to the script.</param>
    /// <param name="currentGame">Current game module identifier.</param>
    /// <param name="games">Available game module metadata.</param>
    /// <param name="projectRoot">Root path of the engine project.</param>
    /// <returns>An executable script action when the script type is supported; otherwise <c>null</c>.</returns>
    IScriptAction? TryCreateEmbedded(
        string scriptType,
        string scriptPath,
        IEnumerable<string> args,
        string currentGame,
        Core.Data.GameModules? games,
        string projectRoot
    );

    /// <summary>
    /// Creates an external script action (for example QuickBMS).
    /// </summary>
    /// <param name="scriptType">Requested external script type.</param>
    /// <param name="scriptPath">Absolute path to the script file.</param>
    /// <param name="gameRoot">Root folder for the active game module.</param>
    /// <param name="inputDir">Input directory for the operation.</param>
    /// <param name="outputDir">Output directory for the operation.</param>
    /// <param name="extension">Optional extension or filter hint.</param>
    /// <param name="projectRoot">Root path of the engine project.</param>
    /// <returns>An executable script action when the script type is supported; otherwise <c>null</c>.</returns>
    IScriptAction? TryCreateExternal(
        string scriptType,
        string scriptPath,
        string gameRoot,
        string inputDir,
        string outputDir,
        string? extension,
        string projectRoot
    );
}