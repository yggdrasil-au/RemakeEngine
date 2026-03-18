using System.Collections.Generic;
using EngineNet.Core.Data;

namespace EngineNet.ScriptEngines;


public sealed class ScriptActionDispatcher {

    // this is used by GameLauncher.cs to run game.toml if game is a script
    // and Runner.RunSingleOperationAsync to run embedded engine operations (lua/js/python)

    /// <summary>
    /// Resolves embedded actions (lua/js/bms) to IAction implementations.
    /// this should be the only way to call any embedded script action
    /// </summary>
    public static class EmbeddedActionDispatcher {
        public static ScriptEngines.Helpers.IAction? TryCreate(
            string scriptType,
            string scriptPath,
            IEnumerable<string> args,
            string currentGame,
            Dictionary<string, GameModuleInfo> games
        ) {
            string t = (scriptType ?? string.Empty).ToLowerInvariant();
            string gameRoot = string.Empty;
            if (games != null && !string.IsNullOrEmpty(currentGame) && games.TryGetValue(currentGame, out GameModuleInfo? info)) {
                gameRoot = info.GameRoot;
            }

            switch (t) {
                case "lua":
                    return new ScriptEngines.Lua.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: Program.rootPath);
                case "js": case "javascript":
                    return new ScriptEngines.Js.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: Program.rootPath);
                case "python": case "py":
                    return new ScriptEngines.Python.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: Program.rootPath);
                default: {
                    Core.Diagnostics.Log($"[EmbeddedActionDispatcher.cs::TryCreate()] Unsupported embedded script type '{scriptType}'");
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
    public static class ExternalActionDispatcher {
        public static ScriptEngines.Helpers.IAction? TryCreate(
            string scriptType,
            string scriptPath,
            string gameRoot,
            string inputDir,
            string outputDir,
            string? extension
        ) {
            string t = (scriptType ?? string.Empty).ToLowerInvariant();
            switch (t) {
                case "bms":
                    return new ScriptEngines.qbms.Main(scriptPath: scriptPath, moduleRoot: gameRoot, inputDir: inputDir, outputDir: outputDir, extension: extension);
                default: {
                    Core.Diagnostics.Log($"[ExternalActionDispatcher.cs::TryCreate()] Unsupported external script type '{scriptType}'");
                    return null;
                }
            }
        }
    }
}
