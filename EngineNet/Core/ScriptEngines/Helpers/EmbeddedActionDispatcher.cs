namespace EngineNet.Core.ScriptEngines.Helpers;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Resolves embedded actions (lua/js/bms) to IAction implementations.
/// </summary>
internal static class EmbeddedActionDispatcher {
    internal static EngineNet.Core.ScriptEngines.Helpers.IAction? TryCreate(
        string scriptType,
        string scriptPath,
        IEnumerable<string> args,
        string currentGame,
        IDictionary<string, object?> games,
        string rootPath) {
        string t = (scriptType ?? string.Empty).ToLowerInvariant();
        switch (t) {
            case "lua":
                return new EngineNet.Core.ScriptEngines.LuaScriptAction(scriptPath: scriptPath, args: args);
            case "js":
                return new EngineNet.Core.ScriptEngines.JsScriptAction(scriptPath: scriptPath, args: args);
            case "bms": {
                if (!games.TryGetValue(currentGame, out object? gobj) || gobj is not IDictionary<string, object?> gdict) {
                    throw new System.Collections.Generic.KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                string gameRoot = gdict.TryGetValue("game_root", out object? gr) ? gr?.ToString() ?? string.Empty : string.Empty;
                // For BMS we need module/project/input/output/ext; the args array should already contain input/output/ext resolved by CommandBuilder
                // The QuickBmsScriptAction constructor expects (scriptPath, moduleRoot, projectRoot, inputDir, outputDir, ext?)
                // We only create the action here when args contain at least input and output; otherwise, let Engine handle errors upstream.
                string inputDir = args is null ? string.Empty : System.Linq.Enumerable.ElementAtOrDefault(args, 0) ?? string.Empty;
                string outputDir = args is null ? string.Empty : System.Linq.Enumerable.ElementAtOrDefault(args, 1) ?? string.Empty;
                string? ext = args is null ? null : System.Linq.Enumerable.ElementAtOrDefault(args, 2);
                return new EngineNet.Core.ScriptEngines.QuickBmsScriptAction(
                    scriptPath: scriptPath,
                    moduleRoot: gameRoot,
                    projectRoot: rootPath,
                    inputDir: inputDir,
                    outputDir: outputDir,
                    extension: ext
                );
            }
            default:
                return null;
        }
    }
}
