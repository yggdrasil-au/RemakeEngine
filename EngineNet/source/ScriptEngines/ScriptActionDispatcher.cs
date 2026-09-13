
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
            if (games != null && !string.IsNullOrEmpty(currentGame) && games.TryGetValue(key: currentGame, out GameModuleInfo? info)) {
                gameRoot = info.GameRoot;
            }

            switch (t) {
                #if ENABLE_LUA
                case "lua": {
                    return new ScriptEngines.Lua.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: projectRoot);
                }
                #endif

                #if ENABLE_JS
                case "js": case "javascript": {
                    return new ScriptEngines.Js.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: projectRoot);
                }
                #endif

                #if ENABLE_PYTHON
                case "python": case "py": {
                    return new ScriptEngines.Python.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: projectRoot);
                }
                #endif

                default: {
                #if !ENABLE_LUA && !ENABLE_JS && !ENABLE_PYTHON
                    Shared.IO.Diagnostics.Log($"No embedded script engines are enabled, cannot create action for '{scriptType}'");
                #endif
                    Shared.IO.Diagnostics.Log($"Unsupported embedded script type '{scriptType}'");
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
                    Shared.IO.Diagnostics.Log($"Unsupported external script type '{scriptType}'");
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
        return EmbeddedActionDispatcher.TryCreate(scriptType: scriptType, scriptPath: scriptPath, args: args, currentGame: currentGame, games: games, projectRoot: projectRoot);
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
        return ExternalActionDispatcher.TryCreate(scriptType: scriptType, scriptPath: scriptPath, gameRoot: gameRoot, inputDir: inputDir, outputDir: outputDir, extension: extension, projectRoot: projectRoot);
    }
}

