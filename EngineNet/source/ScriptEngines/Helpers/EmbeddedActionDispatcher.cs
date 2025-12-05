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
        string rootPath) {
        string t = (scriptType ?? string.Empty).ToLowerInvariant();
        switch (t) {
            case "lua":
                return new ScriptEngines.lua.LuaScriptAction(scriptPath: scriptPath, args: args);
            case "js":
                return new ScriptEngines.js.JsScriptAction(scriptPath: scriptPath, args: args);
            case "bms": {
                if (!games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? gobj)) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                string gameRoot = gobj.GameRoot;
                // For BMS we need module/project/input/output/ext; the args array should already contain input/output/ext resolved by CommandBuilder
                // The QuickBmsScriptAction constructor expects (scriptPath, moduleRoot, projectRoot, inputDir, outputDir, ext?)
                // We only create the action here when args contain at least input and output; otherwise, let Engine handle errors upstream.
                string inputDir = args is null ? string.Empty : System.Linq.Enumerable.ElementAtOrDefault(args, 0) ?? string.Empty;
                string outputDir = args is null ? string.Empty : System.Linq.Enumerable.ElementAtOrDefault(args, 1) ?? string.Empty;
                string? ext = args is null ? null : System.Linq.Enumerable.ElementAtOrDefault(args, 2);
                return new ScriptEngines.QuickBmsScriptAction(
                    scriptPath: scriptPath,
                    moduleRoot: gameRoot,
                    projectRoot: rootPath,
                    inputDir: inputDir,
                    outputDir: outputDir,
                    extension: ext
                );
            }
            case "python":
            case "py":
                return new ScriptEngines.PythonScriptAction(scriptPath: scriptPath, args: args);
            default:
                return null;
        }
    }
}
