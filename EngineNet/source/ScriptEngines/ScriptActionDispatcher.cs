
namespace EngineNet.ScriptEngines;

/// <summary>
/// Centralized dispatcher for script actions, both embedded (lua/js/python) and external (bms).
/// This ensures all script actions are created through a single point, allowing for consistent handling
/// </summary>
public sealed class ScriptActionDispatcher : IScriptActionDispatcher {

    // this is used by GameLauncher.cs to run game.toml if game is a script
    // and Runner.RunSingleOperationAsync to run embedded engine operations (lua/js/python)

    /// <summary>
    /// Resolves embedded actions (lua/js/bms) to IAction implementations.
    /// this should be the only way to call any embedded script action
    /// </summary>
    internal static class EmbeddedActionDispatcher {
        internal static IScriptAction? TryCreate(
            string scriptType,
            string scriptPath,
            IEnumerable<string> args,
            string currentGame,
            Core.Data.GameModules? games,
            string projectRoot
        ) {
            string t = scriptType.ToLowerInvariant();
            string gameRoot = string.Empty;
            if (games != null && !string.IsNullOrEmpty(currentGame) && games.TryGetValue(currentGame, out GameModuleInfo? info)) {
                gameRoot = info.GameRoot;
            }

            switch (t) {
                case "lua":
                    return new ScriptEngines.Lua.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: projectRoot);
                case "js": case "javascript":
                    return new ScriptEngines.Js.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: projectRoot);
                case "python": case "py":
                    return new ScriptEngines.Python.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: projectRoot);
                default: {
                    Shared.IO.Diagnostics.Log($"[EmbeddedActionDispatcher.cs::TryCreate()] Unsupported embedded script type '{scriptType}'");
                    return null;
                }
            }
        }
    }

    // this is used in Runner.RunSingleOperationAsync to run external script types (like bms)

    /// <summary>
    /// Resolves external actions (like bms) to IAction implementations.
    /// this should be the only way to call any external script action
    /// </summary>
    internal static class ExternalActionDispatcher {
        internal static IScriptAction? TryCreate(
            string scriptType,
            string scriptPath,
            string gameRoot,
            string inputDir,
            string outputDir,
            string? extension,
            string projectRoot
        ) {
            string t = scriptType.ToLowerInvariant();
            switch (t) {
                case "bms":
                    return new ScriptEngines.qbms.Main(scriptPath: scriptPath, moduleRoot: gameRoot, inputDir: inputDir, outputDir: outputDir, extension: extension);
                default: {
                    Shared.IO.Diagnostics.Log($"[ExternalActionDispatcher.cs::TryCreate()] Unsupported external script type '{scriptType}'");
                    return null;
                }
            }
        }
    }

    public IScriptAction? TryCreateEmbedded(
        string scriptType,
        string scriptPath,
        IEnumerable<string> args,
        string currentGame,
        Core.Data.GameModules? games,
        string projectRoot
    ) {
        return EmbeddedActionDispatcher.TryCreate(scriptType, scriptPath, args, currentGame, games, projectRoot);
    }

    public IScriptAction? TryCreateExternal(
        string scriptType,
        string scriptPath,
        string gameRoot,
        string inputDir,
        string outputDir,
        string? extension,
        string projectRoot
    ) {
        return ExternalActionDispatcher.TryCreate(scriptType, scriptPath, gameRoot, inputDir, outputDir, extension, projectRoot);
    }
}

