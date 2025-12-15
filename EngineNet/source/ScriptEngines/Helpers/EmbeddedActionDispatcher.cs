namespace EngineNet.ScriptEngines.Helpers;

using System.Collections.Generic;


/// <summary>
/// Resolves embedded actions (lua/js/bms) to IAction implementations.
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
                return new ScriptEngines.lua.LuaScriptAction(scriptPath: scriptPath, args: args, gameRoot: gameRoot, projectRoot: rootPath);
            case "js":
                return new ScriptEngines.js.JsScriptAction(scriptPath: scriptPath, args: args);
            case "python": case "py":
                return new ScriptEngines.PythonScriptAction(scriptPath: scriptPath, args: args);
            default: {
                Core.Diagnostics.Log($"[EmbeddedActionDispatcher.cs::TryCreate()] Unsupported embedded script type '{scriptType}'");
                return null;
            }
        }
    }
}
