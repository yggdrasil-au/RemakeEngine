
namespace EngineNet.ScriptEngines.Helpers;

/// <summary>
/// Resolves embedded actions (lua/js/bms) to IAction implementations.
/// this should be the only way to call any embedded script action
/// </summary>
internal static class EmbeddedActionDispatcher {
    internal static ScriptEngines.Helpers.IAction? TryCreate(
        string scriptType,
        string scriptPath,
        IEnumerable<string> args,
        string currentGame,
        Dictionary<string, Core.Utils.GameModuleInfo> games,
        string rootPath
    ) {
        string t = (scriptType ?? string.Empty).ToLowerInvariant();
        string gameRoot = string.Empty;
        if (games != null && !string.IsNullOrEmpty(currentGame) && games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? info)) {
            gameRoot = info.GameRoot;
        }

        switch (t) {
            case "lua":
                return new ScriptEngines.Lua.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: rootPath);
            case "js":
                return new ScriptEngines.Js.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: rootPath);
            case "python": case "py":
                return new ScriptEngines.Python.Main(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: rootPath);
            default: {
                Core.Diagnostics.Log($"[EmbeddedActionDispatcher.cs::TryCreate()] Unsupported embedded script type '{scriptType}'");
                return null;
            }
        }
    }
}
